using Steamworks;

namespace Sandbox;

public partial struct Friend : IEquatable<Friend>
{
	internal Steamworks.Friend Internal;

	internal Friend( Steamworks.Friend value )
	{
		Internal = value;
	}

	internal Friend( Steamworks.SteamId value )
	{
		Internal = new Steamworks.Friend( value );
	}

	public Friend( ulong steamid )
	{
		Internal = new Steamworks.Friend( steamid );
	}

	public Friend( long steamid )
	{
		Internal = new Steamworks.Friend( (ulong)steamid );
	}

	public override string ToString() => Name;

	/// <summary>
	/// Returns true if this friend is the local user
	/// </summary>
	public readonly bool IsMe => Internal.IsMe;

	/// <summary>
	/// The friend's Steam Id
	/// </summary>
	public readonly ulong Id => Internal.Id.Value;

	/// <summary>
	/// The friend's name
	/// </summary>
	public readonly string Name => Internal.Name;

	/// <summary>
	/// The nickname you've set for this friend, otherwise returns their Steam display name
	/// </summary>
	internal readonly string DisplayName => Internal.DisplayName;

	/// <summary>
	/// Returns true if your friend is online
	/// </summary>
	public readonly bool IsOnline => Internal.IsOnline;

	/// <summary>
	/// Returns true if this user is your friend
	/// </summary>
	public readonly bool IsFriend => Internal.IsFriend;

	/// <summary>
	/// Returns true if you have blocked/ignored this user
	/// </summary>
	public readonly bool IsBlocked => Internal.IsBlocked;

	/// <summary>
	/// Returns true if your friend is away
	/// </summary>
	public readonly bool IsAway => Internal.IsAway;

	/// <summary>
	/// Returns true if this friend is marked as busy
	/// </summary>
	public readonly bool IsBusy => Internal.IsBusy;

	/// <summary>
	/// Returns true if this friend is marked as snoozing
	/// </summary>
	public readonly bool IsSnoozing => Internal.IsSnoozing;

	/// <summary>
	/// Returns a string that was possibly set by rich presence
	/// </summary>
	public readonly string GetRichPresence( string key ) => Internal.GetRichPresence( key );

	internal readonly bool InviteToGame( string connectString ) => Internal.InviteToGame( connectString );

	/// <summary>
	/// Returns true if they're playing this game
	/// </summary>
	public readonly bool IsPlayingThisGame => (Internal.GameInfo?.GameId ?? 0) == Application.AppId;

	/// <summary>
	/// Returns true if they're playing any game
	/// </summary>
	public readonly bool IsPlayingAGame => (Internal.GameInfo?.GameId ?? 0) != 0;

	/// <summary>
	/// Opens the Steam overlay web browser to their user profile.
	/// </summary>
	public void OpenInOverlay()
	{
		Internal.OpenInOverlay( "steamid" );
	}

	/// <summary>
	/// Opens the Steam overlay with a popup that allows the local Steam user to confirm whether to add this user to their Steam friends list.
	/// </summary>
	public void OpenAddFriendOverlay()
	{
		SteamFriends.OpenUserOverlay( Internal.Id, "friendadd" );
	}

	public override bool Equals( object obj ) => obj is Friend friend && Equals( friend );
	public bool Equals( Friend other ) => Id == other.Id;
	public override int GetHashCode() => HashCode.Combine( Id );
	public static bool operator ==( Friend friend1, Friend friend2 ) => friend1.Id == friend2.Id;
	public static bool operator !=( Friend friend1, Friend friend2 ) => friend1.Id != friend2.Id;
}
