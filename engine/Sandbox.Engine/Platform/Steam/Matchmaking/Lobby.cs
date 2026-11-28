using Sandbox;

namespace Sandbox.SteamApi;

internal struct Lobby
{
	public ulong Id { get; internal set; }

	public bool IsParty => GetData( "lobby_type" ) == "party";

	public Lobby( ulong id )
	{
		Id = id;
	}

	/// <summary>
	/// Try to join this room. Will return RoomEnter.Success on success,
	/// and anything else is a failure
	/// </summary>
	internal async Task<RoomEnter> Join()
	{
		var result = await SteamMatchmaking.JoinLobbyAsync( Id );
		return result.Response;
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
		return SteamNative.Matchmaking.InviteUserToLobby( Id, steamid );
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

			return SteamNative.Matchmaking.GetNumLobbyMembers( Id );
		}
	}

	/// <summary>
	/// Returns current members. Need to be in the lobby to see the users.
	/// </summary>
	public IEnumerable<Friend> Members
	{
		get
		{
			if ( Id == 0 || !LobbyManager.ActiveLobbies.Contains( Id ) )
				yield break;

			foreach ( var id in GetMemberIds() )
			{
				if ( id == 0 )
					yield break;

				yield return new Friend( id );
			}
		}
	}

	internal ulong[] GetMemberIds()
	{
		using var members = SteamNative.Matchmaking.GetLobbyMemberIds( Id );
		return members.AsSpan<ulong>().ToArray();
	}

	public bool SetOwner( ulong owner )
	{
		return SteamNative.Matchmaking.SetLobbyOwner( Id, owner );
	}


	/// <summary>
	/// Get data associated with this lobby
	/// </summary>
	public string GetData( string key )
	{
		return SteamNative.Matchmaking.GetLobbyData( Id, key );
	}

	/// <summary>
	/// Get data associated with this lobby
	/// </summary>
	public bool SetData( string key, string value )
	{
		if ( key.Length > 255 ) throw new System.ArgumentException( "Key should be < 255 chars", nameof( key ) );
		if ( value != null && value.Length > 8192 ) throw new System.ArgumentException( "Value should be < 8192 chars", nameof( key ) );
		if ( GetData( key ) == value ) return false;

		return SteamNative.Matchmaking.SetLobbyData( Id, key, value );
	}

	/// <summary>
	/// Removes a metadata key from the lobby
	/// </summary>
	public bool DeleteData( string key )
	{
		if ( string.IsNullOrEmpty( GetData( key ) ) ) return false;
		return SteamNative.Matchmaking.DeleteLobbyData( Id, key );
	}

	/// <summary>
	/// Get all data for this lobby
	/// </summary>
	public IEnumerable<KeyValuePair<string, string>> Data
	{
		get
		{
			using var metadata = System.Text.Json.JsonDocument.Parse( SteamNative.Matchmaking.GetLobbyMetadata( Id ) );
			foreach ( var entry in metadata.RootElement.EnumerateObject() )
			{
				yield return new( entry.Name, entry.Value.GetString() );
			}
		}
	}

	/// <summary>
	/// Gets per-user metadata for someone in this lobby
	/// </summary>
	public string GetMemberData( Friend member, string key )
	{
		return SteamNative.Matchmaking.GetLobbyMemberData( Id, member.Id, key );
	}

	/// <summary>
	/// Sets per-user metadata (for the local user implicitly)
	/// </summary>
	public void SetMemberData( string key, string value )
	{
		SteamNative.Matchmaking.SetLobbyMemberData( Id, key, value );
	}

	public unsafe bool SendChatData( byte[] data )
	{
		fixed ( byte* ptr = data )
		{
			return SteamNative.Matchmaking.SendLobbyChatMsg( Id, (IntPtr)ptr, data.Length );
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
		get => SteamNative.Matchmaking.GetLobbyMemberLimit( Id );
		set => SteamNative.Matchmaking.SetLobbyMemberLimit( Id, value );
	}

	public bool SetPublic()
	{
		return SteamNative.Matchmaking.SetLobbyType( Id, LobbyType.Public );
	}

	public bool SetPrivate()
	{
		return SteamNative.Matchmaking.SetLobbyType( Id, LobbyType.Private );
	}

	public Friend Owner
	{
		get
		{
			var ownerId = SteamNative.Matchmaking.GetLobbyOwner( Id );
			if ( ownerId == 0 )
			{
				ownerId = GetData( "_ownerid" ).ToULong();
			}

			return new Friend( ownerId );
		}
	}

	public override string ToString()
	{
		return $"{Id} [{MemberCount}/{MaxMembers}]";
	}
}
