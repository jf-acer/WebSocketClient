using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp2
{
    class WebSocketValidate
    {
		internal const int MaxControlFramePayloadLength = 123;

		private const int CloseStatusCodeAbort = 1006;

		private const int CloseStatusCodeFailedTLSHandshake = 1015;

		private const int InvalidCloseStatusCodesFrom = 0;

		private const int InvalidCloseStatusCodesTo = 999;

		private const string Separators = "()<>@,;:\\\"/[]?={} ";

		internal static void ThrowIfInvalidState(WebSocketState currentState, bool isDisposed, WebSocketState[] validStates)
		{
			string text = string.Empty;
			if (validStates != null && validStates.Length != 0)
			{
				foreach (WebSocketState webSocketState in validStates)
				{
					if (currentState == webSocketState)
					{
						if (isDisposed)
						{
							throw new ObjectDisposedException("ClientWebSocket");
						}
						return;
					}
				}
				text = string.Join(", ", validStates);
			}
			throw new WebSocketException(/*WebSocketError.InvalidState, SR.Format(SR.net_WebSockets_InvalidState, currentState, text)*/);
		}

		internal static void ValidateSubprotocol(string subProtocol)
		{
			if (string.IsNullOrWhiteSpace(subProtocol))
			{
				throw new ArgumentException(/*SR.net_WebSockets_InvalidEmptySubProtocol, "subProtocol"*/);
			}
			string text = null;
			for (int i = 0; i < subProtocol.Length; i++)
			{
				char c = subProtocol[i];
				if (c < '!' || c > '~')
				{
					text = string.Format(CultureInfo.InvariantCulture, "[{0}]", new object[1] { (int)c });
					break;
				}
				if (!char.IsLetterOrDigit(c) && "()<>@,;:\\\"/[]?={} ".IndexOf(c) >= 0)
				{
					text = c.ToString();
					break;
				}
			}
			if (text != null)
			{
				throw new ArgumentException(/*SR.Format(SR.net_WebSockets_InvalidCharInProtocolString, subProtocol, text), "subProtocol"*/);
			}
		}

		internal static void ValidateCloseStatus(WebSocketCloseStatus closeStatus, string statusDescription)
		{
			if (closeStatus == WebSocketCloseStatus.Empty && !string.IsNullOrEmpty(statusDescription))
			{
				throw new ArgumentException(/*SR.Format(SR.net_WebSockets_ReasonNotNull, statusDescription, WebSocketCloseStatus.Empty), "statusDescription"*/);
			}
			if ((closeStatus >= (WebSocketCloseStatus)0 && closeStatus <= (WebSocketCloseStatus)999) || closeStatus == (WebSocketCloseStatus)1006 || closeStatus == (WebSocketCloseStatus)1015)
			{
				throw new ArgumentException(/*SR.Format(SR.net_WebSockets_InvalidCloseStatusCode, (int)closeStatus), "closeStatus"*/);
			}
			int num = 0;
			if (!string.IsNullOrEmpty(statusDescription))
			{
				num = Encoding.UTF8.GetByteCount(statusDescription);
			}
			if (num > 123)
			{
				throw new ArgumentException(/*SR.Format(SR.net_WebSockets_InvalidCloseStatusDescription, statusDescription, 123), "statusDescription"*/);
			}
		}

		internal static void ThrowPlatformNotSupportedException()
		{
			throw new PlatformNotSupportedException(/*SR.net_WebSockets_UnsupportedPlatform*/);
		}

		internal static void ValidateArraySegment(ArraySegment<byte> arraySegment, string parameterName)
		{
			if (arraySegment.Array == null)
			{
				throw new ArgumentNullException(parameterName + ".Array");
			}
			if (arraySegment.Offset < 0 || arraySegment.Offset > arraySegment.Array.Length)
			{
				throw new ArgumentOutOfRangeException(parameterName + ".Offset");
			}
			if (arraySegment.Count < 0 || arraySegment.Count > arraySegment.Array.Length - arraySegment.Offset)
			{
				throw new ArgumentOutOfRangeException(parameterName + ".Count");
			}
		}

		internal static void ValidateBuffer(byte[] buffer, int offset, int count)
		{
			if (buffer == null)
			{
				throw new ArgumentNullException("buffer");
			}
			if (offset < 0 || offset > buffer.Length)
			{
				throw new ArgumentOutOfRangeException("offset");
			}
			if (count < 0 || count > buffer.Length - offset)
			{
				throw new ArgumentOutOfRangeException("count");
			}
		}
	}
}
