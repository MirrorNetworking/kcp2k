// Kcp based on https://github.com/skywind3000/kcp
// Kept as close to original as possible.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace kcp2k
{
    public class Kcp
    {
        public const int RTO_NDL = 30;         // no delay min rto
        public const int RTO_MIN = 100;        // normal min rto
        public const int RTO_DEF = 200;        // default RTO
        public const int RTO_MAX = 60000;      // maximum RTO
        public const int CMD_PUSH = 81;        // cmd: push data
        public const int CMD_ACK  = 82;        // cmd: ack
        public const int CMD_WASK = 83;        // cmd: window probe (ask)
        public const int CMD_WINS = 84;        // cmd: window size (tell)
        public const int ASK_SEND = 1;         // need to send CMD_WASK
        public const int ASK_TELL = 2;         // need to send CMD_WINS
        public const int WND_SND = 32;         // defualt send window
        public const int WND_RCV = 128;        // default receive window. must be >= max fragment size
        public const int MTU_DEF = 1200;       // default MTU (reduced to 1200 to fit all cases: https://en.wikipedia.org/wiki/Maximum_transmission_unit ; steam uses 1200 too!)
        public const int ACK_FAST = 3;
        public const int INTERVAL = 100;
        public const int OVERHEAD = 24;
        public const int DEADLINK = 20;
        public const int THRESH_INIT = 2;
        public const int THRESH_MIN = 2;
        public const int PROBE_INIT = 7000;    // 7 secs to probe window size
        public const int PROBE_LIMIT = 120000; // up to 120 secs to probe window
        public const int SN_OFFSET = 12;       // max times to trigger fastack

        internal struct AckItem
        {
            internal uint serialNumber;
            internal uint timestamp;
        }

        // kcp members.
        readonly uint conv; // conversation
        uint mtu;
        uint mss;           // maximum segment size
        uint snd_una;       // unacknowledged
        uint snd_nxt;
        uint rcv_nxt;
        uint ssthresh;      // slow start threshold
        int rx_rttval;
        int rx_srtt;        // smoothed round trip time
        int rx_rto;
        int rx_minrto;
        uint snd_wnd;       // send window
        uint rcv_wnd;       // receive window
        uint rmt_wnd;       // remote window
        uint cwnd;          // congestion window
        uint probe;
        uint interval;
        uint ts_flush;
        bool nodelay;
        bool updated;
        uint ts_probe;      // timestamp probe
        uint probe_wait;
        uint incr;

        int fastresend;
        bool nocwnd;
        internal readonly List<Segment> snd_queue = new List<Segment>(16); // send queue
        internal readonly List<Segment> rcv_queue = new List<Segment>(16); // receive queue
        internal readonly List<Segment> snd_buf = new List<Segment>(16);   // send buffer
        internal readonly List<Segment> rcv_buf = new List<Segment>(16);   // receive buffer
        internal readonly List<AckItem> acklist = new List<AckItem>(16);

        byte[] buffer;
        readonly Action<byte[], int> output; // buffer, size


        // get how many packet is waiting to be sent
        public int WaitSnd => snd_buf.Count + snd_queue.Count;

        // internal time.
        readonly Stopwatch refTime = new Stopwatch();
        public uint CurrentMS => (uint)refTime.ElapsedMilliseconds;

        // ikcp_create
        // create a new kcp control object, 'conv' must equal in two endpoint
        // from the same connection.
        public Kcp(uint conv, Action<byte[], int> output)
        {
            this.conv = conv;
            this.output = output;
            snd_wnd = WND_SND;
            rcv_wnd = WND_RCV;
            rmt_wnd = WND_RCV;
            mtu = MTU_DEF;
            mss = mtu - OVERHEAD;
            rx_rto = RTO_DEF;
            rx_minrto = RTO_MIN;
            interval = INTERVAL;
            ts_flush = INTERVAL;
            ssthresh = THRESH_INIT;
            buffer = new byte[(mtu + OVERHEAD) * 3];
            refTime.Start();
        }

        // ikcp_recv
        // receive data from kcp state machine
        //   returns number of bytes read.
        //   returns negative on error.
        // note: pass negative length to peek.
        public int Receive(byte[] buffer, int len)
        {
            bool ispeek = len < 0;

            if (rcv_queue.Count == 0)
                return -1;

            if (len < 0) len = -len;

            int peeksize = PeekSize();

            if (peeksize < 0)
                return -2;

            if (peeksize > len)
                return -3;

            bool recover = rcv_queue.Count >= rcv_wnd;

            // merge fragment.
            int offset = 0;
            len = 0;
            int removed = 0;
            foreach (Segment seg in rcv_queue)
            {
                Buffer.BlockCopy(seg.data.RawBuffer, 0, buffer, offset, seg.data.Position);
                offset += seg.data.Position;

                len += seg.data.Position;
                uint fragment = seg.frg;

                if (!ispeek)
                {
                    // can't remove while iterating. remember how many to remove
                    // and do it after the loop.
                    ++removed;
                    Segment.Return(seg);
                }

                if (fragment == 0)
                    break;
            }
            rcv_queue.RemoveRange(0, removed);

            // move available data from rcv_buf -> rcv_queue
            removed = 0;
            foreach (Segment seg in rcv_buf)
            {
                if (seg.sn == rcv_nxt && rcv_queue.Count < rcv_wnd)
                {
                    // can't remove while iterating. remember how many to remove
                    // and do it after the loop.
                    // note: don't return segment. we only add it to rcv_queue
                    ++removed;
                    // add
                    rcv_queue.Add(seg);
                    rcv_nxt++;
                }
                else
                {
                    break;
                }
            }
            rcv_buf.RemoveRange(0, removed);

            // fast recover
            if (rcv_queue.Count < rcv_wnd && recover)
            {
                // ready to send back CMD_WINS in flush
                // tell remote my window size
                probe |= ASK_TELL;
            }

            return len;
        }

        // ikcp_peeksize
        // check the size of next message in the recv queue
        public int PeekSize()
        {
            int length = 0;

            if (rcv_queue.Count == 0) return -1;

            Segment seq = rcv_queue[0];
            if (seq.frg == 0) return seq.data.Position;

            if (rcv_queue.Count < seq.frg + 1) return -1;

            foreach (Segment seg in rcv_queue)
            {
                length += seg.data.Position;
                if (seg.frg == 0) break;
            }

            return length;
        }

        // ikcp_send
        // sends byte[] to the other end.
        public void Send(byte[] buffer, int index, int length)
        {
            if (length == 0)
                throw new ArgumentException("You cannot send a packet with a length of 0.");

            int count;
            if (length <= mss)
                count = 1;
            else
                count = (int)((length + mss - 1) / mss);

            if (count > 255)
                throw new ArgumentException("Your packet is too big, please reduce its length or increase the MTU with SetMtu().");

            if (count == 0)
                count = 1;

            // fragment
            for (int i = 0; i < count; i++)
            {
                int size = Math.Min(length, (int)mss);

                Segment seg = Segment.Take();
                seg.data.WriteBytes(buffer, index, size);
                index += size;
                length -= size;

                seg.frg = (byte)(count - i - 1);
                snd_queue.Add(seg);
            }
        }

        // ikcp_update_ack
        void UpdateAck(int rtt) // round trip time
        {
            // https://tools.ietf.org/html/rfc6298
            if (rx_srtt == 0)
            {
                rx_srtt = rtt;
                rx_rttval = rtt / 2;
            }
            else
            {
                int delta = rtt - rx_srtt;
                if (delta < 0) delta = -delta;
                rx_rttval = (3 * rx_rttval + delta) / 4;
                rx_srtt = (7 * rx_srtt + rtt) / 8;
                if (rx_srtt < 1) rx_srtt = 1;
            }
            int rto = rx_srtt + Math.Max((int)interval, 4 * rx_rttval);
            rx_rto = Mathf.Clamp(rto, rx_minrto, RTO_MAX);
        }

        // ikcp_shrink_buf
        void ShrinkBuf()
        {
            if (snd_buf.Count > 0)
            {
                Segment seg = snd_buf[0];
                snd_una = seg.sn;
            }
            else
            {
                snd_una = snd_nxt;
            }
        }

        // ikcp_parse_ack
        void ParseAck(uint sn)
        {
            if (sn < snd_una || sn >= snd_nxt)
                return;

            foreach (Segment seg in snd_buf)
            {
                if (sn == seg.sn)
                {
                    // TODO this is different from native C
                    // mark and free space, but leave the segment here,
                    // and wait until `una` to delete this, then we don't
                    // have to shift the segments behind forward,
                    // which is an expensive operation for large window
                    seg.acked = true;
                    break;
                }
                if (sn < seg.sn)
                {
                    break;
                }
            }
        }

        // ikcp_parse_una
        void ParseUna(uint una)
        {
            int count = 0;
            foreach (Segment seg in snd_buf)
            {
                if (una > seg.sn)
                {
                    count++;
                    Segment.Return(seg);
                }
                else
                {
                    break;
                }
            }

            snd_buf.RemoveRange(0, count);
        }

        // ikcp_parse_fastack
        void ParseFastack(uint sn, uint ts)
        {
            if (sn < snd_una || sn >= snd_nxt)
                return;

            foreach (Segment seg in snd_buf)
            {
                if (sn < seg.sn)
                {
                    break;
                }
                else if (sn != seg.sn && seg.ts <= ts)
                {
                    seg.fastack++;
                }
            }
        }

        // ikcp_ack_push
        void AckPush(uint sn, uint ts)
        {
            acklist.Add(new AckItem { serialNumber = sn, timestamp = ts });
        }

        // ikcp_parse_data
        void ParseData(Segment newseg)
        {
            uint sn = newseg.sn;
            if (sn >= rcv_nxt + rcv_wnd || sn < rcv_nxt)
                return;

            InsertSegmentInReceiveBuffer(newseg);
            MoveToReceiveQueue();
        }

        private void InsertSegmentInReceiveBuffer(Segment newseg)
        {
            uint sn = newseg.sn;
            int n = rcv_buf.Count - 1;
            int insert_idx = 0;
            bool repeat = false;
            for (int i = n; i >= 0; i--)
            {
                Segment seg = rcv_buf[i];
                if (seg.sn == sn)
                {
                    repeat = true;
                    break;
                }

                if (sn > seg.sn)
                {
                    insert_idx = i + 1;
                    break;
                }
            }

            if (!repeat)
            {
                if (insert_idx == n + 1)
                    rcv_buf.Add(newseg);
                else
                    rcv_buf.Insert(insert_idx, newseg);
            }
        }

        // move available data from rcv_buf -> rcv_queue
        private void MoveToReceiveQueue()
        {
            int count = 0;
            foreach (Segment seg in rcv_buf)
            {
                if (seg.sn == rcv_nxt && rcv_queue.Count + count < rcv_wnd)
                {
                    rcv_nxt++;
                    count++;
                }
                else
                {
                    break;
                }
            }

            for (int i = 0; i < count; i++)
                rcv_queue.Add(rcv_buf[i]);
            rcv_buf.RemoveRange(0, count);
        }

        // ikcp_input
        /// <summary>Input
        /// <para>Used when you receive a low level packet (eg. UDP packet)</para></summary>
        /// <param name="data"></param>
        /// <param name="index"></param>
        /// <param name="size"></param>
        /// <param name="regular">regular indicates a regular packet has received(not from FEC)</param>
        /// <param name="ackNoDelay">will trigger immediate ACK, but surely it will not be efficient in bandwidth</param>
        public int Input(byte[] data, int index, int size, bool regular, bool ackNoDelay)
        {
            uint s_una = snd_una;
            if (size < OVERHEAD) return -1;

            int offset = index;
            uint latest = 0;
            int flag = 0;

            while (true)
            {
                uint ts = 0;
                uint sn = 0;
                uint length = 0;
                uint una = 0;
                uint conv_ = 0;

                ushort wnd = 0;
                byte cmd = 0;
                byte frg = 0;

                if (size - (offset - index) < OVERHEAD) break;

                offset += Utils.Decode32U(data, offset, ref conv_);

                if (conv != conv_)
                    return -1;

                offset += Utils.Decode8u(data, offset, ref cmd);
                offset += Utils.Decode8u(data, offset, ref frg);
                offset += Utils.Decode16U(data, offset, ref wnd);
                offset += Utils.Decode32U(data, offset, ref ts);
                offset += Utils.Decode32U(data, offset, ref sn);
                offset += Utils.Decode32U(data, offset, ref una);
                offset += Utils.Decode32U(data, offset, ref length);

                if (size - (offset - index) < length)
                    return -2;

                switch (cmd)
                {
                    case CMD_PUSH:
                    case CMD_ACK:
                    case CMD_WASK:
                    case CMD_WINS:
                        break;
                    default:
                        return -3;
                }

                // only trust window updates from regular packets. i.e: latest update
                if (regular)
                {
                    rmt_wnd = wnd;
                }

                ParseUna(una);
                ShrinkBuf();

                if (CMD_ACK == cmd)
                {
                    ParseAck(sn);
                    ParseFastack(sn, ts);
                    flag |= 1;
                    latest = ts;
                }
                else if (CMD_PUSH == cmd)
                {
                    if (sn < rcv_nxt + rcv_wnd)
                    {
                        AckPush(sn, ts);
                        if (sn >= rcv_nxt)
                        {
                            Segment seg = Segment.Take();
                            seg.conv = conv_;
                            seg.cmd = cmd;
                            seg.frg = frg;
                            seg.wnd = wnd;
                            seg.ts = ts;
                            seg.sn = sn;
                            seg.una = una;
                            seg.data.WriteBytes(data, offset, (int)length);
                            ParseData(seg);
                        }
                    }
                }
                else if (CMD_WASK == cmd)
                {
                    // ready to send back CMD_WINS in flush
                    // tell remote my window size
                    probe |= ASK_TELL;
                }
                else if (CMD_WINS == cmd)
                {
                    // do nothing
                }
                else
                {
                    return -3;
                }

                offset += (int)length;
            }

            // update rtt with the latest ts
            // ignore the FEC packet
            if (flag != 0 && regular)
            {
                uint current = CurrentMS;
                if (current >= latest)
                {
                    UpdateAck(Utils.TimeDiff(current, latest));
                }
            }

            // cwnd update when packet arrived
            UpdateCwnd(s_una);

            // ack immediately
            if (ackNoDelay && acklist.Count > 0)
            {
                Flush(true);
            }

            return 0;
        }

        void UpdateCwnd(uint s_una)
        {
            if (!nocwnd && snd_una > s_una && cwnd < rmt_wnd)
            {
                if (cwnd < ssthresh)
                {
                    cwnd++;
                    incr += mss;
                }
                else
                {
                    incr = Math.Max(incr, mss);

                    incr += mss * mss / incr + mss / 16;

                    if ((cwnd + 1) * mss <= incr)
                    {
                        cwnd = incr + mss - 1;

                        if (mss > 0)
                            cwnd /= mss;
                    }
                }
                if (cwnd > rmt_wnd)
                {
                    cwnd = rmt_wnd;
                    incr = rmt_wnd * mss;
                }
            }
        }

        // ikcp_wnd_unused
        uint WndUnused()
        {
            if (rcv_queue.Count < rcv_wnd)
                return rcv_wnd - (uint)rcv_queue.Count;
            return 0;
        }

        // ikcp_flush
        /// <summary>Flush</summary>
        /// <param name="ackOnly">flush remain ack segments</param>
        public uint Flush(bool ackOnly)
        {
            Segment seg = Segment.Take();
            seg.conv = conv;
            seg.cmd = CMD_ACK;
            seg.wnd = WndUnused();
            seg.una = rcv_nxt;

            int writeIndex = 0;

            void makeSpace(int space)
            {
                if (writeIndex + space > mtu)
                {
                    output(buffer, writeIndex);
                    writeIndex = 0;
                }
            }

            void flushBuffer()
            {
                if (writeIndex > 0)
                {
                    output(buffer, writeIndex);
                }
            }

            // flush acknowledges
            for (int i = 0; i < acklist.Count; i++)
            {
                makeSpace(OVERHEAD);
                AckItem ack = acklist[i];
                if (ack.serialNumber >= rcv_nxt || acklist.Count - 1 == i)
                {
                    seg.sn = ack.serialNumber;
                    seg.ts = ack.timestamp;
                    writeIndex += seg.Encode(buffer, writeIndex);
                }
            }
            acklist.Clear();

            // flush remain ack segments
            if (ackOnly)
            {
                flushBuffer();
                return interval;
            }

            uint current = 0;
            // probe window size (if remote window size equals zero)
            if (rmt_wnd == 0)
            {
                current = CurrentMS;
                if (probe_wait == 0)
                {
                    probe_wait = PROBE_INIT;
                    ts_probe = current + probe_wait;
                }
                else
                {
                    if (current >= ts_probe)
                    {
                        probe_wait = Math.Max(probe_wait, PROBE_INIT);
                        probe_wait += probe_wait / 2;
                        probe_wait = Math.Min(probe_wait, PROBE_LIMIT);
                        ts_probe = current + probe_wait;
                        probe |= ASK_SEND;
                    }
                }
            }
            else
            {
                ts_probe = 0;
                probe_wait = 0;
            }

            // flush window probing commands
            if ((probe & ASK_SEND) != 0)
            {
                seg.cmd = CMD_WASK;
                makeSpace(OVERHEAD);
                writeIndex += seg.Encode(buffer, writeIndex);
            }

            if ((probe & ASK_TELL) != 0)
            {
                seg.cmd = CMD_WINS;
                makeSpace(OVERHEAD);
                writeIndex += seg.Encode(buffer, writeIndex);
            }

            probe = 0;

            // calculate window size
            uint cwnd_ = Math.Min(snd_wnd, rmt_wnd);
            if (!nocwnd)
                cwnd_ = Math.Min(cwnd, cwnd_);

            // sliding window, controlled by snd_nxt && sna_una+cwnd
            int newSegsCount = 0;
            for (int k = 0; k < snd_queue.Count; k++)
            {
                if (snd_nxt >= snd_una + cwnd_)
                    break;

                Segment newseg = snd_queue[k];
                newseg.conv = conv;
                newseg.cmd = CMD_PUSH;
                newseg.sn = snd_nxt;
                snd_buf.Add(newseg);
                snd_nxt++;
                newSegsCount++;
            }

            snd_queue.RemoveRange(0, newSegsCount);

            // calculate resent
            uint resent = (uint)fastresend;
            if (fastresend <= 0) resent = 0xffffffff;

            // check for retransmissions
            current = CurrentMS;
            ulong change = 0; ulong lostSegs = 0;
            int minrto = (int)interval;

            for (int k = 0; k < snd_buf.Count; k++)
            {
                Segment segment = snd_buf[k];
                bool needSend = false;
                if (segment.acked)
                    continue;
                if (segment.xmit == 0)  // initial transmit
                {
                    needSend = true;
                    segment.rto = rx_rto;
                    segment.resendts = current + (uint)segment.rto; // TODO + rtomin in C???
                }
                else if (segment.fastack >= resent || segment.fastack > 0 && newSegsCount == 0 ) // fast retransmit
                {
                    needSend = true;
                    segment.fastack = 0;
                    segment.rto = rx_rto;
                    segment.resendts = current + (uint)segment.rto;
                    change++;
                }
                else if (current >= segment.resendts) // RTO
                {
                    needSend = true;
                    if (!nodelay)
                        segment.rto += rx_rto;
                    else
                        segment.rto += rx_rto / 2;
                    segment.fastack = 0;
                    segment.resendts = current + (uint)segment.rto;
                    lostSegs++;
                }

                if (needSend)
                {
                    current = CurrentMS;
                    segment.xmit++;
                    segment.ts = current;
                    segment.wnd = seg.wnd;
                    segment.una = seg.una;

                    int need = OVERHEAD + segment.data.Position;
                    makeSpace(need);
                    writeIndex += segment.Encode(buffer, writeIndex);
                    Buffer.BlockCopy(segment.data.RawBuffer, 0, buffer, writeIndex, segment.data.Position);
                    writeIndex += segment.data.Position;
                }

                // get the nearest rto
                int _rto = Utils.TimeDiff(segment.resendts, current);
                if (_rto > 0 && _rto < minrto)
                {
                    minrto = _rto;
                }
            }

            // flash remain segments
            flushBuffer();

            // cwnd update
            if (!nocwnd)
            {
                CwndUpdate(resent, change, lostSegs);
            }

            return (uint)minrto;
        }

        void CwndUpdate(uint resent, ulong change, ulong lostSegs)
        {
            // update ssthresh
            // rate halving, https://tools.ietf.org/html/rfc6937
            if (change > 0)
            {
                uint inflght = snd_nxt - snd_una;
                ssthresh = inflght / 2;
                if (ssthresh < THRESH_MIN)
                    ssthresh = THRESH_MIN;
                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }

            // congestion control, https://tools.ietf.org/html/rfc5681
            if (lostSegs > 0)
            {
                ssthresh = cwnd / 2;
                if (ssthresh < THRESH_MIN)
                    ssthresh = THRESH_MIN;
                cwnd = 1;
                incr = mss;
            }

            if (cwnd < 1)
            {
                cwnd = 1;
                incr = mss;
            }
        }

        // ikcp_update
        // update state (call it repeatedly, every 10ms-100ms)
        public void Update()
        {
            uint current = CurrentMS;

            if (!updated)
            {
                updated = true;
                ts_flush = current;
            }

            int slap = Utils.TimeDiff(current, ts_flush);

            if (slap >= 10000 || slap < -10000)
            {
                ts_flush = current;
                slap = 0;
            }

            if (slap >= 0)
            {
                ts_flush += interval;
                if (current >= ts_flush)
                    ts_flush = current + interval;
                Flush(false);
            }
        }

        // ikcp_check
        // Determine when should you invoke update
        // Returns when you should invoke update in millisec, if there is no
        // input/_send calling. you can call update in that time, instead of
        // call update repeatly.
        //
        // Important to reduce unnecessary update invoking. use it to schedule
        // update (eg. implementing an epoll-like mechanism, or optimize update
        // when handling massive kcp connections).
        //
        // NOTE: Standard KCP returns time as current + delta. This version
        //       returns delta
        public int Check()
        {
            uint current = CurrentMS;

            uint ts_flush_ = ts_flush;
            int tm_packet = 0x7fffffff;

            if (!updated)
                return 0;

            if (current >= ts_flush_ + 10000 || current < ts_flush_ - 10000)
                ts_flush_ = current;

            if (current >= ts_flush_)
                return 0;

            int tm_flush_ = Utils.TimeDiff(ts_flush_, current);

            foreach (Segment seg in snd_buf)
            {
                int diff = Utils.TimeDiff(seg.resendts, current);
                if (diff <= 0)
                    return 0;
                if (diff < tm_packet)
                    tm_packet = diff;
            }

            int minimal = tm_packet;
            if (tm_packet >= tm_flush_)
                minimal = tm_flush_;
            if (minimal >= interval)
                minimal = (int)interval;

            // NOTE: Original KCP returns current time + delta
            // I changed it to only return delta

            return  minimal;
        }

        // ikcp_setmtu
        // Change MTU (Maximum Transmission Unit) size.
        public void SetMtu(uint mtu)
        {
            if (mtu < 50 || mtu < OVERHEAD)
                throw new ArgumentException("MTU must be higher than 50 and higher than OVERHEAD");

            buffer = new byte[(mtu + OVERHEAD) * 3];
            this.mtu = mtu;
            mss = mtu - OVERHEAD;
        }

        // ikcp_nodelay
        //   Normal: false, 40, 0, 0
        //   Fast:   false, 30, 2, 1
        //   Fast2:   true, 20, 2, 1
        //   Fast3:   true, 10, 2, 1
        public void SetNoDelay(bool nodelay, uint interval = INTERVAL, int resend = 0, bool nocwnd = false)
        {
            this.nodelay = nodelay;
            if (nodelay)
            {
                rx_minrto = RTO_NDL;
            }
            else
            {
                rx_minrto = RTO_MIN;
            }

            if (interval >= 0)
            {
                if (interval > 5000) interval = 5000;
                else if (interval < 10) interval = 10;
                this.interval = interval;
            }

            if (resend >= 0)
            {
                fastresend = resend;
            }

            this.nocwnd = nocwnd;
        }

        // ikcp_wndsize
        public void SetWindowSize(uint sendWindow, uint receiveWindow)
        {
            if (sendWindow > 0)
            {
                snd_wnd = sendWindow;
            }

            if (receiveWindow > 0)
            {
                // must >= max fragment size
                rcv_wnd = Math.Max(receiveWindow, WND_RCV);
            }
        }
    }
}
