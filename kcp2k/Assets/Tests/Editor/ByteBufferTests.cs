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