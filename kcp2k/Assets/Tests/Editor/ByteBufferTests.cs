using NUnit.Framework;
using kcp2k;

public class ByteBufferTests
{
    ByteBuffer buffer;

    [SetUp]
    public void SetUp()
    {
        buffer = new ByteBuffer(1024);
    }

    [TearDown]
    public void TearDown()
    {
        buffer.Dispose();

        // dispose still adds to pool :(( clean up.
        ByteBuffer.pool.Clear();
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
        Assert.That(buffer.writeIndex, Is.EqualTo(2));
    }
}
