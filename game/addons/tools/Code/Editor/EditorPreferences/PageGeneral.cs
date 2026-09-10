using Editor.CodeEditors;

namespace Editor.Preferences;

internal class PageGeneral : Widget
{
	public PageGeneral( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;

		{
			Layout.Add( new Label.Subtitle( "Code" ) );

			var codeEditorSheet = new ControlSheet();
			var codeEditorWidget = codeEditorSheet.AddProperty( () => CodeEditor.Current ) as CodeEditorControlWidget;
			Layout.Add( codeEditorSheet );

			var customSettings = new Widget( this );
			customSettings.Layout = Layout.Column();
			customSettings.Layout.Margin = new Sandbox.UI.Margin( 12, 4, 0, 8 );
			customSettings.Layout.Spacing = 4;
			customSettings.Layout.Add( new Label.Header( "Custom Settings" ) );

			var customSheet = new ControlSheet();
			customSheet.AddProperty( () => CustomCodeEditorSettings.ExecutablePath );
			customSheet.AddProperty( () => CustomCodeEditorSettings.OpenFileArgs );
			customSheet.AddProperty( () => CustomCodeEditorSettings.OpenSolutionArgs );
			customSettings.Layout.Add( customSheet );

			customSettings.Visible = codeEditorWidget?.IsCustomSelected ?? false;
			Layout.Add( customSettings );

			if ( codeEditorWidget is not null )
			{
				codeEditorWidget.CustomEditorSelected += isCustom => customSettings.Visible = isCustom;
			}

			var sheet = new ControlSheet();

			sheet.AddProperty( () => EditorPreferences.ClearConsoleOnPlay );
			sheet.AddProperty( () => EditorPreferences.FullScreenOnPlay );
			sheet.AddProperty( () => EditorPreferences.FastHotload );

			Layout.Add( sheet );
			Layout.AddStretchCell();
		}
	}
}
