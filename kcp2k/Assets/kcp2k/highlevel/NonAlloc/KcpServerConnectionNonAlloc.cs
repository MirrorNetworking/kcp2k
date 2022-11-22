// where-allocation version of KcpServerConnection.
// may not be wanted on all platforms, so it's an extra optional class.
using System;
using System.Net;
using System.Net.Sockets;
using WhereAllocation;

namespace kcp2k
{
    public class KcpServerConnectionNonAlloc : KcpServerConnection
    {
        readonly IPEndPointNonAlloc reusableSendEndPoint;

        public KcpServerConnectionNonAlloc(Socket socket, EndPoint remoteEndpoint, IPEndPointNonAlloc reusableSendEndPoint, bool noDelay, uint interval = Kcp.INTERVAL, int fastResend = 0, bool congestionWindow = true, uint sendWindowSize = Kcp.WND_SND, uint receiveWindowSize = Kcp.WND_RCV, int timeout = KcpConnection.DEFAULT_TIMEOUT, uint maxRetransmits = Kcp.DEADLINK)
            : base(socket, remoteEndpoint, noDelay, interval, fastResend, congestionWindow, sendWindowSize, receiveWindowSize, timeout, maxRetransmits)
        {
            this.reusableSendEndPoint = reusableSendEndPoint;
        }

        protected override void RawSend(ArraySegment<byte> data)
        {
            // where-allocation nonalloc send
            socket.SendTo_NonAlloc(data.Array, data.Offset, data.Count, SocketFlags.None, reusableSendEndPoint);
        }
    }
}