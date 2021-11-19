using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp2
{
	[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
	[DebuggerNonUserCode]
	[CompilerGenerated]
	internal class Strings
	{
		private static ResourceManager resourceMan;

		private static CultureInfo resourceCulture;

		/// <summary>
		///   Returns the cached ResourceManager instance used by this class.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Advanced)]
		internal static ResourceManager ResourceManager
		{
			get
			{
				if (resourceMan == null)
				{
					resourceMan = new ResourceManager("System.Net.WebSockets.Client.Managed.Strings", typeof(Strings).Assembly);
				}
				return resourceMan;
			}
		}

		/// <summary>
		///   Overrides the current thread's CurrentUICulture property for all
		///   resource lookups using this strongly typed resource class.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Advanced)]
		internal static CultureInfo Culture
		{
			get
			{
				return resourceCulture;
			}
			set
			{
				resourceCulture = value;
			}
		}

		/// <summary>
		///   Looks up a localized string similar to The requested security protocol is not supported..
		/// </summary>
		internal static string net_securityprotocolnotsupported => ResourceManager.GetString("net_securityprotocolnotsupported", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to This operation is not supported for a relative URI..
		/// </summary>
		internal static string net_uri_NotAbsolute => ResourceManager.GetString("net_uri_NotAbsolute", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The WebSocket client request requested '{0}' protocol(s), but server is only accepting '{1}' protocol(s)..
		/// </summary>
		internal static string net_WebSockets_AcceptUnsupportedProtocol => ResourceManager.GetString("net_WebSockets_AcceptUnsupportedProtocol", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to There is already one outstanding '{0}' call for this WebSocket instance. ReceiveAsync and SendAsync can be called simultaneously, but at most one outstanding operation for each of them is allowed at the same time..
		/// </summary>
		internal static string net_Websockets_AlreadyOneOutstandingOperation => ResourceManager.GetString("net_Websockets_AlreadyOneOutstandingOperation", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The WebSocket has already been started..
		/// </summary>
		internal static string net_WebSockets_AlreadyStarted => ResourceManager.GetString("net_WebSockets_AlreadyStarted", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The message type '{0}' is not allowed for the '{1}' operation. Valid message types are: '{2}, {3}'. To close the WebSocket, use the '{4}' operation instead. .
		/// </summary>
		internal static string net_WebSockets_Argument_InvalidMessageType => ResourceManager.GetString("net_WebSockets_Argument_InvalidMessageType", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The argument must be a value greater than {0}..
		/// </summary>
		internal static string net_WebSockets_ArgumentOutOfRange_TooSmall => ResourceManager.GetString("net_WebSockets_ArgumentOutOfRange_TooSmall", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to An internal WebSocket error occurred. Please see the innerException, if present, for more details. .
		/// </summary>
		internal static string net_WebSockets_Generic => ResourceManager.GetString("net_WebSockets_Generic", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The WebSocket protocol '{0}' is invalid because it contains the invalid character '{1}'..
		/// </summary>
		internal static string net_WebSockets_InvalidCharInProtocolString => ResourceManager.GetString("net_WebSockets_InvalidCharInProtocolString", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The close status code '{0}' is reserved for system use only and cannot be specified when calling this method..
		/// </summary>
		internal static string net_WebSockets_InvalidCloseStatusCode => ResourceManager.GetString("net_WebSockets_InvalidCloseStatusCode", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The close status description '{0}' is too long. The UTF8-representation of the status description must not be longer than {1} bytes..
		/// </summary>
		internal static string net_WebSockets_InvalidCloseStatusDescription => ResourceManager.GetString("net_WebSockets_InvalidCloseStatusDescription", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to Empty string is not a valid subprotocol value. Please use \"null\" to specify no value..
		/// </summary>
		internal static string net_WebSockets_InvalidEmptySubProtocol => ResourceManager.GetString("net_WebSockets_InvalidEmptySubProtocol", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The '{0}' header value '{1}' is invalid..
		/// </summary>
		internal static string net_WebSockets_InvalidResponseHeader => ResourceManager.GetString("net_WebSockets_InvalidResponseHeader", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The WebSocket is in an invalid state ('{0}') for this operation. Valid states are: '{1}'.
		/// </summary>
		internal static string net_WebSockets_InvalidState => ResourceManager.GetString("net_WebSockets_InvalidState", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The '{0}' instance cannot be used for communication because it has been transitioned into the '{1}' state..
		/// </summary>
		internal static string net_WebSockets_InvalidState_ClosedOrAborted => ResourceManager.GetString("net_WebSockets_InvalidState_ClosedOrAborted", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to Duplicate protocols are not allowed: '{0}'..
		/// </summary>
		internal static string net_WebSockets_NoDuplicateProtocol => ResourceManager.GetString("net_WebSockets_NoDuplicateProtocol", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The WebSocket is not connected..
		/// </summary>
		internal static string net_WebSockets_NotConnected => ResourceManager.GetString("net_WebSockets_NotConnected", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The close status description '{0}' is invalid. When using close status code '{1}' the description must be null..
		/// </summary>
		internal static string net_WebSockets_ReasonNotNull => ResourceManager.GetString("net_WebSockets_ReasonNotNull", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to Only Uris starting with 'ws://' or 'wss://' are supported..
		/// </summary>
		internal static string net_WebSockets_Scheme => ResourceManager.GetString("net_WebSockets_Scheme", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The WebSocket protocol is not supported on this platform..
		/// </summary>
		internal static string net_WebSockets_UnsupportedPlatform => ResourceManager.GetString("net_WebSockets_UnsupportedPlatform", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to Unable to connect to the remote server.
		/// </summary>
		internal static string net_webstatus_ConnectFailure => ResourceManager.GetString("net_webstatus_ConnectFailure", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The base stream is not readable..
		/// </summary>
		internal static string NotReadableStream => ResourceManager.GetString("NotReadableStream", resourceCulture);

		/// <summary>
		///   Looks up a localized string similar to The base stream is not writeable..
		/// </summary>
		internal static string NotWriteableStream => ResourceManager.GetString("NotWriteableStream", resourceCulture);

		internal Strings()
		{
		}
	}

}
