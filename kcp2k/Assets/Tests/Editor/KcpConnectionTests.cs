using NUnit.Framework;

namespace kcp2k.Tests
{
    class MockConnection : KcpConnection
    {
        public MockConnection(bool noDelay, uint interval = Kcp.INTERVAL, int fastResend = 0, bool congestionWindow = true, uint sendWindowSize = Kcp.WND_SND, uint receiveWindowSize = Kcp.WND_RCV, int timeout = DEFAULT_TIMEOUT)
        {
            SetupKcp(noDelay, interval, fastResend, congestionWindow, sendWindowSize, receiveWindowSize, timeout);
        }
        protected override void RawSend(byte[] data, int length) {}
    }

    public class KcpConnectionTests
    {
        [Test]
        public void MaxSendRate()
        {
            //   WND(32) * MTU(1199) = 38,368 bytes
            //   => 38,368 * 1000 / INTERVAL(10) = 3,836,800 bytes/s = 3746.8 KB/s
            KcpConnection connection = new MockConnection(true, 10, 0, true, 32, 64);
            Assert.That(connection.MaxSendRate, Is.EqualTo(3_836_800));
        }

        [Test]
        public void MaxReceiveRate()
        {
            // note: WND needs to be >= max fragment size which is 128!
            //   WND(128) * MTU(1199) = 153,472 bytes
            //   => 153,472 * 1000 / INTERVAL(10) = 15,347,200 bytes/s = 14,987.5 KB/s = 14.63 MB/s
            KcpConnection connection = new MockConnection(true, 10, 0, true, 32, 128);
            Assert.That(connection.MaxReceiveRate, Is.EqualTo(15_347_200));
        }
    }
}