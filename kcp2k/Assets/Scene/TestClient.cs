using System;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestClient : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;

        public void Connect(string ip)
        {
        }

        public void Send(ArraySegment<byte> segment)
        {
        }

        public void Disconnect()
        {
        }

        // MonoBehaviour ///////////////////////////////////////////////////////
        public void LateUpdate()
        {
        }
    }
}