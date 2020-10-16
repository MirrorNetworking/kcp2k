using System;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
            Thread.Sleep(100);
            client.LateUpdate();
            server.LateUpdate();
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
            LogAssert.Expect(LogType.Log, new Regex("KCP: starting server"));
            LogAssert.Expect(LogType.Log, "KCP: server started");
            server.StartServer();

            LogAssert.Expect(LogType.Log, "KCP: server stopped");
            server.StopServer();
        }
    }
}
