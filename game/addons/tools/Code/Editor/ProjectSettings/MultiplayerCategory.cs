namespace Editor.ProjectSettingPages;

[Title( "Multiplayer" ), Icon( "wifi" )]
internal sealed class MultiplayerCategory : ProjectSettingsWindow.Category
{
	PlayerSettings players;
	NetworkingSettings settings;

	public override void OnInit( Project project )
	{
		players = PlayerSettings.Load( project );
		players.Build( BodyLayout, ListenForChanges );

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
		players.Save( Project );

		EditorUtility.SaveProjectSettings( settings, "Networking.config" );

		base.OnSave();
	}
}
