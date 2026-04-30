
using Editor.CodeEditors;

namespace Editor;

[CustomEditor( typeof( ICodeEditor ) )]
public class CodeEditorControlWidget : ControlWidget
{
	public CodeEditorControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = new Sandbox.UI.Margin( 0, 0, 4, 0 );

		var comboBox = new ComboBox( this );

		var codeEditors = EditorTypeLibrary.GetTypes<ICodeEditor>()
			.Where( x => !x.IsInterface )
			.OrderBy( x => x.Name );

		// If we have no code editors, the combobox will end up defaulting to a code editor we don't have installed.
		if ( !codeEditors.Any() )
		{
			comboBox.AddItem( "None - install one!", "error" );
		}

		foreach ( var codeEditor in codeEditors.OrderByDescending( x => x.Create<ICodeEditor>()?.IsInstalled() ) )
		{
			if ( codeEditor.TargetType == typeof( ICodeEditor ) ) continue;

			// Skip CustomCodeEditor from auto-discovery, we handle it separately
			if ( codeEditor.TargetType == typeof( CustomCodeEditor ) ) continue;

			var instance = codeEditor.Create<ICodeEditor>();

			comboBox.AddItem(
				codeEditor.Title,
				codeEditor.Icon,
				() => property.SetValue( codeEditor.Create<ICodeEditor>() ),
				codeEditor.Description,
				false,
				instance.IsInstalled()
			);
		}

		// If a custom editor path is configured, add it with the exe name
		var customPath = CustomCodeEditor.CustomPath;
		if ( !string.IsNullOrEmpty( customPath ) )
		{
			var exeName = System.IO.Path.GetFileNameWithoutExtension( customPath );
			comboBox.AddItem(
				exeName,
				null,
				() => property.SetValue( new CustomCodeEditor() ),
				customPath,
				false,
				System.IO.File.Exists( customPath )
			);
		}

		if ( CodeEditor.Current is not null )
		{
			if ( CodeEditor.Current is CustomCodeEditor && !string.IsNullOrEmpty( customPath ) )
			{
				comboBox.TrySelectNamed( System.IO.Path.GetFileNameWithoutExtension( customPath ) );
			}
			else
			{
				comboBox.TrySelectNamed( DisplayInfo.ForType( CodeEditor.Current.GetType() ).Name );
			}
		}

		Layout.Add( comboBox, 1 );
		Layout.Add( new IconButton( "more_horiz", () => BrowseForEditor( property, comboBox ) )
		{
			ToolTip = "Browse for a code editor executable"
		} );
	}

	private void BrowseForEditor( SerializedProperty property, ComboBox comboBox )
	{
		var fd = new FileDialog( null )
		{
			Title = "Select Code Editor",
		};

		if ( OperatingSystem.IsWindows() )
		{
			fd.DefaultSuffix = ".exe";
			fd.SetNameFilter( "Executables (*.exe)" );
		}
		else
		{
			fd.SetNameFilter( "All files (*)" );
		}

		fd.SetFindExistingFile();
		fd.SetModeOpen();

		if ( !fd.Execute() ) return;

		var selectedPath = fd.SelectedFile;
		if ( string.IsNullOrEmpty( selectedPath ) ) return;

		CustomCodeEditor.CustomPath = selectedPath;

		var exeName = System.IO.Path.GetFileNameWithoutExtension( selectedPath );

		if ( comboBox.FindIndex( exeName ) is null )
		{
			comboBox.AddItem(
				exeName,
				null,
				() => property.SetValue( new CustomCodeEditor() ),
				selectedPath,
				false,
				true
			);
		}

		property.SetValue( new CustomCodeEditor() );
		comboBox.TrySelectNamed( exeName );
	}
}
