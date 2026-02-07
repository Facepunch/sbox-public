using Editor.CodeEditors;

namespace Editor;

[CustomEditor( typeof( ICodeEditor ) )]
public class CodeEditorControlWidget : ControlWidget
{
	public CodeEditorControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		BuildUIImmediate();
	}

	bool _needsRebuild;

	[EditorEvent.Frame]
	public void OnFrame()
	{
		if ( _needsRebuild )
		{
			_needsRebuild = false;
			BuildUIImmediate();
		}
	}

	void BuildUI()
	{
		_needsRebuild = true;
	}

	void BuildUIImmediate()
	{
		Layout.Clear( true );

		var comboBox = new ComboBox( this );
		var codeEditors = EditorTypeLibrary.GetTypes<ICodeEditor>()
			.Where( x => !x.IsInterface && !x.IsAbstract )
			.Select( x =>
			{
				var instance = x.Create<ICodeEditor>();
				return new { Type = x, Instance = instance, IsInstalled = instance?.IsInstalled() ?? false };
			} )
			.OrderByDescending( x => x.IsInstalled )
			.ThenBy( x => x.Type.Name );

		if ( !codeEditors.Any() )
		{
			comboBox.AddItem( "None - install one!", "error" );
		}

		foreach ( var entry in codeEditors )
		{
			var codeEditor = entry.Type;
			var instance = entry.Instance;
			var isInstalled = entry.IsInstalled;

			if ( codeEditor.TargetType == typeof( ICodeEditor ) ) continue;

			var pathKey = $"CodeEditor.{codeEditor.Name}.Path";
			var customPath = EditorCookie.Get( pathKey, "" );
			bool isUsingCustom = !string.IsNullOrEmpty( customPath );

			bool isSelected = CodeEditor.Current?.GetType() == instance?.GetType() && !isUsingCustom;

			// hide forks if they aren't installed (unless it's the main vs code or currently selected)
			if ( !isInstalled && !isSelected && instance is VSCodeBase vsCode && !vsCode.IsPrimary )
			{
				continue;
			}

			comboBox.AddItem(
				codeEditor.Title,
				codeEditor.Icon,
				() =>
				{
					if ( !string.IsNullOrEmpty( EditorCookie.Get( pathKey, "" ) ) )
					{
						EditorCookie.Set( pathKey, "" ); // Clear any override
						CodeEditor.Current = codeEditor.Create<ICodeEditor>();
						BuildUI(); // rebuild because we changed a cookie/item list
					}
					else
					{
						CodeEditor.Current = codeEditor.Create<ICodeEditor>();
					}
				},
				codeEditor.Description,
				isSelected,
				isInstalled 
			);

			// add the custom path entry separately if it exists
			if ( isUsingCustom )
			{
				bool isCustomSelected = CodeEditor.Current?.GetType() == instance?.GetType() && isUsingCustom;

				comboBox.AddItem(
					customPath,
					"folder",
					() =>
					{
						// cookie is already set, so just select it
						CodeEditor.Current = codeEditor.Create<ICodeEditor>();
					},
					$"Manual override for {codeEditor.Title}",
					isCustomSelected,
					true // always considered "installed" if path is set
				);
			}
		}

		// selection logic for the combobox itself to show the right text
		if ( CodeEditor.Current is not null )
		{
			var type = EditorTypeLibrary.GetType( CodeEditor.Current.GetType() );
			var pathKey = $"CodeEditor.{type.Name}.Path";
			var customPath = EditorCookie.Get( pathKey, "" );

			if ( !string.IsNullOrEmpty( customPath ) )
			{
				comboBox.TrySelectNamed( customPath );
			}
			else
			{
				comboBox.TrySelectNamed( type.Title );
			}
		}

		Layout.Add( comboBox );

		var browseBtn = new IconButton( "folder_open" );
		browseBtn.ToolTip = "Manually locate executable (if auto-detection fails)";
		browseBtn.OnClick += () =>
		{
			var fd = new FileDialog( null );
			fd.Title = $"Locate editor executable";
			fd.DefaultSuffix = "exe";
			fd.SetNameFilter( "Executable Files (*.exe)" );
			if ( fd.Execute() )
			{
				var selectedFile = fd.SelectedFile;
				var fileName = System.IO.Path.GetFileName( selectedFile );
				var targetType = CodeEditor.Current?.GetType();

				// see if this exe belongs to a known editor class
				var editorTypes = EditorTypeLibrary.GetTypes<ICodeEditor>()
					.Where( x => !x.IsInterface && !x.IsAbstract );

				foreach ( var type in editorTypes )
				{
					var instance = type.Create<ICodeEditor>();
					if ( instance == null ) continue;

					bool isMatch = false;
					if ( instance is VSCodeBase vsCode )
					{
						isMatch = vsCode.MatchesExecutable( fileName );
					}
					else if ( instance is Rider rider )
					{
						isMatch = rider.MatchesExecutable( fileName );
					}
					else if ( instance is VisualStudio vs )
					{
						isMatch = vs.MatchesExecutable( fileName );
					}

					if ( isMatch )
					{
						targetType = type.TargetType;
						break;
					}
				}

				if ( targetType == null )
				{
					// If we couldn't smart-match the name, default to main VS Code
					targetType = typeof( VisualStudioCode );
				}

				if ( targetType != null )
				{
					EditorCookie.Set( $"CodeEditor.{targetType.Name}.Path", selectedFile );
					
					// Change current editor to the one we just mapped
					CodeEditor.Current = EditorTypeLibrary.Create<ICodeEditor>( targetType );

					BuildUI();
				}
			}
		};
		Layout.Add( browseBtn );
	}
}
