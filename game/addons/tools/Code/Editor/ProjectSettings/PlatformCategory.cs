namespace Editor.ProjectSettingPages;

[Title( "Platform" ), Icon( "chat" )]
internal sealed class PlatformCategory : ProjectSettingsWindow.Category
{
	PlatformSettings settings;

	public override void OnInit( Project project )
	{
		settings = EditorUtility.LoadProjectSettings<PlatformSettings>( "Platform.config" );

		var so = settings.GetSerialized();
		ListenForChanges( so );

		var sheet = new ControlSheet();
		sheet.AddObject( so );
		BodyLayout.Add( sheet );
	}

	public override void OnSave()
	{
		EditorUtility.SaveProjectSettings( settings, "Platform.config" );
		base.OnSave();
	}
}
