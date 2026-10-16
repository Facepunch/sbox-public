using System;
using System.Collections.Generic;
using System.Linq;

namespace MenuProject.MenuUI.Front;

/// <summary>
/// Group friends by party identity, including a leader waiting alone for others to join.
/// Local lobby membership takes precedence over stale rich presence.
/// </summary>
internal static class FriendPartyGrouping
{
	internal record Party<T>( string Id, int Size, List<T> Members );

	/// <summary>
	/// Keep known friends, add the shared roster, and put the leader first without duplicates.
	/// </summary>
	internal static List<ulong> MemberIds( IEnumerable<ulong> friends, IEnumerable<ulong> shared, ulong owner ) =>
		friends.Concat( shared ).Prepend( owner ).Where( x => x != 0 ).Distinct().ToList();

	internal static (List<Party<T>> Parties, List<T> Solo) Group<T>( IEnumerable<T> friends,
		Func<T, ulong> steamId, Func<T, string> partyId, Func<T, int> partySize,
		string myParty, IReadOnlyCollection<ulong> localMembers )
	{
		var parties = new List<Party<T>>();
		var solo = new List<T>();

		string Key( T friend )
		{
			if ( !string.IsNullOrEmpty( myParty ) && localMembers.Contains( steamId( friend ) ) ) return myParty;

			var id = partyId( friend ) ?? "";
			return id == myParty ? "" : id;
		}

		foreach ( var group in friends.GroupBy( Key ) )
		{
			var members = group.ToList();
			if ( string.IsNullOrEmpty( group.Key ) )
			{
				solo.AddRange( members );
				continue;
			}

			// Presence can arrive piecemeal. A known party doesn't stop being a party just
			// because its count is one (or hasn't arrived yet).
			var size = group.Key == myParty ? localMembers.Count : Math.Max( members.Count, members.Max( partySize ) );
			parties.Add( new Party<T>( group.Key, size, members ) );
		}

		return (parties, solo);
	}
}
