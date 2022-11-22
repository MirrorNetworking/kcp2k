using System;
using System.Net;
using System.Net.Sockets;

namespace kcp2k
{
    public class KcpClientConnection : KcpConnection
    {
        // IO
        protected Socket socket;
        protected EndPoint remoteEndPoint;
        public EndPoint GetRemoteEndPoint() => remoteEndPoint;

        // IMPORTANT: raw receive buffer always needs to be of 'MTU' size, even
        //            if MaxMessageSize is larger. kcp always sends in MTU
        //            segments and having a buffer smaller than MTU would
        //            silently drop excess data.
        //            => we need the MTU to fit channel + message!
        readonly byte[] rawReceiveBuffer = new byte[Kcp.MTU_DEF];

        // EndPoint & Receive functions can be overwritten for where-allocation:
        // https://github.com/vis2k/where-allocation
        // NOTE: Client's SendTo doesn't allocate, don't need a virtual.
        protected virtual void CreateRemoteEndPoint(IPAddress[] addresses, ushort port) =>
            remoteEndPoint = new IPEndPoint(addresses[0], port);

        public void Connect(string host,
                            ushort port,
                            bool noDelay,
                            uint interval = Kcp.INTERVAL,
                            int fastResend = 0,
                            bool congestionWindow = true,
                            uint sendWindowSize = Kcp.WND_SND,
                            uint receiveWindowSize = Kcp.WND_RCV,
                            int timeout = DEFAULT_TIMEOUT,
                            uint maxRetransmits = Kcp.DEADLINK,
                            bool maximizeSendReceiveBuffersToOSLimit = false)
        {
            Log.Info($"KcpClient: connect to {host}:{port}");

            // try resolve host name
            if (Common.ResolveHostname(host, out IPAddress[] addresses))
            {
                // create remote endpoint
                CreateRemoteEndPoint(addresses, port);

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
                SetupKcp(RawSend, noDelay, interval, fastResend, congestionWindow, sendWindowSize, receiveWindowSize, timeout, maxRetransmits);

                // client should send handshake to server as very first message
                SendHandshake();

                RawReceive();
            }
            // otherwise call OnDisconnected to let the user know.
            else
            {
                // pass error to user callback. no need to log it manually.
                OnError(ErrorCode.DnsResolve, $"Failed to resolve host: {host}");
                OnDisconnected();
            }
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
                            RawInput(rawReceiveBuffer, msgLength);
                        }
                        else
                        {
                            // pass error to user callback. no need to log it manually.
                            OnError(ErrorCode.InvalidReceive, $"KCP ClientConnection: message of size {msgLength} does not fit into buffer of size {rawReceiveBuffer.Length}. The excess was silently dropped. Disconnecting.");
                            Disconnect();
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
                Disconnect();
            }
        }

        protected void Dispose()
        {
            socket.Close();
            socket = null;
        }

        protected virtual void RawSend(ArraySegment<byte> data)
        {
            socket.Send(data.Array, data.Offset, data.Count, SocketFlags.None);
        }
    }
}
