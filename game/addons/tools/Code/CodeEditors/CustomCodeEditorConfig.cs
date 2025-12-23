using System.Text.Json.Serialization;

namespace Editor.CodeEditors;

/// <summary>
/// Configuration for a custom code editor.
/// </summary>
public class CustomCodeEditorConfig
{
	/// <summary>
	/// Unique ID for the editor
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// Display name for the editor (shown in the combobox).
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Full path to the editor executable.
	/// </summary>
	public string ExecutablePath { get; set; }

	/// <summary>
	/// Command line arguments template for opening files.
	/// Supports {path} {line} {column} placeholders.
	/// Default is "{path}" which just opens the file.
	/// </summary>
	public string Arguments { get; set; } = "\"{path}\"";

	/// <summary>
	/// Command line arguments template for opening solutions.
	/// Supports {solution} placeholder.
	/// </summary>
	public string SolutionArguments { get; set; } = "\"{solution}\"";

	/// <summary>
	/// Whether this configuration is valid (has a name and executable file exists).
	/// </summary>
	[JsonIgnore]
	public bool IsValid => !string.IsNullOrWhiteSpace( ExecutablePath ) &&
						   System.IO.File.Exists( ExecutablePath ) &&
						   !string.IsNullOrWhiteSpace( Name );

	/// <summary>
	/// Creates a new configuration with a generated unique ID.
	/// </summary>
	public CustomCodeEditorConfig()
	{
		Id = Guid.NewGuid().ToString( "N" )[..8];
	}

	/// <summary>
	/// Creates a deep copy of the configuration.
	/// </summary>
	public CustomCodeEditorConfig Clone()
	{
		return new CustomCodeEditorConfig
		{
			Id = Id,
			Name = Name,
			ExecutablePath = ExecutablePath,
			Arguments = Arguments,
			SolutionArguments = SolutionArguments
		};
	}

	/// <summary>
	/// Formats the file arguments template with the provided values.
	/// </summary>
	/// <param name="path">The file path to open.</param>
	/// <param name="line">The line number (defaults to 1).</param>
	/// <param name="column">The column number (defaults to 1).</param>
	/// <returns>The formatted command line arguments.</returns>
	public string FormatArguments( string path, int? line, int? column )
	{
		var sanitizedPath = SanitizePath( path );

		return Arguments
			.Replace( "{path}", sanitizedPath )
			.Replace( "{line}", (line ?? 1).ToString() )
			.Replace( "{column}", (column ?? 1).ToString() );
	}

	/// <summary>
	/// Formats the solution arguments template with the provided solution path.
	/// </summary>
	/// <param name="solution">The solution file path.</param>
	/// <returns>The formatted command line arguments.</returns>
	public string FormatSolutionArguments( string solution )
	{
		var sanitizedSolution = SanitizePath( solution );
		return SolutionArguments.Replace( "{solution}", sanitizedSolution );
	}

	/// <summary>
	/// Sanitizes a file path for use in command line arguments.
	/// Escapes quotes to prevent breaking out of quoted strings.
	/// </summary>
	private static string SanitizePath( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return "";

		return path.Replace( "\"", "\\\"" );
	}
}

/// <summary>
/// Provides argument presets for common editors.
/// Good function to keep updated with new editors.
/// </summary>
public static class EditorArgumentPresets
{
	/// <summary>
	/// Known editor argument patterns, keyed by executable name patterns.
	/// </summary>
	public static readonly Dictionary<string, (string FileArgs, string SolutionArgs)> Presets = new()
	{
		// Could probably improve on this dictionary since it seems every ecosystem keeps their own naming conventions.
		{ "code",          ("-g \"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "codium",        ("-g \"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "code-insiders", ("-g \"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "notepad++",     ("\"{path}\" -n{line} -c{column}", "\"{solution}\"") },
		{ "sublime_text",  ("\"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "subl",          ("\"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "atom",          ("\"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "vim",           ("\"{path}\" +{line}", "\"{solution}\"") },
		{ "nvim",          ("\"{path}\" +{line}", "\"{solution}\"") },
		{ "gvim",          ("\"{path}\" +{line}", "\"{solution}\"") },
		{ "emacs",         ("+{line}:{column} \"{path}\"", "\"{solution}\"") },
		{ "idea",          ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "idea64",        ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "webstorm",      ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "webstorm64",    ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "phpstorm",      ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "phpstorm64",    ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "goland",        ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "goland64",      ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "clion",         ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "clion64",       ("--line {line} --column {column} \"{path}\"", "\"{solution}\"") },
		{ "fleet",         ("\"{path}\"", "\"{solution}\"") },
		{ "zed",           ("\"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "antigravity",   ("-g \"{path}\":{line}:{column}", "\"{solution}\"") },
		{ "cursor",        ("-g \"{path}\":{line}:{column}", "\"{solution}\"") }
	};

	/// <summary>
	/// Gets the argument preset for an executable, or default arguments if no preset is found.
	/// </summary>
	/// <param name="executablePath">Path to the editor executable.</param>
	/// <returns>Tuple of file arguments, solution arguments.</returns>
	public static (string FileArgs, string SolutionArgs) GetPresetForExecutable( string executablePath )
	{
		if ( string.IsNullOrWhiteSpace( executablePath ) )
			return ("{path}", "{solution}"); // Default arguments, will just open the path.

		var fileName = System.IO.Path.GetFileNameWithoutExtension( executablePath ).ToLowerInvariant();

		foreach ( var preset in Presets )
		{
			if ( fileName.Contains( preset.Key, StringComparison.OrdinalIgnoreCase ) )
				return preset.Value;
		}

		// Default: just open the file, useful for editors with no line or column support.
		return ("{path}", "{solution}");
	}
}

/// <summary>
/// Persistent storage for custom code editor configurations.
/// Uses EditorCookie for persistence across editor sessions.
/// </summary>
public static class CustomCodeEditorStorage
{
	private const string StorageKey = "CustomCodeEditors";

	/// <summary>
	/// Gets all stored custom editor configurations.
	/// </summary>
	public static List<CustomCodeEditorConfig> GetAll()
	{
		return EditorCookie.Get( StorageKey, new List<CustomCodeEditorConfig>() );
	}

	/// <summary>
	/// Saves all configurations to EditorCookie.
	/// </summary>
	public static void SaveAll( List<CustomCodeEditorConfig> configs )
	{
		EditorCookie.Set( StorageKey, configs );
	}

	/// <summary>
	/// Adds a new configuration to EditorCookie.
	/// </summary>
	public static void Add( CustomCodeEditorConfig config )
	{
		var configs = GetAll();
		configs.Add( config );
		SaveAll( configs );
	}

	/// <summary>
	/// Updates an existing configuration in EditorCookie.
	/// </summary>
	public static void Update( CustomCodeEditorConfig config )
	{
		var configs = GetAll();
		var index = configs.FindIndex( c => c.Id == config.Id );
		if ( index >= 0 )
		{
			configs[index] = config;
			SaveAll( configs );
		}
	}

	/// <summary>
	/// Removes a configuration from EditorCookie by ID.
	/// </summary>
	public static void Remove( string id )
	{
		var configs = GetAll();
		configs.RemoveAll( c => c.Id == id );
		SaveAll( configs );
	}

	/// <summary>
	/// Gets a configuration by its unique ID.
	/// </summary>
	/// <returns>Returns the configuration, or null if not found.</returns>
	public static CustomCodeEditorConfig GetById( string id )
	{
		return GetAll().FirstOrDefault( c => c.Id == id );
	}
}
