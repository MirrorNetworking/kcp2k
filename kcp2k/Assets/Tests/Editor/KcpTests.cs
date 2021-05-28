// test some of the internal Kcp.cs functions to guarantee stability.
using NUnit.Framework;

namespace kcp2k.Tests
{
    public class KcpTests
    {
        [Test]
        public void InsertSegmentInReceiveBuffer()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert '1' should insert into empty buffer
            Segment one = new Segment{sn=1};
            kcp.InsertSegmentInReceiveBuffer(one);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(1));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(one));

            // insert '3' should insert after '1'
            Segment three = new Segment{sn=3};
            kcp.InsertSegmentInReceiveBuffer(three);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(three));

            // insert '2' should insert before '3'
            Segment two = new Segment{sn=2};
            kcp.InsertSegmentInReceiveBuffer(two);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(two));
            Assert.That(kcp.rcv_buf[2], Is.EqualTo(three));

            // insert '0' should insert before '1'
            Segment zero = new Segment{sn=0};
            kcp.InsertSegmentInReceiveBuffer(zero);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(4));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(zero));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[2], Is.EqualTo(two));
            Assert.That(kcp.rcv_buf[3], Is.EqualTo(three));

            // insert '2' again should do nothing because duplicate
            Segment two_again = new Segment{sn=2};
            kcp.InsertSegmentInReceiveBuffer(two_again);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(4));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(zero));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[2], Is.EqualTo(two));
            Assert.That(kcp.rcv_buf[3], Is.EqualTo(three));
        }

        [Test]
        public void ParseAckFirst()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt
            kcp.snd_nxt = 999;

            // parse ack with sn == 3, should remove the last segment
            kcp.ParseAck(1);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(two));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(three));
        }

        [Test]
        public void ParseAckMiddle()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt
            kcp.snd_nxt = 999;

            // parse ack with sn == 2, should remove the middle segment
            kcp.ParseAck(2);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(one));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(three));
        }

        [Test]
        public void ParseAckLast()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt
            kcp.snd_nxt = 999;

            // parse ack with sn == 3, should remove the last segment
            kcp.ParseAck(3);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(one));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(two));
        }

        [Test]
        public void ParseAckSndNxtSmaller()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt.
            // it should do nothing if snd_nxt is <= sn
            kcp.snd_nxt = 1;

            // parse ack with sn == 3, should remove the last segment
            kcp.ParseAck(1);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(one));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(two));
            Assert.That(kcp.snd_buf[2], Is.EqualTo(three));
        }

        [Test]
        public void WaitSnd()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // add some to send buffer and send queue
            kcp.snd_buf.Add(new Segment());
            kcp.snd_buf.Add(new Segment());
            kcp.snd_queue.Enqueue(new Segment());

            // WaitSnd should be send buffer + queue
            Assert.That(kcp.WaitSnd, Is.EqualTo(3));
        }

        [Test]
        public void ShrinkBufFilledSendBuffer()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // add some to send buffer and send queue
            kcp.snd_buf.Add(new Segment{sn=2});
            kcp.snd_buf.Add(new Segment{sn=3});

            // ShrinkBuf should set snd_una to first send buffer element's 'sn'
            kcp.ShrinkBuf();
            Assert.That(kcp.snd_una, Is.EqualTo(2));
        }

        [Test]
        public void ShrinkBufEmptySendBuffer()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // ShrinkBuf with empty send buffer should set snd_una to snd_nxt
            kcp.snd_nxt = 42;
            kcp.ShrinkBuf();
            Assert.That(kcp.snd_una, Is.EqualTo(42));
        }

        [Test]
        public void SetIntervalTooSmall()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // setting a too small interval should limit it to 10
            kcp.SetInterval(0);
            Assert.That(kcp.interval, Is.EqualTo(10));
        }

        [Test]
        public void SetInterval()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // set interval
            kcp.SetInterval(2500);
            Assert.That(kcp.interval, Is.EqualTo(2500));
        }

        [Test]
        public void SetIntervalTooBig()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // setting a too big interval should limit it to 5000
            kcp.SetInterval(9999);
            Assert.That(kcp.interval, Is.EqualTo(5000));
        }

        [Test]
        public void SetWindowSize()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // set an allowed window size
            kcp.SetWindowSize(42, 512);
            Assert.That(kcp.snd_wnd, Is.EqualTo(42));
            Assert.That(kcp.rcv_wnd, Is.EqualTo(512));
        }

        [Test]
        public void SetWindowSizeWithTooSmallReceiveWindow()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // set window size with receive window < max fragment size (WND_RCV)
            kcp.SetWindowSize(42, Kcp.WND_RCV - 1);
            Assert.That(kcp.snd_wnd, Is.EqualTo(42));
            Assert.That(kcp.rcv_wnd, Is.EqualTo(Kcp.WND_RCV));
        }

        /*[Test]
        public void SetMtu()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // double MTU
            uint mtu = kcp.mtu * 2;
            kcp.SetMtu(mtu);
            Assert.That(kcp.mtu, Is.EqualTo(mtu));
            Assert.That(kcp.buffer.Length, Is.GreaterThanOrEqualTo(mtu));
        }*/

        [Test]
        public void Check()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // update at time = 1
            kcp.Update(1);

            // check at time = 2
            uint next = kcp.Check(2);

            // check returns 'ts_flush + interval', or in other words,
            // 'interval' seconds after UPDATE was called. so 1+100 = 101.
            Assert.That(next, Is.EqualTo(1 + kcp.interval));
        }
    }
}