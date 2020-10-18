using NUnit.Framework;

namespace kcp2k.Tests
{
    public class ByteBufferTests
    {
        ByteBuffer buffer;

        [SetUp]
        public void SetUp()
        {
            buffer = new ByteBuffer();
        }

        [Test]
        public void CreateAndDispose()
        {
            // just run setup & teardown
        }

        [Test]
        public void WriteBytes()
        {
            byte[] bytes = {0xAA, 0xBB, 0xCC, 0xDD};
            buffer.WriteBytes(bytes, 2, 2);
            Assert.That(buffer.Position, Is.EqualTo(2));
            Assert.That(buffer.RawBuffer[0], Is.EqualTo(0xCC));
            Assert.That(buffer.RawBuffer[1], Is.EqualTo(0xDD));
        }

        // need to make sure that multiple writes to same buffer still work fine
        [Test]
        public void WriteBytesTwice()
        {
            // first half
            byte[] bytes = {0xAA, 0xBB};
            buffer.WriteBytes(bytes, 0, 2);
            Assert.That(buffer.Position, Is.EqualTo(2));
            Assert.That(buffer.RawBuffer[0], Is.EqualTo(0xAA));
            Assert.That(buffer.RawBuffer[1], Is.EqualTo(0xBB));

            // second half
            byte[] bytes2 = {0xCC, 0xDD};
            buffer.WriteBytes(bytes2, 0, 2);
            Assert.That(buffer.Position, Is.EqualTo(4));
            Assert.That(buffer.RawBuffer[0], Is.EqualTo(0xAA));
            Assert.That(buffer.RawBuffer[1], Is.EqualTo(0xBB));
            Assert.That(buffer.RawBuffer[2], Is.EqualTo(0xCC));
            Assert.That(buffer.RawBuffer[3], Is.EqualTo(0xDD));
        }

        // writing more than initial capacity should resize automatically
        [Test]
        public void WriteBytesWithResize()
        {
            // create array with unique values
            byte[] bytes = new byte[ByteBuffer.InitialCapacity * 2];
            for (int i = 0; i < bytes.Length; ++i)
                bytes[i] = (byte)(i & 0xFF);

            // write
            buffer.WriteBytes(bytes, 0, bytes.Length);
            Assert.That(buffer.Position, Is.EqualTo(bytes.Length));

            // compare
            for (int i = 0; i < bytes.Length; ++i)
                Assert.That(bytes[i], Is.EqualTo(buffer.RawBuffer[i]));
        }
    }
}