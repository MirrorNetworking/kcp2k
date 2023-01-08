using NUnit.Framework;

namespace kcp2k.Tests
{
    class MockPeer : KcpPeer
    {
        public MockPeer(KcpConfig config) : base(
            (_) => {},
            () => {},
            (_, _) => {},
            () => {},
            (_, _) => {},
            config) {}
    }

    public class KcpPeerTests
    {
        [Test]
        public void MaxSendRate()
        {
            //   WND(32) * MTU(1199) = 38,368 bytes
            //   => 38,368 * 1000 / INTERVAL(10) = 3,836,800 bytes/s = 3746.8 KB/s
            KcpConfig config = new KcpConfig(
                SendWindowSize: 32,
                ReceiveWindowSize: 64
            );

            KcpPeer peer = new MockPeer(config);
            Assert.That(peer.MaxSendRate, Is.EqualTo(3_836_800));
        }

        [Test]
        public void MaxReceiveRate()
        {
            // note: WND needs to be >= max fragment size which is 128!
            //   WND(128) * MTU(1199) = 153,472 bytes
            //   => 153,472 * 1000 / INTERVAL(10) = 15,347,200 bytes/s = 14,987.5 KB/s = 14.63 MB/s
            KcpConfig config = new KcpConfig(
                SendWindowSize: 32,
                ReceiveWindowSize: 128
            );

            KcpPeer peer = new MockPeer(config);
            Assert.That(peer.MaxReceiveRate, Is.EqualTo(15_347_200));
        }
    }
}