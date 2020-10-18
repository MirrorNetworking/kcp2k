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
    }
}