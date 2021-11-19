using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp2
{
	[EventSource(Name = "Microsoft-System-Net-WebSockets-Client")]
	internal sealed class NetEventSource
	{
		public static bool IsEnabled;

		public static void Enter(object obj)
		{
		}

		public static void Exit(object obj)
		{
		}

		public static void Error(object obj, Exception ex)
		{
		}
	}

	public class ClientWebSocket:WebSocket
    {
		private enum InternalState
		{
			Created,
			Connecting,
			Connected,
			Disposed
		}

		private static ClientWebSocketOptions _options;

		private WebSocketHandle _innerWebSocket;

		private int _state;

		public ClientWebSocketOptions Options => _options;

		public override WebSocketCloseStatus? CloseStatus
		{
			get
			{
				if (WebSocketHandle.IsValid(_innerWebSocket))
				{
					return _innerWebSocket.CloseStatus;
				}
				return null;
			}
		}

		public override string CloseStatusDescription
		{
			get
			{
				if (WebSocketHandle.IsValid(_innerWebSocket))
				{
					return _innerWebSocket.CloseStatusDescription;
				}
				return null;
			}
		}

		public override string SubProtocol
		{
			get
			{
				if (WebSocketHandle.IsValid(_innerWebSocket))
				{
					return _innerWebSocket.SubProtocol;
				}
				return null;
			}
		}

		public override WebSocketState State
		{
			get
			{
				if (WebSocketHandle.IsValid(_innerWebSocket))
				{
					return _innerWebSocket.State;
				}
				if (_state == 0)
					return WebSocketState.None;
				if (_state == 1)
					return WebSocketState.Connecting;
				else
					return WebSocketState.Closed;
			}
		}

		public ClientWebSocket()
		{
			if (NetEventSource.IsEnabled)
			{
				NetEventSource.Enter(this);
			}
			WebSocketHandle.CheckPlatformSupport();
			_state = 0;
			_options = new ClientWebSocketOptions();
			if (NetEventSource.IsEnabled)
			{
				NetEventSource.Exit(this);
			}
		}

		public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
		{
			if (uri == null)
			{
				throw new ArgumentNullException("uri");
			}
			if (!uri.IsAbsoluteUri)
			{
				throw new ArgumentException(SR.net_uri_NotAbsolute, "uri");
			}
			if (uri.Scheme != "ws" && uri.Scheme != "wss")
			{
				throw new ArgumentException(SR.net_WebSockets_Scheme, "uri");
			}
			switch (Interlocked.CompareExchange(ref _state, 1, 0))
			{
				case 3:
					throw new ObjectDisposedException(GetType().FullName);
				default:
					throw new InvalidOperationException(SR.net_WebSockets_AlreadyStarted);
				case 0:
					_options.SetToReadOnly();
					return ConnectAsyncCore(uri, cancellationToken);
			}
		}

		private async Task ConnectAsyncCore(Uri uri, CancellationToken cancellationToken)
		{
			_innerWebSocket = WebSocketHandle.Create();
			try
			{
				if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
				{
					throw new ObjectDisposedException(GetType().FullName);
				}
				await _innerWebSocket.ConnectAsyncCore(uri, cancellationToken, _options).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				if (NetEventSource.IsEnabled)
				{
					NetEventSource.Error(this, ex);
				}
				throw;
			}
		}

		public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
		{
			ThrowIfNotConnected();
			return _innerWebSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
		}

		public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
		{
			ThrowIfNotConnected();
			return _innerWebSocket.ReceiveAsync(buffer, cancellationToken);
		}

		public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			ThrowIfNotConnected();
			return _innerWebSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
		}

		public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			ThrowIfNotConnected();
			return _innerWebSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
		}

		public override void Abort()
		{
			if (_state != 3)
			{
				if (WebSocketHandle.IsValid(_innerWebSocket))
				{
					_innerWebSocket.Abort();
				}
				Dispose();
			}
		}

		public override void Dispose()
		{
			if (Interlocked.Exchange(ref _state, 3) != 3 && WebSocketHandle.IsValid(_innerWebSocket))
			{
				_innerWebSocket.Dispose();
			}
		}

		private void ThrowIfNotConnected()
		{
			if (_state == 3)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}
			if (_state != 2)
			{
				throw new InvalidOperationException(SR.net_WebSockets_NotConnected);
			}
		}
	}
}
