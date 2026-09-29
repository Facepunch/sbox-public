using Sandbox;
using Sandbox.Menu;

namespace MenuProject;

/// <summary>
/// What the party deck shows - the real <see cref="PartyRoom"/>, or made up data while <c>party_mock</c>
/// or <c>friends_mock</c> is set, so every state can be looked at without a second account and a slow
/// download. The made up party is the friends list's (<see cref="MenuUI.Front.RailPresence"/>), so the
/// deck and the list always show the same people.
/// </summary>
public static class PartyView
{
	/// <summary>
	/// The states <c>party_mock</c> understands.
	/// </summary>
	public static readonly string[] MockStates = { "off", "idle", "downloading", "fetching", "waiting", "connecting", "failed", "cancelled", "unavailable", "full" };

	[MenuConVar( "party_mock", Help = "Fill the party deck with made up data: off, idle, downloading, fetching, waiting, connecting, failed, cancelled, unavailable, full" )]
	public static string Mock { get; set; } = "off";

	/// <summary>
	/// <c>party_mock</c> is set to one of its states.
	/// </summary>
	public static bool IsMockingJoin => !string.IsNullOrWhiteSpace( Mock ) && Mock != "off" && MockStates.Contains( Mock );

	/// <summary>
	/// Showing made up data instead of the real party - a <c>party_mock</c> state, or <c>friends_mock</c>'s
	/// party sat idle.
	/// </summary>
	public static bool IsMocking => IsMockingJoin || MenuUI.Front.RailPresence.Mocking;

	static PartyRoom Party => PartyRoom.Current;

	/// <summary>
	/// There's a party to show - a real one, or a mock.
	/// </summary>
	public static bool Exists => IsMocking || Party is not null;

	public static Friend Me => new Friend( Connection.Local.SteamId );

	public static IEnumerable<Friend> Members => IsMocking ? MockMembers : Party?.Members ?? Enumerable.Empty<Friend>();

	public static int MemberCount => IsMocking ? MockMembers.Count : Party?.MemberCount ?? 1;

	public static int MaxMembers => IsMocking ? PartyDeck.MAX_MEMBERS : Party?.MaxMembers ?? PartyDeck.MAX_MEMBERS;

	public static Friend Owner => IsMocking ? MockMembers.FirstOrDefault( x => x.Id == MenuUI.Front.RailPresence.MockPartyOwner, Me ) : Party?.Owner ?? Me;

	public static bool OwnerIsMe => Owner.IsMe;

	public static PartyRoom.JoinStage JoiningStage => IsMocking ? MockStage : Party?.JoiningStage ?? PartyRoom.JoinStage.None;

	/// <summary>
	/// What the leader says about joining them - loading, ready, or in a game with no lobby to join yet.
	/// </summary>
	public static PartyRoom.OwnerJoinState LeaderState => IsMocking ? MockLeaderState : Party?.JoinState ?? PartyRoom.OwnerJoinState.None;

	public static LoadingProgress? DownloadProgress => IsMocking ? MockDownload : Party?.DownloadProgress;

	public static LoadingProgress? HostDownloadProgress => IsMocking ? MockHostDownload : Party?.HostDownloadProgress;

	public static string PackageTitle => IsMocking ? "Sandbox" : Party?.PackageTitle;

	/// <summary>
	/// The game the leader's playing - for its thumbnail and art.
	/// </summary>
	public static string PackageIdent => IsMocking ? "facepunch.sandbox" : Party?.PackageIdent;

	public static string JoinError => IsMocking ? "The server didn't respond in time." : Party?.JoinError;

	public static PartyRoom.MemberStatus StatusOf( Friend member ) => IsMocking ? MockStatusOf( member ) : Party?.GetMemberStatus( member ) ?? default;

	public static void Retry()
	{
		if ( IsMocking ) { Mock = "downloading"; return; }
		Party?.RetryJoin();
	}

	public static void Cancel()
	{
		if ( IsMocking ) { Mock = "cancelled"; return; }
		Party?.CancelJoin();
	}

	public static void Leave()
	{
		// Leaving the made up party ends both mocks - there's no party left for the list to show either
		if ( IsMocking )
		{
			Mock = "off";
			MenuUI.Front.RailPresence.Mock = "off";
			return;
		}

		Party?.Leave();
	}

	//
	// The mock
	//

	// "unavailable" is how it really plays out: our download's done and we're waiting, while the
	// leader's in a game with no lobby to join yet
	static PartyRoom.JoinStage MockStage => Mock switch
	{
		"downloading" or "fetching" => PartyRoom.JoinStage.Downloading,
		"waiting" or "unavailable" => PartyRoom.JoinStage.WaitingForHost,
		"connecting" => PartyRoom.JoinStage.Connecting,
		"failed" => PartyRoom.JoinStage.Failed,
		"cancelled" => PartyRoom.JoinStage.Cancelled,
		_ => PartyRoom.JoinStage.None
	};

	static PartyRoom.OwnerJoinState MockLeaderState => Mock switch
	{
		"waiting" => PartyRoom.OwnerJoinState.Loading,
		"unavailable" => PartyRoom.OwnerJoinState.Unavailable,
		"idle" or "full" or "off" => PartyRoom.OwnerJoinState.None,
		_ => PartyRoom.OwnerJoinState.Ready
	};

	/// <summary>
	/// Goes round and round, so the bar and the percentages actually move.
	/// </summary>
	static double Cycle( float seconds, float offset = 0 ) => ((RealTime.Now + offset) % seconds) / seconds;

	static LoadingProgress? MockDownload => Mock switch
	{
		"downloading" => new LoadingProgress { Title = "Downloading", Fraction = Cycle( 20 ), Mbps = 84.2, TotalSize = 1_800_000_000 },
		"connecting" => new LoadingProgress { Title = "Loading", Fraction = Cycle( 6 ) },
		_ => null
	};

	static LoadingProgress? MockHostDownload => Mock == "waiting"
		? new LoadingProgress { Title = "Downloading", Fraction = 0.35 + Cycle( 30 ) * 0.6 }
		: null;

	/// <summary>
	/// You and some of your friends - real names and avatars, so it looks like the real thing. The same
	/// party the friends list shows under <c>friends_mock</c>.
	/// </summary>
	static List<Friend> MockMembers => MenuUI.Front.RailPresence.MockPartyMembers.ToList();

	/// <summary>
	/// A spread of states across the party, so every badge shows up somewhere.
	/// </summary>
	static PartyRoom.MemberStatus MockStatusOf( Friend member )
	{
		if ( member.IsMe )
			return new PartyRoom.MemberStatus( MockStage, (float?)MockDownload?.Fraction );

		// Just a party, nobody doing anything
		if ( Mock is "idle" or "off" )
			return default;

		if ( member.Id == Owner.Id )
		{
			return Mock == "waiting"
				? new PartyRoom.MemberStatus( PartyRoom.JoinStage.Downloading, (float?)MockHostDownload?.Fraction )
				: new PartyRoom.MemberStatus( PartyRoom.JoinStage.Connected, null );
		}

		var index = MockMembers.FindIndex( x => x.Id == member.Id );

		return (index % 5) switch
		{
			1 => MockDownloader( 12, index * 3 ),
			2 => new PartyRoom.MemberStatus( PartyRoom.JoinStage.WaitingForHost, null ),
			3 => new PartyRoom.MemberStatus( PartyRoom.JoinStage.Connected, null ),
			4 => new PartyRoom.MemberStatus( PartyRoom.JoinStage.Failed, null ),
			_ => MockDownloader( 18, index * 7 ),
		};
	}

	/// <summary>
	/// Downloads for most of the cycle, then sits ready for the rest before starting over - so there
	/// are always people finishing, and their state changes can be seen.
	/// </summary>
	static PartyRoom.MemberStatus MockDownloader( float seconds, float offset )
	{
		var t = Cycle( seconds, offset );
		return t < 0.75
			? new PartyRoom.MemberStatus( PartyRoom.JoinStage.Downloading, (float)(t / 0.75) )
			: new PartyRoom.MemberStatus( PartyRoom.JoinStage.WaitingForHost, null );
	}
}
