using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
// DON'T import UnityEngine. kcp2k should be platform independent.

namespace kcp2k.Tests
{
    public struct Message
    {
        public byte[] data;
        public KcpChannel channel;
        public Message(byte[] data, KcpChannel channel)
        {
            this.data = data;
            this.channel = channel;
        }
    }

    public class ClientServerTests
    {
        // force NoDelay and minimum interval.
        // this way UpdateSeveralTimes() doesn't need to wait very long and
        // tests run a lot faster.
        protected const ushort Port = 7777;
        // not all platforms support DualMode.
        // run tests without it so they work on all platforms.
        protected const bool DualMode = false;
        protected const bool NoDelay = true;
        protected const uint Interval = 1; // 1ms so at interval code at least runs.
        protected const int Timeout = 2000;
        // windows can be configured separately to test differently sized windows
        // use 2x defaults so we can test larger max message than defaults too.
        // IMPORTANT: default max message needs 127 fragments.
        //            default x2 needs 255 fragments.
        //            kcp sends 'frg' as 1 byte, so 255 still fits.
        //            need to try x3 to find possible bugs.
        protected const int SendWindowSize = Kcp.WND_SND * 3;
        protected const int ReceiveWindowSize = Kcp.WND_RCV * 3;
        // maximum retransmit attempts until dead_link detected
        // default * 2 to check if configuration works
        protected uint MaxRetransmits = Kcp.DEADLINK * 2;

        protected KcpServer server;
        protected List<Message> serverReceived;

        protected KcpClient client;
        protected List<Message> clientReceived;

        // setup ///////////////////////////////////////////////////////////////
        protected void ClientOnData(ArraySegment<byte> message, KcpChannel channel)
        {
            byte[] copy = new byte[message.Count];
            Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);
            clientReceived.Add(new Message(copy, channel));
        }

        protected void ServerOnData(int connectionId, ArraySegment<byte> message, KcpChannel channel)
        {
            byte[] copy = new byte[message.Count];
            Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);
            serverReceived.Add(new Message(copy, channel));
        }

        protected void SetupLogging()
        {
            // logging
#if UNITY_2018_3_OR_NEWER
            Log.Info = UnityEngine.Debug.Log;
            Log.Warning = UnityEngine.Debug.LogWarning;
            Log.Error = UnityEngine.Debug.LogError;
#else
            Log.Info = Console.WriteLine;
            Log.Warning = Console.WriteLine;
            Log.Error = Console.WriteLine;
#endif
        }

        // virtual so that we can overwrite for where-allocation nonalloc tests
        protected virtual void CreateServer()
        {
            server = new KcpServer(
                (connectionId) => {},
                ServerOnData,
                (connectionId) => {},
                DualMode,
                NoDelay,
                Interval,
                0,
                true,
                SendWindowSize,
                ReceiveWindowSize,
                Timeout,
                MaxRetransmits
            );
            server.NoDelay = NoDelay;
            server.Interval = Interval;
        }

        // virtual so that we can overwrite for where-allocation nonalloc tests
        protected virtual void CreateClient()
        {
            client = new KcpClient(
                () => {},
                ClientOnData,
                () => {}
            );
        }

        [SetUp]
        public void SetUp()
        {
            SetupLogging();

            // create new server & client received list for each test
            serverReceived = new List<Message>();
            clientReceived = new List<Message>();

            // create server & client
            CreateServer();
            CreateClient();
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
            // => need to update at 120 times for default maxed sized messages
            //    where it requires 120+ fragments.
            // => need to update even more often for 2x default max sized
            for (int i = 0; i < 500; ++i)
            {
                client.Tick();
                server.Tick();
                // update 'interval' milliseconds.
                // the lower the interval, the faster the tests will run.
                Thread.Sleep((int)Interval);
            }
        }

        // connect and give it enough time to handle
        void ConnectClientBlocking(string hostname = "127.0.0.1")
        {
            client.Connect(hostname, Port, NoDelay, Interval, 0, true, SendWindowSize, ReceiveWindowSize, Timeout, MaxRetransmits);
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

        void SendClientToServerBlocking(ArraySegment<byte> message, KcpChannel channel)
        {
            client.Send(message, channel);
            UpdateSeveralTimes();
        }

        void SendServerToClientBlocking(int connectionId, ArraySegment<byte> message, KcpChannel channel)
        {
            server.Send(connectionId, message, channel);
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

        // test to prevent https://github.com/vis2k/kcp2k/issues/26
        [Test]
        public void ConnectInvalidHostname_CallsOnDisconnected()
        {
            // make sure OnDisconnected is called.
            // otherwise Mirror etc. would be left hanging in 'Connecting' state
            bool called = false;
            client.OnDisconnected = () => called = true;

            // connect to an invalid name
            ConnectClientBlocking("asdasdasd");

            // OnDisconnected should've been called
            Assert.That(client.connected, Is.False);
            Assert.That(called, Is.True);
        }

        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void ClientToServerMessage(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            SendClientToServerBlocking(new ArraySegment<byte>(message), channel);
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(channel));
        }

        // empty data message should be detected instead of sent
        // if the application tried to send an empty arraysegment then something
        // wrong happened.
        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void ClientToServerEmptyMessage(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientBlocking();

            // sending empty messages is not allowed
#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, "KcpConnection: tried sending empty message. This should never happen. Disconnecting.");
#endif
            byte[] message = new byte[0];
            SendClientToServerBlocking(new ArraySegment<byte>(message), channel);
            Assert.That(serverReceived.Count, Is.EqualTo(0));
        }

        // max sized message should always work
        [Test]
        public void ClientToServerReliableMaxSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[KcpConnection.ReliableMaxMessageSize(ReceiveWindowSize)];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);
            Log.Info($"Sending {message.Length} bytes = {message.Length / 1024} KB message");
            SendClientToServerBlocking(new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));
        }

        // max sized message should always work
        [Test]
        public void ClientToServerUnreliableMaxSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[KcpConnection.UnreliableMaxMessageSize];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);
            Log.Info($"Sending {message.Length} bytes = {message.Length / 1024} KB message");
            SendClientToServerBlocking(new ArraySegment<byte>(message), KcpChannel.Unreliable);
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(KcpChannel.Unreliable));
        }

        // there used to be a bug where sending smaller than MTU sized messages
        // would fail because the raw receive buffer was of [MTU-5]
        // instead of [MTU], so RawReceive would get msgLength=MTU and silently
        // drop the last 5 bytes because buffer was too small.
        // => let's make sure that never happens again.
        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void ClientToServerSlightlySmallerThanMTUSizedMessage(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[Kcp.MTU_DEF - 5];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);
            Log.Info($"Sending {message.Length} bytes = {message.Length / 1024} KB message");
            SendClientToServerBlocking(new ArraySegment<byte>(message), channel);
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(channel));
        }

        // > max sized message should not work
        [Test]
        public void ClientToServerTooLargeReliableMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[KcpConnection.ReliableMaxMessageSize(ReceiveWindowSize) + 1];

#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, $"Failed to send reliable message of size {message.Length} because it's larger than ReliableMaxMessageSize={KcpConnection.ReliableMaxMessageSize(ReceiveWindowSize)}");
#endif
            SendClientToServerBlocking(new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(serverReceived.Count, Is.EqualTo(0));
        }

        [Test]
        public void ClientToServerTooLargeUnreliableMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = new byte[KcpConnection.UnreliableMaxMessageSize + 1];
#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, $"Failed to send unreliable message of size {message.Length} because it's larger than UnreliableMaxMessageSize={KcpConnection.UnreliableMaxMessageSize}");
#endif
            SendClientToServerBlocking(new ArraySegment<byte>(message), KcpChannel.Unreliable);
            Assert.That(serverReceived.Count, Is.EqualTo(0));
        }

        // test to see if successive messages still work fine.
        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void ClientToServerTwoMessages(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            SendClientToServerBlocking(new ArraySegment<byte>(message), channel);

            byte[] message2 = {0x03, 0x04};
            SendClientToServerBlocking(new ArraySegment<byte>(message2), channel);

            Assert.That(serverReceived.Count, Is.EqualTo(2));
            Assert.That(serverReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(channel));

            Assert.That(serverReceived[1].data.SequenceEqual(message2), Is.True);
            Assert.That(serverReceived[1].channel, Is.EqualTo(channel));
        }

        // test to see if mixed reliable & unreliable messages still work fine.
        [Test]
        public void ClientToServerTwoMixedMessages()
        {
            server.Start(Port);
            ConnectClientBlocking();

            byte[] message = {0x01, 0x02};
            SendClientToServerBlocking(new ArraySegment<byte>(message), KcpChannel.Unreliable);

            byte[] message2 = {0x03, 0x04};
            SendClientToServerBlocking(new ArraySegment<byte>(message2), KcpChannel.Reliable);

            Assert.That(serverReceived.Count, Is.EqualTo(2));
            Assert.That(serverReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(KcpChannel.Unreliable));

            Assert.That(serverReceived[1].data.SequenceEqual(message2), Is.True);
            Assert.That(serverReceived[1].channel, Is.EqualTo(KcpChannel.Reliable));
        }

        // send multiple large messages before calling update.
        // this way kcp is forced to buffer / queue them.
        [Test]
        public void ClientToServerMultipleReliableMaxSizedMessagesAtOnce()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // prepare 10 different MTU sized messages.
            // each of them with unique content so we can guarantee arrival.
            List<byte[]> messages = new List<byte[]>();
            for (int i = 0; i < 10; ++i)
            {
                // create message, fill with unique data (j+i & 0xff)
                byte[] message = new byte[KcpConnection.ReliableMaxMessageSize(ReceiveWindowSize)];
                for (int j = 0; j < message.Length; ++j)
                    message[j] = (byte)((j + i) & 0xFF);
                messages.Add(message);
            }

            // send each one without updating server or client yet
            foreach (byte[] message in messages)
                client.Send(new ArraySegment<byte>(message), KcpChannel.Reliable);

            // each max sized message needs a lot of updates for all the fragments.
            // for multiple we need to update a lot more than usual.
            for (int i = 0; i < 10; ++i)
                UpdateSeveralTimes();

            // all received?
            Assert.That(serverReceived.Count, Is.EqualTo(messages.Count));
            for (int i = 0; i < messages.Count; ++i)
            {
                Assert.That(serverReceived[i].data.SequenceEqual(messages[i]), Is.True);
                Assert.That(serverReceived[i].channel, Is.EqualTo(KcpChannel.Reliable));
            }
        }

        // send multiple large messages before calling update.
        // this way kcp is forced to buffer / queue them.
        [Test]
        public void ClientToServerMultipleUnreliableMaxSizedMessagesAtOnce()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // prepare 10 different MTU sized messages.
            // each of them with unique content so we can guarantee arrival.
            List<byte[]> messages = new List<byte[]>();
            for (int i = 0; i < 10; ++i)
            {
                // create message, fill with unique data (j+i & 0xff)
                byte[] message = new byte[KcpConnection.UnreliableMaxMessageSize];
                for (int j = 0; j < message.Length; ++j)
                    message[j] = (byte)((j + i) & 0xFF);
                messages.Add(message);
            }

            // send each one without updating server or client yet
            foreach (byte[] message in messages)
                client.Send(new ArraySegment<byte>(message), KcpChannel.Unreliable);

            // each max sized message needs a lot of updates for all the fragments.
            // for multiple we need to update a lot more than usual.
            for (int i = 0; i < 10; ++i)
                UpdateSeveralTimes();

            // all received?
            Assert.That(serverReceived.Count, Is.EqualTo(messages.Count));
            for (int i = 0; i < messages.Count; ++i)
            {
                Assert.That(serverReceived[i].data.SequenceEqual(messages[i]), Is.True);
                Assert.That(serverReceived[i].channel, Is.EqualTo(KcpChannel.Unreliable));
            }
        }

        [Test]
        public void ServerToClientReliableMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));
        }

        // make sure that Sending an arraysegment checks the segment's offset
        [Test]
        public void ServerToClientReliableMessage_RespectsOffset()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0xFF, 0x03, 0x04};
            ArraySegment<byte> segment = new ArraySegment<byte>(message, 1, 2);
            SendServerToClientBlocking(connectionId, segment, KcpChannel.Reliable);
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(segment), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));
        }

        [Test]
        public void ServerToClientUnreliableMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Unreliable);
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Unreliable));
        }

        // make sure that Sending an arraysegment checks the segment's offset
        [Test]
        public void ServerToClientUnreliableMessage_RespectsOffset()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0xFF, 0x03, 0x04};
            ArraySegment<byte> segment = new ArraySegment<byte>(message, 1, 2);
            SendServerToClientBlocking(connectionId, segment, KcpChannel.Unreliable);
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(segment), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Unreliable));
        }

        // empty data message should be detected instead of sent
        // if the application tried to send an empty arraysegment then something
        // wrong happened.
        [Test]
        public void ServerToClientReliableEmptyMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            // sending empty messages is not allowed.
#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, "KcpConnection: tried sending empty message. This should never happen. Disconnecting.");
#endif

            byte[] message = new byte[0];
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(clientReceived.Count, Is.EqualTo(0));
        }

        // max sized message should always work
        [Test]
        public void ServerToClientReliableMaxSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[KcpConnection.ReliableMaxMessageSize(ReceiveWindowSize)];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);

            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));
        }

        // max sized message should always work
        [Test]
        public void ServerToClientUnreliableMaxSizedMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[KcpConnection.UnreliableMaxMessageSize];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);

            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Unreliable);
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Unreliable));
        }

        // there used to be a bug where sending smaller than MTU sized messages
        // would fail because the raw receive buffer was of [MTU-5]
        // instead of [MTU], so RawReceive would get msgLength=MTU and silently
        // drop the last 5 bytes because buffer was too small.
        // => let's make sure that never happens again.
        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void ServerToClientSlightlySmallerThanMTUSizedMessage(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[Kcp.MTU_DEF - 5];
            for (int i = 0; i < message.Length; ++i)
                message[i] = (byte)(i & 0xFF);

            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), channel);
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(channel));
        }

        // > max sized message should not work
        [Test]
        public void ServerToClientTooLargeReliableMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[KcpConnection.ReliableMaxMessageSize(ReceiveWindowSize) + 1];
#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, $"Failed to send reliable message of size {message.Length} because it's larger than ReliableMaxMessageSize={KcpConnection.ReliableMaxMessageSize(ReceiveWindowSize)}");
#endif
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(clientReceived.Count, Is.EqualTo(0));
        }

        // > max sized message should not work
        [Test]
        public void ServerToClientTooLargeUnreliableMessage()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = new byte[KcpConnection.UnreliableMaxMessageSize + 1];

#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, $"Failed to send unreliable message of size {message.Length} because it's larger than UnreliableMaxMessageSize={KcpConnection.UnreliableMaxMessageSize}");
#endif
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Unreliable);
            Assert.That(clientReceived.Count, Is.EqualTo(0));
        }

        // test to see if successive messages still work fine.
        [Test]
        public void ServerToClientTwoReliableMessages()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Reliable);

            byte[] message2 = {0x05, 0x06};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message2), KcpChannel.Reliable);

            Assert.That(clientReceived.Count, Is.EqualTo(2));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));
            Assert.That(clientReceived[1].data.SequenceEqual(message2), Is.True);
            Assert.That(clientReceived[1].channel, Is.EqualTo(KcpChannel.Reliable));

            client.Disconnect();
            server.Stop();
        }
        // test to see if successive messages still work fine.
        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void ServerToClientTwoMessages(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), channel);

            byte[] message2 = {0x05, 0x06};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message2), channel);

            Assert.That(clientReceived.Count, Is.EqualTo(2));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(channel));
            Assert.That(clientReceived[1].data.SequenceEqual(message2), Is.True);
            Assert.That(clientReceived[1].channel, Is.EqualTo(channel));

            client.Disconnect();
            server.Stop();
        }

        // test to see if mixed messages still work fine.
        [Test]
        public void ServerToClientTwoMixedMessages()
        {
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            byte[] message = {0x03, 0x04};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message), KcpChannel.Unreliable);

            byte[] message2 = {0x05, 0x06};
            SendServerToClientBlocking(connectionId, new ArraySegment<byte>(message2), KcpChannel.Reliable);

            Assert.That(clientReceived.Count, Is.EqualTo(2));
            Assert.That(clientReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Unreliable));
            Assert.That(clientReceived[1].data.SequenceEqual(message2), Is.True);
            Assert.That(clientReceived[1].channel, Is.EqualTo(KcpChannel.Reliable));

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

            if (server.DualMode)
                Assert.That(server.GetClientAddress(connectionId), Is.EqualTo("::ffff:127.0.0.1"));
            else
                Assert.That(server.GetClientAddress(connectionId), Is.EqualTo("127.0.0.1"));
        }

        [Test]
        public void TimeoutDisconnects()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // do nothing for 'Timeout + 1' seconds
            Thread.Sleep(Timeout + 1);

            // now update
#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, $"KCP: Connection timed out after not receiving any message for {Timeout}ms. Disconnecting.");
#endif
            UpdateSeveralTimes();

            // should be disconnected
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void TimeoutIsResetByMessage(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientBlocking();

            // do nothing for 'Timeout / 2' seconds
            int firstSleep = Timeout / 2;
            Thread.Sleep(firstSleep);

            // send one reliable message
            client.Send(new ArraySegment<byte>(new byte[1]), channel);

            // update
            UpdateSeveralTimes();

            // do nothing for exactly the remaining timeout time + 1 to be sure
            Thread.Sleep(Timeout - firstSleep + 1);

            // now update
            UpdateSeveralTimes();

            // should still be connected
            Assert.That(client.connected, Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));
        }

        [Test]
        public void TimeoutIsResetByPing()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // only update but don't send anything for 'Timeout + 1' seconds.
            // ping should be sent internally every second, preventing timeout.
            System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            while (watch.ElapsedMilliseconds < Timeout + 1)
            {
                UpdateSeveralTimes();
            }

            // if ping worked then we shouldn't have timed out
            Assert.That(client.connected, Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(1));
        }

        // Mirror scene changes might take > 10s timeout time.
        // kcp connection should not time out while paused.
        //
        // see also: https://github.com/vis2k/kcp2k/issues/8
        [Test]
        public void TimeoutIsResetWhenUnpaused()
        {
            server.Start(Port);
            ConnectClientBlocking();

            // pause for Timeout + 1 seconds
            client.Pause();
            server.Pause();
            Thread.Sleep(Timeout + 1);

            // unpause
            client.Unpause();
            server.Unpause();

            // update both. neither should time out if Unpause has reset the
            // timeout.
            UpdateSeveralTimes();
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
#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, $"KCP Connection dead_link detected: a message was retransmitted {MaxRetransmits} times without ack. Disconnecting.");
#endif
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
                server.Send(connectionId, new ArraySegment<byte>(message), KcpChannel.Reliable);
            }

            // no need to log thousands of messages. that would take forever.
            client.OnData = (_, _) => {};

            // update should detect the choked connection and disconnect it.
#if UNITY_2018_3_OR_NEWER
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("KCP: disconnecting connection because it can't process data fast enough.*"));
#endif
            UpdateSeveralTimes();

            // client should've disconnected, server should've dropped it
            Assert.That(client.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        // client paused test to make sure we can savely support scene changes
        // in Mirror by calling Pause during the receive while loop to stop
        // receiving immediately!
        [Test]
        public void ClientImmediatelyStopsReceivingWhenPaused()
        {
            // pause client in the middle of a receive while loop by sending two
            // messages and pausing immediately after the first one.
            client.OnData = (message, channel) => {
                ClientOnData(message, channel);
                client.Pause();
            };

            // start
            server.Start(Port);
            ConnectClientBlocking();
            int connectionId = ServerFirstConnectionId();

            // send two messages to client
            server.Send(connectionId, new ArraySegment<byte>(new byte[]{0x03, 0x04}), KcpChannel.Reliable);
            server.Send(connectionId, new ArraySegment<byte>(new byte[]{0x05, 0x06}), KcpChannel.Reliable);

            // update should process only the first message and then stop.
            UpdateSeveralTimes();
            Assert.That(clientReceived.Count, Is.EqualTo(1));
            Assert.That(clientReceived[0].data.SequenceEqual(new byte[]{0x03, 0x04}), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));

            // unpause again make sure the second message is received (not dropped!)
            client.Unpause();
            UpdateSeveralTimes();
            Assert.That(clientReceived.Count, Is.EqualTo(2));
            Assert.That(clientReceived[0].data.SequenceEqual(new byte[]{0x03, 0x04}), Is.True);
            Assert.That(clientReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));
            Assert.That(clientReceived[1].data.SequenceEqual(new byte[]{0x05, 0x06}), Is.True);
            Assert.That(clientReceived[1].channel, Is.EqualTo(KcpChannel.Reliable));
        }

        // server paused test to make sure we can savely support scene changes
        // in Mirror by calling Pause during the receive while loop to stop
        // receiving immediately!
        [Test]
        public void ServerImmediatelyStopsReceivingWhenPaused()
        {
            // pause server in the middle of a receive while loop by sending two
            // messages and pausing immediately after the first one.
            server.OnData = (connectionId, message, channel) => {
                ServerOnData(connectionId, message, channel);
                server.Pause();
            };

            // start
            server.Start(Port);
            ConnectClientBlocking();

            // send two messages to server
            client.Send(new ArraySegment<byte>(new byte[]{0x03, 0x04}), KcpChannel.Reliable);
            client.Send(new ArraySegment<byte>(new byte[]{0x05, 0x06}), KcpChannel.Reliable);

            // update should process only the first message and then stop.
            UpdateSeveralTimes();
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].data.SequenceEqual(new byte[]{0x03, 0x04}), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));

            // unpause again make sure the second message is received (not dropped!)
            server.Unpause();
            UpdateSeveralTimes();
            Assert.That(serverReceived.Count, Is.EqualTo(2));
            Assert.That(serverReceived[0].data.SequenceEqual(new byte[]{0x03, 0x04}), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(KcpChannel.Reliable));
            Assert.That(serverReceived[1].data.SequenceEqual(new byte[]{0x05, 0x06}), Is.True);
            Assert.That(serverReceived[1].channel, Is.EqualTo(KcpChannel.Reliable));
        }
    }
}
