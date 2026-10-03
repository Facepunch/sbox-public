using Sandbox;

namespace MenuProject;

/// <summary>
/// Who you've asked into your party that hasn't turned up yet - so the deck can hold a seat for them
/// and the friends list can show they've been asked, like Dota does. Steam never says an invite was
/// turned down, so one stops being pending when they join, when the party changes (you leave, or
/// make a new one), or once it's been a while.
/// </summary>
public static class PartyInvites
{
	/// <summary>
	/// How long an invite with no answer stays pending.
	/// </summary>
	public const float Timeout = 120f;

	static readonly Dictionary<ulong, RealTimeSince> _sent = new();

	/// <summary>
	/// The party the invites in <see cref="_sent"/> were for - they're dropped when it changes.
	/// </summary>
	static string _party;

	/// <summary>
	/// Ask them in - making a party first if we're not in one. While the party's made up (the party
	/// or friends mocks) it's only recorded: nobody real gets asked into a pretend party.
	/// </summary>
	public static async Task Invite( ulong steamId )
	{
		// Making the party first takes a moment - don't send a second while the first's on its way
		if ( IsPending( steamId ) || !_sending.Add( steamId ) ) return;

		try
		{
			if ( !PartyView.IsMocking )
			{
				if ( !await PartyDeck.EnsurePartyExists() ) return;
				MenuUtility.InviteToParty( steamId );
			}

			Tidy();
			_sent[steamId] = 0;
		}
		finally
		{
			_sending.Remove( steamId );
		}
	}

	static readonly HashSet<ulong> _sending = new();

	/// <summary>
	/// Stop showing it as pending - the invite itself can't be taken back, it's already with them.
	/// </summary>
	public static void Dismiss( ulong steamId ) => _sent.Remove( steamId );

	public static bool IsPending( ulong steamId ) => Entries.Any( x => x.Id == steamId );

	/// <summary>
	/// Everyone still pending, the longest waiting first - the order they were asked in.
	/// </summary>
	public static IReadOnlyList<Friend> Pending => Entries.OrderByDescending( x => x.Seconds ).Select( x => new Friend( x.Id ) ).ToList();

	/// <summary>
	/// How long ago they were asked.
	/// </summary>
	public static float SecondsSince( ulong steamId ) => Entries.FirstOrDefault( x => x.Id == steamId ).Seconds;

	/// <summary>
	/// Changes whenever who's pending does - for panels to rebuild on.
	/// </summary>
	public static int Hash
	{
		get
		{
			var hash = new HashCode();
			foreach ( var entry in Entries ) hash.Add( entry.Id );
			return hash.ToHashCode();
		}
	}

	/// <summary>
	/// The pending invites - ours, plus the made up ones while the party's mocked.
	/// </summary>
	static IEnumerable<(ulong Id, float Seconds)> Entries
	{
		get
		{
			Tidy();

			foreach ( var (id, since) in _sent )
				yield return (id, since);

			if ( PartyView.IsMocking )
			{
				foreach ( var (id, seconds) in MenuUI.Front.RailPresence.MockPendingInvites )
				{
					if ( !_sent.ContainsKey( id ) && !InParty( id ) )
						yield return (id, seconds);
				}
			}
		}
	}

	/// <summary>
	/// Forget the invites that aren't pending any more - they're in now, they've been waiting too
	/// long, or they were for a party we're not in.
	/// </summary>
	static void Tidy()
	{
		var party = PartyView.Exists ? MenuUI.Front.RailPresence.PartyId : null;
		if ( party != _party )
		{
			_party = party;
			_sent.Clear();
			return;
		}

		if ( _sent.Count == 0 ) return;

		foreach ( var id in _sent.Where( x => x.Value >= Timeout || InParty( x.Key ) ).Select( x => x.Key ).ToList() )
			_sent.Remove( id );
	}

	static bool InParty( ulong steamId ) => PartyView.Members.Any( x => x.Id == steamId );
}
