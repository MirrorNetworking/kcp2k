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
    }

    [Test]
    public void CreateAndDispose()
    {
        // just run setup & teardown
    }
}
