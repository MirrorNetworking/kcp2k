using NUnit.Framework;

namespace kcp2k.Tests
{
    class MockConnection : KcpConnection
    {
        public MockConnection(bool noDelay, uint interval = Kcp.INTERVAL, int fastResend = 0, bool congestionWindow = true, uint sendWindowSize = Kcp.WND_SND, uint receiveWindowSize = Kcp.WND_RCV)
        {
            SetupKcp(noDelay, interval, fastResend, congestionWindow, sendWindowSize, receiveWindowSize);
        }
        protected override void RawSend(byte[] data, int length) {}
    }

    public class KcpConnectionTests
    {
        [Test]
        public void MaxSendRate()
        {
            //   WND(32) * MTU(1200) = 38,400 bytes
            //   => 38,400 * 1000 / INTERVAL(10) = 3,840,000 bytes/s = 3750 KB/s
            KcpConnection connection = new MockConnection(true, 10, 0, true, 32, 64);
            Assert.That(connection.MaxSendRate, Is.EqualTo(3_840_000));
        }

        [Test]
        public void MaxReceiveRate()
        {
            // note: WND needs to be >= max fragment size which is 128!
            //   WND(128) * MTU(1200) = 153,600 bytes
            //   => 153,600 * 1000 / INTERVAL(10) = 15,360,000 bytes/s = 15,000 KB/s = 14.6 MB/s
            KcpConnection connection = new MockConnection(true, 10, 0, true, 32, 128);
            Assert.That(connection.MaxReceiveRate, Is.EqualTo(15_360_000));
        }
    }
}