using System.Diagnostics;

namespace Editor.CodeEditors;

/// <summary>
/// A custom code editor implementation that allows configuration of any external editor.
/// </summary>
[Title( "Custom Editor" )]
public class CustomCodeEditor : ICodeEditor
{
	private readonly CustomCodeEditorConfig _config;

	/// <summary>
	/// The configuration for the custom editor.
	/// </summary>
	public CustomCodeEditorConfig Config => _config;

	public CustomCodeEditor() : this( null ) { }

	/// <summary>
	/// Creates a new custom code editor with the given configuration.
	/// </summary>
	/// <param name="config">The editor configuration with executable path and arguments.</param>
	public CustomCodeEditor( CustomCodeEditorConfig config )
	{
		_config = config;
	}

	/// <inheritdoc/>
	public void OpenFile( string path, int? line = null, int? column = null )
	{
		if ( _config == null )
		{
			Log.Warning( "Custom editor has no configuration" );
			return;
		}

		if ( !_config.IsValid )
		{
			Log.Warning( $"Custom editor '{_config.Name}' is not valid - executable may be missing" );
			return;
		}

		var arguments = _config.FormatArguments( path, line, column );
		Launch( arguments );
	}

	/// <inheritdoc/>
	public void OpenSolution()
	{
		if ( _config == null )
		{
			Log.Warning( "Custom editor has no configuration" );
			return;
		}

		if ( !_config.IsValid )
		{
			Log.Warning( $"Custom editor '{_config.Name}' is not valid - executable may be missing" );
			return;
		}

		var solution = CodeEditor.AddonSolutionPath();
		var arguments = _config.FormatSolutionArguments( solution );
		Launch( arguments );
	}

	/// <inheritdoc/>
	public void OpenAddon( Project addon )
	{
		OpenSolution();
	}

	/// <inheritdoc/>
	public bool IsInstalled()
	{
		return _config != null && _config.IsValid;
	}

	/// <summary>
	/// Launches the configured editor, using ShellExecute to cover both GUI and Terminal-based editors.
	/// </summary>
	/// <param name="arguments">The formatted command line arguments.</param>
	private void Launch( string arguments )
	{
		var startInfo = new ProcessStartInfo
		{
			Arguments = arguments,
			FileName = _config.ExecutablePath,
			UseShellExecute = true
		};

		try
		{
			Process.Start( startInfo );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to launch custom editor '{_config.Name}': {ex.Message}" );
		}
	}
}

/// <summary>
/// Registry for managing custom code editor instances (as we can have multiple set up)
/// Holds them in a cache so they don't need to be recreated every time.
/// </summary>
public static class CustomCodeEditorRegistry
{
	private static readonly Dictionary<string, CustomCodeEditor> _instances = new();

	/// <summary>
	/// Clears and rebuilds the instances from our stored configuration.
	/// Call this when configurations are added, updated, or removed from storage.
	/// </summary>
	public static void RefreshInstances()
	{
		_instances.Clear();
		foreach ( var config in CustomCodeEditorStorage.GetAll() )
			_instances[config.Id] = new CustomCodeEditor( config );
	}

	/// <summary>
	/// Gets all custom editors and their configurations.
	/// </summary>
	/// <returns>Yields tuples of custom editor configurations and their corresponding instances.</returns>
	public static IEnumerable<(CustomCodeEditorConfig Config, CustomCodeEditor Editor)> GetAllCustomEditors()
	{
		var configs = CustomCodeEditorStorage.GetAll();

		foreach ( var config in configs )
		{
			if ( !_instances.TryGetValue( config.Id, out var editor ) )
			{
				editor = new CustomCodeEditor( config );
				_instances[config.Id] = editor;
			}
			yield return (config, editor);
		}
	}

	/// <summary>
	/// Gets a custom editor instance by its unique ID.
	/// </summary>
	/// <param name="id">The unique ID of the custom editor.</param>
	/// <returns>Returns the editor instance, or null if not found.</returns>
	public static CustomCodeEditor GetEditorById( string id )
	{
		if ( _instances.TryGetValue( id, out var editor ) )
			return editor;

		var config = CustomCodeEditorStorage.GetById( id );
		if ( config == null )
			return null;

		editor = new CustomCodeEditor( config );
		_instances[id] = editor;
		return editor;
	}
}
