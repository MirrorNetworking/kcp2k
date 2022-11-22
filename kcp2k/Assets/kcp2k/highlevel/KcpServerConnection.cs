using System;
using System.Net;
using System.Net.Sockets;

namespace kcp2k
{
    public class KcpServerConnection
    {
        // kcp
        internal KcpConnection peer = new KcpConnection();

        // IO
        protected Socket socket;
        protected EndPoint remoteEndPoint;
        public EndPoint GetRemoteEndPoint() => remoteEndPoint;

        // Constructor & Send functions can be overwritten for where-allocation:
        // https://github.com/vis2k/where-allocation
        public KcpServerConnection(Socket socket, EndPoint remoteEndPoint, bool noDelay, uint interval = Kcp.INTERVAL, int fastResend = 0, bool congestionWindow = true, uint sendWindowSize = Kcp.WND_SND, uint receiveWindowSize = Kcp.WND_RCV, int timeout = KcpConnection.DEFAULT_TIMEOUT, uint maxRetransmits = Kcp.DEADLINK)
        {
            this.socket = socket;
            this.remoteEndPoint = remoteEndPoint;
            peer.SetupKcp(RawSend, noDelay, interval, fastResend, congestionWindow, sendWindowSize, receiveWindowSize, timeout, maxRetransmits);
        }

        protected virtual void RawSend(ArraySegment<byte> data)
        {
            socket.SendTo(data.Array, data.Offset, data.Count, SocketFlags.None, remoteEndPoint);
        }
    }
}
