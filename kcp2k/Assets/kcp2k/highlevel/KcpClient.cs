// kcp client logic abstracted into a class.
// for use in Mirror, DOTSNET, testing, etc.
using System;
using System.Net;
using System.Net.Sockets;

namespace kcp2k
{
    public class KcpClient
    {
        // kcp
        internal KcpPeer peer;

        // IO
        protected Socket socket;
        public EndPoint remoteEndPoint;

        // IMPORTANT: raw receive buffer always needs to be of 'MTU' size, even
        //            if MaxMessageSize is larger. kcp always sends in MTU
        //            segments and having a buffer smaller than MTU would
        //            silently drop excess data.
        //            => we need the MTU to fit channel + message!
        readonly byte[] rawReceiveBuffer = new byte[Kcp.MTU_DEF];

        // events
        public Action OnConnected;
        public Action<ArraySegment<byte>, KcpChannel> OnData;
        public Action OnDisconnected;
        // error callback instead of logging.
        // allows libraries to show popups etc.
        // (string instead of Exception for ease of use and to avoid user panic)
        public Action<ErrorCode, string> OnError;

        // state
        public bool connected;

        public KcpClient(Action OnConnected,
                         Action<ArraySegment<byte>,
                         KcpChannel> OnData,
                         Action OnDisconnected,
                         Action<ErrorCode, string> OnError)
        {
            this.OnConnected = OnConnected;
            this.OnData = OnData;
            this.OnDisconnected = OnDisconnected;
            this.OnError = OnError;
        }

        public void Connect(string address,
                            ushort port,
                            bool noDelay,
                            uint interval,
                            int fastResend = 0,
                            bool congestionWindow = true,
                            uint sendWindowSize = Kcp.WND_SND,
                            uint receiveWindowSize = Kcp.WND_RCV,
                            int timeout = KcpPeer.DEFAULT_TIMEOUT,
                            uint maxRetransmits = Kcp.DEADLINK,
                            bool maximizeSendReceiveBuffersToOSLimit = false)
        {
            if (connected)
            {
                Log.Warning("KCP: client already connected!");
                return;
            }

            // create fresh peer for each new session
            peer = new KcpPeer();

            // setup events
            peer.OnAuthenticated = () =>
            {
                Log.Info($"KCP: OnClientConnected");
                connected = true;
                OnConnected();
            };
            peer.OnData = (message, channel) =>
            {
                //Log.Debug($"KCP: OnClientData({BitConverter.ToString(message.Array, message.Offset, message.Count)})");
                OnData(message, channel);
            };
            peer.OnDisconnected = () =>
            {
                Log.Info($"KCP: OnClientDisconnected");
                connected = false;
                peer = null;
                socket = null;
                remoteEndPoint = null;
                OnDisconnected();
            };
            peer.OnError = (error, reason) =>
            {
                OnError(error, reason);
            };

            Log.Info($"KcpClient: connect to {address}:{port}");

            // try resolve host name
            if (!Common.ResolveHostname(address, out IPAddress[] addresses))
            {
                // pass error to user callback. no need to log it manually.
                peer.OnError(ErrorCode.DnsResolve, $"Failed to resolve host: {address}");
                peer.OnDisconnected();
                return;
            }

            // create remote endpoint
            remoteEndPoint = new IPEndPoint(addresses[0], port);

            // create socket
            socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            // configure buffer sizes:
            // if connections drop under heavy load, increase to OS limit.
            // if still not enough, increase the OS limit.
            if (maximizeSendReceiveBuffersToOSLimit)
            {
                Common.MaximizeSocketBuffers(socket);
            }
            // otherwise still log the defaults for info.
            else Log.Info($"KcpClient: RecvBuf = {socket.ReceiveBufferSize} SendBuf = {socket.SendBufferSize}. If connections drop under heavy load, enable {nameof(maximizeSendReceiveBuffersToOSLimit)} to increase it to OS limit. If they still drop, increase the OS limit.");

            // connect
            socket.Connect(remoteEndPoint);

            // set up kcp
            // TODO ctor
            peer.SetupKcp(RawSend, noDelay, interval, fastResend, congestionWindow, sendWindowSize, receiveWindowSize, timeout, maxRetransmits);

            // client should send handshake to server as very first message
            peer.SendHandshake();

            RawReceive();
        }

        // call from transport update
        public void RawReceive()
        {
            try
            {
                if (socket != null)
                {
                    while (socket.Poll(0, SelectMode.SelectRead))
                    {
                        // ReceiveFrom allocates.
                        // use Connect() to bind the UDP socket to the end point.
                        // then we can use Receive() instead.
                        // socket.ReceiveFrom(buffer, ref remoteEndPoint);
                        int msgLength = socket.Receive(rawReceiveBuffer);

                        // IMPORTANT: detect if buffer was too small for the
                        //            received msgLength. otherwise the excess
                        //            data would be silently lost.
                        //            (see ReceiveFrom documentation)
                        if (msgLength <= rawReceiveBuffer.Length)
                        {
                            //Log.Debug($"KCP: client raw recv {msgLength} bytes = {BitConverter.ToString(buffer, 0, msgLength)}");
                            peer.RawInput(rawReceiveBuffer, msgLength);
                        }
                        else
                        {
                            // pass error to user callback. no need to log it manually.
                            peer.OnError(ErrorCode.InvalidReceive, $"KCP ClientConnection: message of size {msgLength} does not fit into buffer of size {rawReceiveBuffer.Length}. The excess was silently dropped. Disconnecting.");
                            peer.Disconnect();
                        }
                    }
                }
            }
            // this is fine, the socket might have been closed in the other end
            catch (SocketException ex)
            {
                // the other end closing the connection is not an 'error'.
                // but connections should never just end silently.
                // at least log a message for easier debugging.
                Log.Info($"KCP ClientConnection: looks like the other end has closed the connection. This is fine: {ex}");
                peer.Disconnect();
            }
        }

        protected virtual void RawSend(ArraySegment<byte> data)
        {
            socket.Send(data.Array, data.Offset, data.Count, SocketFlags.None);
        }

        public void Send(ArraySegment<byte> segment, KcpChannel channel)
        {
            if (connected)
            {
                peer.SendData(segment, channel);
            }
            else Log.Warning("KCP: can't send because client not connected!");
        }

        public void Disconnect()
        {
            // only if connected
            // otherwise we end up in a deadlock because of an open Mirror bug:
            // https://github.com/vis2k/Mirror/issues/2353
            if (connected)
            {
                // call Disconnect and let the connection handle it.
                // DO NOT set it to null yet. it needs to be updated a few more
                // times first. let the connection handle it!
                peer?.Disconnect();
            }
        }

        // process incoming messages. should be called before updating the world.
        public void TickIncoming()
        {
            // recv on socket first, then process incoming
            // (even if we didn't receive anything. need to tick ping etc.)
            // (connection is null if not active)
            if (peer != null)
            {
                RawReceive();
                peer.TickIncoming();
            }
        }

        // process outgoing messages. should be called after updating the world.
        public void TickOutgoing()
        {
            // process outgoing
            // (connection is null if not active)
            peer?.TickOutgoing();
        }

        // process incoming and outgoing for convenience
        // => ideally call ProcessIncoming() before updating the world and
        //    ProcessOutgoing() after updating the world for minimum latency
        public void Tick()
        {
            TickIncoming();
            TickOutgoing();
        }

        // TODO call this
        protected void Dispose()
        {
            socket.Close();
            socket = null;
        }
    }
}
