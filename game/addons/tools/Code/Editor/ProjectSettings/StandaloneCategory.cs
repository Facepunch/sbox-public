namespace Editor.ProjectSettingPages;

[Title( "Game Exporting" ), Icon( "publish" )]
internal sealed class StandaloneCategory : ProjectSettingsWindow.Category
{
	[Title( "Disable Whitelist" )]
	public bool IsStandaloneOnly
	{
		get => Project.Config.IsStandaloneOnly;
		set => Project.Config.IsStandaloneOnly = value;
	}

	public override void OnInit( Project project )
	{
		//
		// Standalone games
		//
		if ( project.Config.Type == "game" )
		{
			var so = this.GetSerialized();

			var cs = new ControlSheet();
			cs.AddRow( so.GetProperty( nameof( IsStandaloneOnly ) ) );

			BodyLayout.Add( new InformationBox( $"Disabling the whitelist will prevent you from uploading to {Global.BackendTitle}, but will allow you to expose settings like resolution, fullscreen/windowed, etc." ) );
			BodyLayout.Add( cs );

			ListenForChanges( so );
		}
	}
}
