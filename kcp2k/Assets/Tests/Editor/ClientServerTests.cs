using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace kcp2k.Tests
{
    public class ClientServerTests
    {
        // force NoDelay and minimum interval.
        // this way UpdateSeveralTimes() doesn't need to wait very long and
        // tests run a lot faster.
        const ushort Port = 7777;
        const bool NoDelay = true;
        const uint Interval = 1; // 1ms so at interval code at least runs.

        KcpServer server;
        KcpClient client;

        // setup ///////////////////////////////////////////////////////////////
        [SetUp]
        public void SetUp()
        {
            server = new KcpServer(
                (connectionId) => {},
                (connectionId, message) => Debug.Log($"KCP: OnServerDataReceived({connectionId}, {BitConverter.ToString(message.Array, message.Offset, message.Count)})"),
                (connectionId) => {},
                NoDelay,
                Interval
            );
            server.NoDelay = NoDelay;
            server.Interval = Interval;

            client = new KcpClient(
                () => {},
                (message) => Debug.Log($"KCP: OnClientDataReceived({BitConverter.ToString(message.Array, message.Offset, message.Count)})"),
                () => {}
            );
        }

        [TearDown]
        public void TearDown()
        {
            client.Disconnect();
            server.Stop();
        }

        // helpers /////////////////////////////////////////////////////////////
        int ServerFirstConnectionId()
        {
            return server.connections.First().Key;
        }

        void UpdateSeveralTimes()
        {
            // update serveral times to avoid flaky tests.
            for (int i = 0; i < 50; ++i)
            {
                client.Tick();
                server.Tick();
                // update 'interval' milliseconds.
                // the lower the interval, the faster the tests will run.
                Thread.Sleep((int)Interval);
            }
        }

        // connect and give it enough time to handle
        void ConnectClientBlocking()
        {
            client.Connect("127.0.0.1", Port, NoDelay, Interval);
            UpdateSeveralTimes();
        }

        // disconnect and give it enough time to handle
        void DisconnectClientBlocking()
        {
            client.Disconnect();
            UpdateSeveralTimes();
        }

        // kick and give it enough time to handle
        void KickClientBlocking(int connectionId)
        {
            server.Disconnect(connectionId);
            UpdateSeveralTimes();
        }

        void SendClientToServerBlocking(ArraySegment<byte> message)
        {
            client.Send(message);
            UpdateSeveralTimes();
        }

        void SendServerToClientBlocking(int connectionId, ArraySegment<byte> message)
        {
            server.Send(connectionId, message);
            UpdateSeveralTimes();
        }

        // tests ///////////////////////////////////////////////////////////////
        [Test]
        public void ServerUpdateOnce()
        {
            // just see if we can tick the server after it started
            server.Tick();
        }

        [Test]
        public void ServerStartStop()
        {
            server.Start(Port);
            Assert.That(server.IsActive(), Is.True);
            server.Stop();
            Assert.That(server.IsActive(), Is.False);
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
            server.Start(Port);
            ConnectClientBlocking();

            Assert.That(client.connected, Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));

            // disconnect
            DisconnectClientBlocking();
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        // need to make sure that connect->disconnect->connect works too
        // (to detect cases where connect/disconnect might not clean up properly)
        [Test]
        public void ConnectAndDisconnectClientMultipleTimes()
        {
            server.Start(Port);

            for (int i = 0; i < 10; ++i)
            {
                ConnectClientBlocking();
                Assert.That(client.connected, Is.True);
                Assert.That(server.connections.Count, Is.EqualTo(1));

                DisconnectClientBlocking();
                Assert.That(client.connected, Is.False);
                Assert.That(server.connections.Count, Is.EqualTo(0));
            }

            server.Stop();
        }

        [Test]
        public void ClientToServerMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message)})"));
            SendClientToServerBlocking(new ArraySegment<byte>(message));
        }

        // max sized message should always work
        [Test]
        public void ClientToServerMaxSizedMessage()
        {
            server.Start(Port);
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
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[Kcp.MTU_DEF + 1];
            LogAssert.Expect(LogType.Error, $"Failed to send message of size {message.Length} because it's larger than MaxMessageSize={Kcp.MTU_DEF}");
            SendClientToServerBlocking(new ArraySegment<byte>(message));
        }

        // test to see if successive messages still work fine.
        [Test]
        public void ClientToServerTwoMessages()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message)})"));
            SendClientToServerBlocking(new ArraySegment<byte>(message));

            byte[] message2 = {0x03, 0x04};
            LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message2)})"));
            SendClientToServerBlocking(new ArraySegment<byte>(message2));
        }

        // send multiple large messages before calling update.
        // this way kcp is forced to buffer / queue them.
        [Test]
        public void ClientToServerMultipleMaxSizedMessagesAtOnce()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // prepare 10 different MTU sized messages.
            // each of them with unique content so we can guarantee arrival.
            List<byte[]> messages = new List<byte[]>();
            for (int i = 0; i < 10; ++i)
            {
                // create message, fill with unique data (j+i & 0xff)
                byte[] message = new byte[Kcp.MTU_DEF];
                for (int j = 0; j < Kcp.MTU_DEF; ++j)
                    message[j] = (byte)((j + i) & 0xFF);
                messages.Add(message);
            }

            // send each one without updating server or client yet
            foreach (byte[] message in messages)
                client.Send(new ArraySegment<byte>(message));

            // now update everyone and expect a log for each one
            foreach (byte[] message in messages)
                LogAssert.Expect(LogType.Log, new Regex($"KCP: OnServerDataReceived(.*, {BitConverter.ToString(message)})"));
            UpdateSeveralTimes();
        }

        [Test]
        public void ServerToClientMessage()
        {
            server.Start(Port);
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
            server.Start(Port);
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
            server.Start(Port);
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
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            LogAssert.Expect(LogType.Log, $"KCP: OnClientDataReceived({BitConverter.ToString(message)})");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));

            byte[] message2 = {0x05, 0x06};
            LogAssert.Expect(LogType.Log, $"KCP: OnClientDataReceived({BitConverter.ToString(message2)})");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message2));

            client.Disconnect();
            server.Stop();
        }

        // client disconnecting by himself should disconnect on both ends
        [Test]
        public void ClientVoluntaryDisconnect()
        {
            server.Start(Port);
            ConnectClientBlocking();
            Assert.That(client.connected, Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));

            DisconnectClientBlocking();
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        // server kicking a client should disconnect on both ends
        [Test]
        public void ClientInvoluntaryDisconnect()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            Assert.That(client.connected, Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));

            KickClientBlocking(connectionId);
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        [Test]
        public void ServerGetClientAddress()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            Assert.That(server.GetClientAddress(connectionId), Is.EqualTo("::ffff:127.0.0.1"));
        }

        [Ignore("Client doesn't receive disconnected message, Server adds client again after he still sends random data.")]
        [Test]
        public void ChokeConnectionAutoDisconnects()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            // fill send queue with > QueueDisconnectThreshold messages
            byte[] message = {0x03, 0x04};
            for (int i = 0; i < KcpConnection.QueueDisconnectThreshold + 1; ++i)
            {
                server.Send(connectionId, new ArraySegment<byte>(message));
            }

            // update should disconnect the connection
            LogAssert.Expect(LogType.Warning, new Regex("KCP: disconnecting connection because it can't process data fast enough.*"));
            UpdateSeveralTimes();

            // client should've disconnected, server should've dropped it
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }
    }
}
