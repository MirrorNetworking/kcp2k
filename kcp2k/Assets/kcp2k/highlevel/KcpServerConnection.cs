using System.Net;
using System.Net.Sockets;

namespace kcp2k
{
    public class KcpServerConnection : KcpConnection
    {
        public KcpServerConnection(Socket socket, EndPoint remoteEndpoint, bool noDelay)
        {
            this.socket = socket;
            this.remoteEndpoint = remoteEndpoint;
            SetupKcp(noDelay);
        }

        protected override void RawSend(byte[] data, int length)
        {
            socket.SendTo(data, 0, length, SocketFlags.None, remoteEndpoint);
        }
    }
}
