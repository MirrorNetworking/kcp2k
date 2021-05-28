using NUnit.Framework;

namespace kcp2k.Tests
{
    public class SegmentTests
    {
        [Test]
        public void Encode()
        {
            // get a segment
            Segment seg = new Segment();

            // set some unique values
            seg.conv = 0x04030201;
            seg.cmd = 0x05;
            seg.frg = 0x06;
            seg.wnd = 0x0807;
            seg.ts = 0x0C0B0A09;
            seg.sn = 0x100F0E0D;
            seg.una = 0x14131211;

            // encode with offset
            byte[] data = new byte[100];
            int offset = 4;
            int encoded = seg.Encode(data, offset);
            Assert.That(encoded, Is.EqualTo(24));

            // compare every single byte to be 100% sure
            // => 20 bytes conv/cmd/frg/wnd/ts/sn/una
            Assert.That(data[offset + 0], Is.EqualTo(0x01));
            Assert.That(data[offset + 1], Is.EqualTo(0x02));
            Assert.That(data[offset + 2], Is.EqualTo(0x03));
            Assert.That(data[offset + 3], Is.EqualTo(0x04));
            Assert.That(data[offset + 4], Is.EqualTo(0x05));
            Assert.That(data[offset + 5], Is.EqualTo(0x06));
            Assert.That(data[offset + 6], Is.EqualTo(0x07));
            Assert.That(data[offset + 7], Is.EqualTo(0x08));
            Assert.That(data[offset + 8], Is.EqualTo(0x09));
            Assert.That(data[offset + 9], Is.EqualTo(0x0A));
            Assert.That(data[offset + 10], Is.EqualTo(0x0B));
            Assert.That(data[offset + 11], Is.EqualTo(0x0C));
            Assert.That(data[offset + 12], Is.EqualTo(0x0D));
            Assert.That(data[offset + 13], Is.EqualTo(0x0E));
            Assert.That(data[offset + 14], Is.EqualTo(0x0F));
            Assert.That(data[offset + 15], Is.EqualTo(0x10));
            Assert.That(data[offset + 16], Is.EqualTo(0x11));
            Assert.That(data[offset + 17], Is.EqualTo(0x12));
            Assert.That(data[offset + 18], Is.EqualTo(0x13));
            Assert.That(data[offset + 19], Is.EqualTo(0x14));
            // 4 bytes segment.buffer readable bytes (=0)
            Assert.That(data[offset + 20], Is.EqualTo(0x00));
            Assert.That(data[offset + 21], Is.EqualTo(0x00));
            Assert.That(data[offset + 22], Is.EqualTo(0x00));
            Assert.That(data[offset + 23], Is.EqualTo(0x00));
        }

        [Test]
        public void Reset()
        {
            // get a segment
            Segment seg = new Segment();

            // set some unique values
            seg.conv = 0x04030201;
            seg.cmd = 0x05;
            seg.frg = 0x06;
            seg.wnd = 0x0807;
            seg.ts = 0x0C0B0A09;
            seg.sn = 0x100F0E0D;
            seg.una = 0x14131211;

            // reset
            seg.Reset();
            Assert.That(seg.conv, Is.EqualTo(0));
            Assert.That(seg.cmd, Is.EqualTo(0));
            Assert.That(seg.frg, Is.EqualTo(0));
            Assert.That(seg.wnd, Is.EqualTo(0));
            Assert.That(seg.ts, Is.EqualTo(0));
            Assert.That(seg.sn, Is.EqualTo(0));
            Assert.That(seg.una, Is.EqualTo(0));
            Assert.That(seg.rto, Is.EqualTo(0));
            Assert.That(seg.xmit, Is.EqualTo(0));
            Assert.That(seg.resendts, Is.EqualTo(0));
            Assert.That(seg.fastack, Is.EqualTo(0));
            Assert.That(seg.data.Position, Is.EqualTo(0));
        }
    }
}