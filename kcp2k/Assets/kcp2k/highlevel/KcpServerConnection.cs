// server needs to store a separate KcpPeer for each connection.
// as well as remoteEndPoint so we know where to send data to.
using System;
using System.Net;

namespace kcp2k
{
    public class KcpServerConnection
    {
        // kcp
        internal readonly KcpPeer peer;

        // IO
        public EndPoint remoteEndPoint;

        // Constructor can be overwritten for where-allocation:
        // https://github.com/vis2k/where-allocation
        //
        // RawSend may require (byte[], remoteEndPoint).
        // in that case, simply pass a wrapped function.
        public KcpServerConnection(Action<ArraySegment<byte>> RawSend, EndPoint remoteEndPoint, bool noDelay, uint interval = Kcp.INTERVAL, int fastResend = 0, bool congestionWindow = true, uint sendWindowSize = Kcp.WND_SND, uint receiveWindowSize = Kcp.WND_RCV, int timeout = KcpPeer.DEFAULT_TIMEOUT, uint maxRetransmits = Kcp.DEADLINK)
        {
            this.remoteEndPoint = remoteEndPoint;
            peer = new KcpPeer(RawSend, noDelay, interval, fastResend, congestionWindow, sendWindowSize, receiveWindowSize, timeout, maxRetransmits);
        }
    }
}
