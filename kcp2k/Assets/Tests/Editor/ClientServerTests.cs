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
        List<byte[]> serverReceived;

        KcpClient client;
        List<byte[]> clientReceived;

        // setup ///////////////////////////////////////////////////////////////
        [SetUp]
        public void SetUp()
        {
            // create new server & received list for each test
            serverReceived = new List<byte[]>();
            server = new KcpServer(
                (connectionId) => {},
                (connectionId, message) => {
                    byte[] copy = new byte[message.Count];
                    Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);
                    serverReceived.Add(copy);
                },
                (connectionId) => {},
                NoDelay,
                Interval
            );
            server.NoDelay = NoDelay;
            server.Interval = Interval;

            // create new client & received list for each test
            clientReceived = new List<byte[]>();
            client = new KcpClient(
                () => {},
                (message) => {
                    byte[] copy = new byte[message.Count];
                    Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);
                    clientReceived.Add(copy);
                },
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
            SendClientToServerBlocking(new ArraySegment<byte>(message));
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].SequenceEqual(message), Is.True);
        }

        // max sized message should always work
        [Test]
        public void ClientToServerMaxSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[KcpConnection.MaxMessageSize];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);
            Debug.Log($"Sending {message.Length} bytes = {message.Length / 1024} KB message");
            SendClientToServerBlocking(new ArraySegment<byte>(message));
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].SequenceEqual(message), Is.True);
        }

        // there used to be a bug where sending smaller than MTU sized messages
        // would fail because the raw receive buffer was of [MTU-5]
        // instead of [MTU], so RawReceive would get msgLength=MTU and silently
        // drop the last 5 bytes because buffer was too small.
        // => let's make sure that never happens again.
        [Test]
        public void ClientToServerSlightlySmallerThanMTUSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[Kcp.MTU_DEF - 5];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);
            Debug.Log($"Sending {message.Length} bytes = {message.Length / 1024} KB message");
            SendClientToServerBlocking(new ArraySegment<byte>(message));
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].SequenceEqual(message), Is.True);
        }

        // > max sized message should not work
        [Test]
        public void ClientToServerTooLargeMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[KcpConnection.MaxMessageSize + 1];
            LogAssert.Expect(LogType.Error, $"Failed to send message of size {message.Length} because it's larger than MaxMessageSize={KcpConnection.MaxMessageSize}");
            SendClientToServerBlocking(new ArraySegment<byte>(message));
            Assert.That(serverReceived.Count, Is.EqualTo(0));
        }

        // test to see if successive messages still work fine.
        [Test]
        public void ClientToServerTwoMessages()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            SendClientToServerBlocking(new ArraySegment<byte>(message));

            byte[] message2 = {0x03, 0x04};
            SendClientToServerBlocking(new ArraySegment<byte>(message2));

            Assert.That(serverReceived.Count, Is.EqualTo(2));
            Assert.That(serverReceived[0].SequenceEqual(message), Is.True);
            Assert.That(serverReceived[1].SequenceEqual(message2), Is.True);
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
                byte[] message = new byte[KcpConnection.MaxMessageSize];
                for (int j = 0; j < message.Length; ++j)
                    message[j] = (byte)((j + i) & 0xFF);
                messages.Add(message);
            }

            // send each one without updating server or client yet
            foreach (byte[] message in messages)
                client.Send(new ArraySegment<byte>(message));

            // now update everyone and expect all
            UpdateSeveralTimes();
            Assert.That(serverReceived.Count, Is.EqualTo(messages.Count));
            for (int i = 0; i < messages.Count; ++i)
            {
                Assert.That(serverReceived[i].SequenceEqual(messages[i]), Is.True);
            }
        }

        [Test]
        public void ServerToClientMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].SequenceEqual(message), Is.True);
        }

        // max sized message should always work
        [Test]
        public void ServerToClientMaxSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[KcpConnection.MaxMessageSize];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);

            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].SequenceEqual(message), Is.True);
        }

        // there used to be a bug where sending smaller than MTU sized messages
        // would fail because the raw receive buffer was of [MTU-5]
        // instead of [MTU], so RawReceive would get msgLength=MTU and silently
        // drop the last 5 bytes because buffer was too small.
        // => let's make sure that never happens again.
        [Test]
        public void ServerToClientSlightlySmallerThanMTUSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[Kcp.MTU_DEF - 5];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);

            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].SequenceEqual(message), Is.True);
        }


        // > max sized message should not work
        [Test]
        public void ServerToClientTooLargeMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[KcpConnection.MaxMessageSize + 1];
            LogAssert.Expect(LogType.Error, $"Failed to send message of size {message.Length} because it's larger than MaxMessageSize={KcpConnection.MaxMessageSize}");
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));
            Assert.That(clientReceived.Count, Is.EqualTo(0));
        }

        // test to see if successive messages still work fine.
        [Test]
        public void ServerToClientTwoMessages()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message));

            byte[] message2 = {0x05, 0x06};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message2));

            Assert.That(clientReceived.Count, Is.EqualTo(2));
            Assert.That(clientReceived[0].SequenceEqual(message), Is.True);
            Assert.That(clientReceived[1].SequenceEqual(message2), Is.True);

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

        [Test]
        public void Timeout()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // do nothing for 'Timeout + 1' seconds
            Thread.Sleep(KcpConnection.TIMEOUT + 1);

            // now update
            LogAssert.Expect(LogType.Warning, $"KCP: Connection timed out after {KcpConnection.TIMEOUT}ms. Disconnecting.");
            UpdateSeveralTimes();

            // should be disconnected
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        [Test]
        public void PingPreventsTimeout()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // only update but don't send anything for 'Timeout + 1' seconds.
            // ping should be sent internally every second, preventing timeout.
            System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            while (watch.ElapsedMilliseconds < KcpConnection.TIMEOUT + 1)
            {
                UpdateSeveralTimes();
            }

            // if ping worked then we shouldn't have timed out
            Assert.That(client.connected, Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));
        }

        // fake a kcp dead_link by setting state = -1.
        // KcpConnection should detect it and disconnect.
        [Test]
        public void DeadLink_Fake()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            // fake dead_link by setting kcp.state to -1
            server.connections[connectionId].kcp.state = -1;

            // now update
            LogAssert.Expect(LogType.Warning, $"KCP Connection dead_link detected. Disconnecting.");
            UpdateSeveralTimes();

            // should be disconnected
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

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

            // no need to log thousands of messages. that would take forever.
            client.OnData = _ => {};

            // update should detect the choked connection and disconnect it.
            LogAssert.Expect(LogType.Warning, new Regex("KCP: disconnecting connection because it can't process data fast enough.*"));
            UpdateSeveralTimes();

            // client should've disconnected, server should've dropped it
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }
    }
}
