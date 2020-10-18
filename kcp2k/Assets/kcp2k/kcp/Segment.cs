using System.Collections.Generic;

namespace kcp2k
{
    // KCP Segment Definition
    internal class Segment
    {
        internal uint conv;
        internal uint cmd;
        internal uint frg;
        internal uint wnd;
        internal uint ts;
        internal uint sn;
        internal uint una;
        internal int rto;
        internal uint xmit;
        internal uint resendts;
        internal uint fastack;
        internal bool acked;
        internal ByteBuffer data;

        // pool ////////////////////////////////////////////////////////////////
        internal static readonly Stack<Segment> Pool = new Stack<Segment>(32);

        public static Segment Take()
        {
            if (Pool.Count > 0)
            {
                Segment seg = Pool.Pop();
                return seg;
            }
            return new Segment();
        }

        public static void Return(Segment seg)
        {
            seg.Reset();
            Pool.Push(seg);
        }
        ////////////////////////////////////////////////////////////////////////

        Segment()
        {
            // allocate the ByteBuffer once.
            // note that we don't need to pool ByteBuffer, because Segment is
            // already pooled.
            data = new ByteBuffer();
        }

        // encode a segment into buffer
        internal int Encode(byte[] ptr, int offset)
        {
            int offset_ = offset;
            offset += Utils.Encode32U(ptr, offset, conv);
            offset += Utils.Encode8u(ptr, offset, (byte)cmd);
            offset += Utils.Encode8u(ptr, offset, (byte)frg);
            offset += Utils.Encode16U(ptr, offset, (ushort)wnd);
            offset += Utils.Encode32U(ptr, offset, ts);
            offset += Utils.Encode32U(ptr, offset, sn);
            offset += Utils.Encode32U(ptr, offset, una);
            offset += Utils.Encode32U(ptr, offset, (uint)data.Position);

            return offset - offset_;
        }

        // reset to return a fresh segment to the pool
        internal void Reset()
        {
            conv = 0;
            cmd = 0;
            frg = 0;
            wnd = 0;
            ts = 0;
            sn = 0;
            una = 0;
            rto = 0;
            xmit = 0;
            resendts = 0;
            fastack = 0;
            acked = false;

            // keep buffer for next pool usage, but reset position
            data.Position = 0;
        }
    }
}
