using Sandbox.SteamApi;

namespace Sandbox;

public partial struct Friend : IEquatable<Friend>
{
	/// <summary>
	/// Creates a friend reference from a Steam ID.
	/// </summary>
	public Friend( ulong steamid )
	{
		Id = steamid;
	}

	/// <summary>
	/// Creates a friend reference from a signed Steam ID.
	/// </summary>
	public Friend( long steamid ) : this( (ulong)steamid )
	{
	}

	public override string ToString() => Name;

	/// <summary>
	/// Returns true if this friend is the local user
	/// </summary>
	public readonly bool IsMe => Id == SteamClient.SteamId.ValueUnsigned;

	/// <summary>
	/// The friend's Steam Id
	/// </summary>
	public ulong Id { get; }

	/// <summary>
	/// The friend's name
	/// </summary>
	public readonly string Name => Sandbox.Preferences.StreamerMode
		? Sandbox.Utility.Steam.GetAnonymousName( Id )
		: (!SteamFriends.IsInstalled ? null : _nameCache.GetOrCreate( Id, SteamNative.Friends.GetFriendPersonaName ));

	/// <summary>
	/// The nickname you've set for this friend, otherwise returns their Steam display name
	/// </summary>
	internal readonly string DisplayName
	{
		get
		{
			if ( Sandbox.Preferences.StreamerMode )
				return Sandbox.Utility.Steam.GetAnonymousName( Id );

			if ( !string.IsNullOrWhiteSpace( Nickname ) )
				return Nickname;

			return string.IsNullOrWhiteSpace( Name ) ? null : Name;
		}
	}

	/// <summary>
	/// Returns true if your friend is online
	/// </summary>
	public readonly bool IsOnline => State != FriendState.Offline;

	/// <summary>
	/// Returns true if this user is your friend
	/// </summary>
	public readonly bool IsFriend => Relationship == Relationship.Friend;

	/// <summary>
	/// Returns true if you have blocked/ignored this user
	/// </summary>
	public readonly bool IsBlocked => Relationship == Relationship.Blocked;

	/// <summary>
	/// Returns true if your friend is away
	/// </summary>
	public readonly bool IsAway => State == FriendState.Away;

	/// <summary>
	/// Returns true if this friend is marked as busy
	/// </summary>
	public readonly bool IsBusy => State == FriendState.Busy;

	/// <summary>
	/// Returns true if this friend is marked as snoozing
	/// </summary>
	public readonly bool IsSnoozing => State == FriendState.Snooze;

	/// <summary>
	/// Returns a string that was possibly set by rich presence
	/// </summary>
	public readonly string GetRichPresence( string key )
	{
		if ( !SteamFriends.IsInstalled ) return null;
		var val = SteamNative.Friends.GetFriendRichPresence( Id, key );
		if ( string.IsNullOrEmpty( val ) ) return null;
		return val;
	}

	internal readonly bool InviteToGame( string connectString ) => SteamFriends.IsInstalled && SteamNative.Friends.InviteUserToGame( Id, connectString );

	/// <summary>
	/// Returns true if they're playing this game
	/// </summary>
	public readonly bool IsPlayingThisGame => (GameId ?? 0) == Application.AppId;

	/// <summary>
	/// Returns true if they're playing any game
	/// </summary>
	public readonly bool IsPlayingAGame => (GameId ?? 0) != 0;

	// Steam persona callbacks invalidate these cached native queries.
	static readonly Dictionary<ulong, string> _nameCache = new();
	static readonly Dictionary<ulong, string> _nicknameCache = new();
	static readonly Dictionary<ulong, Relationship> _relationshipCache = new();
	static readonly Dictionary<ulong, FriendState> _stateCache = new();
	static readonly Dictionary<ulong, ulong?> _gameIdCache = new();

	readonly string Nickname
	{
		get
		{
			if ( !SteamFriends.IsInstalled || !IsFriend )
				return null;

			var nickname = _nicknameCache.GetOrCreate( Id, SteamNative.Friends.GetPlayerNickname );
			return string.IsNullOrWhiteSpace( nickname ) ? null : nickname;
		}
	}

	readonly Relationship Relationship => !SteamFriends.IsInstalled ? Relationship.None : _relationshipCache.GetOrCreate( Id, SteamNative.Friends.GetFriendRelationship );

	readonly FriendState State
	{
		get
		{
			if ( !SteamFriends.IsInstalled )
				return FriendState.Offline;

			if ( !_stateCache.TryGetValue( Id, out var val ) )
			{
				val = SteamNative.Friends.GetFriendPersonaState( Id );
				_stateCache[Id] = val;
			}

			return val;
		}
	}

	/// <summary>
	/// The Steam game this friend is playing, if available.
	/// </summary>
	internal readonly ulong? GameId => !SteamFriends.IsInstalled ? null : _gameIdCache.GetOrCreate( Id, id =>
		SteamNative.Friends.GetFriendGamePlayed( id, out var gameId ) ? gameId : null );

	internal static void InvalidateCache( ulong id, PersonaChange changeFlags )
	{
		if ( changeFlags.Contains( PersonaChange.Name ) || changeFlags.Contains( PersonaChange.NameFirstSet ) )
			_nameCache.Remove( id );

		if ( changeFlags.Contains( PersonaChange.Nickname ) )
			_nicknameCache.Remove( id );

		if ( changeFlags.Contains( PersonaChange.RelationshipChanged ) )
		{
			_relationshipCache.Remove( id );
			_nicknameCache.Remove( id );
		}

		if ( changeFlags.Contains( PersonaChange.Status ) || changeFlags.Contains( PersonaChange.GoneOffline ) || changeFlags.Contains( PersonaChange.ComeOnline ) )
			_stateCache.Remove( id );

		if ( changeFlags.Contains( PersonaChange.GamePlayed ) || changeFlags.Contains( PersonaChange.GameServer ) )
			_gameIdCache.Remove( id );
	}

	internal static void ClearCache()
	{
		_nameCache.Clear();
		_nicknameCache.Clear();
		_relationshipCache.Clear();
		_stateCache.Clear();
		_gameIdCache.Clear();
	}

	private readonly string ProfileUrl => $"https://steamcommunity.com/profiles/{Id}";

	/// <summary>
	/// Opens their Steam profile in the Steam overlay, or in the default browser when the overlay is unavailable.
	/// </summary>
	public void OpenInOverlay()
	{
		if ( Id == 0 )
			return;

		Game.Overlay.OpenSteamPage( ProfileUrl, steamOverlay: true );
	}

	/// <summary>
	/// Opens Steam's Add Friend confirmation, or opens their Steam profile in the default browser when the overlay is unavailable.
	/// </summary>
	public void OpenAddFriendOverlay()
	{
		if ( Id == 0 )
			return;

		if ( !SteamFriends.OpenUserOverlay( Id, "friendadd" ) )
		{
			Game.Overlay.OpenSteamPage( ProfileUrl, steamOverlay: false );
		}
	}

	public override bool Equals( object obj ) => obj is Friend friend && Equals( friend );
	public bool Equals( Friend other ) => Id == other.Id;
	public override int GetHashCode() => HashCode.Combine( Id );
	public static bool operator ==( Friend friend1, Friend friend2 ) => friend1.Id == friend2.Id;
	public static bool operator !=( Friend friend1, Friend friend2 ) => friend1.Id != friend2.Id;
}

file static class FriendCacheExtensions
{
	internal static TValue GetOrCreate<TKey, TValue>( this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> createNew )
	{
		if ( !dict.TryGetValue( key, out var val ) )
		{
			val = createNew( key );
			dict.Add( key, val );
		}

		return val;
	}
}
