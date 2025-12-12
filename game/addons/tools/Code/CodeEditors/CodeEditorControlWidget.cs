using Editor.CodeEditors;

namespace Editor;

/// <summary>
/// Custom control widget for selecting and configuring code editors.
/// Displays a dropdown of available editors with a settings button for managing custom editors.
/// </summary>
[CustomEditor( typeof( ICodeEditor ) )]
public class CodeEditorControlWidget : ControlWidget
{
	private readonly ComboBox _comboBox;
	private readonly SerializedProperty _property;

	public CodeEditorControlWidget( SerializedProperty property ) : base( property )
	{
		_property = property;

		Layout = Layout.Row();
		Layout.Spacing = 0;

		_comboBox = new ComboBox( this );
		RefreshEditorList();

		Layout.Add( _comboBox, 1 );

		// Settings button for adding/editing custom editors
		var settingsBtn = new IconButton( "settings" );
		settingsBtn.ToolTip = "Code Editor Settings";
		settingsBtn.OnClick = OpenSettingsMenu;
		settingsBtn.Background = Color.Transparent;
		Layout.Add( settingsBtn );
	}

	/// <summary>
	/// Rebuilds the editor dropdown list with all available built-in and custom editors.
	/// </summary>
	private void RefreshEditorList()
	{
		_comboBox.Clear();

		// Get all built-in default code editors
		var installedEditors = EditorTypeLibrary.GetTypes<ICodeEditor>()
			.Where( x => !x.IsInterface && x.TargetType != typeof( ICodeEditor ) && x.Name != "CustomCodeEditor" )
			.OrderBy( x => x.Name )
			.ToList();

		// Get all custom editors
		var customEditors = CustomCodeEditorRegistry.GetAllCustomEditors().ToList();

		// If we have no code editors at all, complain
		if ( !installedEditors.Any() && !customEditors.Any() )
		{
			_comboBox.AddItem( "None - install one!", "error" );
			return;
		}

		// Create instances once and cache the installed status
		var editorInstances = installedEditors
			.Select( x => (Type: x, Instance: x.Create<ICodeEditor>()) )
			.Select( x => (x.Type, x.Instance, IsInstalled: x.Instance?.IsInstalled() ?? false) )
			.OrderByDescending( x => x.IsInstalled )
			.ToList();

		foreach ( var (codeEditor, instance, isInstalled) in editorInstances )
		{

			// Shows an obvious message if its not installed instead of just disabling the click.
			var displayName = isInstalled ? codeEditor.Title : $"{codeEditor.Title} (Not Installed)";
			var icon = isInstalled ? "check" : "block";
			var description = isInstalled ? codeEditor.Description : "This editor was not found on your system";

			// Add the editor to the combobox
			_comboBox.AddItem(
				displayName,
				icon,
				() => SelectEditor( codeEditor.Name, null ),
				description,
				false,
				isInstalled
			);
		}

		// Add custom editors to the combobox last so they appear at the bottom
		if ( customEditors.Any() )
		{
			foreach ( var (config, editor) in customEditors )
			{
				var isInstalled = editor.IsInstalled();

				// Show indication if the exe is no longer found at the config path
				var displayName = isInstalled ? config.Name : $"{config.Name} (Not Found)";
				var icon = isInstalled ? "check" : "block";
				var description = isInstalled ? $"Custom: {config.ExecutablePath}" : $"Executable not found: {config.ExecutablePath}";

				_comboBox.AddItem(
					displayName,
					icon,
					() => SelectEditor( null, config.Id ),
					description,
					false,
					isInstalled
				);
			}
		}

		UpdateSelection();
	}

	/// <summary>
	/// Selects a code editor by type name (for built-in) or custom ID (for custom editors).
	/// </summary>
	/// <param name="typeName">The type name of a built-in editor, or null for custom editors.</param>
	/// <param name="customId">The unique ID of a custom editor, or null for built-in editors.</param>
	private void SelectEditor( string typeName, string customId )
	{
		CodeEditor.InvalidateCache();

		// If we have a custom ID, use that to get the editor
		if ( !string.IsNullOrEmpty( customId ) )
		{
			var editor = CustomCodeEditorRegistry.GetEditorById( customId );
			if ( editor != null )
			{
				_property.SetValue( editor );
				EditorCookie.SetString( "CodeEditor", $"Custom:{customId}" );
			}
		}
		// If we have a type name, use that to get the editor
		else if ( !string.IsNullOrEmpty( typeName ) )
		{
			var editorType = EditorTypeLibrary.GetTypes<ICodeEditor>().FirstOrDefault( t => t.Name == typeName );
			if ( editorType != null )
			{
				_property.SetValue( editorType.Create<ICodeEditor>() );
				EditorCookie.SetString( "CodeEditor", typeName );
			}
		}
	}

	/// <summary>
	/// Updates the combobox selection to match the current code editor.
	/// </summary>
	private void UpdateSelection()
	{
		if ( CodeEditor.Current is CustomCodeEditor customEditor )
		{
			var name = customEditor.Config?.Name ?? "Custom Editor";
			// Try both with and without the "(Not Found)" suffix for backwards compatibility
			if ( !_comboBox.TrySelectNamed( name ) )
				_comboBox.TrySelectNamed( $"{name} (Not Found)" );
		}
		else if ( CodeEditor.Current is not null )
		{
			var name = DisplayInfo.ForType( CodeEditor.Current.GetType() ).Name;
			// Try both with and without the "(Not Installed)" suffix for backwards compatibility
			if ( !_comboBox.TrySelectNamed( name ) )
				_comboBox.TrySelectNamed( $"{name} (Not Installed)" );
		}
	}

	/// <summary>
	/// Opens the settings menu for adding and editing custom code editors.
	/// </summary>
	private void OpenSettingsMenu()
	{
		var menu = new Menu( this );
		menu.MaximumHeight = 400;

		menu.AddOption( "Add Custom Editor...", "add", () =>
		{
			new CustomCodeEditorSettingsDialog( null, RefreshEditorList );
		} );

		var customEditors = CustomCodeEditorStorage.GetAll();
		if ( customEditors.Any() )
		{
			menu.AddSeparator();

			foreach ( var config in customEditors )
			{
				menu.AddOption( config.Name, "edit", () =>
				{
					new CustomCodeEditorSettingsDialog( config, RefreshEditorList );
				} );
			}
		}

		menu.OpenAtCursor();
	}
}
