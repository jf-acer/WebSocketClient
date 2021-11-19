using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp2
{
    static class Extention
    {
		internal static class WebSocketUtil
		{
			public static ManagedWebSocket CreateClientWebSocket(Stream innerStream, string subProtocol, int receiveBufferSize, int sendBufferSize, TimeSpan keepAliveInterval, bool useZeroMaskingKey, ArraySegment<byte> internalBuffer)
			{
				if (innerStream == null)
				{
					throw new ArgumentNullException("innerStream");
				}
				if (!innerStream.CanRead || !innerStream.CanWrite)
				{
					throw new ArgumentException((!innerStream.CanRead) ? SR.NotReadableStream : SR.NotWriteableStream, "innerStream");
				}
				if (subProtocol != null)
				{
					WebSocketValidate.ValidateSubprotocol(subProtocol);
				}
				if (keepAliveInterval != Timeout.InfiniteTimeSpan && keepAliveInterval < TimeSpan.Zero)
				{
					throw new ArgumentOutOfRangeException("keepAliveInterval", keepAliveInterval, SR.Format(SR.net_WebSockets_ArgumentOutOfRange_TooSmall, 0));
				}
				if (receiveBufferSize <= 0 || sendBufferSize <= 0)
				{
					throw new ArgumentOutOfRangeException((receiveBufferSize <= 0) ? "receiveBufferSize" : "sendBufferSize", (receiveBufferSize <= 0) ? receiveBufferSize : sendBufferSize, SR.Format(SR.net_WebSockets_ArgumentOutOfRange_TooSmall, 0));
				}
				return ManagedWebSocket.CreateFromConnectedStream(innerStream, isServer: false, subProtocol, keepAliveInterval, receiveBufferSize, internalBuffer);
			}
		}
		internal static string SubstringTrim(this string value, int startIndex)
		{
			return value.SubstringTrim(startIndex, value.Length - startIndex);
		}

		public static string GetIdnHost(this Uri uri)
        {
            return new IdnMapping().GetAscii(uri.Host);
        }
		internal static string SubstringTrim(this string value, int startIndex, int length)
		{
			if (length == 0)
			{
				return string.Empty;
			}
			int num = startIndex + length - 1;
			while (startIndex <= num && char.IsWhiteSpace(value[startIndex]))
			{
				startIndex++;
			}
			while (num >= startIndex && char.IsWhiteSpace(value[num]))
			{
				num--;
			}
			int num2 = num - startIndex + 1;
			if (num2 != 0)
			{
				if (num2 != value.Length)
				{
					return value.Substring(startIndex, num2);
				}
				return value;
			}
			return string.Empty;
		}

	}

    internal static class SocketExtensions
    {
        public static Task ConnectAsync(this Socket socket, IPAddress address, int port)
        {
            return Task.Factory.FromAsync((IPAddress targetAddress, int targetPort, AsyncCallback callback, object state) => ((Socket)state).BeginConnect(targetAddress, targetPort, callback, state), delegate (IAsyncResult asyncResult)
            {
                ((Socket)asyncResult.AsyncState).EndConnect(asyncResult);
            }, address, port, socket);
        }
    }
}
