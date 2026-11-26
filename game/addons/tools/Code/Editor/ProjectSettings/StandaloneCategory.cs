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

	/// <summary>
	/// Shown while your exported game loads a scene, instead of the startup image. Give it a camera and some UI, which
	/// can show what's loading from <c>LoadingScreen</c>.
	/// </summary>
	[ResourceType( "scene" )]
	public string LoadingScene { get; set; }

	public override void OnInit( Project project )
	{
		//
		// Standalone games
		//
		if ( project.Config.Type == "game" )
		{
			LoadingScene = Project.Config.GetMetaOrDefault( "LoadingScene", "" );

			var so = this.GetSerialized();

			var cs = new ControlSheet();
			cs.AddRow( so.GetProperty( nameof( IsStandaloneOnly ) ) );
			cs.AddRow( so.GetProperty( nameof( LoadingScene ) ) );

			BodyLayout.Add( new InformationBox( $"Disabling the whitelist will prevent you from uploading to {Global.BackendTitle}, but will allow you to expose settings like resolution, fullscreen/windowed, etc." ) );
			BodyLayout.Add( cs );

			ListenForChanges( so );
		}
	}

	public override void OnSave()
	{
		if ( Project.Config.Type == "game" )
			Project.Config.SetMeta( "LoadingScene", string.IsNullOrEmpty( LoadingScene ) ? null : LoadingScene );

		base.OnSave();
	}
}
