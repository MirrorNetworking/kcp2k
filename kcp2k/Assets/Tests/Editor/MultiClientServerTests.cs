// test with multiple clients.
// to reproduce https://github.com/MirrorNetworking/Mirror/issues/3296
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

// DON'T import UnityEngine. kcp2k should be platform independent.

namespace kcp2k.Tests
{
    public class MultipleClientServerTests
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

        protected KcpClient clientA;
        protected List<Message> clientReceivedA;

        protected KcpClient clientB;
        protected List<Message> clientReceivedB;

        // setup ///////////////////////////////////////////////////////////////
        protected void ClientOnDataA(ArraySegment<byte> message, KcpChannel channel)
        {
            byte[] copy = new byte[message.Count];
            Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);
            clientReceivedA.Add(new Message(copy, channel));
        }

        protected void ClientOnDataB(ArraySegment<byte> message, KcpChannel channel)
        {
            byte[] copy = new byte[message.Count];
            Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);
            clientReceivedB.Add(new Message(copy, channel));
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
                (connectionId, error, reason) => Log.Warning($"connId={connectionId}: {error}, {reason}"),
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
        protected virtual void CreateClients()
        {
            clientA = new KcpClient(
                () => {},
                ClientOnDataA,
                () => {},
                (error, reason) => Log.Warning($"A: {error}, {reason}")
            );
            clientB = new KcpClient(
                () => {},
                ClientOnDataB,
                () => {},
                (error, reason) => Log.Warning($"B: {error}, {reason}")
            );
        }

        [SetUp]
        public void SetUp()
        {
            SetupLogging();

            // create new server & client received list for each test
            serverReceived = new List<Message>();
            clientReceivedA = new List<Message>();
            clientReceivedB = new List<Message>();

            // create server & client
            CreateServer();
            CreateClients();
        }

        [TearDown]
        public void TearDown()
        {
            clientA.Disconnect();
            clientB.Disconnect();
            server.Stop();
        }

        // helpers /////////////////////////////////////////////////////////////
        int ServerFirstConnectionId()
        {
            return server.connections.First().Key;
        }

        int ServerLastConnectionId()
        {
            return server.connections.Last().Key;
        }

        void UpdateSeveralTimes()
        {
            // update serveral times to avoid flaky tests.
            // => need to update at 120 times for default maxed sized messages
            //    where it requires 120+ fragments.
            // => need to update even more often for 2x default max sized
            for (int i = 0; i < 500; ++i)
            {
                clientA.Tick();
                clientB.Tick();
                server.Tick();
                // update 'interval' milliseconds.
                // the lower the interval, the faster the tests will run.
                Thread.Sleep((int)Interval);
            }
        }

        // connect and give it enough time to handle
        void ConnectClientsBlocking(string hostname = "127.0.0.1")
        {
            clientA.Connect(hostname, Port, NoDelay, Interval, 0, true, SendWindowSize, ReceiveWindowSize, Timeout, MaxRetransmits);
            UpdateSeveralTimes();

            clientB.Connect(hostname, Port, NoDelay, Interval, 0, true, SendWindowSize, ReceiveWindowSize, Timeout, MaxRetransmits);
            UpdateSeveralTimes();
        }

        // disconnect and give it enough time to handle
        void DisconnectClientsBlocking()
        {
            clientA.Disconnect();
            clientB.Disconnect();
            UpdateSeveralTimes();
        }

        // kick and give it enough time to handle
        void KickClientBlocking(int connectionId)
        {
            server.Disconnect(connectionId);
            UpdateSeveralTimes();
        }

        void SendClientToServerBlocking(KcpClient client, ArraySegment<byte> message, KcpChannel channel)
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
        public void ConnectAndDisconnectClients()
        {
            // connect
            server.Start(Port);
            ConnectClientsBlocking();

            Assert.That(clientA.connected, Is.True);
            Assert.That(clientB.connected, Is.True);
            Assert.That(server.connections.Count, Is.EqualTo(2));

            // disconnect
            DisconnectClientsBlocking();
            Assert.That(clientA.connected, Is.False);
            Assert.That(clientB.connected, Is.False);
            Assert.That(server.connections.Count, Is.EqualTo(0));
        }

        // need to make sure that connect->disconnect->connect works too
        // (to detect cases where connect/disconnect might not clean up properly)
        [Test]
        public void ConnectAndDisconnectClientsMultipleTimes()
        {
            server.Start(Port);

            for (int i = 0; i < 10; ++i)
            {
                ConnectClientsBlocking();
                Assert.That(clientA.connected, Is.True);
                Assert.That(clientB.connected, Is.True);
                Assert.That(server.connections.Count, Is.EqualTo(2));

                DisconnectClientsBlocking();
                Assert.That(clientA.connected, Is.False);
                Assert.That(clientB.connected, Is.False);
                Assert.That(server.connections.Count, Is.EqualTo(0));
            }

            server.Stop();
        }

        [Test]
        [TestCase(KcpChannel.Reliable)]
        [TestCase(KcpChannel.Unreliable)]
        public void ClientsToServerMessage(KcpChannel channel)
        {
            server.Start(Port);
            ConnectClientsBlocking();

            byte[] message = {0x01, 0x02};

            SendClientToServerBlocking(clientA, new ArraySegment<byte>(message), channel);
            Assert.That(serverReceived.Count, Is.EqualTo(1));
            Assert.That(serverReceived[0].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[0].channel, Is.EqualTo(channel));

            SendClientToServerBlocking(clientB, new ArraySegment<byte>(message), channel);
            Assert.That(serverReceived.Count, Is.EqualTo(2));
            Assert.That(serverReceived[1].data.SequenceEqual(message), Is.True);
            Assert.That(serverReceived[1].channel, Is.EqualTo(channel));
        }

        [Test]
        public void ServerToClientsReliableMessage()
        {
            server.Start(Port);
            ConnectClientsBlocking();
            int connectionIdA = ServerFirstConnectionId();
            int connectionIdB = ServerLastConnectionId();

            byte[] message = {0x03, 0x04};

            SendServerToClientBlocking(connectionIdA, new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(clientReceivedA.Count, Is.EqualTo(1));
            Assert.That(clientReceivedA[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceivedA[0].channel, Is.EqualTo(KcpChannel.Reliable));

            SendServerToClientBlocking(connectionIdB, new ArraySegment<byte>(message), KcpChannel.Reliable);
            Assert.That(clientReceivedB.Count, Is.EqualTo(1));
            Assert.That(clientReceivedB[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceivedB[0].channel, Is.EqualTo(KcpChannel.Reliable));
        }

        [Test]
        public void ServerToClientUnreliableMessage()
        {
            server.Start(Port);
            ConnectClientsBlocking();
            int connectionIdA = ServerFirstConnectionId();
            int connectionIdB = ServerLastConnectionId();

            byte[] message = {0x03, 0x04};

            SendServerToClientBlocking(connectionIdA, new ArraySegment<byte>(message), KcpChannel.Unreliable);
            Assert.That(clientReceivedA.Count, Is.EqualTo(1));
            Assert.That(clientReceivedA[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceivedA[0].channel, Is.EqualTo(KcpChannel.Unreliable));

            SendServerToClientBlocking(connectionIdB, new ArraySegment<byte>(message), KcpChannel.Unreliable);
            Assert.That(clientReceivedB.Count, Is.EqualTo(1));
            Assert.That(clientReceivedB[0].data.SequenceEqual(message), Is.True);
            Assert.That(clientReceivedB[0].channel, Is.EqualTo(KcpChannel.Unreliable));
        }
    }
}
