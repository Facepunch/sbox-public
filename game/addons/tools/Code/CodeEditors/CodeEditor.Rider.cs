using System;
using System.Text;
using System.Text.RegularExpressions;
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

	public bool IsInstalled() => !string.IsNullOrEmpty( FindRider() );

	private static void Launch( string arguments )
	{
		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			CreateNoWindow = true,
			Arguments = arguments,
			FileName = FindRider()
		};

		System.Diagnostics.Process.Start( startInfo );
	}

	private static string RiderPath;

	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>" )]
	private static string FindRider()
	{
		if ( RiderPath != null )
		{
			return RiderPath;
		}

		// Always use whatever the user has open first as you can have multiple Rider installations
		foreach ( var p in System.Diagnostics.Process.GetProcessesByName( "rider64" ) )
		{
			RiderPath = p.MainModule.FileName;
			return RiderPath;
		}

		string value = null;
		using ( var key = Registry.ClassesRoot.OpenSubKey( @"Applications\\rider64.exe\\shell\\open\\command" ) )
		{
			value = key?.GetValue( "" ) as string;
		}

		if ( value == null )
		{
			var riderPathsDict = new Dictionary<string, string>();

			using ( var appsSubKey = Registry.ClassesRoot.OpenSubKey( "Applications" ) )
			{
				var riderKeyNames = appsSubKey?.GetSubKeyNames().Where( name => name.StartsWith( "Toolbox.Rider." ) );
				if ( riderKeyNames != null )
				{
					foreach ( var riderKeyName in riderKeyNames )
					{
						using var riderKey = appsSubKey.OpenSubKey( riderKeyName + @"\shell\open" );
						using var commandKey = riderKey?.OpenSubKey( "command" );

						var riderName = riderKey?.GetValue( "FriendlyAppName" ) as string;
						var riderPath = commandKey?.GetValue( null ) as string;

						if ( riderName != null && riderPath != null )
							riderPathsDict[riderName] = riderPath;
					}
				}
			}

			value = riderPathsDict
				.OrderByDescending( pair => GetVersionRank( pair.Key ) )
				.Select( pair => pair.Value )
				.FirstOrDefault();
		}

		if ( value == null )
		{
			return null;
		}

		// Given `"C:\Program Files\JetBrains\JetBrains Rider 2022.1.2\bin\rider64.exe" "%1"` grab the first bit
		Regex rgx = new Regex( "\"(.*)\" \".*\"", RegexOptions.IgnoreCase );
		var matches = rgx.Matches( value );
		if ( matches.Count == 0 || matches[0].Groups.Count < 2 )
		{
			return null;
		}

		RiderPath = matches[0].Groups[1].Value;
		return RiderPath;
	}

	private static (uint Year, uint Minor, uint Channel, uint Patch, uint Eap) GetVersionRank( string name )
	{
		var versionMatch = Regex.Match( name, @"(?<!\d)(?<year>\d{4})\.(?<minor>\d+)(?:\.(?<patch>\d+))?", RegexOptions.IgnoreCase );
		if ( !versionMatch.Success ||
			!uint.TryParse( versionMatch.Groups["year"].Value, out var year ) ||
			!uint.TryParse( versionMatch.Groups["minor"].Value, out var minor ) )
		{
			return default;
		}

		uint.TryParse( versionMatch.Groups["patch"].Value, out var patch );

		var isEap = name.Contains( "EAP", StringComparison.OrdinalIgnoreCase );
		var isNightly = name.Contains( "Nightly", StringComparison.OrdinalIgnoreCase );
		var channel = isEap ? 1u : isNightly ? 0u : 2u;

		var eapMatch = Regex.Match( name, @"\bEAP\s*(?<number>\d+)", RegexOptions.IgnoreCase );
		uint.TryParse( eapMatch.Groups["number"].Value, out var eap );

		return (year, minor, channel, patch, eap);
	}
}
