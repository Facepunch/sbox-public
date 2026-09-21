using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Editor.CodeEditors;

[Title( "Rider" )]
public class Rider : ICodeEditor
{
	public void OpenFile( string path, int? line = null, int? column = null )
	{
		var solution = CodeEditor.FindSolutionFromPath( System.IO.Path.GetDirectoryName( path ) );

		var args = new StringBuilder();
		args.Append( $"\"{solution}\" " );
		if ( line is not null )
			args.Append( $"--line {line} " );
		if ( column is not null )
			args.Append( $"--column {column} " );
		args.Append( $"\"{path}\"" );

		Launch( args.ToString() );
	}

	public void OpenSolution()
	{
		Launch( $"\"{CodeEditor.AddonSolutionPath()}\"" );
	}

	public void OpenAddon( Project addon )
	{
		OpenSolution();
	}

	public bool IsInstalled() => FindRider() is not null;

	private static void Launch( string arguments ) => CodeEditorLocator.Launch( FindRider(), arguments );

	private static string RiderPath;

	private static string FindRider()
	{
		if ( RiderPath is not null )
			return RiderPath;

		// Prefer whatever the user already has open, as you can have multiple Rider installations.
		// Process.MainModule isn't supported on macOS, so only bother where it works.
		if ( !OperatingSystem.IsMacOS() )
		{
			foreach ( var p in System.Diagnostics.Process.GetProcessesByName( OperatingSystem.IsWindows() ? "rider64" : "rider" ) )
				return RiderPath = p.MainModule.FileName;
		}

		return RiderPath = CodeEditorLocator.Find(
			// Toolbox puts its launcher scripts on PATH on every platform
			"rider",
			"rider64",
			// Windows
			"%ProgramFiles%/JetBrains/JetBrains Rider/bin/rider64.exe",
			// macOS
			"/Applications/Rider.app/Contents/MacOS/rider",
			"~/Applications/Rider.app/Contents/MacOS/rider",
			// Linux
			"/opt/rider/bin/rider.sh" )
			?? FindInRegistry();
	}

	/// <summary>
	/// Toolbox installs under %LOCALAPPDATA% and doesn't reliably put Rider on PATH, so on Windows
	/// fall back to the registry - both the installer and Toolbox register rider64.exe there.
	/// ponytail: first one that's on disk wins, no version ranking.
	/// </summary>
	private static string FindInRegistry()
	{
		if ( !OperatingSystem.IsWindows() )
			return null;

		using var apps = Registry.ClassesRoot.OpenSubKey( "Applications" );

		foreach ( var name in apps?.GetSubKeyNames() ?? [] )
		{
			if ( !name.Contains( "Rider", StringComparison.OrdinalIgnoreCase ) ) continue;

			using var key = apps.OpenSubKey( $@"{name}\shell\open\command" );

			// `"C:\JetBrains Rider 2022.1.2\bin\rider64.exe" "%1"` - grab the quoted exe
			if ( key?.GetValue( null ) is string command && command.Split( '"' ) is [_, var path, ..] && File.Exists( path ) )
				return path;
		}

		return null;
	}
}
