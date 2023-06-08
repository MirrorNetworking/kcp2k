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
            config,
            0) {}
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
            Assert.That(peer.MaxSendRate, Is.EqualTo(3_824_000));
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
            Assert.That(peer.MaxReceiveRate, Is.EqualTo(15_296_000));
        }

        // test to prevent https://github.com/vis2k/kcp2k/issues/49
        [Test]
        public void InputTooSmall()
        {
            KcpConfig config = new KcpConfig(
                SendWindowSize: 32,
                ReceiveWindowSize: 128
            );

            KcpPeer peer = new MockPeer(config);

            // try all sizes which are too small.
            // we need at least 1 byte channel + 4 bytes cookie
            peer.RawInput(new byte[]{1});
            peer.RawInput(new byte[]{1, 2});
            peer.RawInput(new byte[]{1, 2, 3});
            peer.RawInput(new byte[]{1, 2, 3, 4});
            peer.RawInput(new byte[]{1, 3, 3, 4, 5});
        }
    }
}