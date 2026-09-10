using System;

namespace Editor.CodeEditors;

/// <summary>
/// A user-configurable code editor that launches any executable with customizable argument templates.
/// Argument templates support the following placeholders:
/// <list type="bullet">
/// <item><c>{file}</c> — Full path to the file being opened</item>
/// <item><c>{line}</c> — Line number (defaults to 1)</item>
/// <item><c>{column}</c> — Column number (defaults to 1)</item>
/// <item><c>{solution}</c> — Path to the solution file</item>
/// <item><c>{project_root}</c> — Root path of the current project</item>
/// </list>
/// </summary>
[Title( "Custom" )]
[Order( 999 )]
public class CustomCodeEditor : ICodeEditor
{
	private const string CookiePrefix = "CustomCodeEditor";
	internal const string ExecutablePathKey = $"{CookiePrefix}.ExecutablePath";
	internal const string OpenFileArgsKey = $"{CookiePrefix}.OpenFileArgs";
	internal const string OpenSolutionArgsKey = $"{CookiePrefix}.OpenSolutionArgs";

	internal const string DefaultOpenFileArgs = "{file}";
	internal const string DefaultOpenSolutionArgs = "{solution}";

	/// <summary>
	/// Returns a friendly name derived from the executable filename.
	/// e.g. "C:\Program Files\Notepad++\notepad++.exe" → "notepad++"
	/// </summary>
	public string Title
	{
		get
		{
			var executablePath = EditorCookie.GetString( ExecutablePathKey, null );
			if ( string.IsNullOrWhiteSpace( executablePath ) )
				return "Custom";

			var fileName = System.IO.Path.GetFileNameWithoutExtension( executablePath );
			if ( string.IsNullOrWhiteSpace( fileName ) )
				return "Custom";

			return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase( fileName.ToLower() );
		}
	}

	/// <inheritdoc/>
	public void OpenFile( string path, int? line = null, int? column = null )
	{
		var executablePath = EditorCookie.GetString( ExecutablePathKey, null );
		if ( string.IsNullOrWhiteSpace( executablePath ) )
		{
			Log.Warning( "Custom code editor: No executable path configured." );
			return;
		}

		string solution = null;
		try
		{
			solution = CodeEditor.FindSolutionFromPath( System.IO.Path.GetDirectoryName( path ) );
		}
		catch { }

		var argsTemplate = EditorCookie.GetString( OpenFileArgsKey, DefaultOpenFileArgs );
		var arguments = ReplaceTokens( argsTemplate, path, line, column, solution );

		Launch( executablePath, arguments );
	}

	/// <inheritdoc/>
	public void OpenSolution()
	{
		var executablePath = EditorCookie.GetString( ExecutablePathKey, null );
		if ( string.IsNullOrWhiteSpace( executablePath ) )
		{
			Log.Warning( "Custom code editor: No executable path configured." );
			return;
		}

		var argsTemplate = EditorCookie.GetString( OpenSolutionArgsKey, DefaultOpenSolutionArgs );
		var arguments = ReplaceTokens( argsTemplate, null, null, null, CodeEditor.AddonSolutionPath() );

		Launch( executablePath, arguments );
	}

	/// <inheritdoc/>
	public void OpenAddon( Project addon )
	{
		OpenSolution();
	}

	/// <inheritdoc/>
	public bool IsInstalled() => true;

	private static string ReplaceTokens( string template, string file = null, int? line = null, int? column = null, string solution = null, string projectRoot = null )
	{
		if ( string.IsNullOrWhiteSpace( template ) )
			return string.Empty;

		projectRoot ??= Project.Current?.GetRootPath() ?? "";

		return template
			.Replace( "{file}", Quote( file ) )
			.Replace( "{line}", (line ?? 1).ToString() )
			.Replace( "{column}", (column ?? 1).ToString() )
			.Replace( "{solution}", Quote( solution ) )
			.Replace( "{project_root}", Quote( projectRoot ) );
	}

	/// <summary>
	/// Wraps a path in quotes if it needs it, so args templates don't have to quote path tokens themselves.
	/// </summary>
	private static string Quote( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return "";

		return value.Contains( ' ' ) ? $"\"{value}\"" : value;
	}

	private static void Launch( string executablePath, string arguments )
	{
		try
		{
			var startInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = executablePath,
				Arguments = arguments,
				CreateNoWindow = true,
			};

			System.Diagnostics.Process.Start( startInfo );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Custom code editor: Failed to launch '{executablePath}': {ex.Message}" );
		}
	}
}
