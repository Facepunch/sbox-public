using System.Runtime.InteropServices;

namespace Steamworks.Data
{
	internal partial struct gameserveritem_t
	{
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_gameserveritem_t_Construct", CallingConvention = Platform.CC )]
		internal static extern void InternalConstruct( ref gameserveritem_t self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_gameserveritem_t_GetName", CallingConvention = Platform.CC )]
		internal static extern Utf8StringPointer InternalGetName( ref gameserveritem_t self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_gameserveritem_t_SetName", CallingConvention = Platform.CC )]
		internal static extern void InternalSetName( ref gameserveritem_t self, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pName );

	}

	internal partial struct servernetadr_t
	{
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_Construct", CallingConvention = Platform.CC )]
		internal static extern void InternalConstruct( ref servernetadr_t self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_Init", CallingConvention = Platform.CC )]
		internal static extern void InternalInit( ref servernetadr_t self, uint ip, ushort usQueryPort, ushort usConnectionPort );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_GetQueryPort", CallingConvention = Platform.CC )]
		internal static extern ushort InternalGetQueryPort( ref servernetadr_t self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_SetQueryPort", CallingConvention = Platform.CC )]
		internal static extern void InternalSetQueryPort( ref servernetadr_t self, ushort usPort );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_GetConnectionPort", CallingConvention = Platform.CC )]
		internal static extern ushort InternalGetConnectionPort( ref servernetadr_t self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_SetConnectionPort", CallingConvention = Platform.CC )]
		internal static extern void InternalSetConnectionPort( ref servernetadr_t self, ushort usPort );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_GetIP", CallingConvention = Platform.CC )]
		internal static extern uint InternalGetIP( ref servernetadr_t self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_SetIP", CallingConvention = Platform.CC )]
		internal static extern void InternalSetIP( ref servernetadr_t self, uint unIP );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_GetConnectionAddressString", CallingConvention = Platform.CC )]
		internal static extern Utf8StringPointer InternalGetConnectionAddressString( ref servernetadr_t self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_GetQueryAddressString", CallingConvention = Platform.CC )]
		internal static extern Utf8StringPointer InternalGetQueryAddressString( ref servernetadr_t self );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_IsLessThan", CallingConvention = Platform.CC )]
		internal static extern bool InternalIsLessThan( ref servernetadr_t self, ref servernetadr_t netadr );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_servernetadr_t_Assign", CallingConvention = Platform.CC )]
		internal static extern void InternalAssign( ref servernetadr_t self, ref servernetadr_t that );

	}

	internal partial struct SteamDatagramHostedAddress
	{
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamDatagramHostedAddress_Clear", CallingConvention = Platform.CC )]
		internal static extern void InternalClear( ref SteamDatagramHostedAddress self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamDatagramHostedAddress_GetPopID", CallingConvention = Platform.CC )]
		internal static extern SteamNetworkingPOPID InternalGetPopID( ref SteamDatagramHostedAddress self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamDatagramHostedAddress_SetDevAddress", CallingConvention = Platform.CC )]
		internal static extern void InternalSetDevAddress( ref SteamDatagramHostedAddress self, uint nIP, ushort nPort, SteamNetworkingPOPID popid );

	}

	internal partial struct SteamIPAddress
	{
		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamIPAddress_t_IsSet", CallingConvention = Platform.CC )]
		internal static extern bool InternalIsSet( ref SteamIPAddress self );

	}

	internal partial struct NetIdentity
	{
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_Clear", CallingConvention = Platform.CC )]
		internal static extern void InternalClear( ref NetIdentity self );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_IsInvalid", CallingConvention = Platform.CC )]
		internal static extern bool InternalIsInvalid( ref NetIdentity self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetSteamID", CallingConvention = Platform.CC )]
		internal static extern void InternalSetSteamID( ref NetIdentity self, SteamId steamID );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetSteamID", CallingConvention = Platform.CC )]
		internal static extern SteamId InternalGetSteamID( ref NetIdentity self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetSteamID64", CallingConvention = Platform.CC )]
		internal static extern void InternalSetSteamID64( ref NetIdentity self, ulong steamID );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetSteamID64", CallingConvention = Platform.CC )]
		internal static extern ulong InternalGetSteamID64( ref NetIdentity self );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetXboxPairwiseID", CallingConvention = Platform.CC )]
		internal static extern bool InternalSetXboxPairwiseID( ref NetIdentity self, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pszString );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetXboxPairwiseID", CallingConvention = Platform.CC )]
		internal static extern Utf8StringPointer InternalGetXboxPairwiseID( ref NetIdentity self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetPSNID", CallingConvention = Platform.CC )]
		internal static extern void InternalSetPSNID( ref NetIdentity self, ulong id );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetPSNID", CallingConvention = Platform.CC )]
		internal static extern ulong InternalGetPSNID( ref NetIdentity self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetIPAddr", CallingConvention = Platform.CC )]
		internal static extern IntPtr InternalGetIPAddr( ref NetIdentity self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetIPv4Addr", CallingConvention = Platform.CC )]
		internal static extern void InternalSetIPv4Addr( ref NetIdentity self, uint nIPv4, ushort nPort );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetIPv4", CallingConvention = Platform.CC )]
		internal static extern uint InternalGetIPv4( ref NetIdentity self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetFakeIPType", CallingConvention = Platform.CC )]
		internal static extern SteamNetworkingFakeIPType InternalGetFakeIPType( ref NetIdentity self );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_IsFakeIP", CallingConvention = Platform.CC )]
		internal static extern bool InternalIsFakeIP( ref NetIdentity self );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetLocalHost", CallingConvention = Platform.CC )]
		internal static extern void InternalSetLocalHost( ref NetIdentity self );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_IsLocalHost", CallingConvention = Platform.CC )]
		internal static extern bool InternalIsLocalHost( ref NetIdentity self );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetGenericString", CallingConvention = Platform.CC )]
		internal static extern bool InternalSetGenericString( ref NetIdentity self, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pszString );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetGenericString", CallingConvention = Platform.CC )]
		internal static extern Utf8StringPointer InternalGetGenericString( ref NetIdentity self );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_SetGenericBytes", CallingConvention = Platform.CC )]
		internal static extern bool InternalSetGenericBytes( ref NetIdentity self, IntPtr data, uint cbLen );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_GetGenericBytes", CallingConvention = Platform.CC )]
		internal static extern byte InternalGetGenericBytes( ref NetIdentity self, ref int cbLen );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_IsEqualTo", CallingConvention = Platform.CC )]
		internal static extern bool InternalIsEqualTo( ref NetIdentity self, ref NetIdentity x );

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_ToString", CallingConvention = Platform.CC )]
		internal static extern void InternalToString( ref NetIdentity self, IntPtr buf, uint cbBuf );

		[return: MarshalAs( UnmanagedType.I1 )]
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingIdentity_ParseString", CallingConvention = Platform.CC )]
		internal static extern bool InternalParseString( ref NetIdentity self, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pszStr );

	}

	internal partial struct NetMsg
	{
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingMessage_t_Release", CallingConvention = Platform.CC )]
		internal static unsafe extern void InternalRelease( NetMsg* self );

	}

}
