using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Unity.Collections.LowLevel.Unsafe;
using Debug = UnityEngine.Debug;

namespace kcp2k
{
    enum KcpState { Connected, Handshake, Authenticated, Disconnected }

    public abstract class KcpConnection
    {
        protected Socket socket;
        protected EndPoint remoteEndpoint;
        internal Kcp kcp;

        // kcp can have several different states, let's use a state machine
        KcpState state = KcpState.Disconnected;
        KcpState lastState = KcpState.Disconnected;

        public event Action OnConnected;
        public event Action<ArraySegment<byte>> OnData;
        public event Action OnDisconnected;

        // If we don't receive anything these many milliseconds
        // then consider us disconnected
        public const int TIMEOUT = 3000;

        // internal time.
        // StopWatch offers ElapsedMilliSeconds and should be more precise than
        // Unity's time.deltaTime over long periods.
        readonly Stopwatch refTime = new Stopwatch();

        // recv buffer to avoid allocations
        byte[] buffer = new byte[Kcp.MTU_DEF];

        internal static readonly ArraySegment<byte> Hello = new ArraySegment<byte>(new byte[] { 0 });
        private static readonly ArraySegment<byte> Goodbye = new ArraySegment<byte>(new byte[] { 1 });

        // if we send more than kcp can handle, we will get ever growing
        // send/recv buffers and queues and minutes of latency.
        // => if a connection can't keep up, it should be disconnected instead
        //    to protect the server under heavy load, and because there is no
        //    point in growing to gigabytes of memory or minutes of latency!
        // => 10k seems more than enough room to still recover from.
        //
        // note: we have a ChokeConnectionAutoDisconnects test for this too!
        internal const int QueueDisconnectThreshold = 10000;

        // NoDelay & interval are the most important configurations.
        // let's force require the parameters so we don't forget it anywhere.
        protected void SetupKcp(bool noDelay, uint interval = Kcp.INTERVAL)
        {
            kcp = new Kcp(0, RawSend);
            kcp.SetNoDelay(noDelay ? 1u : 0u, interval);
            refTime.Start();
            state = KcpState.Connected;

            Tick();
        }

        bool IsChoked(out int total)
        {
            // disconnect connections that can't process the load.
            // see QueueSizeDisconnect comments.
            total = kcp.rcv_queue.Count + kcp.snd_queue.Count +
                    kcp.rcv_buf.Count + kcp.snd_buf.Count;
            return total >= QueueDisconnectThreshold;
        }

        // reads the next message from connection.
        bool ReceiveNext(out ArraySegment<byte> message)
        {
            // read only one message
            int msgSize = kcp.PeekSize();
            if (msgSize > 0)
            {
                // only allow receiving up to MaxMessageSize sized messages.
                // otherwise we would get BlockCopy ArgumentException anyway.
                if (msgSize <= Kcp.MTU_DEF)
                {
                    int received = kcp.Receive(buffer, msgSize);
                    if (received >= 0)
                    {
                        message = new ArraySegment<byte>(buffer, 0, msgSize);
                        return true;
                    }
                    else
                    {
                        // if receive failed, close everything
                        Debug.LogWarning($"Receive failed with error={received}. closing connection.");
                        Disconnect();
                    }
                }
                // we don't allow sending messages > Max, so this must be an
                // attacker. let's disconnect to avoid allocation attacks etc.
                else
                {
                    Debug.LogWarning($"KCP: possible allocation attack for msgSize {msgSize} > max {Kcp.MTU_DEF}. Disconnecting the connection.");
                    Disconnect();
                }
            }
            return false;
        }

        void TickConnected(uint time)
        {
            kcp.Update(time);
            // send handshake to the other end, wait for a reply
            // note that server & client connections both send the
            // handshake immediately. there is no particular order.
            Debug.Log("KcpConnection: sending Handshake to other end!");
            Send(Hello);
            state = KcpState.Handshake;
        }

        void TickHandshake(uint time)
        {
            kcp.Update(time);

            // any message received?
            if (ReceiveNext(out ArraySegment<byte> message))
            {
                // handshake message?
                if (SegmentsEqual(message, Hello))
                {
                    Debug.Log("KCP: received handshake");
                    state = KcpState.Authenticated;
                    OnConnected?.Invoke();
                }
                // otherwise it's random data from the internet, not
                // from a legitimate player. disconnect.
                else
                {
                    Debug.LogWarning("KCP: received random data before handshake. Disconnecting the connection.");
                    Disconnect();
                }
            }
        }

        void TickAuthenticated(uint time)
        {
            kcp.Update(time);

            // we can only send to authenticated connections.
            // so we need to detect chocked connections here.
            if (IsChoked(out int total))
            {
                Debug.LogWarning($"KCP: disconnecting connection because it can't process data fast enough.\n" +
                                 $"Queue total {total}>{QueueDisconnectThreshold}. rcv_queue={kcp.rcv_queue.Count} snd_queue={kcp.snd_queue.Count} rcv_buf={kcp.rcv_buf.Count} snd_buf={kcp.snd_buf.Count}\n" +
                                 $"* Try to Enable NoDelay, decrease INTERVAL, increase SEND/RECV WINDOW or compress data.\n" +
                                 $"* Or perhaps the network is simply too slow on our end, or on the other end.\n");
                Disconnect();
            }

            // process all received messages
            while (ReceiveNext(out ArraySegment<byte> message))
            {
                // disconnect message?
                if (SegmentsEqual(message, Goodbye))
                {
                    Debug.Log("KCP: received disconnect message");
                    Disconnect();
                    break;
                }
                // otherwise regular message
                else
                {
                    // only accept regular messages
                    //Debug.LogWarning($"Kcp recv msg: {BitConverter.ToString(buffer, 0, msgSize)}");
                    OnData?.Invoke(message);
                }
            }
        }

        void TickDisconnected(uint time)
        {
            // don't update while disconnected

            // TODO keep updating while disconnected so everything
            // is flushed out?
            // or use a Disconnecting state for a second or so

            // not disconnected before?
            // then call OnDisconnected
            if (lastState != KcpState.Disconnected)
            {
                Debug.Log("KCP Connection: Disconnected.");
                OnDisconnected?.Invoke();
            }
        }

        public void Tick()
        {
            uint time = (uint)refTime.ElapsedMilliseconds;

            try
            {
                switch (state)
                {
                    case KcpState.Connected:
                    {
                        TickConnected(time);
                        break;
                    }
                    case KcpState.Handshake:
                    {
                        TickHandshake(time);
                        break;
                    }
                    case KcpState.Authenticated:
                    {
                        TickAuthenticated(time);
                        break;
                    }
                    case KcpState.Disconnected:
                    {
                        TickDisconnected(time);
                        break;
                    }
                }
            }
            catch (SocketException)
            {
                // this is ok, the connection was closed
                state = KcpState.Disconnected;
            }
            catch (ObjectDisposedException)
            {
                // fine, socket was closed, no more ticking needed
                state = KcpState.Disconnected;
            }
            catch (Exception ex)
            {
                // unexpected
                state = KcpState.Disconnected;
                Debug.LogException(ex);
            }

            // remember previous state
            lastState = state;
        }

        public void RawInput(byte[] buffer, int msgLength)
        {
            int input = kcp.Input(buffer, msgLength);
            if (input == 0)
            {
                //lastReceived = (uint)refTime.ElapsedMilliseconds;
            }
            else Debug.LogWarning($"Input failed with error={input} for buffer with length={msgLength}");
        }

        protected abstract void RawSend(byte[] data, int length);

        public void Send(ArraySegment<byte> data)
        {
            // only allow sending up to MaxMessageSize sized messages.
            // other end won't process bigger messages anyway.
            if (data.Count <= Kcp.MTU_DEF)
            {
                int sent = kcp.Send(data.Array, data.Offset, data.Count);
                if (sent < 0)
                {
                    Debug.LogWarning($"Send failed with error={sent} for segment with length={data.Count}");
                }
            }
            else Debug.LogError($"Failed to send message of size {data.Count} because it's larger than MaxMessageSize={Kcp.MTU_DEF}");
        }

        protected virtual void Dispose()
        {
        }

        // ArraySegment content comparison without Linq
        public static unsafe bool SegmentsEqual(ArraySegment<byte> a, ArraySegment<byte> b)
        {
            if (a.Count == b.Count)
            {
                fixed (byte* aPtr = &a.Array[a.Offset],
                             bPtr = &b.Array[b.Offset])
                {
                    return UnsafeUtility.MemCmp(aPtr, bPtr, a.Count) == 0;
                }
            }
            return false;
        }

        // disconnect this connection
        public void Disconnect()
        {
            // do nothing if already disconnected
            if (state == KcpState.Disconnected) return;

            // set as Disconnected BEFORE calling OnDisconnected event.
            // this avoids potential deadlocks like this Mirror bug:
            // https://github.com/vis2k/Mirror/issues/2357
            // where OnDisconnected->Mirror.OnDisconnected->ServerDisconnect->
            //       Connection.Disconnect->Kcp.Disconnect->OnDisconnected->
            //       DEADLOCK
            state = KcpState.Disconnected;

            // send a disconnect message and disconnect
            if (socket.Connected)
            {
                try
                {
                    Send(Goodbye);
                    kcp.Flush();

                    // call OnDisconnected event, even if we manually
                    // disconnected
                    OnDisconnected?.Invoke();
                }
                catch (SocketException)
                {
                    // this is ok,  the connection was already closed
                }
                catch (ObjectDisposedException)
                {
                    // this is normal when we stop the server
                    // the socket is stopped so we can't send anything anymore
                    // to the clients

                    // the clients will eventually timeout and realize they
                    // were disconnected
                }
            }
        }

        // get remote endpoint
        public EndPoint GetRemoteEndPoint() => remoteEndpoint;
    }
}
