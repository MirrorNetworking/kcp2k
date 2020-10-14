using System;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestServer : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;

        public void StartServer()
        {
        }

        public void Send(int connectionId, ArraySegment<byte> segment)
        {
        }

        public bool Disconnect(int connectionId)
        {
            return false;
        }

        public string GetAddress(int connectionId)
        {
            return "";
        }

        public void StopServer()
        {
        }

        // MonoBehaviour ///////////////////////////////////////////////////////
        public void LateUpdate()
        {
        }
    }
}
