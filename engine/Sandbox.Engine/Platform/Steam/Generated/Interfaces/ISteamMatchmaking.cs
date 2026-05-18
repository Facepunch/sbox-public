using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Steamworks.Data;


namespace Steamworks
{
	internal unsafe class ISteamMatchmaking : SteamInterface
	{

		internal ISteamMatchmaking( bool IsGameServer )
		{
			SetupInterface( IsGameServer );
		}

		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamMatchmaking_v009", CallingConvention = Platform.CC )]
		internal static extern IntPtr SteamAPI_SteamMatchmaking_v009();
		internal override IntPtr GetUserInterfacePointer() => SteamAPI_SteamMatchmaking_v009();


		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetFavoriteGameCount", CallingConvention = Platform.CC )]
		private static extern int _GetFavoriteGameCount( IntPtr self );

		#endregion
		internal int GetFavoriteGameCount()
		{
			var returnValue = _GetFavoriteGameCount( Self );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetFavoriteGame", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetFavoriteGame( IntPtr self, int iGame, ref AppId pnAppID, ref uint pnIP, ref ushort pnConnPort, ref ushort pnQueryPort, ref uint punFlags, ref uint pRTime32LastPlayedOnServer );

		#endregion
		internal bool GetFavoriteGame( int iGame, ref AppId pnAppID, ref uint pnIP, ref ushort pnConnPort, ref ushort pnQueryPort, ref uint punFlags, ref uint pRTime32LastPlayedOnServer )
		{
			var returnValue = _GetFavoriteGame( Self, iGame, ref pnAppID, ref pnIP, ref pnConnPort, ref pnQueryPort, ref punFlags, ref pRTime32LastPlayedOnServer );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddFavoriteGame", CallingConvention = Platform.CC )]
		private static extern int _AddFavoriteGame( IntPtr self, AppId nAppID, uint nIP, ushort nConnPort, ushort nQueryPort, uint unFlags, uint rTime32LastPlayedOnServer );

		#endregion
		internal int AddFavoriteGame( AppId nAppID, uint nIP, ushort nConnPort, ushort nQueryPort, uint unFlags, uint rTime32LastPlayedOnServer )
		{
			var returnValue = _AddFavoriteGame( Self, nAppID, nIP, nConnPort, nQueryPort, unFlags, rTime32LastPlayedOnServer );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_RemoveFavoriteGame", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _RemoveFavoriteGame( IntPtr self, AppId nAppID, uint nIP, ushort nConnPort, ushort nQueryPort, uint unFlags );

		#endregion
		internal bool RemoveFavoriteGame( AppId nAppID, uint nIP, ushort nConnPort, ushort nQueryPort, uint unFlags )
		{
			var returnValue = _RemoveFavoriteGame( Self, nAppID, nIP, nConnPort, nQueryPort, unFlags );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_RequestLobbyList", CallingConvention = Platform.CC )]
		private static extern SteamAPICall_t _RequestLobbyList( IntPtr self );

		#endregion
		internal CallResult<LobbyMatchList_t> RequestLobbyList()
		{
			var returnValue = _RequestLobbyList( Self );
			return new CallResult<LobbyMatchList_t>( returnValue, IsServer );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddRequestLobbyListStringFilter", CallingConvention = Platform.CC )]
		private static extern void _AddRequestLobbyListStringFilter( IntPtr self, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKeyToMatch, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchValueToMatch, LobbyComparison eComparisonType );

		#endregion
		internal void AddRequestLobbyListStringFilter( [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKeyToMatch, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchValueToMatch, LobbyComparison eComparisonType )
		{
			_AddRequestLobbyListStringFilter( Self, pchKeyToMatch, pchValueToMatch, eComparisonType );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddRequestLobbyListNumericalFilter", CallingConvention = Platform.CC )]
		private static extern void _AddRequestLobbyListNumericalFilter( IntPtr self, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKeyToMatch, int nValueToMatch, LobbyComparison eComparisonType );

		#endregion
		internal void AddRequestLobbyListNumericalFilter( [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKeyToMatch, int nValueToMatch, LobbyComparison eComparisonType )
		{
			_AddRequestLobbyListNumericalFilter( Self, pchKeyToMatch, nValueToMatch, eComparisonType );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddRequestLobbyListNearValueFilter", CallingConvention = Platform.CC )]
		private static extern void _AddRequestLobbyListNearValueFilter( IntPtr self, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKeyToMatch, int nValueToBeCloseTo );

		#endregion
		internal void AddRequestLobbyListNearValueFilter( [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKeyToMatch, int nValueToBeCloseTo )
		{
			_AddRequestLobbyListNearValueFilter( Self, pchKeyToMatch, nValueToBeCloseTo );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddRequestLobbyListFilterSlotsAvailable", CallingConvention = Platform.CC )]
		private static extern void _AddRequestLobbyListFilterSlotsAvailable( IntPtr self, int nSlotsAvailable );

		#endregion
		internal void AddRequestLobbyListFilterSlotsAvailable( int nSlotsAvailable )
		{
			_AddRequestLobbyListFilterSlotsAvailable( Self, nSlotsAvailable );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter", CallingConvention = Platform.CC )]
		private static extern void _AddRequestLobbyListDistanceFilter( IntPtr self, LobbyDistanceFilter eLobbyDistanceFilter );

		#endregion
		internal void AddRequestLobbyListDistanceFilter( LobbyDistanceFilter eLobbyDistanceFilter )
		{
			_AddRequestLobbyListDistanceFilter( Self, eLobbyDistanceFilter );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter", CallingConvention = Platform.CC )]
		private static extern void _AddRequestLobbyListResultCountFilter( IntPtr self, int cMaxResults );

		#endregion
		internal void AddRequestLobbyListResultCountFilter( int cMaxResults )
		{
			_AddRequestLobbyListResultCountFilter( Self, cMaxResults );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_AddRequestLobbyListCompatibleMembersFilter", CallingConvention = Platform.CC )]
		private static extern void _AddRequestLobbyListCompatibleMembersFilter( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal void AddRequestLobbyListCompatibleMembersFilter( SteamId steamIDLobby )
		{
			_AddRequestLobbyListCompatibleMembersFilter( Self, steamIDLobby );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyByIndex", CallingConvention = Platform.CC )]
		private static extern SteamId _GetLobbyByIndex( IntPtr self, int iLobby );

		#endregion
		internal SteamId GetLobbyByIndex( int iLobby )
		{
			var returnValue = _GetLobbyByIndex( Self, iLobby );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_CreateLobby", CallingConvention = Platform.CC )]
		private static extern SteamAPICall_t _CreateLobby( IntPtr self, LobbyType eLobbyType, int cMaxMembers );

		#endregion
		internal CallResult<LobbyCreated_t> CreateLobby( LobbyType eLobbyType, int cMaxMembers )
		{
			var returnValue = _CreateLobby( Self, eLobbyType, cMaxMembers );
			return new CallResult<LobbyCreated_t>( returnValue, IsServer );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_JoinLobby", CallingConvention = Platform.CC )]
		private static extern SteamAPICall_t _JoinLobby( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal CallResult<LobbyEnter_t> JoinLobby( SteamId steamIDLobby )
		{
			var returnValue = _JoinLobby( Self, steamIDLobby );
			return new CallResult<LobbyEnter_t>( returnValue, IsServer );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_LeaveLobby", CallingConvention = Platform.CC )]
		private static extern void _LeaveLobby( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal void LeaveLobby( SteamId steamIDLobby )
		{
			_LeaveLobby( Self, steamIDLobby );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_InviteUserToLobby", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _InviteUserToLobby( IntPtr self, SteamId steamIDLobby, SteamId steamIDInvitee );

		#endregion
		internal bool InviteUserToLobby( SteamId steamIDLobby, SteamId steamIDInvitee )
		{
			var returnValue = _InviteUserToLobby( Self, steamIDLobby, steamIDInvitee );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetNumLobbyMembers", CallingConvention = Platform.CC )]
		private static extern int _GetNumLobbyMembers( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal int GetNumLobbyMembers( SteamId steamIDLobby )
		{
			var returnValue = _GetNumLobbyMembers( Self, steamIDLobby );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex", CallingConvention = Platform.CC )]
		private static extern SteamId _GetLobbyMemberByIndex( IntPtr self, SteamId steamIDLobby, int iMember );

		#endregion
		internal SteamId GetLobbyMemberByIndex( SteamId steamIDLobby, int iMember )
		{
			var returnValue = _GetLobbyMemberByIndex( Self, steamIDLobby, iMember );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyData", CallingConvention = Platform.CC )]
		private static extern Utf8StringPointer _GetLobbyData( IntPtr self, SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey );

		#endregion
		internal string GetLobbyData( SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey )
		{
			var returnValue = _GetLobbyData( Self, steamIDLobby, pchKey );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLobbyData", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetLobbyData( IntPtr self, SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchValue );

		#endregion
		internal bool SetLobbyData( SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchValue )
		{
			var returnValue = _SetLobbyData( Self, steamIDLobby, pchKey, pchValue );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyDataCount", CallingConvention = Platform.CC )]
		private static extern int _GetLobbyDataCount( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal int GetLobbyDataCount( SteamId steamIDLobby )
		{
			var returnValue = _GetLobbyDataCount( Self, steamIDLobby );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyDataByIndex", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetLobbyDataByIndex( IntPtr self, SteamId steamIDLobby, int iLobbyData, IntPtr pchKey, int cchKeyBufferSize, IntPtr pchValue, int cchValueBufferSize );

		#endregion
		internal unsafe bool GetLobbyDataByIndex( SteamId steamIDLobby, int iLobbyData, out string pchKey, out string pchValue )
		{
			const int KeyBufferSize = 256;
			const int ValueBufferSize = 8192;

			byte* keyBuffer = stackalloc byte[KeyBufferSize];
			byte* valueBuffer = stackalloc byte[ValueBufferSize];

			var returnValue = _GetLobbyDataByIndex( Self, steamIDLobby, iLobbyData, (IntPtr)keyBuffer, KeyBufferSize, (IntPtr)valueBuffer, ValueBufferSize );

			if ( !returnValue )
			{
				pchKey = string.Empty;
				pchValue = string.Empty;
				return false;
			}

			pchKey = Helpers.MemoryToString( (IntPtr)keyBuffer, KeyBufferSize );
			pchValue = Helpers.MemoryToString( (IntPtr)valueBuffer, ValueBufferSize );
			return true;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_DeleteLobbyData", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _DeleteLobbyData( IntPtr self, SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey );

		#endregion
		internal bool DeleteLobbyData( SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey )
		{
			var returnValue = _DeleteLobbyData( Self, steamIDLobby, pchKey );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyMemberData", CallingConvention = Platform.CC )]
		private static extern Utf8StringPointer _GetLobbyMemberData( IntPtr self, SteamId steamIDLobby, SteamId steamIDUser, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey );

		#endregion
		internal string GetLobbyMemberData( SteamId steamIDLobby, SteamId steamIDUser, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey )
		{
			var returnValue = _GetLobbyMemberData( Self, steamIDLobby, steamIDUser, pchKey );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLobbyMemberData", CallingConvention = Platform.CC )]
		private static extern void _SetLobbyMemberData( IntPtr self, SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchValue );

		#endregion
		internal void SetLobbyMemberData( SteamId steamIDLobby, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchKey, [MarshalAs( UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof( Utf8StringToNative ) )] string pchValue )
		{
			_SetLobbyMemberData( Self, steamIDLobby, pchKey, pchValue );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SendLobbyChatMsg", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SendLobbyChatMsg( IntPtr self, SteamId steamIDLobby, IntPtr pvMsgBody, int cubMsgBody );

		#endregion
		internal bool SendLobbyChatMsg( SteamId steamIDLobby, IntPtr pvMsgBody, int cubMsgBody )
		{
			var returnValue = _SendLobbyChatMsg( Self, steamIDLobby, pvMsgBody, cubMsgBody );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyChatEntry", CallingConvention = Platform.CC )]
		private static extern int _GetLobbyChatEntry( IntPtr self, SteamId steamIDLobby, int iChatID, ref SteamId pSteamIDUser, IntPtr pvData, int cubData, ref ChatEntryType peChatEntryType );

		#endregion
		internal int GetLobbyChatEntry( SteamId steamIDLobby, int iChatID, ref SteamId pSteamIDUser, IntPtr pvData, int cubData, ref ChatEntryType peChatEntryType )
		{
			var returnValue = _GetLobbyChatEntry( Self, steamIDLobby, iChatID, ref pSteamIDUser, pvData, cubData, ref peChatEntryType );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_RequestLobbyData", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _RequestLobbyData( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal bool RequestLobbyData( SteamId steamIDLobby )
		{
			var returnValue = _RequestLobbyData( Self, steamIDLobby );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLobbyGameServer", CallingConvention = Platform.CC )]
		private static extern void _SetLobbyGameServer( IntPtr self, SteamId steamIDLobby, uint unGameServerIP, ushort unGameServerPort, SteamId steamIDGameServer );

		#endregion
		internal void SetLobbyGameServer( SteamId steamIDLobby, uint unGameServerIP, ushort unGameServerPort, SteamId steamIDGameServer )
		{
			_SetLobbyGameServer( Self, steamIDLobby, unGameServerIP, unGameServerPort, steamIDGameServer );
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyGameServer", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetLobbyGameServer( IntPtr self, SteamId steamIDLobby, ref uint punGameServerIP, ref ushort punGameServerPort, ref SteamId psteamIDGameServer );

		#endregion
		internal bool GetLobbyGameServer( SteamId steamIDLobby, ref uint punGameServerIP, ref ushort punGameServerPort, ref SteamId psteamIDGameServer )
		{
			var returnValue = _GetLobbyGameServer( Self, steamIDLobby, ref punGameServerIP, ref punGameServerPort, ref psteamIDGameServer );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLobbyMemberLimit", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetLobbyMemberLimit( IntPtr self, SteamId steamIDLobby, int cMaxMembers );

		#endregion
		internal bool SetLobbyMemberLimit( SteamId steamIDLobby, int cMaxMembers )
		{
			var returnValue = _SetLobbyMemberLimit( Self, steamIDLobby, cMaxMembers );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit", CallingConvention = Platform.CC )]
		private static extern int _GetLobbyMemberLimit( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal int GetLobbyMemberLimit( SteamId steamIDLobby )
		{
			var returnValue = _GetLobbyMemberLimit( Self, steamIDLobby );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLobbyType", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetLobbyType( IntPtr self, SteamId steamIDLobby, LobbyType eLobbyType );

		#endregion
		internal bool SetLobbyType( SteamId steamIDLobby, LobbyType eLobbyType )
		{
			var returnValue = _SetLobbyType( Self, steamIDLobby, eLobbyType );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLobbyJoinable", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetLobbyJoinable( IntPtr self, SteamId steamIDLobby, [MarshalAs( UnmanagedType.U1 )] bool bLobbyJoinable );

		#endregion
		internal bool SetLobbyJoinable( SteamId steamIDLobby, [MarshalAs( UnmanagedType.U1 )] bool bLobbyJoinable )
		{
			var returnValue = _SetLobbyJoinable( Self, steamIDLobby, bLobbyJoinable );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_GetLobbyOwner", CallingConvention = Platform.CC )]
		private static extern SteamId _GetLobbyOwner( IntPtr self, SteamId steamIDLobby );

		#endregion
		internal SteamId GetLobbyOwner( SteamId steamIDLobby )
		{
			var returnValue = _GetLobbyOwner( Self, steamIDLobby );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLobbyOwner", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetLobbyOwner( IntPtr self, SteamId steamIDLobby, SteamId steamIDNewOwner );

		#endregion
		internal bool SetLobbyOwner( SteamId steamIDLobby, SteamId steamIDNewOwner )
		{
			var returnValue = _SetLobbyOwner( Self, steamIDLobby, steamIDNewOwner );
			return returnValue;
		}

		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_SetLinkedLobby", CallingConvention = Platform.CC )]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetLinkedLobby( IntPtr self, SteamId steamIDLobby, SteamId steamIDLobbyDependent );

		#endregion
		internal bool SetLinkedLobby( SteamId steamIDLobby, SteamId steamIDLobbyDependent )
		{
			var returnValue = _SetLinkedLobby( Self, steamIDLobby, steamIDLobbyDependent );
			return returnValue;
		}

	}
}
