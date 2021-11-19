//using ConsoleApp2.Buffers;
using ConsoleApp2.Buffers;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp2
{
    internal sealed class ManagedWebSocket : WebSocket
	{
		private sealed class Utf8MessageState
		{
			internal bool SequenceInProgress;

			internal int AdditionalBytesExpected;

			internal int ExpectedValueMin;

			internal int CurrentDecodeBits;
		}

		private enum MessageOpcode : byte
		{
			Continuation = 0,
			Text = 1,
			Binary = 2,
			Close = 8,
			Ping = 9,
			Pong = 10
		}

		[StructLayout(LayoutKind.Auto)]
		private struct MessageHeader
		{
			internal MessageOpcode Opcode;

			internal bool Fin;

			internal long PayloadLength;

			internal int Mask;
		}

		/// <summary>Per-thread cached 4-byte mask byte array.</summary>
		[ThreadStatic]
		private static byte[] t_headerMask;

		/// <summary>Thread-safe random number generator used to generate masks for each send.</summary>
		private static readonly RandomNumberGenerator s_random = RandomNumberGenerator.Create();

		/// <summary>Encoding for the payload of text messages: UTF8 encoding that throws if invalid bytes are discovered, per the RFC.</summary>
		private static readonly UTF8Encoding s_textEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

		/// <summary>Valid states to be in when calling SendAsync.</summary>
		private static readonly WebSocketState[] s_validSendStates = new WebSocketState[2]
		{
		WebSocketState.Open,
		WebSocketState.CloseReceived
		};

		/// <summary>Valid states to be in when calling ReceiveAsync.</summary>
		private static readonly WebSocketState[] s_validReceiveStates = new WebSocketState[2]
		{
		WebSocketState.Open,
		WebSocketState.CloseSent
		};

		/// <summary>Valid states to be in when calling CloseOutputAsync.</summary>
		private static readonly WebSocketState[] s_validCloseOutputStates = new WebSocketState[2]
		{
		WebSocketState.Open,
		WebSocketState.CloseReceived
		};

		/// <summary>Valid states to be in when calling CloseAsync.</summary>
		private static readonly WebSocketState[] s_validCloseStates = new WebSocketState[3]
		{
		WebSocketState.Open,
		WebSocketState.CloseReceived,
		WebSocketState.CloseSent
		};

		/// <summary>The maximum size in bytes of a message frame header that includes mask bytes.</summary>
		private const int MaxMessageHeaderLength = 14;

		/// <summary>The maximum size of a control message payload.</summary>
		private const int MaxControlPayloadLength = 125;

		/// <summary>Length of the mask XOR'd with the payload data.</summary>
		private const int MaskLength = 4;

		/// <summary>The stream used to communicate with the remote server.</summary>
		private readonly Stream _stream;

		/// <summary>
		/// true if this is the server-side of the connection; false if it's client.
		/// This impacts masking behavior: clients always mask payloads they send and
		/// expect to always receive unmasked payloads, whereas servers always send
		/// unmasked payloads and expect to always receive masked payloads.
		/// </summary>
		private readonly bool _isServer;

		/// <summary>The agreed upon subprotocol with the server.</summary>
		private readonly string _subprotocol;

		/// <summary>Timer used to send periodic pings to the server, at the interval specified</summary>
		private readonly Timer _keepAliveTimer;

		/// <summary>CancellationTokenSource used to abort all current and future operations when anything is canceled or any error occurs.</summary>
		private readonly CancellationTokenSource _abortSource = new CancellationTokenSource();

		/// <summary>Buffer used for reading data from the network.</summary>
		private byte[] _receiveBuffer;

		/// <summary>Gets whether the receive buffer came from the ArrayPool.</summary>
		private readonly bool _receiveBufferFromPool;

		/// <summary>
		/// Tracks the state of the validity of the UTF8 encoding of text payloads.  Text may be split across fragments.
		/// </summary>
		private readonly Utf8MessageState _utf8TextState = new Utf8MessageState();

		/// <summary>
		/// Semaphore used to ensure that calls to SendFrameAsync don't run concurrently.  While <see cref="F:System.Net.WebSockets.Managed.ManagedWebSocket._lastSendAsync" />
		/// is used to fail if a caller tries to issue another SendAsync while a previous one is running, internally
		/// we use SendFrameAsync as an implementation detail, and it should not cause user requests to SendAsync to fail,
		/// nor should such internal usage be allowed to run concurrently with other internal usage or with SendAsync.
		/// </summary>
		private readonly SemaphoreSlim _sendFrameAsyncLock = new SemaphoreSlim(1, 1);

		/// <summary>The current state of the web socket in the protocol.</summary>
		private WebSocketState _state = WebSocketState.Open;

		/// <summary>true if Dispose has been called; otherwise, false.</summary>
		private bool _disposed;

		/// <summary>Whether we've ever sent a close frame.</summary>
		private bool _sentCloseFrame;

		/// <summary>Whether we've ever received a close frame.</summary>
		private bool _receivedCloseFrame;

		/// <summary>The reason for the close, as sent by the server, or null if not yet closed.</summary>
		private WebSocketCloseStatus? _closeStatus;

		/// <summary>A description of the close reason as sent by the server, or null if not yet closed.</summary>
		private string _closeStatusDescription;

		/// <summary>
		/// The last header received in a ReceiveAsync.  If ReceiveAsync got a header but then
		/// returned fewer bytes than was indicated in the header, subsequent ReceiveAsync calls
		/// will use the data from the header to construct the subsequent receive results, and
		/// the payload length in this header will be decremented to indicate the number of bytes
		/// remaining to be received for that header.  As a result, between fragments, the payload
		/// length in this header should be 0.
		/// </summary>
		private MessageHeader _lastReceiveHeader = new MessageHeader
		{
			Opcode = MessageOpcode.Text,
			Fin = true
		};

		/// <summary>The offset of the next available byte in the _receiveBuffer.</summary>
		private int _receiveBufferOffset;

		/// <summary>The number of bytes available in the _receiveBuffer.</summary>
		private int _receiveBufferCount;

		/// <summary>
		/// When dealing with partially read fragments of binary/text messages, a mask previously received may still
		/// apply, and the first new byte received may not correspond to the 0th position in the mask.  This value is
		/// the next offset into the mask that should be applied.
		/// </summary>
		private int _receivedMaskOffsetOffset;

		/// <summary>
		/// Temporary send buffer.  This should be released back to the ArrayPool once it's
		/// no longer needed for the current send operation.  It is stored as an instance
		/// field to minimize needing to pass it around and to avoid it becoming a field on
		/// various async state machine objects.
		/// </summary>
		private byte[] _sendBuffer;

		/// <summary>
		/// Whether the last SendAsync had endOfMessage==false. We need to track this so that we
		/// can send the subsequent message with a continuation opcode if the last message was a fragment.
		/// </summary>
		private bool _lastSendWasFragment;

		/// <summary>
		/// The task returned from the last SendAsync operation to not complete synchronously.
		/// If this is not null and not completed when a subsequent SendAsync is issued, an exception occurs.
		/// </summary>
		private Task _lastSendAsync;

		/// <summary>
		/// The task returned from the last ReceiveAsync operation to not complete synchronously.
		/// If this is not null and not completed when a subsequent ReceiveAsync is issued, an exception occurs.
		/// </summary>
		private Task<WebSocketReceiveResult> _lastReceiveAsync;

		/// <summary>Lock used to protect update and check-and-update operations on _state.</summary>
		private object StateUpdateLock => _abortSource;

		/// <summary>
		/// We need to coordinate between receives and close operations happening concurrently, as a ReceiveAsync may
		/// be pending while a Close{Output}Async is issued, which itself needs to loop until a close frame is received.
		/// As such, we need thread-safety in the management of <see cref="F:System.Net.WebSockets.Managed.ManagedWebSocket._lastReceiveAsync" />. 
		/// </summary>
		private object ReceiveAsyncLock => _utf8TextState;

		public override WebSocketCloseStatus? CloseStatus => _closeStatus;

		public override string CloseStatusDescription => _closeStatusDescription;

		public override WebSocketState State => _state;

		public override string SubProtocol => _subprotocol;

		/// <summary>Creates a <see cref="T:System.Net.WebSockets.Managed.ManagedWebSocket" /> from a <see cref="T:System.IO.Stream" /> connected to a websocket endpoint.</summary>
		/// <param name="stream">The connected Stream.</param>
		/// <param name="isServer">true if this is the server-side of the connection; false if this is the client-side of the connection.</param>
		/// <param name="subprotocol">The agreed upon subprotocol for the connection.</param>
		/// <param name="keepAliveInterval">The interval to use for keep-alive pings.</param>
		/// <param name="receiveBufferSize">The buffer size to use for received data.</param>
		/// <param name="receiveBuffer">Optional buffer to use for receives.</param>
		/// <returns>The created <see cref="T:System.Net.WebSockets.Managed.ManagedWebSocket" /> instance.</returns>
		public static ManagedWebSocket CreateFromConnectedStream(Stream stream, bool isServer, string subprotocol, TimeSpan keepAliveInterval, int receiveBufferSize, ArraySegment<byte>? receiveBuffer = null)
		{
			return new ManagedWebSocket(stream, isServer, subprotocol, keepAliveInterval, receiveBufferSize, receiveBuffer);
		}

		/// <summary>Initializes the websocket.</summary>
		/// <param name="stream">The connected Stream.</param>
		/// <param name="isServer">true if this is the server-side of the connection; false if this is the client-side of the connection.</param>
		/// <param name="subprotocol">The agreed upon subprotocol for the connection.</param>
		/// <param name="keepAliveInterval">The interval to use for keep-alive pings.</param>
		/// <param name="receiveBufferSize">The buffer size to use for received data.</param>
		/// <param name="receiveBuffer">Optional buffer to use for receives</param>
		private ManagedWebSocket(Stream stream, bool isServer, string subprotocol, TimeSpan keepAliveInterval, int receiveBufferSize, ArraySegment<byte>? receiveBuffer)
		{
			_stream = stream;
			_isServer = isServer;
			_subprotocol = subprotocol;
			if (receiveBuffer.HasValue && receiveBuffer.Value.Offset == 0 && receiveBuffer.Value.Count == receiveBuffer.Value.Array?.Length && receiveBuffer.Value.Count >= 14)
			{
				_receiveBuffer = receiveBuffer.Value.Array;
			}
			else
			{
				_receiveBufferFromPool = true;
				_receiveBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(receiveBufferSize, 14));
			}
			_abortSource.Token.Register(delegate (object s)
			{
				ManagedWebSocket managedWebSocket = (ManagedWebSocket)s;
				lock (managedWebSocket.StateUpdateLock)
				{
					WebSocketState state = managedWebSocket._state;
					if (state != WebSocketState.Closed && state != WebSocketState.Aborted)
					{
						managedWebSocket._state = ((state != 0 && state != WebSocketState.Connecting) ? WebSocketState.Aborted : WebSocketState.Closed);
					}
				}
			}, this);
			if (keepAliveInterval > TimeSpan.Zero)
			{
				_keepAliveTimer = new Timer(delegate (object s)
				{
					((ManagedWebSocket)s).SendKeepAliveFrameAsync();
				}, this, keepAliveInterval, keepAliveInterval);
			}
		}

		public override void Dispose()
		{
			lock (StateUpdateLock)
			{
				DisposeCore();
			}
		}

		private void DisposeCore()
		{
			if (!_disposed)
			{
				_disposed = true;
				_keepAliveTimer?.Dispose();
				_stream?.Dispose();
				if (_receiveBufferFromPool)
				{
					byte[] receiveBuffer = _receiveBuffer;
					_receiveBuffer = null;
					ArrayPool<byte>.Shared.Return(receiveBuffer);
				}
				if (_state < WebSocketState.Aborted)
				{
					_state = WebSocketState.Closed;
				}
			}
		}

		public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
		{
			if (messageType != 0 && messageType != WebSocketMessageType.Binary)
			{
				throw new ArgumentException(/*SR.Format(SR.net_WebSockets_Argument_InvalidMessageType, "Close", "SendAsync", "Binary", "Text", "CloseOutputAsync"), "messageType"*/);
			}
			WebSocketValidate.ValidateArraySegment(buffer, "buffer");
			try
			{
				WebSocketValidate.ThrowIfInvalidState(_state, _disposed, s_validSendStates);
				ThrowIfOperationInProgress(_lastSendAsync, "SendAsync");
			}
			catch (Exception exception)
			{
				TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
				taskCompletionSource.SetException(exception);
				return taskCompletionSource.Task;
			}
			MessageOpcode opcode = ((!_lastSendWasFragment) ? ((messageType != WebSocketMessageType.Binary) ? MessageOpcode.Text : MessageOpcode.Binary) : MessageOpcode.Continuation);
			Task task = SendFrameAsync(opcode, endOfMessage, buffer, cancellationToken);
			_lastSendWasFragment = !endOfMessage;
			_lastSendAsync = task;
			return task;
		}

		public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
		{
			WebSocketValidate.ValidateArraySegment(buffer, "buffer");
			try
			{
				WebSocketValidate.ThrowIfInvalidState(_state, _disposed, s_validReceiveStates);
				lock (ReceiveAsyncLock)
				{
					ThrowIfOperationInProgress(_lastReceiveAsync, "ReceiveAsync");
					return _lastReceiveAsync = ReceiveAsyncPrivate(buffer, cancellationToken);
				}
			}
			catch (Exception exception)
			{
				TaskCompletionSource<WebSocketReceiveResult> taskCompletionSource = new TaskCompletionSource<WebSocketReceiveResult>();
				taskCompletionSource.SetException(exception);
				return taskCompletionSource.Task;
			}
		}

		public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			WebSocketValidate.ValidateCloseStatus(closeStatus, statusDescription);
			try
			{
				WebSocketValidate.ThrowIfInvalidState(_state, _disposed, s_validCloseStates);
			}
			catch (Exception exception)
			{
				TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
				taskCompletionSource.SetException(exception);
				return taskCompletionSource.Task;
			}
			return CloseAsyncPrivate(closeStatus, statusDescription, cancellationToken);
		}

		public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			WebSocketValidate.ValidateCloseStatus(closeStatus, statusDescription);
			try
			{
				WebSocketValidate.ThrowIfInvalidState(_state, _disposed, s_validCloseOutputStates);
			}
			catch (Exception exception)
			{
				TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
				taskCompletionSource.SetException(exception);
				return taskCompletionSource.Task;
			}
			return SendCloseFrameAsync(closeStatus, statusDescription, cancellationToken);
		}

		public override void Abort()
		{
			_abortSource.Cancel();
			Dispose();
		}

		/// <summary>Sends a websocket frame to the network.</summary>
		/// <param name="opcode">The opcode for the message.</param>
		/// <param name="endOfMessage">The value of the FIN bit for the message.</param>
		/// <param name="payloadBuffer">The buffer containing the payload data fro the message.</param>
		/// <param name="cancellationToken">The CancellationToken to use to cancel the websocket.</param>
		private Task SendFrameAsync(MessageOpcode opcode, bool endOfMessage, ArraySegment<byte> payloadBuffer, CancellationToken cancellationToken)
		{
			if (!cancellationToken.CanBeCanceled && _sendFrameAsyncLock.Wait(0))
			{
				return SendFrameLockAcquiredNonCancelableAsync(opcode, endOfMessage, payloadBuffer);
			}
			return SendFrameFallbackAsync(opcode, endOfMessage, payloadBuffer, cancellationToken);
		}

		/// <summary>Sends a websocket frame to the network. The caller must hold the sending lock.</summary>
		/// <param name="opcode">The opcode for the message.</param>
		/// <param name="endOfMessage">The value of the FIN bit for the message.</param>
		/// <param name="payloadBuffer">The buffer containing the payload data fro the message.</param>
		private Task SendFrameLockAcquiredNonCancelableAsync(MessageOpcode opcode, bool endOfMessage, ArraySegment<byte> payloadBuffer)
		{
			Task task = null;
			bool flag = true;
			try
			{
				int count = WriteFrameToSendBuffer(opcode, endOfMessage, payloadBuffer);
				task = _stream.WriteAsync(_sendBuffer, 0, count, CancellationToken.None);
				if (task.IsCompleted)
				{
					task.GetAwaiter().GetResult();
					return Task.FromResult(result: true);
				}
				flag = false;
			}
			catch (Exception innerException)
			{
				TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
				taskCompletionSource.SetException((_state == WebSocketState.Aborted) ? CreateOperationCanceledException(innerException) : new WebSocketException(WebSocketError.ConnectionClosedPrematurely, innerException));
				return taskCompletionSource.Task;
			}
			finally
			{
				if (flag)
				{
					_sendFrameAsyncLock.Release();
					ReleaseSendBuffer();
				}
			}
			return task.ContinueWith(delegate (Task t, object s)
			{
				ManagedWebSocket managedWebSocket = (ManagedWebSocket)s;
				managedWebSocket._sendFrameAsyncLock.Release();
				managedWebSocket.ReleaseSendBuffer();
				try
				{
					t.GetAwaiter().GetResult();
				}
				catch (Exception innerException2)
				{
					throw (managedWebSocket._state == WebSocketState.Aborted) ? CreateOperationCanceledException(innerException2) : new WebSocketException(WebSocketError.ConnectionClosedPrematurely, innerException2);
				}
			}, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		}

		private async Task SendFrameFallbackAsync(MessageOpcode opcode, bool endOfMessage, ArraySegment<byte> payloadBuffer, CancellationToken cancellationToken)
		{
			await _sendFrameAsyncLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				int count = WriteFrameToSendBuffer(opcode, endOfMessage, payloadBuffer);
				using (cancellationToken.Register(delegate (object s)
				{
					((ManagedWebSocket)s).Abort();
				}, this))
				{
					await _stream.WriteAsync(_sendBuffer, 0, count, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch (Exception innerException)
			{
				throw (_state == WebSocketState.Aborted) ? CreateOperationCanceledException(innerException, cancellationToken) : new WebSocketException(WebSocketError.ConnectionClosedPrematurely, innerException);
			}
			finally
			{
				_sendFrameAsyncLock.Release();
				ReleaseSendBuffer();
			}
		}

		/// <summary>Writes a frame into the send buffer, which can then be sent over the network.</summary>
		private int WriteFrameToSendBuffer(MessageOpcode opcode, bool endOfMessage, ArraySegment<byte> payloadBuffer)
		{
			AllocateSendBuffer(payloadBuffer.Count + 14);
			int? num = null;
			int num2;
			if (_isServer)
			{
				num2 = WriteHeader(opcode, _sendBuffer, payloadBuffer, endOfMessage, useMask: false);
			}
			else
			{
				num = WriteHeader(opcode, _sendBuffer, payloadBuffer, endOfMessage, useMask: true);
				num2 = num.GetValueOrDefault() + 4;
			}
			if (payloadBuffer.Count > 0)
			{
				Buffer.BlockCopy(payloadBuffer.Array, payloadBuffer.Offset, _sendBuffer, num2, payloadBuffer.Count);
				if (num.HasValue)
				{
					ApplyMask(_sendBuffer, num2, _sendBuffer, num.Value, 0, payloadBuffer.Count);
				}
			}
			return num2 + payloadBuffer.Count;
		}

		private void SendKeepAliveFrameAsync()
		{
			if (_sendFrameAsyncLock.Wait(0))
			{
				SendFrameLockAcquiredNonCancelableAsync(MessageOpcode.Ping, endOfMessage: true, new ArraySegment<byte>(new byte[0]));
			}
		}

		private static int WriteHeader(MessageOpcode opcode, byte[] sendBuffer, ArraySegment<byte> payload, bool endOfMessage, bool useMask)
		{
			sendBuffer[0] = (byte)opcode;
			if (endOfMessage)
			{
				sendBuffer[0] |= 128;
			}
			int num;
			if (payload.Count <= 125)
			{
				sendBuffer[1] = (byte)payload.Count;
				num = 2;
			}
			else if (payload.Count <= 65535)
			{
				sendBuffer[1] = 126;
				sendBuffer[2] = (byte)(payload.Count / 256);
				sendBuffer[3] = (byte)payload.Count;
				num = 4;
			}
			else
			{
				sendBuffer[1] = 127;
				int num2 = payload.Count;
				for (int num3 = 9; num3 >= 2; num3--)
				{
					sendBuffer[num3] = (byte)num2;
					num2 /= 256;
				}
				num = 10;
			}
			if (useMask)
			{
				sendBuffer[1] |= 128;
				WriteRandomMask(sendBuffer, num);
			}
			return num;
		}

		/// <summary>Writes a 4-byte random mask to the specified buffer at the specified offset.</summary>
		/// <param name="buffer">The buffer to which to write the mask.</param>
		/// <param name="offset">The offset into the buffer at which to write the mask.</param>
		private static void WriteRandomMask(byte[] buffer, int offset)
		{
			byte[] array = t_headerMask ?? (t_headerMask = new byte[4]);
			s_random.GetBytes(array);
			Buffer.BlockCopy(array, 0, buffer, offset, 4);
		}

		/// <summary>
		/// Receive the next text, binary, continuation, or close message, returning information about it and
		/// writing its payload into the supplied buffer.  Other control messages may be consumed and processed
		/// as part of this operation, but data about them will not be returned.
		/// </summary>
		/// <param name="payloadBuffer">The buffer into which payload data should be written.</param>
		/// <param name="cancellationToken">The CancellationToken used to cancel the websocket.</param>
		/// <returns>Information about the received message.</returns>
		private async Task<WebSocketReceiveResult> ReceiveAsyncPrivate(ArraySegment<byte> payloadBuffer, CancellationToken cancellationToken)
		{
			CancellationTokenRegistration registration = cancellationToken.Register(delegate (object s)
			{
				((ManagedWebSocket)s).Abort();
			}, this);
			try
			{
				MessageHeader header;
				while (true)
				{
					header = _lastReceiveHeader;
					if (header.PayloadLength == 0L)
					{
						if (_receiveBufferCount < (_isServer ? 10 : 14))
						{
							if (_receiveBufferCount < 2)
							{
								await EnsureBufferContainsAsync(2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
							}
							long num = _receiveBuffer[_receiveBufferOffset + 1] & 0x7F;
							if (_isServer || num > 125)
							{
								int minimumRequiredBytes = 2 + (_isServer ? 4 : 0) + ((num > 125) ? ((num == 126) ? 2 : 8) : 0);
								await EnsureBufferContainsAsync(minimumRequiredBytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
							}
						}
						if (!TryParseMessageHeaderFromReceiveBuffer(out header))
						{
							await CloseWithReceiveErrorAndThrowAsync(WebSocketCloseStatus.ProtocolError, WebSocketError.Faulted, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
						}
						_receivedMaskOffsetOffset = 0;
					}
					if (header.Opcode != MessageOpcode.Ping && header.Opcode != MessageOpcode.Pong)
					{
						break;
					}
					await HandleReceivedPingPongAsync(header, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (header.Opcode == MessageOpcode.Close)
				{
					return await HandleReceivedCloseAsync(header, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (header.Opcode == MessageOpcode.Continuation)
				{
					header.Opcode = _lastReceiveHeader.Opcode;
				}
				int bytesToRead = (int)Math.Min(payloadBuffer.Count, header.PayloadLength);
				if (bytesToRead == 0)
				{
					_lastReceiveHeader = header;
					return new WebSocketReceiveResult(0, (header.Opcode != MessageOpcode.Text) ? WebSocketMessageType.Binary : WebSocketMessageType.Text, header.PayloadLength == 0L && header.Fin);
				}
				if (_receiveBufferCount == 0)
				{
					await EnsureBufferContainsAsync(1, cancellationToken, throwOnPrematureClosure: false).ConfigureAwait(continueOnCapturedContext: false);
				}
				int bytesToCopy = Math.Min(bytesToRead, _receiveBufferCount);
				if (_isServer)
				{
					_receivedMaskOffsetOffset = ApplyMask(_receiveBuffer, _receiveBufferOffset, header.Mask, _receivedMaskOffsetOffset, bytesToCopy);
				}
				Buffer.BlockCopy(_receiveBuffer, _receiveBufferOffset, payloadBuffer.Array, payloadBuffer.Offset, bytesToCopy);
				ConsumeFromBuffer(bytesToCopy);
				header.PayloadLength -= bytesToCopy;
				if (header.Opcode == MessageOpcode.Text && !TryValidateUtf8(new ArraySegment<byte>(payloadBuffer.Array, payloadBuffer.Offset, bytesToCopy), header.Fin, _utf8TextState))
				{
					await CloseWithReceiveErrorAndThrowAsync(WebSocketCloseStatus.InvalidPayloadData, WebSocketError.Faulted, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				_lastReceiveHeader = header;
				return new WebSocketReceiveResult(bytesToCopy, (header.Opcode != MessageOpcode.Text) ? WebSocketMessageType.Binary : WebSocketMessageType.Text, bytesToCopy == 0 || (header.Fin && header.PayloadLength == 0));
			}
			catch (Exception innerException)
			{
				if (_state == WebSocketState.Aborted)
				{
					throw new OperationCanceledException("Aborted", innerException);
				}
				throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, innerException);
			}
			finally
			{
				registration.Dispose();
			}
		}

		/// <summary>Processes a received close message.</summary>
		/// <param name="header">The message header.</param>
		/// <param name="cancellationToken">The cancellation token to use to cancel the websocket.</param>
		/// <returns>The received result message.</returns>
		private async Task<WebSocketReceiveResult> HandleReceivedCloseAsync(MessageHeader header, CancellationToken cancellationToken)
		{
			lock (StateUpdateLock)
			{
				_receivedCloseFrame = true;
				if (_state < WebSocketState.CloseReceived)
				{
					_state = WebSocketState.CloseReceived;
				}
			}
			WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure;
			string closeStatusDescription = string.Empty;
			if (header.PayloadLength == 1)
			{
				await CloseWithReceiveErrorAndThrowAsync(WebSocketCloseStatus.ProtocolError, WebSocketError.Faulted, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			else if (header.PayloadLength >= 2)
			{
				if (_receiveBufferCount < header.PayloadLength)
				{
					await EnsureBufferContainsAsync((int)header.PayloadLength, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (_isServer)
				{
					ApplyMask(_receiveBuffer, _receiveBufferOffset, header.Mask, 0, header.PayloadLength);
				}
				closeStatus = (WebSocketCloseStatus)((_receiveBuffer[_receiveBufferOffset] << 8) | _receiveBuffer[_receiveBufferOffset + 1]);
				if (!IsValidCloseStatus(closeStatus))
				{
					await CloseWithReceiveErrorAndThrowAsync(WebSocketCloseStatus.ProtocolError, WebSocketError.Faulted, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (header.PayloadLength > 2)
				{
					try
					{
						closeStatusDescription = s_textEncoding.GetString(_receiveBuffer, _receiveBufferOffset + 2, (int)header.PayloadLength - 2);
					}
					catch (DecoderFallbackException innerException)
					{
						await CloseWithReceiveErrorAndThrowAsync(WebSocketCloseStatus.ProtocolError, WebSocketError.Faulted, cancellationToken, innerException).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				ConsumeFromBuffer((int)header.PayloadLength);
			}
			_closeStatus = closeStatus;
			_closeStatusDescription = closeStatusDescription;
			return new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true, closeStatus, closeStatusDescription);
		}

		/// <summary>Processes a received ping or pong message.</summary>
		/// <param name="header">The message header.</param>
		/// <param name="cancellationToken">The cancellation token to use to cancel the websocket.</param>
		private async Task HandleReceivedPingPongAsync(MessageHeader header, CancellationToken cancellationToken)
		{
			if (header.PayloadLength > 0 && _receiveBufferCount < header.PayloadLength)
			{
				await EnsureBufferContainsAsync((int)header.PayloadLength, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (header.Opcode == MessageOpcode.Ping)
			{
				if (_isServer)
				{
					ApplyMask(_receiveBuffer, _receiveBufferOffset, header.Mask, 0, header.PayloadLength);
				}
				await SendFrameAsync(MessageOpcode.Pong, endOfMessage: true, new ArraySegment<byte>(_receiveBuffer, _receiveBufferOffset, (int)header.PayloadLength), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (header.PayloadLength > 0)
			{
				ConsumeFromBuffer((int)header.PayloadLength);
			}
		}

		/// <summary>Check whether a close status is valid according to the RFC.</summary>
		/// <param name="closeStatus">The status to validate.</param>
		/// <returns>true if the status if valid; otherwise, false.</returns>
		private static bool IsValidCloseStatus(WebSocketCloseStatus closeStatus)
		{
			if (closeStatus < WebSocketCloseStatus.NormalClosure || closeStatus >= (WebSocketCloseStatus)5000)
			{
				return false;
			}
			if (closeStatus >= (WebSocketCloseStatus)3000)
			{
				return true;
			}
			if ((uint)(closeStatus - 1000) <= 3u || (uint)(closeStatus - 1007) <= 4u)
			{
				return true;
			}
			return false;
		}

		/// <summary>Send a close message to the server and throw an exception, in response to getting bad data from the server.</summary>
		/// <param name="closeStatus">The close status code to use.</param>
		/// <param name="error">The error reason.</param>
		/// <param name="cancellationToken">The CancellationToken used to cancel the websocket.</param>
		/// <param name="innerException">An optional inner exception to include in the thrown exception.</param>
		private async Task CloseWithReceiveErrorAndThrowAsync(WebSocketCloseStatus closeStatus, WebSocketError error, CancellationToken cancellationToken, Exception innerException = null)
		{
			if (!_sentCloseFrame)
			{
				await CloseOutputAsync(closeStatus, string.Empty, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			_receiveBufferCount = 0;
			throw new WebSocketException(error, innerException);
		}

		/// <summary>Parses a message header from the buffer.  This assumes the header is in the buffer.</summary>
		/// <param name="resultHeader">The read header.</param>
		/// <returns>true if a header was read; false if the header was invalid.</returns>
		private bool TryParseMessageHeaderFromReceiveBuffer(out MessageHeader resultHeader)
		{
			MessageHeader messageHeader = default(MessageHeader);
			messageHeader.Fin = (_receiveBuffer[_receiveBufferOffset] & 0x80) != 0;
			bool flag = (_receiveBuffer[_receiveBufferOffset] & 0x70) != 0;
			messageHeader.Opcode = (MessageOpcode)(_receiveBuffer[_receiveBufferOffset] & 0xFu);
			bool flag2 = (_receiveBuffer[_receiveBufferOffset + 1] & 0x80) != 0;
			messageHeader.PayloadLength = _receiveBuffer[_receiveBufferOffset + 1] & 0x7F;
			ConsumeFromBuffer(2);
			if (messageHeader.PayloadLength == 126)
			{
				messageHeader.PayloadLength = (_receiveBuffer[_receiveBufferOffset] << 8) | _receiveBuffer[_receiveBufferOffset + 1];
				ConsumeFromBuffer(2);
			}
			else if (messageHeader.PayloadLength == 127)
			{
				messageHeader.PayloadLength = 0L;
				for (int i = 0; i < 8; i++)
				{
					messageHeader.PayloadLength = (messageHeader.PayloadLength << 8) | _receiveBuffer[_receiveBufferOffset + i];
				}
				ConsumeFromBuffer(8);
			}
			bool flag3 = flag;
			if (flag2)
			{
				if (!_isServer)
				{
					flag3 = true;
				}
				messageHeader.Mask = CombineMaskBytes(_receiveBuffer, _receiveBufferOffset);
				ConsumeFromBuffer(4);
			}
			switch (messageHeader.Opcode)
			{
				case MessageOpcode.Continuation:
					if (_lastReceiveHeader.Fin)
					{
						flag3 = true;
					}
					break;
				case MessageOpcode.Text:
				case MessageOpcode.Binary:
					if (!_lastReceiveHeader.Fin)
					{
						flag3 = true;
					}
					break;
				case MessageOpcode.Close:
				case MessageOpcode.Ping:
				case MessageOpcode.Pong:
					if (messageHeader.PayloadLength > 125 || !messageHeader.Fin)
					{
						flag3 = true;
					}
					break;
				default:
					flag3 = true;
					break;
			}
			resultHeader = messageHeader;
			return !flag3;
		}

		/// <summary>Send a close message, then receive until we get a close response message.</summary>
		/// <param name="closeStatus">The close status to send.</param>
		/// <param name="statusDescription">The close status description to send.</param>
		/// <param name="cancellationToken">The CancellationToken to use to cancel the websocket.</param>
		private async Task CloseAsyncPrivate(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
		{
			if (!_sentCloseFrame)
			{
				await SendCloseFrameAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			byte[] closeBuffer = ArrayPool<byte>.Shared.Rent(139);
			try
			{
				while (!_receivedCloseFrame)
				{
					Task<WebSocketReceiveResult> task;
					lock (ReceiveAsyncLock)
					{
						if (_receivedCloseFrame)
						{
							break;
						}
						task = _lastReceiveAsync;
						if (task == null || (task.Status == TaskStatus.RanToCompletion && task.Result.MessageType != WebSocketMessageType.Close))
						{
							task = (_lastReceiveAsync = ReceiveAsyncPrivate(new ArraySegment<byte>(closeBuffer), cancellationToken));
						}
						goto IL_018d;
					}
					IL_018d:
					await task.ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(closeBuffer);
			}
			lock (StateUpdateLock)
			{
				DisposeCore();
				if (_state < WebSocketState.Closed)
				{
					_state = WebSocketState.Closed;
				}
			}
		}

		/// <summary>Sends a close message to the server.</summary>
		/// <param name="closeStatus">The close status to send.</param>
		/// <param name="closeStatusDescription">The close status description to send.</param>
		/// <param name="cancellationToken">The CancellationToken to use to cancel the websocket.</param>
		private async Task SendCloseFrameAsync(WebSocketCloseStatus closeStatus, string closeStatusDescription, CancellationToken cancellationToken)
		{
			byte[] buffer = null;
			try
			{
				int num = 2;
				if (string.IsNullOrEmpty(closeStatusDescription))
				{
					buffer = ArrayPool<byte>.Shared.Rent(num);
				}
				else
				{
					num += s_textEncoding.GetByteCount(closeStatusDescription);
					buffer = ArrayPool<byte>.Shared.Rent(num);
					s_textEncoding.GetBytes(closeStatusDescription, 0, closeStatusDescription.Length, buffer, 2);
				}
				ushort num2 = (ushort)closeStatus;
				buffer[0] = (byte)(num2 >> 8);
				buffer[1] = (byte)(num2 & 0xFFu);
				await SendFrameAsync(MessageOpcode.Close, endOfMessage: true, new ArraySegment<byte>(buffer, 0, num), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				if (buffer != null)
				{
					ArrayPool<byte>.Shared.Return(buffer);
				}
			}
			lock (StateUpdateLock)
			{
				_sentCloseFrame = true;
				if (_state <= WebSocketState.CloseReceived)
				{
					_state = WebSocketState.CloseSent;
				}
			}
		}

		private void ConsumeFromBuffer(int count)
		{
			_receiveBufferCount -= count;
			_receiveBufferOffset += count;
		}

		private async Task EnsureBufferContainsAsync(int minimumRequiredBytes, CancellationToken cancellationToken, bool throwOnPrematureClosure = true)
		{
			if (_receiveBufferCount >= minimumRequiredBytes)
			{
				return;
			}
			if (_receiveBufferCount > 0)
			{
				Buffer.BlockCopy(_receiveBuffer, _receiveBufferOffset, _receiveBuffer, 0, _receiveBufferCount);
			}
			_receiveBufferOffset = 0;
			while (_receiveBufferCount < minimumRequiredBytes)
			{
				int num = await _stream.ReadAsync(_receiveBuffer, _receiveBufferCount, _receiveBuffer.Length - _receiveBufferCount, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				_receiveBufferCount += num;
				if (num == 0)
				{
					if (_disposed)
					{
						throw new ObjectDisposedException("ClientWebSocket");
					}
					if (throwOnPrematureClosure)
					{
						throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
					}
					break;
				}
			}
		}

		/// <summary>Gets a send buffer from the pool.</summary>
		private void AllocateSendBuffer(int minLength)
		{
			_sendBuffer = ArrayPool<byte>.Shared.Rent(minLength);
		}

		/// <summary>Releases the send buffer to the pool.</summary>
		private void ReleaseSendBuffer()
		{
			byte[] sendBuffer = _sendBuffer;
			if (sendBuffer != null)
			{
				_sendBuffer = null;
				ArrayPool<byte>.Shared.Return(sendBuffer);
			}
		}

		private static int CombineMaskBytes(byte[] buffer, int maskOffset)
		{
			return BitConverter.ToInt32(buffer, maskOffset);
		}

		/// <summary>Applies a mask to a portion of a byte array.</summary>
		/// <param name="toMask">The buffer to which the mask should be applied.</param>
		/// <param name="toMaskOffset">The offset into <paramref name="toMask" /> at which the mask should start to be applied.</param>
		/// <param name="mask">The array containing the mask to apply.</param>
		/// <param name="maskOffset">The offset into <paramref name="mask" /> of the mask to apply of length <see cref="F:System.Net.WebSockets.Managed.ManagedWebSocket.MaskLength" />.</param>
		/// <param name="maskOffsetIndex">The next position offset from <paramref name="maskOffset" /> of which by to apply next from the mask.</param>
		/// <param name="count">The number of bytes starting from <paramref name="toMaskOffset" /> to which the mask should be applied.</param>
		/// <returns>The updated maskOffsetOffset value.</returns>
		private static int ApplyMask(byte[] toMask, int toMaskOffset, byte[] mask, int maskOffset, int maskOffsetIndex, long count)
		{
			return ApplyMask(toMask, toMaskOffset, CombineMaskBytes(mask, maskOffset), maskOffsetIndex, count);
		}

		/// <summary>Applies a mask to a portion of a byte array.</summary>
		/// <param name="toMask">The buffer to which the mask should be applied.</param>
		/// <param name="toMaskOffset">The offset into <paramref name="toMask" /> at which the mask should start to be applied.</param>
		/// <param name="mask">The four-byte mask, stored as an Int32.</param>
		/// <param name="maskIndex">The index into the mask.</param>
		/// <param name="count">The number of bytes to mask.</param>
		/// <returns>The next index into the mask to be used for future applications of the mask.</returns>
		private unsafe static int ApplyMask(byte[] toMask, int toMaskOffset, int mask, int maskIndex, long count)
		{
			int num = maskIndex * 8;
			int num2 = (int)((uint)mask >> num) | (mask << 32 - num);


			//if (Vector.IsHardwareAccelerated && Vector<byte>.Count % 4 == 0 && count >= Vector<byte>.Count)
			//{
			//	Vector<byte> vector = Vector.AsVectorByte(new Vector<int>(num2));
			//	while (count >= Vector<byte>.Count)
			//	{
			//		count -= Vector<byte>.Count;
			//		(vector ^ new Vector<byte>(toMask, toMaskOffset)).CopyTo(toMask, toMaskOffset);
			//		toMaskOffset += Vector<byte>.Count;
			//	}
			//}

			if (count > 0)
			{
				fixed (byte* ptr = toMask)
				{
					byte* ptr2 = ptr + toMaskOffset;
					if ((long)ptr2 % 4L == 0L)
					{
						while (count >= 4)
						{
							count -= 4;
							*(int*)ptr2 ^= num2;
							ptr2 += 4;
						}
					}
					if (count > 0)
					{
						byte* ptr3 = (byte*)(&mask);
						byte* ptr4 = ptr2 + count;
						while (ptr2 < ptr4)
						{
							byte* intPtr = ptr2++;
							*intPtr = (byte)(*intPtr ^ ptr3[maskIndex]);
							maskIndex = (maskIndex + 1) & 3;
						}
					}
				}
			}
			return maskIndex;
		}

		/// <summary>Aborts the websocket and throws an exception if an existing operation is in progress.</summary>
		private void ThrowIfOperationInProgress(Task operationTask, [CallerMemberName] string methodName = null)
		{
			if (operationTask != null && !operationTask.IsCompleted)
			{
				Abort();
				throw new InvalidOperationException(/*SR.Format(SR.net_Websockets_AlreadyOneOutstandingOperation, methodName)*/);
			}
		}

		/// <summary>Creates an OperationCanceledException instance, using a default message and the specified inner exception and token.</summary>
		private static Exception CreateOperationCanceledException(Exception innerException, CancellationToken cancellationToken = default(CancellationToken))
		{
			return new OperationCanceledException(new OperationCanceledException().Message, innerException, cancellationToken);
		}

		private static bool TryValidateUtf8(ArraySegment<byte> arraySegment, bool endOfMessage, Utf8MessageState state)
		{
			int num = arraySegment.Offset;
			while (num < arraySegment.Offset + arraySegment.Count)
			{
				if (!state.SequenceInProgress)
				{
					state.SequenceInProgress = true;
					byte b = arraySegment.Array[num];
					num++;
					if ((b & 0x80) == 0)
					{
						state.AdditionalBytesExpected = 0;
						state.CurrentDecodeBits = b & 0x7F;
						state.ExpectedValueMin = 0;
					}
					else
					{
						if ((b & 0xC0) == 128)
						{
							return false;
						}
						if ((b & 0xE0) == 192)
						{
							state.AdditionalBytesExpected = 1;
							state.CurrentDecodeBits = b & 0x1F;
							state.ExpectedValueMin = 128;
						}
						else if ((b & 0xF0) == 224)
						{
							state.AdditionalBytesExpected = 2;
							state.CurrentDecodeBits = b & 0xF;
							state.ExpectedValueMin = 2048;
						}
						else
						{
							if ((b & 0xF8) != 240)
							{
								return false;
							}
							state.AdditionalBytesExpected = 3;
							state.CurrentDecodeBits = b & 7;
							state.ExpectedValueMin = 65536;
						}
					}
				}
				while (state.AdditionalBytesExpected > 0 && num < arraySegment.Offset + arraySegment.Count)
				{
					byte b2 = arraySegment.Array[num];
					if ((b2 & 0xC0) != 128)
					{
						return false;
					}
					num++;
					state.AdditionalBytesExpected--;
					state.CurrentDecodeBits = (state.CurrentDecodeBits << 6) | (b2 & 0x3F);
					if (state.AdditionalBytesExpected == 1 && state.CurrentDecodeBits >= 864 && state.CurrentDecodeBits <= 895)
					{
						return false;
					}
					if (state.AdditionalBytesExpected == 2 && state.CurrentDecodeBits >= 272)
					{
						return false;
					}
				}
				if (state.AdditionalBytesExpected == 0)
				{
					state.SequenceInProgress = false;
					if (state.CurrentDecodeBits < state.ExpectedValueMin)
					{
						return false;
					}
				}
			}
			if (endOfMessage && state.SequenceInProgress)
			{
				return false;
			}
			return true;
		}
	}
}
