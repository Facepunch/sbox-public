namespace Editor.Preferences;

internal class PageGeneral : Widget
{
	public PageGeneral( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;

		{
			Layout.Add( new Label.Subtitle( "Code" ) );

			var sheet = new ControlSheet();

			sheet.AddProperty( () => CodeEditor.Current );
			sheet.AddProperty( () => EditorPreferences.ClearConsoleOnPlay );
			sheet.AddProperty( () => EditorPreferences.FullScreenOnPlay );
			sheet.AddProperty( () => EditorPreferences.FastHotload );

			Layout.Add( sheet );
		}

		{
			Layout.AddSpacingCell( 16 );
			Layout.Add( new Label.Subtitle( "Scene" ) );

			var sceneSheet = new ControlSheet();

			sceneSheet.AddProperty( () => EditorPreferences.ExternalSceneChange );

			Layout.Add( sceneSheet );
			Layout.AddStretchCell();
		}
	}
}
