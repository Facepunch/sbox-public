using Sandbox;
using Steamworks.Data;

namespace Steamworks;

internal struct Friend
{
	public SteamId Id;

	public Friend( SteamId steamid )
	{
		Id = steamid;
	}

	public override string ToString()
	{
		return $"{Name} ({Id})";
	}

	/// <summary>
	/// Returns true if this is the local user
	/// </summary>
	public bool IsMe => Id == SteamClient.SteamId;

	/// <summary>
	/// Return true if this is a friend
	/// </summary>
	public bool IsFriend => Relationship == Relationship.Friend;

	/// <summary>
	/// Returns true if you have this user blocked
	/// </summary>
	public bool IsBlocked => Relationship == Relationship.Blocked;

	/// <summary>
	/// Return true if this user is playing the game we're running
	/// </summary>
	public bool IsPlayingThisGame => GameInfo?.GameID == SteamClient.AppId;

	/// <summary>
	/// Return true if this user is playing another game
	/// </summary>
	public bool IsPlaying => GameInfo?.GameID > 0;

	/// <summary>
	/// Returns true if this friend is online
	/// </summary>
	public bool IsOnline => State != FriendState.Offline;

	/// <summary>
	/// Sometimes we don't know the user's name. This will wait until we have
	/// downloaded the information on this user.
	/// </summary>
	public async Task RequestInfoAsync()
	{
		await SteamFriends.CacheUserInformationAsync( Id, true );
	}

	/// <summary>
	/// Returns true if this friend is marked as away
	/// </summary>
	public bool IsAway => State == FriendState.Away;

	/// <summary>
	/// Returns true if this friend is marked as busy
	/// </summary>
	public bool IsBusy => State == FriendState.Busy;

	/// <summary>
	/// Returns true if this friend is marked as snoozing
	/// </summary>
	public bool IsSnoozing => State == FriendState.Snooze;

	//
	// These take an annoying amount of time to call, innocently iterating on a friends list of 20 was eating 1-2ms
	// And some people have 500+, this could be done a bit neater but I didn't want to break any APIs and this seemed
	// like the best layer to do it on.
	// We get a PersonaStateChange_t whenever any of these are dirty, and clear the cache(s) depending on flags
	//

	internal static Dictionary<ulong, string> _nameCache = new();
	internal static Dictionary<ulong, Relationship> _relationshipCache = new();
	internal static Dictionary<ulong, FriendState> _stateCache = new();
	internal static Dictionary<ulong, int> _steamLevelCache = new();
	internal static Dictionary<ulong, FriendGameInfo?> _gameInfoCache = new();

	public string Name => !SteamFriends.IsInstalled ? null : _nameCache.GetOrCreate( Id.Value, SteamFriends.Internal.GetFriendPersonaName );
	public Relationship Relationship => !SteamFriends.IsInstalled ? Steamworks.Relationship.None : _relationshipCache.GetOrCreate( Id.Value, SteamFriends.Internal.GetFriendRelationship );
	public FriendState State
	{
		get
		{
			if ( !SteamFriends.IsInstalled )
				return FriendState.Offline;

			if ( !_stateCache.TryGetValue( Id.Value, out var val ) )
			{
				val = SteamFriends.Internal.GetFriendPersonaState( Id.Value );
				_stateCache[Id.Value] = val;
			}

			return val;
		}
	}
	public int SteamLevel => !SteamFriends.IsInstalled ? 0 : _steamLevelCache.GetOrCreate( Id.Value, SteamFriends.Internal.GetFriendSteamLevel );

	public FriendGameInfo? GameInfo => !SteamFriends.IsInstalled ? null : _gameInfoCache.GetOrCreate( Id, x =>
	{
		FriendGameInfo_t gameInfo = default;
		if ( !SteamFriends.Internal.GetFriendGamePlayed( x, ref gameInfo ) )
			return null;

		return FriendGameInfo.From( gameInfo );
	} );

	public IEnumerable<string> NameHistory
	{
		get
		{
			if ( !SteamFriends.IsInstalled )
				yield break;

			for ( int i = 0; i < 32; i++ )
			{
				var n = SteamFriends.Internal.GetFriendPersonaNameHistory( Id, i );
				if ( string.IsNullOrEmpty( n ) )
					break;

				yield return n;
			}
		}
	}

	public bool IsIn( SteamId group_or_room )
	{
		if ( !SteamFriends.IsInstalled ) return false;
		return SteamFriends.Internal.IsUserInSource( Id, group_or_room );
	}

	public struct FriendGameInfo
	{
		internal ulong GameID; // m_gameID class CGameID
		internal uint GameIP; // m_unGameIP uint32
		internal ulong SteamIDLobby; // m_steamIDLobby class CSteamID

		public ulong GameId => GameID;
		public int ConnectionPort;
		public int QueryPort;

		public uint IpAddressRaw => GameIP;
		public System.Net.IPAddress IpAddress => Utility.Int32ToIp( GameIP );

		internal static FriendGameInfo From( FriendGameInfo_t i )
		{
			return new FriendGameInfo
			{
				GameID = i.GameID,
				GameIP = i.GameIP,
				ConnectionPort = i.GamePort,
				QueryPort = i.QueryPort,
				SteamIDLobby = i.SteamIDLobby,
			};
		}
	}

	internal async Task<Data.Image?> GetSmallAvatarAsync()
	{
		return await SteamFriends.GetSmallAvatarAsync( Id );
	}

	internal async Task<Data.Image?> GetMediumAvatarAsync()
	{
		return await SteamFriends.GetMediumAvatarAsync( Id );
	}

	internal async Task<Data.Image?> GetLargeAvatarAsync()
	{
		return await SteamFriends.GetLargeAvatarAsync( Id );
	}

	public string GetRichPresence( string key )
	{
		if ( !SteamFriends.IsInstalled ) return null;
		var val = SteamFriends.Internal.GetFriendRichPresence( Id, key );
		if ( string.IsNullOrEmpty( val ) ) return null;
		return val;
	}

	/// <summary>
	/// Invite this friend to the game that we are playing
	/// </summary>
	internal bool InviteToGame( string Text )
	{
		if ( !SteamFriends.IsInstalled ) return false;
		return SteamFriends.Internal.InviteUserToGame( Id, Text );
	}

	/// <summary>
	/// Activates the Steam Overlay to a specific dialog
	/// </summary>
	/// <param name="type">
	/// "steamid" - Opens the overlay web browser to the specified user or groups profile.
	/// "chat" - Opens a chat window to the specified user, or joins the group chat.
	/// "jointrade" - Opens a window to a Steam Trading session that was started with the ISteamEconomy/StartTrade Web API.
	/// "stats" - Opens the overlay web browser to the specified user's stats.
	/// "achievements" - Opens the overlay web browser to the specified user's achievements.
	/// "friendadd" - Opens the overlay in minimal mode prompting the user to add the target user as a friend.
	/// "friendremove" - Opens the overlay in minimal mode prompting the user to remove the target friend.
	/// "friendrequestaccept" - Opens the overlay in minimal mode prompting the user to accept an incoming friend invite.
	/// "friendrequestignore" - Opens the overlay in minimal mode prompting the user to ignore an incoming friend invite.
	/// </param>
	internal void OpenInOverlay( string type = "steamid" )
	{
		SteamFriends.OpenUserOverlay( Id, type );
	}

	/// <summary>
	/// Sends a message to a Steam friend. Returns true if success
	/// </summary>
	internal bool SendMessage( string message )
	{
		if ( !SteamFriends.IsInstalled ) return false;
		return SteamFriends.Internal.ReplyToFriendMessage( Id, message );
	}

	internal static void SteamFriends_OnPersonaStateChange( SteamId id, PersonaChange changeFlags )
	{
		if ( changeFlags.Contains( PersonaChange.Name ) )
			_nameCache.Remove( id );

		if ( changeFlags.Contains( PersonaChange.RelationshipChanged ) )
			_relationshipCache.Remove( id );

		if ( changeFlags.Contains( PersonaChange.Status ) || changeFlags.Contains( PersonaChange.GoneOffline ) || changeFlags.Contains( PersonaChange.ComeOnline ) )
			_stateCache.Remove( id );

		if ( changeFlags.Contains( PersonaChange.SteamLevel ) )
			_steamLevelCache.Remove( id );

		if ( changeFlags.Contains( PersonaChange.GamePlayed ) || changeFlags.Contains( PersonaChange.GameServer ) )
			_gameInfoCache.Remove( id );
	}
}

static class DictionaryExtensions
{
	public static TValue GetOrCreate<TKey, TValue>( this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> createNew )
	{
		if ( !dict.TryGetValue( key, out var val ) )
		{
			val = createNew( key );
			dict.Add( key, val );
		}

		return val;
	}
}
