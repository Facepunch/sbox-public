using Sandbox;

namespace MenuProject;

/// <summary>
/// How a game is played - on your own, with others, together or against each other - and how many it's
/// meant for. From the player counts and modes the game declares (RecommendedMinPlayers,
/// RecommendedMaxPlayers, Coop, Versus), or failing those, its tags.
/// </summary>
/// <param name="RecommendedMin">The fewest players it's meant for - 0 when it doesn't say.</param>
/// <param name="RecommendedMax">The most players it's meant for - 0 for no limit, or when it doesn't say.</param>
/// <param name="MaxPlayers">The most a lobby of it can hold.</param>
public readonly record struct PlayerModes( bool Solo, bool Multiplayer, bool Coop, bool Versus, int RecommendedMin = 0, int RecommendedMax = 0, int MaxPlayers = 0 )
{
	/// <summary>
	/// No play modes declared or tagged - we don't know, so nothing's shown rather than a guess.
	/// </summary>
	public bool IsUnknown => !Solo && !Multiplayer;

	/// <summary>
	/// It says how many players it's meant for.
	/// </summary>
	public bool HasRecommendedPlayers => RecommendedMin > 0 || RecommendedMax > 0;

	public static PlayerModes For( Package package )
	{
		if ( package is null || package.TypeName != "game" ) return default;

		var tags = package.Tags ?? Array.Empty<string>();
		bool Has( string tag ) => tags.Contains( tag, StringComparer.OrdinalIgnoreCase );

		// The game's own say, from its project settings - or what was on the website, before that
		int Count( string key ) => package.GetMeta( key, 0 ) is > 0 and var meta ? meta : package.GetValue( key, 0 );
		bool Flag( string key ) => package.GetMeta( key, false ) || package.GetValue( key, false );

		var recommendedMin = Count( "RecommendedMinPlayers" );
		var recommendedMax = Count( "RecommendedMaxPlayers" );
		// Raw, not Info.MaxPlayers - that's 1 when it's not set (or not come with a list result yet), which
		// would have every multiplayer game turn away any party. 0 is not known, and isn't checked against
		var maxPlayers = package.GetMeta( "MaxPlayers", 0 );

		var coop = Flag( "Coop" ) || Has( "coop" );
		var versus = Flag( "Versus" ) || Has( "versus" ) || Has( "pvp" );

		return Make( Has( "singleplayer" ), Has( "multiplayer" ), coop, versus, recommendedMin, recommendedMax, maxPlayers );
	}

	/// <summary>
	/// From its Steam Workshop item - the tags and player counts it was published with. Unknown for an item
	/// published before they were, see <see cref="For(Package, WorkshopItemDetails)"/>.
	/// </summary>
	public static PlayerModes For( WorkshopItemDetails item )
	{
		if ( item is null ) return default;

		bool Has( string tag ) => item.Tags.Contains( tag, StringComparer.OrdinalIgnoreCase );
		int Count( string key ) => item.KeyValues.TryGetValue( key, out var value ) && int.TryParse( value, out var n ) ? n : 0;

		return Make( Has( "Singleplayer" ), Has( "Multiplayer" ), Has( "Co-op" ), Has( "Versus" ), Count( "RecommendedMinPlayers" ), Count( "RecommendedMaxPlayers" ), Count( "MaxPlayers" ) );
	}

	/// <summary>
	/// From its Steam Workshop item when that says, otherwise from the package - an item published before it had
	/// the tags doesn't.
	/// </summary>
	public static PlayerModes For( Package package, WorkshopItemDetails workshop )
	{
		var modes = For( workshop );
		return modes.IsUnknown && !modes.HasRecommendedPlayers ? For( package ) : modes;
	}

	/// <summary>
	/// What it's meant for decides, when it says - otherwise whether it says it's singleplayer or multiplayer
	/// </summary>
	static PlayerModes Make( bool solo, bool multiplayer, bool coop, bool versus, int recommendedMin, int recommendedMax, int maxPlayers )
	{
		if ( recommendedMin > 0 || recommendedMax > 0 )
		{
			var min = recommendedMin > 0 ? recommendedMin : 1;
			return new PlayerModes( min <= 1, recommendedMax == 0 || recommendedMax > 1 || coop || versus, coop, versus, recommendedMin, recommendedMax, maxPlayers );
		}

		return new PlayerModes( solo, multiplayer || coop || versus, coop, versus, 0, 0, maxPlayers );
	}

	/// <summary>
	/// The icon for it - one person, or a group.
	/// </summary>
	public string Icon => Multiplayer ? "groups" : "person";

	/// <summary>
	/// A word or two for the card - "Co-op", "Versus", "Multiplayer", "Singleplayer". Null when we
	/// don't know.
	/// </summary>
	public string Label
	{
		get
		{
			if ( IsUnknown ) return null;
			if ( !Multiplayer ) return "#menu.games.singleplayer";

			return (Solo, Coop, Versus) switch
			{
				(true, true, true ) => "#menu.games.solo_or_co_op_versus",
				(true, true, false ) => "#menu.games.solo_or_co_op",
				(true, false, true ) => "#menu.games.solo_or_versus",
				(true, false, false ) => "#menu.games.solo_or_multiplayer",
				(false, true, true ) => "#menu.games.co_op_versus",
				(false, true, false ) => "#menu.games.co_op",
				(false, false, true ) => "#menu.games.versus",
				_ => "#menu.home.multiplayer",
			};
		}
	}

	/// <summary>
	/// What that means for you, in a sentence - for a tooltip.
	/// </summary>
	public string Hint
	{
		get
		{
			if ( IsUnknown ) return null;
			if ( !Multiplayer ) return Language.GetPhrase( "menu.games.played_on_your_own" );

			var phrase = (Solo, Coop, Versus) switch
			{
				(true, true, true ) => "menu.games.play_on_your_own_or_team_up_with_friends_or_take_them_on",
				(true, true, false ) => "menu.games.play_on_your_own_or_team_up_with_friends",
				(true, false, true ) => "menu.games.play_on_your_own_or_take_on_other_players",
				(true, false, false ) => "menu.games.play_on_your_own_or_play_with_other_people",
				(false, true, true ) => "menu.games.team_up_with_friends_or_take_them_on",
				(false, true, false ) => "menu.games.team_up_with_friends",
				(false, false, true ) => "menu.games.take_on_other_players",
				_ => "menu.games.play_with_other_people",
			};

			return Language.GetPhrase( phrase );
		}
	}

	/// <summary>
	/// How many it's meant for - "1 player", "2 to 4 players", "4+ players". Null when it doesn't say.
	/// </summary>
	public string PlayersText
	{
		get
		{
			if ( !HasRecommendedPlayers ) return null;

			var min = RecommendedMin > 0 ? RecommendedMin : 1;
			var max = RecommendedMax;

			if ( max <= 0 ) return $"{min}+ players";
			if ( min == max ) return min == 1 ? "1 player" : $"{min} players";
			return $"{min} to {max} players";
		}
	}

	/// <summary>
	/// How well a group this size suits it.
	/// </summary>
	public enum FitKind
	{
		/// <summary>Nothing to go on.</summary>
		Unknown,

		/// <summary>What it's made for.</summary>
		Good,

		/// <summary>Not what it's made for - worth saying before you go in.</summary>
		Poor
	}

	/// <summary>
	/// How well a group of <paramref name="players"/> suits it, and why, when it doesn't - a line short
	/// enough for one line of a popup or the game card. On your own is always fine - a multiplayer game
	/// you'd join other people in. A party has to be within what it's made for, and fit in a lobby of it.
	/// </summary>
	public (FitKind Fit, string Why) FitFor( int players )
	{
		if ( IsUnknown && !HasRecommendedPlayers ) return (FitKind.Unknown, null);
		if ( players <= 1 ) return (FitKind.Good, null);

		if ( !Multiplayer )
			return (FitKind.Poor, "#menu.games.this_game_is_singleplayer");

		if ( MaxPlayers > 0 && players > MaxPlayers )
			return (FitKind.Poor, $"This game holds up to {MaxPlayers} players. You have {players}.");

		if ( RecommendedMax > 0 && players > RecommendedMax )
			return (FitKind.Poor, $"This game is made for {PlayersText}. You have {players}.");

		if ( RecommendedMin > players )
			return (FitKind.Poor, $"This game is made for {PlayersText}. You have {players}.");

		return (FitKind.Good, null);
	}

	/// <summary>
	/// How many are playing - your party, or just you. Through the party deck's view of it, so
	/// menu_mock_party can stand in for one.
	/// </summary>
	public static int PartySize => PartyView.Exists ? PartyView.MemberCount : 1;
}
