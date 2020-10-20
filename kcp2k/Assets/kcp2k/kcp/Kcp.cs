// Kcp based on https://github.com/skywind3000/kcp
// Kept as close to original as possible.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace kcp2k
{
    public class Kcp
    {
        // original Kcp has a define option, which is not defined by default:
        // #define FASTACK_CONSERVE

        public const int RTO_NDL = 30;           // no delay min rto
        public const int RTO_MIN = 100;          // normal min rto
        public const int RTO_DEF = 200;          // default RTO
        public const int RTO_MAX = 60000;        // maximum RTO
        public const int CMD_PUSH = 81;          // cmd: push data
        public const int CMD_ACK  = 82;          // cmd: ack
        public const int CMD_WASK = 83;          // cmd: window probe (ask)
        public const int CMD_WINS = 84;          // cmd: window size (tell)
        public const int ASK_SEND = 1;           // need to send CMD_WASK
        public const int ASK_TELL = 2;           // need to send CMD_WINS
        public const int WND_SND = 32;           // defualt send window
        public const int WND_RCV = 128;          // default receive window. must be >= max fragment size
        public const int MTU_DEF = 1200;         // default MTU (reduced to 1200 to fit all cases: https://en.wikipedia.org/wiki/Maximum_transmission_unit ; steam uses 1200 too!)
        public const int ACK_FAST = 3;
        public const int INTERVAL = 100;
        public const int OVERHEAD = 24;
        public const int DEADLINK = 20;
        public const int THRESH_INIT = 2;
        public const int THRESH_MIN = 2;
        public const int PROBE_INIT = 7000;      // 7 secs to probe window size
        public const int PROBE_LIMIT = 120000;   // up to 120 secs to probe window
        public const int FASTACK_LIMIT = 5; // max times to trigger fastack
        public const int SN_OFFSET = 12;         // max times to trigger fastack

        internal struct AckItem
        {
            internal uint serialNumber;
            internal uint timestamp;
        }

        // kcp members.
        int state;
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
        uint xmit;
        bool nodelay;
        bool updated;
        uint ts_probe;      // timestamp probe
        uint probe_wait;
        uint dead_link;
        uint incr;
        uint current;       // current time (milliseconds). set by Update.

        int fastresend;
        int fastlimit;
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
            fastlimit = FASTACK_LIMIT;
            dead_link = DEADLINK;
            buffer = new byte[(mtu + OVERHEAD) * 3];
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
        public int Send(byte[] buffer, int offset, int len)
        {
            int count;

            if (len < 0) return -1;

            // streaming mode: removed. we never want to send 'hello' and
            // receive 'he' 'll' 'o'. we want to always receive 'hello'.

            if (len <= mss) count = 1;
            else count = (int)((len + mss - 1) / mss);

            if (count >= WND_RCV) return -2;

            if (count == 0) count = 1;

            // fragment
            for (int i = 0; i < count; i++)
            {
                int size = len > (int)mss ? (int)mss : len;
                Segment seg = Segment.Take();

                if (len > 0)
                {
                    seg.data.WriteBytes(buffer, offset, size);
                }
                // seg.len = size: WriteBytes sets segment.Position!
                seg.frg = (byte)(count - i - 1);
                snd_queue.Add(seg);
                offset += size;
                len -= size;
            }

            return 0;
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
            int removed = 0;
            foreach (Segment seg in snd_buf)
            {
                if (Utils.TimeDiff(una, seg.sn) > 0)
                {
                    // can't remove while iterating. remember how many to remove
                    // and do it after the loop.
                    ++removed;
                    Segment.Return(seg);
                }
                else
                {
                    break;
                }
            }
            snd_buf.RemoveRange(0, removed);
        }

        // ikcp_parse_fastack
        void ParseFastack(uint sn, uint ts)
        {
            if (Utils.TimeDiff(sn, snd_una) < 0 || Utils.TimeDiff(sn, snd_nxt) >= 0)
                return;

            foreach (Segment seg in snd_buf)
            {
                if (Utils.TimeDiff(sn, seg.sn) < 0)
                {
                    break;
                }
                else if (sn != seg.sn && seg.ts <= ts)
                {
#if !FASTACK_CONSERVE
                    seg.fastack++;
# else
                    if (Utils.TimeDiff(ts, seg.ts) >= 0)
                        seg.fastack++;
#endif
                }
            }
        }

        // ikcp_ack_push
        // appends an ack.
        void AckPush(uint sn, uint ts)
        {
            acklist.Add(new AckItem{ serialNumber = sn, timestamp = ts });
        }

        // ikcp_parse_data
        void ParseData(Segment newseg)
        {
            uint sn = newseg.sn;

            if (Utils.TimeDiff(sn, rcv_nxt + rcv_wnd) >= 0 ||
                Utils.TimeDiff(sn, rcv_nxt) < 0)
            {
                // TODO native C deletes the segment. should we Return it to pool?
                return;
            }

            InsertSegmentInReceiveBuffer(newseg);
            MoveReceiveBufferDataToReceiveQueue();
        }

        void InsertSegmentInReceiveBuffer(Segment newseg)
        {
            uint sn = newseg.sn;
            bool repeat = false;

            // original C iterates backwards, so we need to do that as well.
            int n = rcv_buf.Count - 1;
            int insert_idx = 0;
            for (int i = n; i >= 0; i--)
            {
                Segment seg = rcv_buf[i];
                if (seg.sn == sn)
                {
                    repeat = true;
                    break;
                }
                if (Utils.TimeDiff(sn, seg.sn) > 0)
                {
                    insert_idx = i + 1; // TODO this is not in original C. and why +1?
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
            else
            {
                // TODO original C deletes the segment. should we Return it to pool?
            }
        }

        // move available data from rcv_buf -> rcv_queue
        void MoveReceiveBufferDataToReceiveQueue()
        {
            int removed = 0;
            foreach (Segment seg in rcv_buf)
            {
                if (seg.sn == rcv_nxt && rcv_queue.Count < rcv_wnd)
                {
                    // can't remove while iterating. remember how many to remove
                    // and do it after the loop.
                    ++removed;
                    rcv_queue.Add(seg);
                    rcv_nxt++;
                }
                else
                {
                    break;
                }
            }
            rcv_buf.RemoveRange(0, removed);
        }

        // ikcp_input
        /// used when you receive a low level packet (eg. UDP packet)
        public int Input(byte[] data, int index, int size, bool regular)
        {
            uint prev_una = snd_una;
            uint latest_ts = 0;
            int flag = 0;

            if (size < OVERHEAD) return -1;

            int offset = index;

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

                if (conv != conv_) return -1;

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
                    latest_ts = ts;
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
                if (current >= latest_ts)
                {
                    UpdateAck(Utils.TimeDiff(current, latest_ts));
                }
            }

            // cwnd update when packet arrived
            UpdateCwnd(prev_una);

            return 0;
        }

        void UpdateCwnd(uint prev_una)
        {
            if (!nocwnd && snd_una > prev_una && cwnd < rmt_wnd)
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
        // flush remain ack segments
        public void Flush()
        {
            int offset = 0;    // buffer ptr in original C
            bool lost = false; // lost segments

            // helper functions
            void MakeSpace(int space)
            {
                if (offset + space > mtu)
                {
                    output(buffer, offset);
                    offset = 0;
                }
            }

            void FlushBuffer()
            {
                if (offset > 0)
                {
                    output(buffer, offset);
                }
            }

            // 'ikcp_update' haven't been called.
            if (!updated) return;

            Segment seg = Segment.Take();
            seg.conv = conv;
            seg.cmd = CMD_ACK;
            seg.wnd = WndUnused();
            seg.una = rcv_nxt;

            // flush acknowledges
            foreach (AckItem ack in acklist)
            {
                MakeSpace(OVERHEAD);
                // ikcp_ack_get assigns ack[i] to seg.sn, seg.ts
                seg.sn = ack.serialNumber;
                seg.ts = ack.timestamp;
                offset += seg.Encode(buffer, offset);
            }

            acklist.Clear();

            // probe window size (if remote window size equals zero)
            if (rmt_wnd == 0)
            {
                if (probe_wait == 0)
                {
                    probe_wait = PROBE_INIT;
                    ts_probe = current + probe_wait;
                }
                else
                {
                    if (Utils.TimeDiff(current, ts_probe) >= 0)
                    {
                        if (probe_wait < PROBE_INIT)
                            probe_wait = PROBE_INIT;
                        probe_wait += probe_wait / 2;
                        if (probe_wait > PROBE_LIMIT)
                            probe_wait = PROBE_LIMIT;
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
                MakeSpace(OVERHEAD);
                offset += seg.Encode(buffer, offset);
            }

            // flush window probing commands
            if ((probe & ASK_TELL) != 0)
            {
                seg.cmd = CMD_WINS;
                MakeSpace(OVERHEAD);
                offset += seg.Encode(buffer, offset);
            }

            probe = 0;

            // calculate window size
            uint cwnd_ = Math.Min(snd_wnd, rmt_wnd);
            if (!nocwnd) cwnd_ = Math.Min(cwnd, cwnd_);

            // move data from snd_queue to snd_buf
            // sliding window, controlled by snd_nxt && sna_una+cwnd
            // TODO convert to while timediff like original C when using queues!
            int removed = 0;
            for (int k = 0; k < snd_queue.Count; k++)
            {
                // TODO original C uses this check in while with < 0
                // instead we want to STOP when the opposite happens, so >= 0!
                if (Utils.TimeDiff(snd_nxt, snd_una + cwnd_) >= 0)
                    break;

                Segment newseg = snd_queue[k];
                // can't remove while iterating. remember how many to remove
                // and do it after the loop.
                removed++;

                newseg.conv = conv;
                newseg.cmd = CMD_PUSH;
                newseg.wnd = seg.wnd;
                newseg.ts = current;
                newseg.sn = snd_nxt++;
                newseg.una = rcv_nxt;
                newseg.resendts = current;
                newseg.rto = rx_rto;
                newseg.fastack = 0;
                newseg.xmit = 0;
                snd_buf.Add(newseg);
            }
            snd_queue.RemoveRange(0, removed);

            // calculate resent
            uint resent = fastresend > 0 ? (uint)fastresend : 0xffffffff;
            uint rtomin = nodelay == false ? (uint)rx_rto >> 3 : 0;

            // flush data segments
            int change = 0;
            foreach (Segment segment in snd_buf)
            {
                bool needsend = false;
                // initial transmit
                if (segment.xmit == 0)
                {
                    needsend = true;
                    segment.xmit++;
                    segment.rto = rx_rto;
                    segment.resendts = current + (uint)segment.rto + rtomin;
                }
                // RTO
                else if (Utils.TimeDiff(current, segment.resendts) >= 0)
                {
                    needsend = true;
                    segment.xmit++;
                    xmit++;
                    if (!nodelay)
                    {
                        segment.rto += Math.Max(segment.rto, rx_rto);
                    }
                    else
                    {
                        // original C has:
                        // int step = (nodelay < 2) ? ((int)(segment.rto)) : rx_rto;
                        // but nodelay is a bool and only ever 0 or 1, so use segment.rto
                        segment.rto += segment.rto / 2;
                    }
                    segment.resendts = current + (uint)segment.rto;
                    lost = true;
                }
                // fast retransmit
                else if (segment.fastack >= resent)
                {
                    if (segment.xmit <= fastlimit || fastlimit <= 0)
                    {
                        needsend = true;
                        segment.xmit++;
                        segment.fastack = 0;
                        segment.resendts = current + (uint)segment.rto;
                        change++;
                    }
                }

                if (needsend)
                {
                    segment.ts = current;
                    segment.wnd = seg.wnd;
                    segment.una = rcv_nxt;

                    int need = OVERHEAD + segment.data.Position;
                    MakeSpace(need);

                    offset += segment.Encode(buffer, offset);

                    if (segment.data.Position > 0)
                    {
                        Buffer.BlockCopy(segment.data.RawBuffer, 0, buffer, offset, segment.data.Position);
                        offset += segment.data.Position;
                    }

                    if (segment.xmit >= dead_link)
                    {
                        state = -1;
                    }
                }
            }

            // flash remain segments
            FlushBuffer();

            // update ssthresh
            // rate halving, https://tools.ietf.org/html/rfc6937
            if (change > 0)
            {
                uint inflight = snd_nxt - snd_una;
                ssthresh = inflight / 2;
                if (ssthresh < THRESH_MIN)
                    ssthresh = THRESH_MIN;
                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }

            // congestion control, https://tools.ietf.org/html/rfc5681
            if (lost)
            {
                // original C uses 'cwnd', not kcp->cwnd!
                ssthresh = cwnd_ / 2;
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
        // update state (call it repeatedly, every 10ms-100ms), or you can ask
        // Check() when to call it again (without Input/Send calling).
        //
        // 'current' - current timestamp in millisec. pass it to Kcp so that
        // Kcp doesn't have to do any stopwatch/deltaTime/etc. code
        public void Update(uint currentTimeMilliSeconds)
        {
            current = currentTimeMilliSeconds;

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
                if (Utils.TimeDiff(current, ts_flush) >= 0)
                {
                    ts_flush = current + interval;
                }
                Flush();
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
            uint ts_flush_ = ts_flush;
            int tm_flush = 0x7fffffff;
            int tm_packet = 0x7fffffff;

            if (!updated)
            {
                // original C returns current. we return delta = 0.
                return 0;
            }

            if (Utils.TimeDiff(current, ts_flush_) >= 10000 ||
                Utils.TimeDiff(current, ts_flush_) < -10000)
            {
                ts_flush_ = current;
            }

            if (Utils.TimeDiff(current, ts_flush_) >= 0)
            {
                // original C returns current. we return delta = 0.
                return 0;
            }

            tm_flush = Utils.TimeDiff(ts_flush_, current);

            foreach (Segment seg in snd_buf)
            {
                int diff = Utils.TimeDiff(seg.resendts, current);
                if (diff <= 0)
                {
                    // original C returns current. we return delta = 0.
                    return 0;
                }
                if (diff < tm_packet) tm_packet = diff;
            }

            int minimal = tm_packet < tm_flush ? tm_packet : tm_flush;
            if (minimal >= interval) minimal = (int)interval;

            // original C returns current + minimal. we return delta = minimal.
            return minimal;
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

        // ikcp_interval
        public void SetInterval(uint interval)
        {
            if (interval > 5000) interval = 5000;
            else if (interval < 10) interval = 10;
            this.interval = interval;
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
