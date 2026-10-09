using System;

namespace Editor.CodeEditors;

[Title( "Visual Studio" )]
public class VisualStudio : ICodeEditor
{
	public void OpenFile( string path, int? line, int? column )
	{
		var sln = CodeEditor.FindSolutionFromPath( System.IO.Path.GetDirectoryName( path ) );
		Launch( $"\"{sln}\" \"{path}\" \"{line ?? 1}\"" ); // TODO: vsopen doesn't do column but it's an easy add
	}

	public void OpenSolution()
	{
		Launch( $"\"{CodeEditor.AddonSolutionPath()}\"" );
	}

	public void OpenAddon( Project addon )
	{
		OpenSolution();
	}

	public bool IsInstalled() => !string.IsNullOrEmpty( FindVisualStudio() );

	/// <summary>
	/// Uses vsopen to open the file in a currently running instance of Visual Studio.
	/// Failing that it will launch it.
	/// </summary>
	private static void Launch( string arguments )
	{
		CodeEditorLocator.Launch( $"{Environment.CurrentDirectory}/bin/win64/vsopen.exe", $"\"{FindVisualStudio()}\" {arguments}" );
	}

	static string VisualStudioPath;

	/// <summary>
	/// Uses vswhere (https://github.com/microsoft/vswhere) to find where Visual Studio is installed.
	/// This will return the most latest version, as well as one with .NET SDK installed.
	/// </summary>
	/// <returns>The full installation path of devenv.exe, or null when there isn't one.</returns>
	static string FindVisualStudio()
	{
		// Windows only, and vswhere.exe only ships in the win64 bin
		if ( !OperatingSystem.IsWindows() )
			return null;

		if ( VisualStudioPath != null )
		{
			return VisualStudioPath;
		}

		// Always use the same one that's already open
		foreach ( var p in System.Diagnostics.Process.GetProcessesByName( "devenv" ) )
		{
			VisualStudioPath = p.MainModule.FileName;
			return VisualStudioPath;
		}

		// Otherwise use vswhere to find the latest one with .NET installed (prerelease is valid too)
		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			CreateNoWindow = true,
			Arguments = "-latest -prerelease -requires Microsoft.NetCore.Component.SDK -property productPath",
			FileName = $"{Environment.CurrentDirectory}/bin/win64/vswhere.exe",
			RedirectStandardOutput = true
		};

		var process = System.Diagnostics.Process.Start( startInfo );
		VisualStudioPath = process.StandardOutput.ReadToEnd().Trim();
		return VisualStudioPath;
	}
}
