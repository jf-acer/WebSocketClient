using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ConsoleApp2.Extention;
using static ConsoleApp2.SecurityPackageInfo;

namespace ConsoleApp2
{
    class WebSocketHandle
	{
		[ThreadStatic]
		private static StringBuilder t_cachedStringBuilder;

		/// <summary>Default encoding for HTTP requests. Latin alphabeta no 1, ISO/IEC 8859-1.</summary>
		private static readonly Encoding s_defaultHttpEncoding = Encoding.GetEncoding(28591);

		/// <summary>Size of the receive buffer to use.</summary>
		private const int DefaultReceiveBufferSize = 4096;

		/// <summary>GUID appended by the server as part of the security key response.  Defined in the RFC.</summary>
		private const string WSServerGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

		private readonly CancellationTokenSource _abortSource = new CancellationTokenSource();

		private WebSocketState _state = WebSocketState.Connecting;

		private ManagedWebSocket _webSocket;

		public WebSocketCloseStatus? CloseStatus => _webSocket?.CloseStatus;

		public string CloseStatusDescription => _webSocket?.CloseStatusDescription;

		public WebSocketState State => _webSocket?.State ?? _state;

		public string SubProtocol => _webSocket?.SubProtocol;

		public static WebSocketHandle Create()
		{
			return new WebSocketHandle();
		}

		public static bool IsValid(WebSocketHandle handle)
		{
			return handle != null;
		}

		public static void CheckPlatformSupport()
		{
		}

		public void Dispose()
		{
			_state = WebSocketState.Closed;
			_webSocket?.Dispose();
		}

		public void Abort()
		{
			_abortSource.Cancel();
			_webSocket?.Abort();
		}

		public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
		{
			return _webSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
		}

		public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
		{
			return _webSocket.ReceiveAsync(buffer, cancellationToken);
		}

		public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			return _webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
		}

		public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			return _webSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
		}

		public async Task ConnectAsyncCore(Uri uri, CancellationToken cancellationToken, ClientWebSocketOptions options)
		{
			CancellationTokenRegistration registration = cancellationToken.Register(delegate (object s)
			{
				((WebSocketHandle)s).Abort();
			}, this);
			try
			{
				Uri httpUri = new UriBuilder(uri)
				{
					Scheme = ((uri.Scheme == "ws") ? "http" : "https")
				}.Uri;
				Uri connectUri = httpUri;
				bool useProxy = false;
				if (options.Proxy != null && !options.Proxy.IsBypassed(httpUri))
				{
					useProxy = true;
					connectUri = options.Proxy.GetProxy(httpUri);
				}
				Stream stream = new NetworkStream(await ConnectSocketAsync(connectUri.Host, connectUri.Port, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), ownsSocket: true);
				if (useProxy)
				{
					stream = await EstablishTunnelTrhoughWebProxy(stream, httpUri, connectUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (httpUri.Scheme == "https")
				{
					SslStream sslStream = new SslStream(stream);
					await sslStream.AuthenticateAsClientAsync(httpUri.Host, options.ClientCertificates, SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12, checkCertificateRevocation: false).ConfigureAwait(continueOnCapturedContext: false);
					stream = sslStream;
				}
				KeyValuePair<string, string> secKeyAndSecWebSocketAccept = CreateSecKeyAndSecWebSocketAccept();
				byte[] array = BuildRequestHeader(uri, options, secKeyAndSecWebSocketAccept.Key);
				await stream.WriteAsync(array, 0, array.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				_webSocket = WebSocketUtil.CreateClientWebSocket(stream, await ParseAndValidateConnectResponseAsync(stream, options, secKeyAndSecWebSocketAccept.Value, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), options.ReceiveBufferSize, options.SendBufferSize, options.KeepAliveInterval, useZeroMaskingKey: false, options.Buffer.GetValueOrDefault());
				if (_state == WebSocketState.Aborted)
				{
					_webSocket.Abort();
				}
				else if (_state == WebSocketState.Closed)
				{
					_webSocket.Dispose();
				}
			}
			catch (Exception ex)
			{
				if (_state < WebSocketState.Closed)
				{
					_state = WebSocketState.Closed;
				}
				Abort();
				if (ex is WebSocketException)
				{
					throw;
				}
				throw new WebSocketException(/*SR.net_webstatus_ConnectFailure, ex*/);
			}
			finally
			{
				registration.Dispose();
			}
		}

		private async Task<Stream> EstablishTunnelTrhoughWebProxy(Stream stream, Uri httpUri, Uri proxyUri, CancellationToken cancellationToken)
		{
			byte[] bytes = s_defaultHttpEncoding.GetBytes($"CONNECT {httpUri.Host}:{httpUri.Port} HTTP/1.1\r\nHost: {httpUri.Host}:{httpUri.Port}\r\n\r\n");
			await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			string text = await ReadResponseHeaderLineAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (text.StartsWith("HTTP/1.1 407"))
			{
				List<string> authPackages = new List<string>();
				string text2;
				while (!string.IsNullOrEmpty(text2 = await ReadResponseHeaderLineAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
				{
					if (text2.ToLowerInvariant().StartsWith("proxy-authenticate: "))
					{
						authPackages.Add(text2.ToLowerInvariant().Substring("proxy-authenticate: ".Length));
					}
				}
				stream.Close();
				stream = new NetworkStream(await ConnectSocketAsync(proxyUri.Host, proxyUri.Port, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), ownsSocket: true);
				await AuthenticateProxyStream(stream, authPackages, proxyUri.Host, httpUri, cancellationToken);
			}
			else if (text.StartsWith("HTTP/1.1 200"))
			{
				while (!string.IsNullOrEmpty(await ReadResponseHeaderLineAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
				{
				}
			}
			return stream;
		}

		private async Task AuthenticateProxyStream(Stream stream, List<string> proxyAuthPackages, string proxyHost, Uri targetUrl, CancellationToken cancellationToken)
		{
			SecurityPackageInfo[] source = SSPIClient.EnumerateSecurityPackages();
			string packageToUse = "NTLM";
			foreach (string package in proxyAuthPackages)
			{
				try
				{
					SecurityPackageInfo securityPackageInfo = source.FirstOrDefault((SecurityPackageInfo p) => p.Name.ToLowerInvariant() == package);
					SecurityCapabilities securityCapabilities = SecurityCapabilities.SupportsConnections | SecurityCapabilities.AccepsWin32Names;
					if ((securityPackageInfo.Capabilities & securityCapabilities) != 0)
					{
						packageToUse = securityPackageInfo.Name;
						goto IL_0098;
					}
				}
				catch (Exception)
				{
				}
			}
			goto IL_0098;
			IL_0098:
			SSPIClient sspi = new SSPIClient(packageToUse);
			byte[] serverToken = null;
			bool authSucceeded = false;
			string clientToken = Convert.ToBase64String(sspi.GetClientToken(serverToken));
			while (!authSucceeded)
			{
				string text = "Proxy-Authorization: " + packageToUse + " " + clientToken;
				byte[] bytes = s_defaultHttpEncoding.GetBytes($"CONNECT {targetUrl.Host}:{targetUrl.Port} HTTP/1.1\r\nHost: {targetUrl.Host}:{targetUrl.Port}\r\n{text}\r\n\r\n");
				await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				string statusline = await ReadResponseHeaderLineAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				string text2;
				while (!string.IsNullOrEmpty(text2 = await ReadResponseHeaderLineAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
				{
					if (text2.ToLowerInvariant().StartsWith("proxy-authenticate: " + packageToUse.ToLowerInvariant()))
					{
						serverToken = Convert.FromBase64String(text2.Substring(("proxy-authenticate: " + packageToUse.ToLowerInvariant()).Length));
					}
				}
				if (statusline.StartsWith("HTTP/1.1 200"))
				{
					authSucceeded = true;
				}
				else
				{
					clientToken = Convert.ToBase64String(sspi.GetClientToken(serverToken));
				}
			}
		}

		/// <summary>Connects a socket to the specified host and port, subject to cancellation and aborting.</summary>
		/// <param name="host">The host to which to connect.</param>
		/// <param name="port">The port to which to connect on the host.</param>
		/// <param name="cancellationToken">The CancellationToken to use to cancel the websocket.</param>
		/// <returns>The connected Socket.</returns>
		private async Task<Socket> ConnectSocketAsync(string host, int port, CancellationToken cancellationToken)
		{
			IPAddress[] array = await Dns.GetHostAddressesAsync(host).ConfigureAwait(continueOnCapturedContext: false);
			ExceptionDispatchInfo exceptionDispatchInfo = null;
			IPAddress[] array2 = array;
			foreach (IPAddress iPAddress in array2)
			{
				Socket socket = new Socket(iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				try
				{
					using (cancellationToken.Register(delegate (object s)
					{
						((Socket)s).Dispose();
					}, socket))
					{
						using (_abortSource.Token.Register(delegate (object s)
						{
							((Socket)s).Dispose();
						}, socket))
						{
							_ = 1;
							try
							{
								await SocketExtensions.ConnectAsync(socket, iPAddress, port).ConfigureAwait(continueOnCapturedContext: false);
							}
							catch (ObjectDisposedException innerException)
							{
								CancellationToken token = (cancellationToken.IsCancellationRequested ? cancellationToken : _abortSource.Token);
								if (token.IsCancellationRequested)
								{
									throw new OperationCanceledException(new OperationCanceledException().Message, innerException, token);
								}
							}
						}
					}
					cancellationToken.ThrowIfCancellationRequested();
					_abortSource.Token.ThrowIfCancellationRequested();
					return socket;
				}
				catch (Exception source)
				{
					socket.Dispose();
					exceptionDispatchInfo = ExceptionDispatchInfo.Capture(source);
				}
			}
			exceptionDispatchInfo?.Throw();
			throw new WebSocketException(/*SR.net_webstatus_ConnectFailure*/);
		}

		

		/// <summary>Creates a byte[] containing the headers to send to the server.</summary>
		/// <param name="uri">The Uri of the server.</param>
		/// <param name="options">The options used to configure the websocket.</param>
		/// <param name="secKey">The generated security key to send in the Sec-WebSocket-Key header.</param>
		/// <returns>The byte[] containing the encoded headers ready to send to the network.</returns>
		private static byte[] BuildRequestHeader(Uri uri, ClientWebSocketOptions options, string secKey)
		{
			StringBuilder stringBuilder = t_cachedStringBuilder ?? (t_cachedStringBuilder = new StringBuilder());
			try
			{
				stringBuilder.Append("GET ").Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
				string value = options.RequestHeaders["Host"];
				stringBuilder.Append("Host: ");
				if (string.IsNullOrEmpty(value))
				{
					stringBuilder.Append(uri.GetIdnHost()).Append(':').Append(uri.Port)
						.Append("\r\n");
				}
				else
				{
					stringBuilder.Append(value).Append("\r\n");
				}
				stringBuilder.Append("Connection: Upgrade\r\n");
				stringBuilder.Append("Upgrade: websocket\r\n");
				stringBuilder.Append("Sec-WebSocket-Version: 13\r\n");
				stringBuilder.Append("Sec-WebSocket-Key: ").Append(secKey).Append("\r\n");
				string[] allKeys = options.RequestHeaders.AllKeys;
				foreach (string text in allKeys)
				{
					if (!string.Equals(text, "Host", StringComparison.OrdinalIgnoreCase))
					{
						stringBuilder.Append(text).Append(": ").Append(options.RequestHeaders[text])
							.Append("\r\n");
					}
				}
				if (options.RequestedSubProtocols.Count > 0)
				{
					stringBuilder.Append("Sec-WebSocket-Protocol").Append(": ");
					stringBuilder.Append(options.RequestedSubProtocols[0]);
					for (int j = 1; j < options.RequestedSubProtocols.Count; j++)
					{
						stringBuilder.Append(", ").Append(options.RequestedSubProtocols[j]);
					}
					stringBuilder.Append("\r\n");
				}
				if (options.Cookies != null)
				{
					string cookieHeader = options.Cookies.GetCookieHeader(uri);
					if (!string.IsNullOrWhiteSpace(cookieHeader))
					{
						stringBuilder.Append("Cookie").Append(": ").Append(cookieHeader)
							.Append("\r\n");
					}
				}
				stringBuilder.Append("\r\n");
				return s_defaultHttpEncoding.GetBytes(stringBuilder.ToString());
			}
			finally
			{
				stringBuilder.Clear();
			}
		}

		/// <summary>
		/// Creates a pair of a security key for sending in the Sec-WebSocket-Key header and
		/// the associated response we expect to receive as the Sec-WebSocket-Accept header value.
		/// </summary>
		/// <returns>A key-value pair of the request header security key and expected response header value.</returns>
		private static KeyValuePair<string, string> CreateSecKeyAndSecWebSocketAccept()
		{
			string text = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
			SHA1 sHA = SHA1.Create();
			return new KeyValuePair<string, string>(text, Convert.ToBase64String(sHA.ComputeHash(Encoding.ASCII.GetBytes(text + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))));
		}

		/// <summary>Read and validate the connect response headers from the server.</summary>
		/// <param name="stream">The stream from which to read the response headers.</param>
		/// <param name="options">The options used to configure the websocket.</param>
		/// <param name="expectedSecWebSocketAccept">The expected value of the Sec-WebSocket-Accept header.</param>
		/// <param name="cancellationToken">The CancellationToken to use to cancel the websocket.</param>
		/// <returns>The agreed upon subprotocol with the server, or null if there was none.</returns>
		private async Task<string> ParseAndValidateConnectResponseAsync(Stream stream,ClientWebSocketOptions options, string expectedSecWebSocketAccept, CancellationToken cancellationToken)
		{
			string text = await ReadResponseHeaderLineAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrEmpty(text))
			{
				throw new WebSocketException(/*SR.Format(SR.net_webstatus_ConnectFailure)*/);
			}
			if (!text.StartsWith("HTTP/1.1 ", StringComparison.Ordinal) || text.Length < "HTTP/1.1 101".Length)
			{
				throw new WebSocketException(WebSocketError.HeaderError);
			}
			if (!text.StartsWith("HTTP/1.1 101", StringComparison.Ordinal) || (text.Length > "HTTP/1.1 101".Length && !char.IsWhiteSpace(text["HTTP/1.1 101".Length])))
			{
				throw new WebSocketException(/*SR.net_webstatus_ConnectFailure*/);
			}
			bool foundUpgrade = false;
			bool foundConnection = false;
			bool foundSecWebSocketAccept = false;
			string subprotocol = null;
			string text2;
			while (!string.IsNullOrEmpty(text2 = await ReadResponseHeaderLineAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
			{
				int num = text2.IndexOf(':');
				if (num == -1)
				{
					throw new WebSocketException(WebSocketError.HeaderError);
				}
				string text3 = text2.SubstringTrim(0, num);
				string headerValue = text2.SubstringTrim(num + 1);
				ValidateAndTrackHeader("Connection", "Upgrade", text3, headerValue, ref foundConnection);
				ValidateAndTrackHeader("Upgrade", "websocket", text3, headerValue, ref foundUpgrade);
				ValidateAndTrackHeader("Sec-WebSocket-Accept", expectedSecWebSocketAccept, text3, headerValue, ref foundSecWebSocketAccept);
				if (string.Equals("Sec-WebSocket-Protocol", text3, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(headerValue))
				{
					string text4 = options.RequestedSubProtocols.Find((string requested) => string.Equals(requested, headerValue, StringComparison.OrdinalIgnoreCase));
					if (text4 == null || subprotocol != null)
					{
						throw new WebSocketException(/*WebSocketError.UnsupportedProtocol, SR.Format(SR.net_WebSockets_AcceptUnsupportedProtocol, string.Join(", ", options.RequestedSubProtocols), subprotocol)*/);
					}
					subprotocol = text4;
				}
			}
			if (!foundUpgrade || !foundConnection || !foundSecWebSocketAccept)
			{
				throw new WebSocketException(/*SR.net_webstatus_ConnectFailure*/);
			}
			return subprotocol;
		}

		/// <summary>Validates a received header against expected values and tracks that we've received it.</summary>
		/// <param name="targetHeaderName">The header name against which we're comparing.</param>
		/// <param name="targetHeaderValue">The header value against which we're comparing.</param>
		/// <param name="foundHeaderName">The actual header name received.</param>
		/// <param name="foundHeaderValue">The actual header value received.</param>
		/// <param name="foundHeader">A bool tracking whether this header has been seen.</param>
		private static void ValidateAndTrackHeader(string targetHeaderName, string targetHeaderValue, string foundHeaderName, string foundHeaderValue, ref bool foundHeader)
		{
			bool flag = string.Equals(targetHeaderName, foundHeaderName, StringComparison.OrdinalIgnoreCase);
			if (!foundHeader)
			{
				if (flag)
				{
					if (!string.Equals(targetHeaderValue, foundHeaderValue, StringComparison.OrdinalIgnoreCase))
					{
						throw new WebSocketException(/*SR.Format(SR.net_WebSockets_InvalidResponseHeader, targetHeaderName, foundHeaderValue)*/);
					}
					foundHeader = true;
				}
			}
			else if (flag)
			{
				throw new WebSocketException(/*SR.Format(SR.net_webstatus_ConnectFailure)*/);
			}
		}

		/// <summary>Reads a line from the stream.</summary>
		/// <param name="stream">The stream from which to read.</param>
		/// <param name="cancellationToken">The CancellationToken used to cancel the websocket.</param>
		/// <returns>The read line, or null if none could be read.</returns>
		private static async Task<string> ReadResponseHeaderLineAsync(Stream stream, CancellationToken cancellationToken)
		{
			StringBuilder sb = t_cachedStringBuilder;
			if (sb != null)
			{
				t_cachedStringBuilder = null;
			}
			else
			{
				sb = new StringBuilder();
			}
			byte[] arr = new byte[1];
			char prevChar = '\0';
			try
			{
				while (await stream.ReadAsync(arr, 0, 1, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) == 1)
				{
					char c = (char)arr[0];
					if (prevChar == '\r' && c == '\n')
					{
						break;
					}
					sb.Append(c);
					prevChar = c;
				}
				if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
				{
					sb.Length--;
				}
				return sb.ToString();
			}
			finally
			{
				sb.Clear();
				t_cachedStringBuilder = sb;
			}
		}
	}
}
