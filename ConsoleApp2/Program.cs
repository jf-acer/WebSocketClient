using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ConsoleApp2
{
    class Program
    {
        static void Main(string[] args)
        {
            ClientWebSocket clientWebSocket = new ClientWebSocket();
            clientWebSocket.ConnectAsync(new Uri("ws://10.88.22.82:1789"),CancellationToken.None).Wait();
            //发送消息
            while (true)
            {
                ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes("ok"));
                clientWebSocket.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                Thread.Sleep(1000);
            }
        }
    }
}
