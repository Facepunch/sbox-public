using Sandbox.Network;

namespace NetworkTests;

[TestClass]
public class JoinAccessTests
{
	static bool CanJoin( LobbyPrivacy privacy, bool allowed, bool friend ) => JoinAccess.CanJoin( privacy, allowed, () => friend, out _ );

	[TestMethod]
	public void PublicLetsAnyoneIn()
	{
		Assert.IsTrue( CanJoin( LobbyPrivacy.Public, allowed: false, friend: false ) );
	}

	[TestMethod]
	public void PrivateNeedsAnInvite()
	{
		Assert.IsFalse( CanJoin( LobbyPrivacy.Private, allowed: false, friend: false ) );
		Assert.IsFalse( CanJoin( LobbyPrivacy.Private, allowed: false, friend: true ) );
		Assert.IsTrue( CanJoin( LobbyPrivacy.Private, allowed: true, friend: false ) );
	}

	[TestMethod]
	public void FriendsOnlyLetsInFriendsAndInvites()
	{
		Assert.IsFalse( CanJoin( LobbyPrivacy.FriendsOnly, allowed: false, friend: false ) );
		Assert.IsTrue( CanJoin( LobbyPrivacy.FriendsOnly, allowed: false, friend: true ) );
		Assert.IsTrue( CanJoin( LobbyPrivacy.FriendsOnly, allowed: true, friend: false ) );
	}

	[TestMethod]
	public void UnknownPrivacyIsTreatedAsPrivate()
	{
		Assert.IsFalse( JoinAccess.CanJoin( (LobbyPrivacy)99, false, () => true, out var reason ) );
		Assert.IsNotNull( reason );
	}

	[TestMethod]
	public void DeniedJoinersGetAReason()
	{
		JoinAccess.CanJoin( LobbyPrivacy.Private, false, () => false, out var reason );
		Assert.IsFalse( string.IsNullOrWhiteSpace( reason ) );

		JoinAccess.CanJoin( LobbyPrivacy.Public, false, () => false, out reason );
		Assert.IsNull( reason );
	}

	[TestMethod]
	public void InviteIgnoresDuplicatesAndZero()
	{
		var access = new JoinAccess();

		Assert.IsTrue( access.Invite( 1 ) );
		Assert.IsFalse( access.Invite( 1 ) );
		Assert.IsFalse( access.Invite( 0 ) );

		Assert.AreEqual( 1, access.Invited.Count );
		Assert.IsTrue( access.IsAllowed( 1 ) );
		Assert.IsFalse( access.IsAllowed( 2 ) );
	}

	[TestMethod]
	public void FullListForgetsTheOldestInvite()
	{
		var access = new JoinAccess();

		for ( ulong i = 1; i <= JoinAccess.MaxInvited + 1; i++ )
		{
			access.Invite( i );
		}

		Assert.AreEqual( JoinAccess.MaxInvited, access.Invited.Count );
		Assert.IsFalse( access.IsAllowed( 1 ) );
		Assert.IsTrue( access.IsAllowed( 2 ) );
		Assert.IsTrue( access.IsAllowed( JoinAccess.MaxInvited + 1 ) );
	}

	[TestMethod]
	public void FloodingInvitesDoesntLockOutPlayersWhoGotIn()
	{
		var access = new JoinAccess();
		access.Invite( 7 );
		access.Admit( 7 );

		for ( ulong i = 1000; i < 1000 + JoinAccess.MaxInvited * 2; i++ )
		{
			access.Invite( i );
		}

		Assert.IsTrue( access.IsAdmitted( 7 ) );
		Assert.IsTrue( access.IsAllowed( 7 ) );
	}

	[TestMethod]
	public void AdmittingMovesTheInviteOut()
	{
		var access = new JoinAccess();
		access.Invite( 3 );
		access.Admit( 3 );
		access.Admit( 0 );

		Assert.AreEqual( 0, access.Invited.Count );
		Assert.AreEqual( 1, access.Admitted.Count );
	}

	[TestMethod]
	public void LoadReplacesTheRules()
	{
		var access = new JoinAccess { Privacy = LobbyPrivacy.Public };
		access.Invite( 5 );
		access.Admit( 6 );

		access.Load( LobbyPrivacy.Private, [1, 2], [3] );

		Assert.AreEqual( LobbyPrivacy.Private, access.Privacy );
		Assert.IsFalse( access.IsAllowed( 5 ) );
		Assert.IsFalse( access.IsAllowed( 6 ) );
		Assert.IsTrue( access.IsAllowed( 1 ) );
		Assert.IsTrue( access.IsAllowed( 2 ) );
		Assert.IsTrue( access.IsAdmitted( 3 ) );
		Assert.IsFalse( access.IsAdmitted( 1 ) );

		// No lists is nobody
		access.Load( LobbyPrivacy.FriendsOnly, null, null );
		Assert.AreEqual( 0, access.Invited.Count );
		Assert.AreEqual( 0, access.Admitted.Count );
	}
}
