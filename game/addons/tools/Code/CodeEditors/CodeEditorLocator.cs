using System;
using System.Diagnostics;
using System.IO;

namespace Editor.CodeEditors;

/// <summary>
/// Shared executable lookup and launching for code editors, so they all find themselves the same
/// way on Windows, macOS and Linux: whatever is on PATH first, then wherever installers put it.
/// </summary>
internal static class CodeEditorLocator
{
	/// <summary>
	/// The first candidate that exists, or null. Bare names ("code") are looked up on PATH,
	/// anything else is treated as a path, with <c>~</c> and <c>%VARS%</c> expanded. Candidates for
	/// other platforms simply won't exist, so one list covers all of them.
	/// </summary>
	public static string Find( params string[] candidates )
	{
		foreach ( var candidate in candidates )
		{
			if ( candidate.Contains( '/' ) || candidate.Contains( '\\' ) )
			{
				var path = Expand( candidate );
				if ( File.Exists( path ) ) return path;
				continue;
			}

			if ( OnPath( candidate ) is string found ) return found;
		}

		return null;
	}

	/// <summary>
	/// Starts an editor executable, if we found one. Windows batch launchers (code.cmd, rider.bat)
	/// can't be started directly, so they go via cmd.
	/// </summary>
	public static void Launch( string executable, string arguments )
	{
		if ( string.IsNullOrEmpty( executable ) )
		{
			Log.Warning( "Code editor: couldn't find the editor executable." );
			return;
		}

		if ( executable.EndsWith( ".cmd", StringComparison.OrdinalIgnoreCase ) ||
			executable.EndsWith( ".bat", StringComparison.OrdinalIgnoreCase ) )
		{
			arguments = $"/c \"\"{executable}\" {arguments}\"";
			executable = "cmd.exe";
		}

		try
		{
			Process.Start( new ProcessStartInfo
			{
				FileName = executable,
				Arguments = arguments,
				CreateNoWindow = true,
			} );
		}
		catch ( Exception e )
		{
			Log.Error( $"Code editor: failed to launch '{executable}': {e.Message}" );
		}
	}

	private static string Expand( string path )
	{
		if ( path.StartsWith( '~' ) )
			path = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ) + path[1..];

		return Environment.ExpandEnvironmentVariables( path );
	}

	private static string OnPath( string name )
	{
		var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : new[] { "" };

		foreach ( var dir in (Environment.GetEnvironmentVariable( "PATH" ) ?? "")
			.Split( Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ) )
		{
			foreach ( var extension in extensions )
			{
				var candidate = Path.Combine( dir.Trim().Trim( '"' ), name + extension );
				if ( File.Exists( candidate ) ) return candidate;
			}
		}

		return null;
	}
}
