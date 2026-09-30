[TestClass]
public class LobbyInviteTests
{
	[TestMethod]
	public void RepeatedNotificationDoesNotQueueAnotherPrompt()
	{
		var invites = new LobbyInviteFilter();
		Assert.IsTrue( invites.TryReceive( 1, 0 ) );
		Assert.IsFalse( invites.TryReceive( 1, 0 ) );
		Assert.IsFalse( invites.TryReceive( 1, 5 ) );
		Assert.IsFalse( invites.TryReceive( 1, 29 ) );
	}

	[TestMethod]
	public void AnotherPartyCanInviteWhileTheFirstIsSuppressed()
	{
		var invites = new LobbyInviteFilter();
		Assert.IsTrue( invites.TryReceive( 1, 0 ) );
		Assert.IsTrue( invites.TryReceive( 2, 1 ) );
		Assert.IsFalse( invites.TryReceive( 2, 2 ) );
	}

	[TestMethod]
	public void LaterInvitationIsAllowedWithoutDuplicatesExtendingTheWindow()
	{
		var invites = new LobbyInviteFilter();
		Assert.IsTrue( invites.TryReceive( 1, 0 ) );
		Assert.IsFalse( invites.TryReceive( 1, 29 ) );
		Assert.IsTrue( invites.TryReceive( 1, 30 ) );
		Assert.IsFalse( invites.TryReceive( 1, 31 ) );
	}
}
