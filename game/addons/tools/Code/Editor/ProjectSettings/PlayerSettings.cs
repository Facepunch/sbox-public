namespace Editor.ProjectSettingPages;

/// <summary>
/// How many players a game is for and how they play, edited together in the Multiplayer project
/// settings and in the publish wizard. All plain project meta keys.
/// </summary>
internal sealed class PlayerSettings
{
	/// <summary>
	/// The minimum amount of players required to play this game.
	/// </summary>
	[Range( 1, 64 )]
	public int MinimumPlayers { get; set; }

	/// <summary>
	/// The maximum amount of players this game can have at one time.
	/// </summary>
	[Range( 1, 64 )]
	public int MaximumPlayers { get; set; }

	/// <summary>
	/// The fewest players the game is meant for. 1 if it can be played alone.
	/// </summary>
	[Range( 0, 64 ), Title( "Recommended Min Players" )]
	public int RecommendedMinPlayers { get; set; }

	/// <summary>
	/// The most players the game is meant for. 0 for no limit, e.g. 4+.
	/// </summary>
	[Range( 0, 64 ), Title( "Recommended Max Players" )]
	public int RecommendedMaxPlayers { get; set; }

	/// <summary>
	/// Players work together.
	/// </summary>
	public bool Coop { get; set; }

	/// <summary>
	/// Players compete against each other.
	/// </summary>
	public bool Versus { get; set; }

	public static PlayerSettings Load( Project project )
	{
		var meta = project.Config;
		var settings = new PlayerSettings
		{
			MinimumPlayers = meta.GetMetaOrDefault( "MinPlayers", 1 ),
			MaximumPlayers = meta.GetMetaOrDefault( "MaxPlayers", 16 ),
		};

		if ( meta.TryGetMeta( "RecommendedMinPlayers", out int recommendedMin ) )
		{
			settings.RecommendedMinPlayers = recommendedMin;
			settings.RecommendedMaxPlayers = meta.GetMetaOrDefault( "RecommendedMaxPlayers", 0 );
			settings.Coop = meta.GetMetaOrDefault( "Coop", false );
			settings.Versus = meta.GetMetaOrDefault( "Versus", false );
		}
		else
		{
			WebsiteGameConfig.FillPlayers( project, settings );
		}

		return settings;
	}

	public void Save( Project project )
	{
		project.Config.SetMeta( "MinPlayers", MinimumPlayers );
		project.Config.SetMeta( "MaxPlayers", MaximumPlayers );
		project.Config.SetMeta( "RecommendedMinPlayers", RecommendedMinPlayers );
		project.Config.SetMeta( "RecommendedMaxPlayers", RecommendedMaxPlayers );
		project.Config.SetMeta( "Coop", Coop );
		project.Config.SetMeta( "Versus", Versus );
	}

	/// <summary>
	/// Adds the fields and a live preview of the game card to <paramref name="layout"/>. <paramref name="listen"/>
	/// is given the serialized object so the caller can track changes.
	/// </summary>
	public void Build( Layout layout, Action<SerializedObject> listen )
	{
		var so = this.GetSerialized();
		listen( so );

		var preview = new Label.Body( "" );
		var callerChanged = so.OnPropertyChanged;
		so.OnPropertyChanged = p =>
		{
			callerChanged?.Invoke( p );
			preview.Text = Preview;
		};

		layout.Add( new InformationBox( "<p>Minimum and Maximum Players are the lobby limit. Recommended players is what your game is meant for - that's what players see on your game's card in the menu.</p>" ) );

		var sheet = new ControlSheet();
		sheet.AddRow( so.GetProperty( nameof( MinimumPlayers ) ) );
		sheet.AddRow( so.GetProperty( nameof( MaximumPlayers ) ) );
		sheet.AddRow( so.GetProperty( nameof( RecommendedMinPlayers ) ) );
		sheet.AddRow( so.GetProperty( nameof( RecommendedMaxPlayers ) ) );
		sheet.AddRow( so.GetProperty( nameof( Coop ) ) );
		sheet.AddRow( so.GetProperty( nameof( Versus ) ) );
		layout.Add( sheet );

		preview.Text = Preview;
		layout.Add( preview );
	}

	/// <summary>How the game card will describe these settings.</summary>
	public string Preview
	{
		get
		{
			if ( RecommendedMinPlayers <= 0 && RecommendedMaxPlayers <= 0 )
				return "Shown on your game's card as: no player count yet - set Recommended players.";

			var min = RecommendedMinPlayers > 0 ? RecommendedMinPlayers : MinimumPlayers;
			var max = RecommendedMaxPlayers;

			var count = max <= 0 ? $"{min}+ players"
				: min == max ? (min == 1 ? "1 player" : $"{min} players")
				: $"{min} - {max} players";

			var single = min == 1;
			var multi = max <= 0 || max > 1;
			var mode = single && multi ? "Single & multiplayer" : single ? "Single player" : "Multiplayer";

			var parts = new List<string> { mode, count };
			if ( Coop ) parts.Add( "Coop" );
			if ( Versus ) parts.Add( "Versus" );

			return $"Shown on your game's card as: {string.Join( " · ", parts )}";
		}
	}

	/// <summary>Why these can't be saved as they are, or null.</summary>
	public string Error
	{
		get
		{
			if ( MinimumPlayers > MaximumPlayers )
				return "Minimum Players is more than Maximum Players.";

			if ( RecommendedMinPlayers > 0 && RecommendedMaxPlayers > 0 && RecommendedMinPlayers > RecommendedMaxPlayers )
				return "Recommended Min Players is more than Recommended Max Players.";

			if ( RecommendedMinPlayers > MaximumPlayers || RecommendedMaxPlayers > MaximumPlayers )
				return "Recommended players can't be more than Maximum Players.";

			return null;
		}
	}

	/// <summary>Worth pointing out, but fine to save.</summary>
	public string Warning => RecommendedMinPlayers <= 0 && RecommendedMaxPlayers <= 0
		? "Set Recommended players, so players know how many people your game is for."
		: null;
}
