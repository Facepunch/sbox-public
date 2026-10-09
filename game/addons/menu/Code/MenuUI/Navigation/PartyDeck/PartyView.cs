using Sandbox;
using Sandbox.Menu;
using Sandbox.Modals;

namespace MenuProject;

/// <summary>
/// What the party deck shows - the real <see cref="PartyRoom"/>, or made up data while <c>menu_mock_party</c>
/// or <c>menu_mock_friends</c> is set, so every state can be looked at without a second account and a slow
/// download. The made up party is the friends list's (<see cref="MenuUI.Front.RailPresence"/>), so the
/// deck and the list always show the same people.
/// </summary>
public static class PartyView
{
	/// <summary>
	/// The states <c>menu_mock_party</c> understands.
	/// </summary>
	public static readonly string[] MockStates = { "off", "idle", "downloading", "fetching", "waiting", "connecting", "failed", "cancelled", "unavailable", "full" };

	[MenuConVar( "menu_mock_party", Help = "Fill the party deck with made up data: off, idle, downloading, fetching, waiting, connecting, failed, cancelled, unavailable, full" )]
	public static string Mock { get; set; } = "off";

	/// <summary>
	/// <c>menu_mock_party</c> is set to one of its states.
	/// </summary>
	public static bool IsMockingJoin => !string.IsNullOrWhiteSpace( Mock ) && Mock != "off" && MockStates.Contains( Mock );

	/// <summary>
	/// Showing made up data instead of the real party - a <c>menu_mock_party</c> state, or <c>menu_mock_friends</c>'s
	/// party sat idle.
	/// </summary>
	public static bool IsMocking => IsMockingJoin || MenuUI.Front.RailPresence.Mocking;

	static PartyRoom Party => PartyRoom.Current;
	static string mockName;
	static bool mockPublic;
	static int mockMaxMembers = PartyDeck.MAX_MEMBERS;
	static Package mockGame;
	static CreateGameResults? mockGameSettings;
	static bool mockReady;

	/// <summary>
	/// The last name saved for one of your parties, reused when creating a new party.
	/// </summary>
	[MenuConVar( "party_name", Help = "Default name for new parties. Leave empty to use your Steam name.", Saved = true )]
	public static string DefaultName { get; set; } = "";

	/// <summary>
	/// The stored name for party creation, or a name based on your Steam profile until you choose one.
	/// </summary>
	internal static string NameForNewParty => string.IsNullOrWhiteSpace( DefaultName ) ? $"{Sandbox.Utility.Steam.PersonaName}'s Party" : DefaultName;

	/// <summary>
	/// The party name, with the local player's chat filter applied.
	/// </summary>
	public static string Name => IsMocking ? Sandbox.Utility.Steam.FilterChat( mockName ?? $"{Owner.Name}'s Party", Owner.Id ) : Party?.Name ?? "Party";

	/// <summary>
	/// Whether anyone can discover and join the party.
	/// </summary>
	public static bool IsPublic => IsMocking ? mockPublic : Party?.IsPublic ?? false;

	/// <summary>
	/// Change the party's name as its leader, including when previewing the party UI.
	/// </summary>
	public static void Rename( string name )
	{
		if ( !Exists || !OwnerIsMe ) return;

		if ( IsMocking )
		{
			mockName = name.Trim();
		}
		else
		{
			Party.Name = name;
			ConsoleSystem.SetValue( "party_name", name.Trim() );
		}
	}

	/// <summary>
	/// Change who can join the party as its leader, including when previewing the party UI.
	/// </summary>
	public static void SetPublic( bool isPublic )
	{
		if ( !Exists || !OwnerIsMe ) return;

		if ( IsMocking )
		{
			mockPublic = isPublic;
		}
		else
		{
			Party.SetPublic( isPublic );
		}
	}

	/// <summary>
	/// There's a party to show - a real one, or a mock.
	/// </summary>
	public static bool Exists => IsMocking || Party is not null;

	public static Friend Me => new Friend( Connection.Local.SteamId );

	public static IEnumerable<Friend> Members => IsMocking ? MockMembers : Party?.Members ?? Enumerable.Empty<Friend>();

	public static int MemberCount => IsMocking ? MockMembers.Count : Party?.MemberCount ?? 1;

	/// <summary>
	/// The maximum number of players allowed in the party.
	/// </summary>
	public static int MaxMembers => IsMocking ? mockMaxMembers : Party?.MaxMembers ?? PartyDeck.MAX_MEMBERS;

	/// <summary>
	/// Change the party's capacity without removing existing members.
	/// </summary>
	internal static void SetMaxMembers( int count )
	{
		if ( !Exists || !OwnerIsMe ) return;
		if ( count < Math.Max( 1, MemberCount ) || count > PartyDeck.MAX_MEMBERS ) return;

		if ( IsMocking )
		{
			mockMaxMembers = count;
		}
		else
		{
			Party.MaxMembers = count;
		}
	}

	/// <summary>
	/// The game selected to play next, independent of any game already being played.
	/// </summary>
	internal static string SelectedGameIdent => IsMocking ? mockGame?.FullIdent : Party?.SelectedGameIdent;

	/// <summary>
	/// Whether this package matches the party's current game choice.
	/// </summary>
	internal static bool IsGameSelected( Package package ) => IsMocking ? package is not null && package.FullIdent == mockGame?.FullIdent : Party?.IsGameSelected( package ) ?? false;

	/// <summary>
	/// The selected game's display title.
	/// </summary>
	internal static string SelectedGameTitle => IsMocking ? mockGame?.Title : Party?.SelectedGameTitle;

	/// <summary>
	/// The setup shared by the leader for the selected game.
	/// </summary>
	internal static CreateGameResults? SelectedGameSettings => IsMocking ? mockGameSettings : Party?.SelectedGameSettings;

	/// <summary>
	/// Choose the game to play next, or clear the choice with null.
	/// </summary>
	internal static void SelectGame( Package package )
	{
		if ( !Exists || !OwnerIsMe ) return;

		if ( IsMocking )
		{
			if ( mockGame?.FullIdent != package?.FullIdent )
			{
				mockGameSettings = null;
				mockReady = false;
			}
			mockGame = package;
		}
		else
		{
			Party.SelectGame( package );
		}
	}

	/// <summary>
	/// Save a setup only while its game is still selected.
	/// </summary>
	internal static void ConfigureGame( string gameIdent, CreateGameResults settings )
	{
		if ( !Exists || !OwnerIsMe ) return;

		if ( IsMocking )
		{
			if ( gameIdent == SelectedGameIdent )
			{
				if ( !Modals.GameModalComponents.GameSetup.SettingsEqual( mockGameSettings, settings ) ) mockReady = false;
				mockGameSettings = settings;
			}
		}
		else
		{
			Party.ConfigureGame( gameIdent, settings );
		}
	}

	public static Friend Owner => IsMocking ? MockMembers.FirstOrDefault( x => x.Id == MenuUI.Front.RailPresence.MockPartyOwner, Me ) : Party?.Owner ?? Me;

	public static bool OwnerIsMe => Owner.IsMe;

	/// <summary>
	/// Whether this member has agreed to start with the current game setup.
	/// </summary>
	internal static bool IsMemberReady( Friend member )
	{
		if ( LeaderState != PartyRoom.OwnerJoinState.None ) return false;
		return IsMocking
			? member.Id != Owner.Id && (member.IsMe ? mockReady : SelectedGameSettings is not null)
			: Party?.IsMemberReady( member ) ?? false;
	}

	/// <summary>
	/// Change the local member's ready vote, including in the party preview.
	/// </summary>
	internal static void SetReady( bool ready )
	{
		if ( !Exists || OwnerIsMe ) return;

		if ( IsMocking )
		{
			mockReady = ready;
		}
		else
		{
			Party.SetReady( ready );
		}
	}

	public static PartyRoom.JoinStage JoiningStage => IsMocking ? MockStage : Party?.JoiningStage ?? PartyRoom.JoinStage.None;

	/// <summary>
	/// What the leader says about joining them - loading, ready, or in a game with no lobby to join yet.
	/// </summary>
	public static PartyRoom.OwnerJoinState LeaderState => IsMocking ? MockLeaderState : Party?.JoinState ?? PartyRoom.OwnerJoinState.None;

	public static LoadingProgress? DownloadProgress => IsMocking ? MockDownload : Party?.DownloadProgress;

	public static LoadingProgress? HostDownloadProgress => IsMocking ? MockHostDownload : Party?.HostDownloadProgress;

	/// <summary>
	/// The title of the game the leader is currently playing, if any.
	/// </summary>
	public static string PackageTitle => IsMocking ? MockLeaderState == PartyRoom.OwnerJoinState.None ? null : "Sandbox" : Party?.PackageTitle;

	/// <summary>
	/// The game the leader's playing - for its thumbnail and art.
	/// </summary>
	public static string PackageIdent => IsMocking ? MockLeaderState == PartyRoom.OwnerJoinState.None ? null : "facepunch.sandbox" : Party?.PackageIdent;

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
			mockName = null;
			mockPublic = false;
			mockMaxMembers = PartyDeck.MAX_MEMBERS;
			mockGame = null;
			mockGameSettings = null;
			mockReady = false;
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
	/// party the friends list shows under <c>menu_mock_friends</c>.
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
