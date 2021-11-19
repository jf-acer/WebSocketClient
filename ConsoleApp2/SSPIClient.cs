using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp2
{
	public struct SecurityPackageInfo
	{
		[Flags]
		public enum SecurityCapabilities
		{
			SupportsIntegrity = 0x1,
			SupportsPrivacy = 0x2,
			SupportsTokenOnly = 0x4,
			SupportsDatagram = 0x8,
			SupportsConnections = 0x10,
			MultipleLegsRequired = 0x20,
			ClientOnly = 0x40,
			ExtendedErrorSupport = 0x80,
			SupportsImpersonation = 0x100,
			AccepsWin32Names = 0x200,
			SupportsStreams = 0x400,
			Negotiable = 0x800,
			GSSAPICompatible = 0x1000,
			SupportsLogon = 0x2000,
			BuffersAreASCII = 0x4000,
			SupportsTokenFragmentation = 0x8000,
			SupportsMutualAuthentication = 0x10000,
			SupportsDelegation = 0x20000,
			SupportsChecksumOnly = 0x40000,
			SupportsRestrictedTokens = 0x80000,
			ExtendsNegotiate = 0x100000,
			NegotiableByExtendedNegotiate = 0x200000,
			AppContainerPassThrough = 0x400000,
			AppContainerChecks = 0x800000,
			CredentialIsolationEnabled = 0x1000000
		}
		[MarshalAs(UnmanagedType.U4)]
		public SecurityCapabilities Capabilities;

		public ushort Version;

		public ushort RpcId;

		public uint MaxTokenSize;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string Name;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string Comment;
	}
	public enum SecBufferType
	{
		Empty,
		Data,
		Token,
		PackageParameters,
		MissingBuffer,
		ExtraData,
		StreamTrailer,
		StreamHeader,
		NegotiationInfo,
		Padding,
		Stream,
		ObjectIdList,
		OidListSignature,
		Target,
		ChannelBindings,
		ChangePassResp,
		TargetHost,
		Alert,
		AppProtocolIds,
		StrpProtProfiles,
		StrpMasterKeyId,
		TokenBinding,
		PresharedKey,
		PresharedKeyId,
		DtlsMtu
	}
	public struct SecBuffer
	{
		public uint size;

		[MarshalAs(UnmanagedType.U4)]
		public SecBufferType type;

		public IntPtr bufferPtr;
	}
	[Flags]
	public enum CredentialsUse
	{
		Inbound = 0x1,
		Outbound = 0x2,
		InboundAndOutbound = 0x3
	}
	[Flags]
	public enum ContextRequirements
	{
		Delegation = 0x1,
		MutualAuthentication = 0x2,
		ReplayDetection = 0x4,
		SequenceDetection = 0x8,
		Confidentiality = 0x10,
		UseSessionKey = 0x20,
		PromptForCredentials = 0x40,
		UseSuppliedCredentials = 0x80,
		AllocateMemory = 0x100,
		UseDceStyle = 0x200,
		DatagramCommunications = 0x400,
		ConnectionCommunications = 0x800,
		CallLevel = 0x1000,
		FragmentSupplied = 0x2000,
		ExtendedError = 0x4000,
		StreamCommunications = 0x8000,
		Integrity = 0x10000,
		Identity = 0x20000,
		NullSession = 0x40000,
		ManualCredValidation = 0x80000,
		Reserved = 0x100000,
		FragmentToFit = 0x200000,
		ForwardCredentials = 0x400000,
		NoIntegrity = 0x800000,
		UseHttpStyle = 0x1000000,
		UnverifiedTargetName = 0x20000000,
		ConfidentialityOnly = 0x40000000
	}

	public struct SecurityHandle
	{
		public IntPtr LowPart;

		public IntPtr HighPart;
	}

	public struct SecBufferDescription
	{
		public uint version;

		public uint numOfBuffers;

		public IntPtr buffersPtr;
	}

	public class SSPIClient : IDisposable
	{
		private const int NoError = 0;

		private const int ContinueNeeded = 590610;

		private const int NativeDataRepresentation = 16;

		private SecurityHandle _credHandle;

		private SecurityHandle _contextHandle;

		private DateTime _credExpiration;

		private DateTime _contextExpiration;

		public DateTime TokenExpiration => _contextExpiration;

		public SSPIClient(string packageName)
		{
			SecurityHandle securityHandle = new SecurityHandle
			{
				HighPart = IntPtr.Zero,
				LowPart = IntPtr.Zero
			};
			_credHandle = securityHandle;
			securityHandle = new SecurityHandle
			{
				HighPart = IntPtr.Zero,
				LowPart = IntPtr.Zero
			};
			_contextHandle = securityHandle;
			_contextExpiration = DateTime.MinValue;
			_credExpiration = DateTime.MinValue;
			ulong expiration = 0uL;
			AcquireCredentialsHandle(null, packageName, CredentialsUse.Outbound, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref _credHandle, ref expiration);
			try
			{
				_credExpiration = DateTime.FromFileTime((long)expiration);
			}
			catch (ArgumentException)
			{
				_credExpiration = DateTime.MaxValue;
			}
		}

		public byte[] GetClientToken(byte[] serverToken)
		{
			GCHandle gCHandle = GCHandle.Alloc(serverToken, GCHandleType.Pinned);
			SecBuffer secBuffer = default(SecBuffer);
			secBuffer.type = SecBufferType.Token;
			secBuffer.size = (uint)((serverToken != null) ? serverToken.Length : 0);
			secBuffer.bufferPtr = gCHandle.AddrOfPinnedObject();
			GCHandle gCHandle2 = GCHandle.Alloc(secBuffer, GCHandleType.Pinned);
			secBuffer = default(SecBuffer);
			secBuffer.type = SecBufferType.Token;
			secBuffer.size = 0u;
			secBuffer.bufferPtr = IntPtr.Zero;
			GCHandle gCHandle3 = GCHandle.Alloc(secBuffer, GCHandleType.Pinned);
			byte[] array = null;
			try
			{
				SecBufferDescription secBufferDescription = default(SecBufferDescription);
				secBufferDescription.version = 0u;
				secBufferDescription.numOfBuffers = 1u;
				secBufferDescription.buffersPtr = gCHandle2.AddrOfPinnedObject();
				SecBufferDescription inBuffDesc = secBufferDescription;
				secBufferDescription = default(SecBufferDescription);
				secBufferDescription.version = 0u;
				secBufferDescription.numOfBuffers = 1u;
				secBufferDescription.buffersPtr = gCHandle3.AddrOfPinnedObject();
				SecBufferDescription outBuffDesc = secBufferDescription;
				ulong expiration = 0uL;
				ContextRequirements contextAttributes = (ContextRequirements)0;
				int num = 0;
				num = ((serverToken != null) ? InitializeSecurityContext(ref _credHandle, ref _contextHandle, null, ContextRequirements.AllocateMemory | ContextRequirements.ConnectionCommunications, 0, 16, ref inBuffDesc, 0, ref _contextHandle, ref outBuffDesc, ref contextAttributes, ref expiration) : InitializeSecurityContext(ref _credHandle, IntPtr.Zero, null, ContextRequirements.AllocateMemory | ContextRequirements.ConnectionCommunications, 0, 16, IntPtr.Zero, 0, ref _contextHandle, ref outBuffDesc, ref contextAttributes, ref expiration));
				if (num != 0 && num != 590610)
				{
					throw new Win32Exception(num);
				}
				SecBuffer secBuffer2 = (SecBuffer)Marshal.PtrToStructure(outBuffDesc.buffersPtr, typeof(SecBuffer));
				array = new byte[secBuffer2.size];
				Marshal.Copy(secBuffer2.bufferPtr, array, 0, (int)secBuffer2.size);
				FreeContextBuffer(secBuffer2.bufferPtr);
				try
				{
					_contextExpiration = DateTime.FromFileTimeUtc((long)expiration);
					return array;
				}
				catch (ArgumentException)
				{
					_contextExpiration = DateTime.MaxValue;
					return array;
				}
			}
			finally
			{
				gCHandle3.Free();
				gCHandle2.Free();
				gCHandle.Free();
			}
		}

		public static SecurityPackageInfo[] EnumerateSecurityPackages()
		{
			uint numOfPackages = 0u;
			IntPtr packageInfosPtr = IntPtr.Zero;
			int num = EnumerateSecurityPackagesW(ref numOfPackages, ref packageInfosPtr);
			if (num != 0)
			{
				throw new Win32Exception(num);
			}
			try
			{
				SecurityPackageInfo[] array = new SecurityPackageInfo[numOfPackages];
				int offset = Marshal.SizeOf(typeof(SecurityPackageInfo));
				IntPtr intPtr = packageInfosPtr;
				for (int i = 0; i < numOfPackages; i++)
				{
					array[i] = (SecurityPackageInfo)Marshal.PtrToStructure(intPtr, typeof(SecurityPackageInfo));
					intPtr = IntPtr.Add(intPtr, offset);
				}
				return array;
			}
			finally
			{
				FreeContextBuffer(packageInfosPtr);
			}
		}

		[DllImport("secur32", CharSet = CharSet.Unicode)]
		private static extern int AcquireCredentialsHandle(string principal, string package, [MarshalAs(UnmanagedType.U4)] CredentialsUse credentialUse, IntPtr authenticationID, IntPtr authData, IntPtr getKeyFn, IntPtr getKeyArgument, ref SecurityHandle credential, ref ulong expiration);

		[DllImport("secur32", CharSet = CharSet.Unicode)]
		private static extern int InitializeSecurityContext(ref SecurityHandle credential, ref SecurityHandle context, string pszTargetName, [MarshalAs(UnmanagedType.U4)] ContextRequirements requirements, int Reserved1, int TargetDataRep, ref SecBufferDescription inBuffDesc, int Reserved2, ref SecurityHandle newContext, ref SecBufferDescription outBuffDesc, ref ContextRequirements contextAttributes, ref ulong expiration);

		[DllImport("secur32", CharSet = CharSet.Unicode)]
		private static extern int InitializeSecurityContext(ref SecurityHandle credential, IntPtr context, string pszTargetName, [MarshalAs(UnmanagedType.U4)] ContextRequirements requirements, int Reserved1, int TargetDataRep, IntPtr inBuffDesc, int Reserved2, ref SecurityHandle newContext, ref SecBufferDescription outBuffDesc, ref ContextRequirements contextAttributes, ref ulong expiration);

		[DllImport("secur32", CharSet = CharSet.Unicode)]
		private static extern int FreeCredentialsHandle(ref SecurityHandle credential);

		[DllImport("secur32", CharSet = CharSet.Unicode)]
		private static extern int DeleteSecurityContext(ref SecurityHandle context);

		[DllImport("secur32", CharSet = CharSet.Unicode)]
		private static extern int FreeContextBuffer(IntPtr buffer);

		[DllImport("secur32", CharSet = CharSet.Unicode)]
		private static extern int EnumerateSecurityPackagesW(ref uint numOfPackages, ref IntPtr packageInfosPtr);

		public void Dispose()
		{
			FreeCredentialsHandle(ref _credHandle);
			DeleteSecurityContext(ref _contextHandle);
		}
	}
}
