using System;
using System.Threading;
using NUnit.Framework;
using kcp2k.Examples;

namespace kcp2k.Tests
{
    public class ClientServerTests
    {
        // use the TestServer + LogAssert.Expect. simple and stupid.
        TestServer server;
        TestClient client;

        // setup ///////////////////////////////////////////////////////////////
        [SetUp]
        public void SetUp()
        {
            server = new TestServer();
            client = new TestClient();
        }

        [TearDown]
        public void TearDown()
        {
            client.Disconnect();
            server.StopServer();
        }

        // helpers /////////////////////////////////////////////////////////////
        // connect and give it enough time to handle
        void ConnectClientBlocking()
        {
            client.Connect("127.0.0.1");
            // need update a few times for handshake to finish
            for (int i = 0; i < 10; ++i)
            {
                Thread.Sleep(10);
                client.LateUpdate();
                server.LateUpdate();
            }
        }

        // disconnect and give it enough time to handle
        void DisconnectClientBlocking()
        {
            client.Disconnect();
            Thread.Sleep(100);
            client.LateUpdate();
            server.LateUpdate();
        }

        // kick and give it enough time to handle
        void KickClientBlocking(int connectionId)
        {
            server.Disconnect(connectionId);
            Thread.Sleep(100);
            client.LateUpdate();
            server.LateUpdate();
        }

        void SendClientToServerBlocking(ArraySegment<byte> message)
        {
            client.Send(message);
            Thread.Sleep(100);
            client.LateUpdate();
            server.LateUpdate();
        }

        void SendServerToClientBlocking(int connectionId, ArraySegment<byte> message)
        {
            server.Send(connectionId, message);
            Thread.Sleep(100);
            client.LateUpdate();
            server.LateUpdate();
        }

        // tests ///////////////////////////////////////////////////////////////
        [Test]
        public void ServerUpdateOnce()
        {
            // just see if we can tick the server after it started
            server.LateUpdate();
        }

        [Test]
        public void ServerStartStop()
        {
            server.StartServer();
            Assert.That(server.Active, Is.True);
            server.StopServer();
            Assert.That(server.Active, Is.False);
        }

        [Test]
        public void ServerStartStopMultiple()
        {
            for (int i = 0; i < 10; ++i)
            {
                ServerStartStop();
            }
        }

        [Test]
        public void ConnectAndDisconnectClient()
        {
            // connect
            server.StartServer();
            ConnectClientBlocking();

            Assert.That(client.Connected(), Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));

            // disconnect
            DisconnectClientBlocking();
            Assert.That(client.Connected(), Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        // need to make sure that connect->disconnect->connect works too
        // (to detect cases where connect/disconnect might not clean up properly)
        [Test]
        public void ConnectAndDisconnectClientMultipleTimes()
        {
            server.StartServer();

            for (int i = 0; i < 10; ++i)
            {
                ConnectClientBlocking();
                Assert.That(client.Connected(), Is.True);
                Assert.That(server.connections.Count, Is.EqualTo(1));

                DisconnectClientBlocking();
                Assert.That(client.Connected(), Is.False);
                Assert.That(server.connections.Count, Is.EqualTo(0));
            }

            server.StopServer();
        }
    }
}
