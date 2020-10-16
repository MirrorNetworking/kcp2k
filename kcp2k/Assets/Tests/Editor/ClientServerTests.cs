using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using kcp2k.Examples;
using UnityEngine;
using UnityEngine.TestTools;

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
        int ServerFirstConnectionId()
        {
            return server.connections.First().Key;
        }

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
            // need to update a few times
            for (int i = 0; i < 10; ++i)
            {
                Thread.Sleep(10);
                client.LateUpdate();
                server.LateUpdate();
            }
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

        [Test]
        public void ClientToServerMessage()
        {
            server.StartServer();
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message)})"));
            SendClientToServerBlocking(new ArraySegment<byte>(message));
        }

        // max sized message should always work
        [Test]
        public void ClientToServerMaxSizedMessage()
        {
            server.StartServer();
            ConnectClientBlocking();

            byte[] message = new byte[Kcp.MTU_DEF];
            for (int i = 0; i < Kcp.MTU_DEF; ++i)
                message[i] = (byte)(i & 0xFF);
            LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message)})"));
            SendClientToServerBlocking(new ArraySegment<byte>(message));
        }

        // > max sized message should not work
        [Test]
        public void ClientToServerTooLargeMessage()
        {
            server.StartServer();
            ConnectClientBlocking();

            byte[] message = new byte[Kcp.MTU_DEF + 1];
            LogAssert.Expect(LogType.Error, $"Failed to send message of size {message.Length} because it's larger than MaxMessageSize={Kcp.MTU_DEF}");
            SendClientToServerBlocking(new ArraySegment<byte>(message));
        }

        // test to see if successive messages still work fine.
        [Test]
        public void ClientToServerTwoMessages()
        {
            server.StartServer();
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message)})"));
            SendClientToServerBlocking(new ArraySegment<byte>(message));

            byte[] message2 = {0x03, 0x04};
            LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message2)})"));
            SendClientToServerBlocking(new ArraySegment<byte>(message2));
        }

        [Test]
        public void ServerToClientMessage()
        {
            server.StartServer();
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            LogAssert.Expect(LogType.Log, $"KCP: OnClientDataReceived({BitConverter.ToString(message)})");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));
        }

        // max sized message should always work
        [Test]
        public void ServerToClientMaxSizedMessage()
        {
            server.StartServer();
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[Kcp.MTU_DEF];
            for (int i = 0; i < Kcp.MTU_DEF; ++i)
                message[i] = (byte)(i & 0xFF);

            LogAssert.Expect(LogType.Log, $"KCP: OnClientDataReceived({BitConverter.ToString(message)})");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));
        }

        // > max sized message should not work
        [Test]
        public void ServerToClientTooLargeMessage()
        {
            server.StartServer();
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[Kcp.MTU_DEF + 1];
            LogAssert.Expect(LogType.Error, $"Failed to send message of size {message.Length} because it's larger than MaxMessageSize={Kcp.MTU_DEF}");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));
        }

        // test to see if successive messages still work fine.
        [Test]
        public void ServerToClientTwoMessages()
        {
            server.StartServer();
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            LogAssert.Expect(LogType.Log, $"KCP: OnClientDataReceived({BitConverter.ToString(message)})");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));

            byte[] message2 = {0x05, 0x06};
            LogAssert.Expect(LogType.Log, $"KCP: OnClientDataReceived({BitConverter.ToString(message2)})");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message2));

            client.Disconnect();
            server.StopServer();
        }

        // client disconnecting by himself should disconnect on both ends
        [Test]
        public void ClientVoluntaryDisconnect()
        {
            server.StartServer();
            ConnectClientBlocking();
            Assert.That(client.Connected(), Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));

            DisconnectClientBlocking();
            Assert.That(client.Connected(), Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        // server kicking a client should disconnect on both ends
        [Test]
        public void ClientInvoluntaryDisconnect()
        {
            server.StartServer();
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            Assert.That(client.Connected(), Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));

            KickClientBlocking(connectionId);
            Assert.That(client.Connected(), Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        [Test]
        public void ServerGetClientAddress()
        {
            server.StartServer();
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            Assert.That(server.GetAddress(connectionId), Is.EqualTo("::ffff:127.0.0.1"));
        }
    }
}
