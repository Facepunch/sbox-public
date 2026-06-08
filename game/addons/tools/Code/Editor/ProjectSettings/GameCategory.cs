namespace Editor.ProjectSettingPages;

[Title( "Game Setup" ), Icon( "games" )]
internal sealed class GameCategory : ProjectSettingsWindow.Category
{
	/// <summary>
	/// This scene is loaded when the game starts.
	/// </summary>
	public SceneFile StartupScene { get; set; }

	/// <summary>
	/// This scene is loaded when a game is started with a targeted map. Leave blank if you don't support map loading.
	/// </summary>
	public SceneFile MapStartupScene { get; set; }

	/// <summary>
	/// This scene is loaded when the Dedicated Server starts.
	/// </summary>
	public SceneFile ServerStartupScene { get; set; }

	/// <summary>
	/// This scene is additive loaded to every scene you load. You can use this to add UI or other common things
	/// that need to be present in every loaded scene.
	/// </summary>
	public SceneFile SystemScene { get; set; }


	/// <summary>
	/// This game uses the Streamer Api. This will enable the Streamer Mode features in the menu, and allow you to use the Streamer API in your game.
	/// </summary>
	public bool UsesStreamerFeatures { get; set; }

	LaunchModes LaunchMode { get; set; }

	public override void OnInit( Project project )
	{
		StartupScene = ResourceLibrary.Get<SceneFile>( Project.Config.GetMetaOrDefault( "StartupScene", "start.scene" ) );
		MapStartupScene = ResourceLibrary.Get<SceneFile>( Project.Config.GetMetaOrDefault( "MapStartupScene", "" ) );
		LaunchMode = Project.Config.GetMetaOrDefault( "LaunchMode", LaunchModes.Normal );
		ServerStartupScene = ResourceLibrary.Get<SceneFile>( Project.Config.GetMetaOrDefault( "DedicatedServerStartupScene", "" ) );
		SystemScene = ResourceLibrary.Get<SceneFile>( Project.Config.GetMetaOrDefault( "SystemScene", "" ) );
		UsesStreamerFeatures = Project.Config.GetMetaOrDefault( "UsesStreamerFeatures", false );

		{
			var so = this.GetSerialized();
			var sheet = new ControlSheet();

			sheet.AddRow( so.GetProperty( nameof( StartupScene ) ) );
			sheet.AddRow( so.GetProperty( nameof( MapStartupScene ) ) );
			sheet.AddRow( so.GetProperty( nameof( ServerStartupScene ) ) );
			sheet.AddRow( so.GetProperty( nameof( SystemScene ) ) );
			sheet.AddRow( so.GetProperty( nameof( LaunchMode ) ) );

			sheet.AddGroup( "Features", [so.GetProperty( nameof( UsesStreamerFeatures ) )] );

			BodyLayout.Add( sheet );
			ListenForChanges( so );
		}

	}

	public override void OnSave()
	{
		Project.Config.SetMeta( "StartupScene", StartupScene?.ResourcePath ?? null );
		Project.Config.SetMeta( "MapStartupScene", MapStartupScene?.ResourcePath ?? null );
		Project.Config.SetMeta( "LaunchMode", LaunchMode );
		Project.Config.SetMeta( "DedicatedServerStartupScene", ServerStartupScene?.ResourcePath ?? null );
		Project.Config.SetMeta( "SystemScene", SystemScene?.ResourcePath ?? null );
		Project.Config.SetMeta( "UsesStreamerFeatures", UsesStreamerFeatures ? UsesStreamerFeatures : null );

		base.OnSave();
	}

	enum LaunchModes
	{
		/// <summary>
		/// Launching the game will just create a new game.
		/// </summary>
		[Icon( "smart_display" )]
		Normal,

		/// <summary>
		/// Show a popup when starting the game to allow the user to select a map before launching.
		/// </summary>
		[Icon( "wysiwyg" )]
		Launcher,

		/// <summary>
		/// Launching the game will try to join any lobby that is available. If no lobbies are available, we will create a new game - which assumably will create a lobby.
		/// </summary>
		[Icon( "fast_forward" )]
		QuickPlay,

		/// <summary>
		/// Launching the game will show a list of available servers. The game can only be hosted on a Dedicated Server.
		/// </summary>
		[Icon( "dns" )]
		DedicatedServerOnly
	}
}
