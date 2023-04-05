using NUnit.Framework;
using System.Net;

namespace kcp2k.Tests
{
    public class CommonTests
    {
        [Test]
        public void ConnectionHash()
        {
            IPEndPoint endPointA = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 7777);
            IPEndPoint endPointB = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 7777);
            IPEndPoint endPointC = new IPEndPoint(IPAddress.Parse("127.9.0.1"), 7777);
            IPEndPoint endPointD = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 7778);
            IPEndPoint endPointE = new IPEndPoint(IPAddress.Parse("127.9.0.1"), 7778);

            // same ip:port
            Assert.That(Common.ConnectionHash(endPointA), Is.EqualTo(35685658));
            Assert.That(Common.ConnectionHash(endPointB), Is.EqualTo(35685658));

            // different ip
            Assert.That(Common.ConnectionHash(endPointC), Is.EqualTo(1049776323));

            // different port
            Assert.That(Common.ConnectionHash(endPointD), Is.EqualTo(35685657));

            // different ip:port
            Assert.That(Common.ConnectionHash(endPointE), Is.EqualTo(1049776320));
        }
    }
}
