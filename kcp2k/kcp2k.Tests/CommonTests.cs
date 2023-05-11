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

            // hashes will be different in different .net environments.
            // for example, Unity will have different hashes than .net core.
            // for this reason we don't hardcode them

            // same ip:port
            int hashA = Common.ConnectionHash(endPointA);
            int hashB = Common.ConnectionHash(endPointB);
            int hashC = Common.ConnectionHash(endPointC);
            int hashD = Common.ConnectionHash(endPointD);
            int hashE = Common.ConnectionHash(endPointE);

            // same ip:port
            Assert.That(hashA, Is.EqualTo(hashB));

            // different ip
            Assert.That(hashC, !Is.EqualTo(hashA));

            // different port
            Assert.That(hashD, !Is.EqualTo(hashA));

            // different ip:port
            Assert.That(hashE, !Is.EqualTo(hashA));
        }

        [Test]
        public void GenerateCookie()
        {
            Assert.That(Common.GenerateCookie(), !Is.EqualTo(0));
            Assert.That(Common.GenerateCookie(), !Is.EqualTo(Common.GenerateCookie()));
        }
    }
}
