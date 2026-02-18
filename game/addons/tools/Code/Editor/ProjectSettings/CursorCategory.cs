
namespace Editor.ProjectSettingPages;

[Title( "Cursors" ), Icon( "mouse" )]
internal sealed class CursorCategory : ProjectSettingsWindow.Category
{
	CursorSettings settings;

	public override void OnInit( Project project )
	{
		base.OnInit( project );

		settings = EditorUtility.LoadProjectSettings<CursorSettings>( "Cursors.config" );
		settings.Cursors ??= new();

		BodyLayout.Add( new InformationBox( "Your project can register new cursor types or override system cursors." ) );

		var warning = new WarningBox( "You will likely have to add your cursor images to resource files for now.", this );
		BodyLayout.Add( warning );

		var so = EditorTypeLibrary.GetSerializedObject( settings );
		BodyLayout.Add( ControlSheet.Create( so ) );

		ListenForChanges( so );
	}

	public override void OnSave()
	{
		EditorUtility.SaveProjectSettings( settings, "Cursors.config" );

		base.OnSave();
	}
}
