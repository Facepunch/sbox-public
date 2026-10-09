using Sandbox;

namespace MenuProject.MenuUI.Front;

/// <summary>
/// Everything the friends rail reads about your friends and your party - who's online, what they're
/// playing, their rich presence, who's in your lobby. Straight from Steam normally; with
/// <c>menu_mock_friends</c> on, a made up world built from your real friends (real names and faces) that
/// covers every case the rail handles.
/// </summary>
public static class RailPresence
{
	[MenuConVar( "menu_mock_friends", Help = "Fill the friends list with made up parties and states: off, leader (you lead your party), member (a friend leads it)" )]
	public static string Mock { get; set; } = "off";

	public static bool Mocking => Mock is "leader" or "member";

	static string _builtFor;
	static MockWorld _built;

	/// <summary>
	/// The made up world - built whenever either mock is on, since <c>menu_mock_party</c> uses its party
	/// too. Rebuilt when either changes.
	/// </summary>
	static MockWorld Built
	{
		get
		{
			if ( !Mocking && !PartyView.IsMockingJoin ) return null;

			var key = $"{Mock}|{PartyView.Mock}";
			if ( _builtFor == key && _built is not null ) return _built;

			// The joining states are you following the leader into their game - a leader never joins
			// their own, so those always put a friend in charge. Otherwise menu_mock_friends says, and you
			// lead if it doesn't
			var joining = PartyView.IsMockingJoin && PartyView.Mock is not ("idle" or "full");
			var leader = !joining && (!Mocking || Mock == "leader");

			_builtFor = key;
			_built = MockWorld.Build( leader, full: PartyView.Mock == "full" );
			return _built;
		}
	}

	/// <summary>
	/// The made up friends - only while <c>menu_mock_friends</c> is on. <c>menu_mock_party</c> alone makes up the
	/// party, not everyone else.
	/// </summary>
	static MockWorld World => Mocking ? Built : null;

	/// <summary>
	/// The made up party, for <see cref="PartyView"/> - you, some friends and someone you don't know.
	/// </summary>
	internal static IReadOnlyList<Friend> MockPartyMembers => Built?.PartyMembers ?? (IReadOnlyList<Friend>)Array.Empty<Friend>();

	internal static ulong MockPartyOwner => Built?.PartyOwner ?? PartyView.Me.Id;

	/// <summary>
	/// Made up invites the made up party's waiting on, and how long they've been waiting - for
	/// <see cref="PartyInvites"/>. Counting up from when the mock started, wrapping before they'd time out.
	/// </summary>
	internal static IEnumerable<(ulong Id, float Seconds)> MockPendingInvites => Built is { } w
		? w.Invited.Select( x => (x.Id, (x.Seconds + (float)w.Started) % (PartyInvites.Timeout - 10)) )
		: Enumerable.Empty<(ulong, float)>();

	public static IEnumerable<Friend> Friends => World?.Friends ?? MenuUtility.Friends.Where( x => !x.IsMe );

	public static string Get( Friend friend, string key ) => World?.Find( friend )?.Get( key ) ?? (World is null ? friend.GetRichPresence( key ) : null);

	public static IEnumerable<Friend> MembersOf( Friend friend ) => World is { } world
		? world.PartyRoster( Get( friend, "party_id" ) )
		: MenuUtility.GetPartyMembers( friend );

	public static bool IsOnline( Friend friend ) => World?.Find( friend )?.Online ?? friend.IsOnline;
	public static bool IsPlayingThisGame( Friend friend ) => World?.Find( friend )?.Playing ?? friend.IsPlayingThisGame;
	public static bool IsPlayingAGame( Friend friend ) => World?.Find( friend )?.OtherGame ?? friend.IsPlayingAGame;
	public static bool IsAway( Friend friend ) => World?.Find( friend )?.Away ?? (friend.IsAway || friend.IsSnoozing);
	public static bool IsBusy( Friend friend ) => World?.Find( friend )?.Busy ?? friend.IsBusy;

	//
	// Your party - whatever the party deck's showing, real or made up, so the two always agree
	//

	/// <summary>
	/// Your party's id, as it appears in rich presence - null when you're not in one.
	/// </summary>
	public static string PartyId => PartyView.IsMocking ? MockWorld.MockPartyId : PartyRoom.Current?.Id.ToString();

	/// <summary>
	/// Everyone in your party's lobby, you included.
	/// </summary>
	public static IReadOnlyList<Friend> PartyMembers => PartyView.Exists ? PartyView.Members.ToList() : (IReadOnlyList<Friend>)Array.Empty<Friend>();

	public static ulong PartyOwner => PartyView.Exists ? PartyView.Owner.Id : 0;

	public static bool CanInvite( Friend friend ) => PartyView.IsMocking
		? !friend.IsMe && !PartyMembers.Any( x => x.Id == friend.Id )
		: MenuUtility.CanInviteToParty( friend );

	/// <summary>
	/// One made up friend - their status, and the rich presence they'd be sending.
	/// </summary>
	sealed class MockState
	{
		public bool Online = true, Playing, OtherGame, Away, Busy;
		public readonly Dictionary<string, string> Presence = new();

		public string Get( string key ) => Presence.GetValueOrDefault( key );
	}

	sealed class MockWorld
	{
		public const string MockPartyId = "mock-mine";

		public List<Friend> Friends = new();
		public List<Friend> PartyMembers = new();
		public List<(ulong Id, float Seconds)> Invited = new();
		public RealTimeSince Started = 0;
		public ulong PartyOwner;

		readonly Dictionary<ulong, MockState> _states = new();

		/// <summary>
		/// Made up state for anyone the mock knows about - friends and the strangers it made from them.
		/// Anyone else (you) is read from Steam as normal.
		/// </summary>
		public MockState Find( Friend friend ) => _states.GetValueOrDefault( friend.Id );

		public IEnumerable<Friend> PartyRoster( string id ) => string.IsNullOrEmpty( id ) ? Enumerable.Empty<Friend>()
			: _states.Where( x => x.Value.Get( "party_id" ) == id ).Select( x => new Friend( x.Key ) );

		const string Game = "facepunch.sandbox";
		const string GameName = "Sandbox";

		public static MockWorld Build( bool leader, bool full )
		{
			var w = new MockWorld();
			var pool = MenuUtility.Friends.Where( x => !x.IsMe ).OrderBy( x => x.Id ).ToList();

			// The last few play people who aren't your friends - they never show up on the list
			// themselves, only as faces in the parties they're in
			Friend? TakeStranger()
			{
				if ( pool.Count == 0 ) return null;

				var s = pool[^1];
				pool.RemoveAt( pool.Count - 1 );
				return s;
			}

			var strangerInMyParty = TakeStranger();
			var strangerLeader = TakeStranger();

			w.Friends = pool;

			int next = 0;
			Friend? Take() => next < pool.Count ? pool[next++] : null;

			MockState Set( Friend? friend, bool playing = true, string title = "menu", string connect = null, bool editor = false )
			{
				if ( friend is not { } f ) return null;

				var state = new MockState { Playing = playing };
				if ( playing )
				{
					state.Presence["gametitle"] = title;
					state.Presence["gamename"] = title == "menu" ? null : Game;
					state.Presence["connect"] = connect;
					state.Presence["in_editor"] = editor ? "1" : "0";
				}

				w._states[f.Id] = state;
				return state;
			}

			void Party( MockState state, string id, ulong owner, int size )
			{
				if ( state is null ) return;

				state.Presence["party_id"] = id;
				state.Presence["party_owner"] = owner.ToString();
				state.Presence["party_size"] = size.ToString();
				state.Presence["party_max"] = PartyDeck.MAX_MEMBERS.ToString();
			}

			//
			// Your party - you, two friends and someone you don't know. One friend's rich presence
			// hasn't caught up yet (no party_id), so only the lobby knows they're with you.
			//
			var me = PartyView.Me;
			var mate = Take();
			var lagging = Take();

			w.PartyMembers = new[] { me, mate, lagging, strangerInMyParty }.Where( x => x is not null ).Select( x => x.Value ).ToList();
			w.PartyOwner = leader || mate is null ? me.Id : mate.Value.Id;

			Party( Set( mate ), MockPartyId, w.PartyOwner, w.PartyMembers.Count );
			Set( lagging );
			Party( Set( strangerInMyParty ), MockPartyId, w.PartyOwner, w.PartyMembers.Count );

			// Says they're in your party, but they left - they're not in the lobby, so they're on their own
			Party( Set( Take() ), MockPartyId, w.PartyOwner, w.PartyMembers.Count );

			//
			// Other people's parties
			//

			// Three friends together in a game you can join, led by one of them
			var host = Take();
			var partyA = new[] { host, Take(), Take() };
			foreach ( var f in partyA )
				Party( Set( f, title: GameName, connect: f?.Id == host?.Id ? "+connect 1" : null ), "mock-a", host?.Id ?? 0, 3 );

			// A friend with a leader and another member outside your friends list.
			Party( Set( Take(), title: GameName ), "mock-b", strangerLeader?.Id ?? 0, 3 );
			Party( Set( strangerLeader, title: GameName ), "mock-b", strangerLeader?.Id ?? 0, 3 );
			var strangerMember = TakeStranger();
			Party( Set( strangerMember, title: GameName ), "mock-b", strangerLeader?.Id ?? 0, 3 );

			// A big one - ten friends and two others, more faces than fit
			var bigLead = Take();
			Party( Set( bigLead ), "mock-c", bigLead?.Id ?? 0, 12 );
			for ( int i = 0; i < 9; i++ )
				Party( Set( Take() ), "mock-c", bigLead?.Id ?? 0, 12 );

			//
			// On their own
			//
			Set( Take(), title: GameName, connect: "+connect 2" );  // in a game you can join
			Set( Take(), title: GameName );                          // in a game you can't
			Set( Take(), editor: true );                             // making something
			Set( Take(), editor: true );
			Set( Take() );                                           // in the main menu

			//
			// Not in s&box
			//
			if ( Set( Take(), playing: false ) is { } other ) other.OtherGame = true;
			if ( Set( Take(), playing: false ) is { } away ) away.Away = true;
			if ( Set( Take(), playing: false ) is { } busy ) busy.Busy = true;
			Set( Take(), playing: false );

			//
			// Asked into your party, not in yet - in the main menu, deciding
			//
			foreach ( var seconds in new[] { 12f, 47f } )
			{
				if ( Take() is not { } invited ) break;

				Set( invited );
				w.Invited.Add( (invited.Id, seconds) );
			}

			// menu_mock_party full - fill the party up with whoever's left
			while ( full && w.PartyMembers.Count < PartyDeck.MAX_MEMBERS && Take() is { } extra )
			{
				w.PartyMembers.Add( extra );
				Party( Set( extra ), MockPartyId, w.PartyOwner, PartyDeck.MAX_MEMBERS );
			}

			// Everyone else offline
			while ( Take() is { } f )
				w._states[f.Id] = new MockState { Online = false };

			// The strangers never appear on the list - but their faces need a state behind them
			foreach ( var s in new[] { strangerInMyParty, strangerLeader } )
			{
				if ( s is { } stranger && !w._states.ContainsKey( stranger.Id ) )
					Set( stranger );
			}

			return w;
		}
	}
}
