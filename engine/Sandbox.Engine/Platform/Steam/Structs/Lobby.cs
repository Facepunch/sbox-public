using Sandbox;

namespace Steamworks.Data;

internal struct Lobby
{
	public SteamId Id { get; internal set; }

	public bool IsParty => GetData( "lobby_type" ) == "party";

	public Lobby( SteamId id )
	{
		Id = id;
	}

	/// <summary>
	/// Try to join this room. Will return RoomEnter.Success on success,
	/// and anything else is a failure
	/// </summary>
	internal async Task<RoomEnter> Join()
	{
		var result = await SteamMatchmaking.Internal.JoinLobby( Id );
		if ( !result.HasValue ) return RoomEnter.Error;

		return (RoomEnter)result.Value.EChatRoomEnterResponse;
	}

	/// <summary>
	/// Leave a lobby; this will take effect immediately on the client side
	/// other users in the lobby will be notified by a LobbyChatUpdate_t callback
	/// </summary>
	public void Leave()
	{
		LobbyManager.OnLeave( Id );
	}

	/// <summary>
	/// Invite another user to the lobby
	/// will return true if the invite is successfully sent, whether or not the target responds
	/// returns false if the local user is not connected to the Steam servers
	/// </summary>
	public bool InviteFriend( SteamId steamid )
	{
		return SteamMatchmaking.Internal.InviteUserToLobby( Id, steamid );
	}

	/// <summary>
	/// Invite another user to the lobby
	/// </summary>
	public void InviteOverlay()
	{
		SteamFriends.OpenGameInviteOverlay( Id );
	}

	/// <summary>
	/// Get current lobby's member count
	/// </summary>
	public int MemberCount
	{
		get
		{
			if ( Id == 0 )
				return 0;

			return SteamMatchmaking.Internal.GetNumLobbyMembers( Id );
		}
	}

	/// <summary>
	/// Returns current members. Need to be in the lobby to see the users.
	/// </summary>
	public IEnumerable<Friend> Members
	{
		get
		{
			if ( Id == 0 || !LobbyManager.ActiveLobbies.Contains( Id.Value ) )
				yield break;

			var c = MemberCount;
			for ( int i = 0; i < c; i++ )
			{
				var idx = SteamMatchmaking.Internal.GetLobbyMemberByIndex( Id, i );
				if ( idx == 0 )
					yield break;

				yield return new Friend( idx );
			}
		}
	}

	public bool SetOwner( ulong owner )
	{
		return SteamMatchmaking.Internal.SetLobbyOwner( Id, owner );
	}


	/// <summary>
	/// Get data associated with this lobby
	/// </summary>
	public string GetData( string key )
	{
		return SteamMatchmaking.Internal.GetLobbyData( Id, key );
	}

	/// <summary>
	/// Get data associated with this lobby
	/// </summary>
	public bool SetData( string key, string value )
	{
		if ( key.Length > 255 ) throw new System.ArgumentException( "Key should be < 255 chars", nameof( key ) );
		if ( value != null && value.Length > 8192 ) throw new System.ArgumentException( "Value should be < 8192 chars", nameof( key ) );
		if ( GetData( key ) == value ) return false;

		return SteamMatchmaking.Internal.SetLobbyData( Id, key, value );
	}

	/// <summary>
	/// Removes a metadata key from the lobby
	/// </summary>
	public bool DeleteData( string key )
	{
		if ( string.IsNullOrEmpty( GetData( key ) ) ) return false;
		return SteamMatchmaking.Internal.DeleteLobbyData( Id, key );
	}

	/// <summary>
	/// Get all data for this lobby
	/// </summary>
	public IEnumerable<KeyValuePair<string, string>> Data
	{
		get
		{
			var cnt = SteamMatchmaking.Internal.GetLobbyDataCount( Id );

			for ( int i = 0; i < cnt; i++ )
			{
				if ( SteamMatchmaking.Internal.GetLobbyDataByIndex( Id, i, out var a, out var b ) )
				{
					yield return new KeyValuePair<string, string>( a, b );
				}
			}
		}
	}

	/// <summary>
	/// Gets per-user metadata for someone in this lobby
	/// </summary>
	public string GetMemberData( Friend member, string key )
	{
		return SteamMatchmaking.Internal.GetLobbyMemberData( Id, member.Id, key );
	}

	/// <summary>
	/// Sets per-user metadata (for the local user implicitly)
	/// </summary>
	public void SetMemberData( string key, string value )
	{
		SteamMatchmaking.Internal.SetLobbyMemberData( Id, key, value );
	}

	public unsafe bool SendChatData( byte[] data )
	{
		fixed ( byte* ptr = data )
		{
			return SteamMatchmaking.Internal.SendLobbyChatMsg( Id, (IntPtr)ptr, data.Length );
		}
	}

	/// <summary>
	/// Make the request to refresh this lobby data, and hang on till we get a result.
	/// Will timeout after 5s and return false.
	/// </summary>
	public async Task<bool> Refresh()
	{
		return await LobbyManager.Refresh( this );
	}

	/// <summary>
	/// Max members able to join this lobby. Cannot be over 250.
	/// Can only be set by the owner
	/// </summary>
	public int MaxMembers
	{
		get => SteamMatchmaking.Internal.GetLobbyMemberLimit( Id );
		set => SteamMatchmaking.Internal.SetLobbyMemberLimit( Id, value );
	}

	public bool SetPublic()
	{
		return SteamMatchmaking.Internal.SetLobbyType( Id, LobbyType.Public );
	}

	public bool SetPrivate()
	{
		return SteamMatchmaking.Internal.SetLobbyType( Id, LobbyType.Private );
	}

	public bool SetInvisible()
	{
		return SteamMatchmaking.Internal.SetLobbyType( Id, LobbyType.Invisible );
	}

	public bool SetFriendsOnly()
	{
		return SteamMatchmaking.Internal.SetLobbyType( Id, LobbyType.FriendsOnly );
	}

	public bool SetJoinable( bool b )
	{
		return SteamMatchmaking.Internal.SetLobbyJoinable( Id, b );
	}

	/// <summary>
	/// You must be the lobby owner to set the owner
	/// </summary>
	public Friend Owner
	{
		get
		{
			var ownerId = SteamMatchmaking.Internal.GetLobbyOwner( Id );
			if ( ownerId.Value == 0 )
			{
				ownerId.Value = GetData( "_ownerid" ).ToULong( ownerId.Value );
			}

			return new Friend( ownerId );
		}
		set => SteamMatchmaking.Internal.SetLobbyOwner( Id, value.Id );
	}

	/// <summary>
	/// Check if the specified SteamId owns the lobby
	/// </summary>
	public bool IsOwnedBy( SteamId k ) => Owner.Id == k;

	public override string ToString()
	{
		return $"{Id} [{MemberCount}/{MaxMembers}]";
	}
}
