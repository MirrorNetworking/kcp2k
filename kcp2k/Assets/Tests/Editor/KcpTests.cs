// test some of the internal Kcp.cs functions to guarantee stability.
using NUnit.Framework;

namespace kcp2k.Tests
{
    public class KcpTests
    {
        [TearDown]
        public void TearDown()
        {
            // clear segment pool because we do things with segments
            Segment.Pool.Clear();
        }

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
    }
}