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

	public bool MatchesExecutable( string fileName )
	{
		return fileName.Equals( "rider64.exe", StringComparison.OrdinalIgnoreCase ) ||
			   fileName.Equals( "rider.exe", StringComparison.OrdinalIgnoreCase );
	}

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

		// check manual override cookie
		var cookiePath = EditorCookie.Get( "CodeEditor.Rider.Path", "" );
		if ( !string.IsNullOrEmpty( cookiePath ) && System.IO.File.Exists( cookiePath ) )
		{
			RiderPath = cookiePath;
			return RiderPath;
		}

		// Always use whatever the user has open first as you can have multiple rider installations
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
							riderPathsDict.Add( riderName.Split( ' ' ).Skip( 1 ).Aggregate( ( s, s1 ) => s + " " + s1 ), riderPath );
					}
				}
			}

			// Convert version to a number so it can be ranked
			// Prefers highest major
			// Then prefers normal release, Early Access Program version and lastly Nightly
			//YYYYMMPPEE
			//2024.3.5 - 2024030500
			//2024.3.6 - 2024030600
			//2024.3 Nigthly - 2024030000
			//2025.1 EAP7 - 2025010007

			if ( riderPathsDict.Count > 1 )
			{
				var rankedRiderPathsDict = riderPathsDict.OrderByDescending( pair =>
				{
					var year = uint.Parse( pair.Key.Split( '.' )[0] );
					uint minor;
					if ( !pair.Key.Contains( "Nightly" ) && !pair.Key.Contains( "EAP" ) )
						minor = uint.Parse( pair.Key.Split( '.' )[1] );
					else
						minor = uint.Parse( pair.Key.Split( '.' )[1].Split( " " )[0] );
					var patch = pair.Key.Contains( "Nightly" ) || pair.Key.Contains( "EAP" )
						? 00
						: uint.Parse( pair.Key.Split( '.' )[2] );
					var eap = pair.Key.Contains( "EAP" )
						? uint.Parse( pair.Key.Split( '.' )[1].Split( " " )[1].Replace( "EAP", "" ) )
						: 00;

					ulong final = year * 1000000 + minor * 10000 + patch * 100 + eap;

					return final;
				} );

				value = rankedRiderPathsDict.First().Value;
			}
			else
				value = riderPathsDict.Count != 0 ? riderPathsDict.First().Value : null;
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
}
