using System.Security.Cryptography;
using System.Text;

namespace Sandbox.Network;

/// <summary>
/// Who the host lets into a private, friends-only or password protected game. A Steam lobby's type only
/// hides it - anyone with the lobby id can still join - so the host checks every joiner against this.
/// </summary>
internal sealed class JoinAccess
{
	/// <summary>
	/// Enough for every invite a game will realistically send. Past this the oldest invite is forgotten.
	/// Anyone in the game can invite anyone, so this can't be unbounded.
	/// </summary>
	internal const int MaxInvited = 512;

	public LobbyPrivacy Privacy { get; set; }

	/// <summary>
	/// What a joiner has to give to get in, like Source's sv_password. Empty for none.
	/// </summary>
	public string Password { get; set; } = "";

	public bool HasPassword => !string.IsNullOrEmpty( Password );

	/// <summary>
	/// What a joiner with the wrong password is told. Their client knows it, so it can forget the password it sent.
	/// </summary>
	internal const string BadPasswordReason = "Bad password";

	// Oldest first, so a full list forgets the stalest invite
	readonly List<ulong> invited = new();

	// Everyone who got in. Only grows when a real Steam account passes the check, so it isn't capped.
	readonly HashSet<ulong> admitted = new();

	public IReadOnlyList<ulong> Invited => invited;
	public IReadOnlyCollection<ulong> Admitted => admitted;

	/// <summary>
	/// Someone in the game invited this player. Returns false if they were already invited.
	/// </summary>
	public bool Invite( ulong steamId )
	{
		if ( steamId == 0 || invited.Contains( steamId ) )
			return false;

		if ( invited.Count >= MaxInvited )
			invited.RemoveAt( 0 );

		invited.Add( steamId );
		return true;
	}

	/// <summary>
	/// This player got in, so they can come back.
	/// </summary>
	public void Admit( ulong steamId )
	{
		if ( steamId == 0 )
			return;

		admitted.Add( steamId );
		invited.Remove( steamId );
	}

	public bool IsAdmitted( ulong steamId ) => admitted.Contains( steamId );

	public bool IsAllowed( ulong steamId ) => admitted.Contains( steamId ) || invited.Contains( steamId );

	/// <summary>
	/// Take over the previous host's rules.
	/// </summary>
	public void Load( LobbyPrivacy privacy, IEnumerable<ulong> invitedIds, IEnumerable<ulong> admittedIds )
	{
		Privacy = privacy;
		invited.Clear();
		admitted.Clear();

		foreach ( var id in admittedIds ?? [] )
		{
			Admit( id );
		}

		foreach ( var id in invitedIds ?? [] )
		{
			if ( !admitted.Contains( id ) )
				Invite( id );
		}
	}

	/// <summary>
	/// Does this joiner have to give the password? Not if someone in the game invited them, or they've been
	/// in before. Pass 0 for a joiner whose Steam Id can't be trusted.
	/// </summary>
	public bool NeedsPassword( ulong steamId ) => HasPassword && !IsAllowed( steamId );

	public bool CheckPassword( string password )
	{
		if ( !HasPassword )
			return true;

		return CryptographicOperations.FixedTimeEquals( Encoding.UTF8.GetBytes( Password ), Encoding.UTF8.GetBytes( password ?? "" ) );
	}

	/// <summary>
	/// Can this player join? Returns the reason to show them if not.
	/// </summary>
	public bool CanJoin( ulong steamId, out string denialReason )
	{
		return CanJoin( Privacy, IsAllowed( steamId ), () => new Friend( steamId ).IsFriend, out denialReason );
	}

	internal static bool CanJoin( LobbyPrivacy privacy, bool allowed, Func<bool> isFriendOfHost, out string denialReason )
	{
		denialReason = null;

		if ( privacy == LobbyPrivacy.Public || allowed )
			return true;

		if ( privacy == LobbyPrivacy.FriendsOnly )
		{
			if ( isFriendOfHost() )
				return true;

			denialReason = "This lobby is Friends Only.";
			return false;
		}

		// Private, or anything we don't understand
		denialReason = "This game is private. You need an invite from someone in it to join.";
		return false;
	}
}
