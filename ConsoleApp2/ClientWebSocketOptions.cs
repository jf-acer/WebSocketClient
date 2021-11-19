using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp2
{
    public class ClientWebSocketOptions
    {
		private bool _isReadOnly;

		private readonly List<string> _requestedSubProtocols;

		private readonly WebHeaderCollection _requestHeaders;

		private TimeSpan _keepAliveInterval = WebSocket.DefaultKeepAliveInterval;

		private bool _useDefaultCredentials;

		private ICredentials _credentials;

		private IWebProxy _proxy;

		private X509CertificateCollection _clientCertificates;

		private CookieContainer _cookies;

		private int _receiveBufferSize = 4096;

		private int _sendBufferSize = 4096;

		private ArraySegment<byte>? _buffer;

		internal WebHeaderCollection RequestHeaders => _requestHeaders;

		internal List<string> RequestedSubProtocols => _requestedSubProtocols;

		public bool UseDefaultCredentials
		{
			get
			{
				return _useDefaultCredentials;
			}
			set
			{
				ThrowIfReadOnly();
				_useDefaultCredentials = value;
			}
		}

		public ICredentials Credentials
		{
			get
			{
				return _credentials;
			}
			set
			{
				ThrowIfReadOnly();
				_credentials = value;
			}
		}

		public IWebProxy Proxy
		{
			get
			{
				return _proxy;
			}
			set
			{
				ThrowIfReadOnly();
				_proxy = value;
			}
		}

		public X509CertificateCollection ClientCertificates
		{
			get
			{
				if (_clientCertificates == null)
				{
					_clientCertificates = new X509CertificateCollection();
				}
				return _clientCertificates;
			}
			set
			{
				ThrowIfReadOnly();
				if (value == null)
				{
					throw new ArgumentNullException("value");
				}
				_clientCertificates = value;
			}
		}

		public CookieContainer Cookies
		{
			get
			{
				return _cookies;
			}
			set
			{
				ThrowIfReadOnly();
				_cookies = value;
			}
		}

		public TimeSpan KeepAliveInterval
		{
			get
			{
				return _keepAliveInterval;
			}
			set
			{
				ThrowIfReadOnly();
				if (value != Timeout.InfiniteTimeSpan && value < TimeSpan.Zero)
				{
					object actualValue = value;
					string net_WebSockets_ArgumentOutOfRange_TooSmall = SR.net_WebSockets_ArgumentOutOfRange_TooSmall;
					object[] array = new object[1];
					TimeSpan infiniteTimeSpan = Timeout.InfiniteTimeSpan;
					array[0] = infiniteTimeSpan.ToString();
					throw new ArgumentOutOfRangeException("value", actualValue, SR.Format(net_WebSockets_ArgumentOutOfRange_TooSmall, array));
				}
				_keepAliveInterval = value;
			}
		}

		internal int ReceiveBufferSize => _receiveBufferSize;

		internal int SendBufferSize => _sendBufferSize;

		internal ArraySegment<byte>? Buffer => _buffer;

		internal ClientWebSocketOptions()
		{
			_requestedSubProtocols = new List<string>();
			_requestHeaders = new WebHeaderCollection();
		}

		public void SetRequestHeader(string headerName, string headerValue)
		{
			ThrowIfReadOnly();
			_requestHeaders[headerName] = headerValue;
		}

		public void AddSubProtocol(string subProtocol)
		{
			ThrowIfReadOnly();
			WebSocketValidate.ValidateSubprotocol(subProtocol);
			foreach (string requestedSubProtocol in _requestedSubProtocols)
			{
				if (string.Equals(requestedSubProtocol, subProtocol, StringComparison.OrdinalIgnoreCase))
				{
					throw new ArgumentException(SR.Format(SR.net_WebSockets_NoDuplicateProtocol, subProtocol), "subProtocol");
				}
			}
			_requestedSubProtocols.Add(subProtocol);
		}

		public void SetBuffer(int receiveBufferSize, int sendBufferSize)
		{
			ThrowIfReadOnly();
			if (receiveBufferSize <= 0)
			{
				throw new ArgumentOutOfRangeException("receiveBufferSize", receiveBufferSize, SR.Format(SR.net_WebSockets_ArgumentOutOfRange_TooSmall, 1));
			}
			if (sendBufferSize <= 0)
			{
				throw new ArgumentOutOfRangeException("sendBufferSize", sendBufferSize, SR.Format(SR.net_WebSockets_ArgumentOutOfRange_TooSmall, 1));
			}
			_receiveBufferSize = receiveBufferSize;
			_sendBufferSize = sendBufferSize;
			_buffer = null;
		}

		public void SetBuffer(int receiveBufferSize, int sendBufferSize, ArraySegment<byte> buffer)
		{
			ThrowIfReadOnly();
			if (receiveBufferSize <= 0)
			{
				throw new ArgumentOutOfRangeException("receiveBufferSize", receiveBufferSize, SR.Format(SR.net_WebSockets_ArgumentOutOfRange_TooSmall, 1));
			}
			if (sendBufferSize <= 0)
			{
				throw new ArgumentOutOfRangeException("sendBufferSize", sendBufferSize, SR.Format(SR.net_WebSockets_ArgumentOutOfRange_TooSmall, 1));
			}
			WebSocketValidate.ValidateArraySegment(buffer, "buffer");
			if (buffer.Count == 0)
			{
				throw new ArgumentOutOfRangeException("buffer");
			}
			_receiveBufferSize = receiveBufferSize;
			_sendBufferSize = sendBufferSize;
			_buffer = buffer;
		}

		internal void SetToReadOnly()
		{
			_isReadOnly = true;
		}

		private void ThrowIfReadOnly()
		{
			if (_isReadOnly)
			{
				throw new InvalidOperationException(SR.net_WebSockets_AlreadyStarted);
			}
		}
	}
}
