namespace Editor.ProjectSettingPages;

[Title( "Multiplayer" ), Icon( "wifi" )]
internal sealed class MultiplayerCategory : ProjectSettingsWindow.Category
{
	/// <summary>
	/// The minimum amount of players required to play this game.
	/// </summary>
	[Range( 1, 16 )]
	public int MinimumPlayers { get; set; }

	/// <summary>
	/// The maximum amount of players this game can have at one time.
	/// </summary>
	[Range( 1, 16 )]
	public int MaximumPlayers { get; set; }

	NetworkingSettings settings;

	public override void OnInit( Project project )
	{
		MinimumPlayers = Project.Config.GetMetaOrDefault( "MinPlayers", 1 );
		MaximumPlayers = Project.Config.GetMetaOrDefault( "MaxPlayers", 16 );

		{
			var so = this.GetSerialized();
			ListenForChanges( so );

			var row1 = BodyLayout.AddColumn();
			row1.Spacing = 4;

			var sheet = new ControlSheet();
			sheet.AddRow( so.GetProperty( nameof( MinimumPlayers ) ) );
			sheet.AddRow( so.GetProperty( nameof( MaximumPlayers ) ) );

			row1.Add( sheet );
		}

		settings = EditorUtility.LoadProjectSettings<NetworkingSettings>( "Networking.config" );

		StartSection( "Peer to Peer" );

		{
			var so = settings.GetSerialized();
			ListenForChanges( so );

			var sheet = new ControlSheet();
			sheet.AddObject( so );
			BodyLayout.Add( sheet );
		}
	}

	public override void OnSave()
	{
		Project.Config.SetMeta( "MinPlayers", MinimumPlayers );
		Project.Config.SetMeta( "MaxPlayers", MaximumPlayers );

		EditorUtility.SaveProjectSettings( settings, "Networking.config" );

		base.OnSave();
	}
}

