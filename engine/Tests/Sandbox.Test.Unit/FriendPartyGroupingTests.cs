using MenuProject.MenuUI.Front;
using System.Collections.Generic;

[TestClass]
public class FriendPartyGroupingTests
{
	[TestMethod]
	public void SharedRosterShowsNonFriendsWithoutDuplicatingLeaderOrFriends()
	{
		var ids = FriendPartyGrouping.MemberIds( [1, 2], [1, 2, 3, 4], 3 );
		CollectionAssert.AreEqual( new ulong[] { 3, 1, 2, 4 }, ids );
	}

	[TestMethod]
	public void MissingRosterKeepsKnownFriendsAndLeader()
	{
		CollectionAssert.AreEqual( new ulong[] { 3, 1, 2 }, FriendPartyGrouping.MemberIds( [1, 2], [], 3 ) );
		CollectionAssert.AreEqual( new ulong[] { 1 }, FriendPartyGrouping.MemberIds( [1], [], 0 ) );
	}

	[TestMethod]
	public void DepartedNonFriendDisappearsOnNextRosterRefresh()
	{
		Assert.AreEqual( 3, FriendPartyGrouping.MemberIds( [1], [1, 2, 3], 1 ).Count );
		CollectionAssert.AreEqual( new ulong[] { 1, 2 }, FriendPartyGrouping.MemberIds( [1], [1, 2], 1 ) );
	}

	record Presence( ulong SteamId, string PartyId = null, int PartySize = 0 );

	static (List<FriendPartyGrouping.Party<Presence>> Parties, List<Presence> Solo) Group(
		Presence[] friends, string myParty = null, params ulong[] localMembers ) => FriendPartyGrouping.Group(
			friends, x => x.SteamId, x => x.PartyId, x => x.PartySize, myParty, localMembers );

	[TestMethod]
	public void HostWaitingAloneAppearsAsAParty()
	{
		var host = new Presence( 1, "office-party", 1 );
		var (parties, solo) = Group( [host] );

		Assert.AreEqual( 1, parties.Count );
		Assert.AreEqual( "office-party", parties[0].Id );
		Assert.AreEqual( 1, parties[0].Size );
		CollectionAssert.AreEqual( new[] { host }, parties[0].Members );
		Assert.AreEqual( 0, solo.Count );
	}

	[TestMethod]
	public void PartyIdentityIsEnoughBeforeTheCountArrives()
	{
		var (parties, solo) = Group( [new Presence( 1, "private-party" )] );
		Assert.AreEqual( 1, parties.Count );
		Assert.AreEqual( 1, parties[0].Size );
		Assert.AreEqual( 0, solo.Count );
	}

	[TestMethod]
	public void FriendsWithoutAPartyStaySoloEvenWithAStaleCount()
	{
		var friends = new[] { new Presence( 1 ), new Presence( 2, "", 4 ) };
		var (parties, solo) = Group( friends );
		Assert.AreEqual( 0, parties.Count );
		CollectionAssert.AreEqual( friends, solo );
	}

	[TestMethod]
	public void SharedPartyUsesTheLargestReportedCountIncludingStrangers()
	{
		var (parties, solo) = Group( [new Presence( 1, "party", 1 ), new Presence( 2, "party", 5 )] );
		Assert.AreEqual( 1, parties.Count );
		Assert.AreEqual( 5, parties[0].Size );
		Assert.AreEqual( 2, parties[0].Members.Count );
		Assert.AreEqual( 0, solo.Count );
	}

	[TestMethod]
	public void DifferentSingleMemberPartiesStaySeparate()
	{
		var (parties, solo) = Group( [new Presence( 1, "a", 1 ), new Presence( 2, "b", 1 )] );
		CollectionAssert.AreEquivalent( new[] { "a", "b" }, parties.Select( x => x.Id ).ToArray() );
		Assert.AreEqual( 0, solo.Count );
	}

	[TestMethod]
	public void LocalLobbyOverridesMissingOrStalePresence()
	{
		var (parties, solo) = Group( [new Presence( 1 ), new Presence( 2, "old-party", 10 )], "mine", 1, 2, 3 );
		Assert.AreEqual( 1, parties.Count );
		Assert.AreEqual( "mine", parties[0].Id );
		Assert.AreEqual( 3, parties[0].Size );
		Assert.AreEqual( 2, parties[0].Members.Count );
		Assert.AreEqual( 0, solo.Count );
	}

	[TestMethod]
	public void FriendWhoLeftOurLobbyIsNotGroupedFromStalePresence()
	{
		var departed = new Presence( 2, "mine", 2 );
		var (parties, solo) = Group( [departed], "mine", 1 );
		Assert.AreEqual( 0, parties.Count );
		CollectionAssert.AreEqual( new[] { departed }, solo );
	}

	[TestMethod]
	public void PartyDisappearsWhenItsLastFriendsPresenceClears()
	{
		var host = new Presence( 1, "party", 1 );
		Assert.AreEqual( 1, Group( [host] ).Parties.Count );
		var (parties, solo) = Group( [host with { PartyId = null }] );
		Assert.AreEqual( 0, parties.Count );
		Assert.AreEqual( 1, solo.Count );
	}
}
