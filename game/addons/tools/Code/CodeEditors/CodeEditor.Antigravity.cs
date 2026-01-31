using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Editor.CodeEditors;

[Title( "Antigravity IDE" )]
public class Antigravity : ICodeEditor
{
	public void OpenFile( string path, int? line, int? column )
	{
		var sln = CodeEditor.FindSolutionFromPath( System.IO.Path.GetDirectoryName( path ) );
		var rootPath = Path.GetDirectoryName( sln );

		Launch( $"-r -g \"{path}:{line}:{column}\" \"{rootPath}\"" );
	}

	public void OpenSolution()
	{
		Launch( $"\"{Project.Current.GetRootPath()}\"" );
	}

	public void OpenAddon( Project addon )
	{
		var projectPath = (addon != null) ? addon.GetRootPath() : "";

		Launch( $"\"{projectPath}\"" );
	}

	public bool IsInstalled() => !string.IsNullOrEmpty( GetLocation() );

	private static void Launch( string arguments )
	{
		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			FileName = GetLocation(),
			Arguments = arguments,
			CreateNoWindow = true,
		};

		System.Diagnostics.Process.Start( startInfo );
	}

	static string Location;

	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>" )]
	private static string GetLocation()
	{
		if ( Location != null )
		{
			return Location;
		}

		string value = null;
		using ( var key = Registry.ClassesRoot.OpenSubKey( @"Applications\\Antigravity.exe\\shell\\open\\command" ) )
		{
			value = key?.GetValue( "" ) as string;
		}

		if ( value == null )
		{
			return null;
		}

		// Given `"%LocalAppData%\Programs\Antigravity\Antigravity.exe" "%1"` grab the first bit
		Regex rgx = new Regex( "\"(.*)\" \".*\"", RegexOptions.IgnoreCase );
		var matches = rgx.Matches( value );
		if ( matches.Count == 0 || matches[0].Groups.Count < 2 )
		{
			return null;
		}

		Location = matches[0].Groups[1].Value;
		return Location;
	}
}
