using Sandbox;

namespace MenuProject;

/// <summary>
/// How a game is played - on your own, with others, together or against each other - read from its
/// tags alone, since those come with list results and nothing else here does.
/// </summary>
public readonly record struct PlayerModes( bool Solo, bool Multiplayer, bool Coop, bool Versus )
{
	/// <summary>
	/// No play mode tags - we don't know, so nothing's shown rather than a guess.
	/// </summary>
	public bool IsUnknown => !Solo && !Multiplayer;

	public static PlayerModes For( Package package )
	{
		if ( package is null || package.TypeName != "game" ) return default;

		var tags = package.Tags ?? Array.Empty<string>();
		bool Has( string tag ) => tags.Contains( tag, StringComparer.OrdinalIgnoreCase );

		var coop = Has( "coop" );
		var versus = Has( "versus" ) || Has( "pvp" );

		return new PlayerModes( Has( "singleplayer" ), Has( "multiplayer" ) || coop || versus, coop, versus );
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
			if ( !Multiplayer ) return "Singleplayer";

			var kind = Coop && Versus ? "Co-op & Versus" : Coop ? "Co-op" : Versus ? "Versus" : "Multiplayer";
			return Solo ? $"Solo or {kind}" : kind;
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
			if ( !Multiplayer ) return "Played on your own";

			var with = Coop && Versus ? "team up with friends or take them on" : Coop ? "team up with friends" : Versus ? "take on other players" : "play with other people";
			return Solo ? $"Play on your own, or {with}" : char.ToUpper( with[0] ) + with[1..];
		}
	}
}
