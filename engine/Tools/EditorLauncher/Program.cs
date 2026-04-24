using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SboxEditorLauncher;

/// <summary>
/// A dumb little program for launching the s&box editor through it's own Steam App.
/// It'll make sure s&box is actually installed, find it and run sbox-dev.exe
/// Passing through any command line args properly etc.
/// </summary>
static partial class Program
{
	const uint SboxAppId = 590830;
	const string SboxDevExe = "sbox-dev.exe";

	[STAThread]
	static int Main()
	{
		var steamPath = FindSteamPath();
		if ( steamPath is null )
		{
			ShowError( "Steam is not installed.\n\nPlease install Steam and try again." );
			return 1;
		}

		var sboxPath = FindSboxInstallPath( steamPath );
		if ( sboxPath is null )
		{
			// s&box not installed — trigger Steam install
			try
			{
				Process.Start( new ProcessStartInfo
				{
					FileName = $"steam://install/{SboxAppId}",
					UseShellExecute = true
				} );
			}
			catch
			{
				// Ignore — Steam might not be running
			}

			return 1;
		}

		var devExePath = Path.Combine( sboxPath, SboxDevExe );
		if ( !File.Exists( devExePath ) )
		{
			ShowError( $"Could not find {SboxDevExe} in:\n{sboxPath}\n\nPlease verify the s&box installation in Steam." );
			return 1;
		}

		return LaunchEditor( devExePath, sboxPath );
	}

	static int LaunchEditor( string exePath, string workingDirectory )
	{
		var args = Environment.GetCommandLineArgs().Skip( 1 ).ToArray();

		var startInfo = new ProcessStartInfo
		{
			FileName = exePath,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false
		};

		foreach ( var arg in args )
			startInfo.ArgumentList.Add( arg );

		try
		{
			var process = Process.Start( startInfo );
			if ( process is null )
			{
				ShowError( "Failed to start the s&box editor." );
				return 1;
			}

			process.WaitForExit();
			return process.ExitCode;
		}
		catch ( Exception ex )
		{
			ShowError( $"Failed to start the s&box editor:\n{ex.Message}" );
			return 1;
		}
	}

	/// <summary>
	/// Finds the Steam installation path by checking the registry.
	/// Checks HKCU first (most common), then HKLM as fallback.
	/// </summary>
	static string FindSteamPath()
	{
		// HKCU is the most reliable source for modern Steam installs
		var path = GetRegistryString( Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath" );
		if ( IsValidSteamPath( path ) )
			return NormalizePath( path );

		path = GetRegistryString( Registry.CurrentUser, @"Software\Valve\Steam", "InstallPath" );
		if ( IsValidSteamPath( path ) )
			return NormalizePath( path );

		// HKLM fallback (32-bit registry view)
		using var hklm32 = RegistryKey.OpenBaseKey( RegistryHive.LocalMachine, RegistryView.Registry32 );
		path = GetRegistryString( hklm32, @"SOFTWARE\Valve\Steam", "InstallPath" );
		if ( IsValidSteamPath( path ) )
			return NormalizePath( path );

		return null;
	}

	/// <summary>
	/// Finds the s&box installation path by scanning Steam library folders.
	/// </summary>
	static string FindSboxInstallPath( string steamPath )
	{
		var libraryFoldersPath = Path.Combine( steamPath, "steamapps", "libraryfolders.vdf" );
		if ( !File.Exists( libraryFoldersPath ) )
			return null;

		var libraryPaths = ParseLibraryFolders( libraryFoldersPath );

		// Also check the default Steam library
		var defaultLibrary = Path.Combine( steamPath, "steamapps" );
		if ( !libraryPaths.Contains( defaultLibrary, StringComparer.OrdinalIgnoreCase ) )
			libraryPaths.Insert( 0, defaultLibrary );

		foreach ( var library in libraryPaths )
		{
			var manifestPath = Path.Combine( library, $"appmanifest_{SboxAppId}.acf" );
			if ( !File.Exists( manifestPath ) )
				continue;

			var installDir = ParseAppManifestInstallDir( manifestPath );
			if ( installDir is null )
				continue;

			var fullPath = Path.Combine( library, "common", installDir );
			if ( Directory.Exists( fullPath ) )
				return fullPath;
		}

		return null;
	}

	/// <summary>
	/// Parses libraryfolders.vdf to extract all Steam library folder paths.
	/// Handles both old format ("1" "D:\\path") and new format ("1" { "path" "D:\\path" }).
	/// </summary>
	static List<string> ParseLibraryFolders( string filePath )
	{
		var paths = new List<string>();
		var content = File.ReadAllText( filePath );
		var tokens = TokenizeVdf( content );

		// Walk the token stream looking for library entries
		for ( int i = 0; i < tokens.Count - 1; i++ )
		{
			// Skip non-numeric keys at the top level
			if ( !int.TryParse( tokens[i], out _ ) )
				continue;

			var next = tokens[i + 1];

			// Old format: "0" "C:\\Program Files\\Steam"
			if ( next != "{" )
			{
				var unescaped = UnescapeVdfString( next );
				if ( Directory.Exists( Path.Combine( unescaped, "steamapps" ) ) )
					paths.Add( Path.Combine( unescaped, "steamapps" ) );
				continue;
			}

			// New format: "0" { "path" "C:\\Program Files\\Steam" ... }
			int braceDepth = 0;
			for ( int j = i + 1; j < tokens.Count - 1; j++ )
			{
				if ( tokens[j] == "{" ) braceDepth++;
				if ( tokens[j] == "}" ) braceDepth--;
				if ( braceDepth <= 0 ) break;

				if ( tokens[j].Equals( "path", StringComparison.OrdinalIgnoreCase ) && j + 1 < tokens.Count )
				{
					var pathValue = UnescapeVdfString( tokens[j + 1] );
					if ( Directory.Exists( Path.Combine( pathValue, "steamapps" ) ) )
						paths.Add( Path.Combine( pathValue, "steamapps" ) );
					break;
				}
			}
		}

		return paths;
	}

	/// <summary>
	/// Parses an appmanifest ACF file to extract the installdir value.
	/// </summary>
	static string ParseAppManifestInstallDir( string filePath )
	{
		var content = File.ReadAllText( filePath );
		var tokens = TokenizeVdf( content );

		for ( int i = 0; i < tokens.Count - 1; i++ )
		{
			if ( tokens[i].Equals( "installdir", StringComparison.OrdinalIgnoreCase ) )
				return UnescapeVdfString( tokens[i + 1] );
		}

		return null;
	}

	/// <summary>
	/// Tokenizes a VDF/KeyValues file into a list of string tokens.
	/// Handles quoted strings (with escape sequences) and braces.
	/// </summary>
	static List<string> TokenizeVdf( string content )
	{
		var tokens = new List<string>();
		int i = 0;

		while ( i < content.Length )
		{
			char c = content[i];

			// Skip whitespace
			if ( char.IsWhiteSpace( c ) )
			{
				i++;
				continue;
			}

			// Skip line comments
			if ( c == '/' && i + 1 < content.Length && content[i + 1] == '/' )
			{
				while ( i < content.Length && content[i] != '\n' )
					i++;
				continue;
			}

			// Braces
			if ( c is '{' or '}' )
			{
				tokens.Add( c.ToString() );
				i++;
				continue;
			}

			// Quoted string
			if ( c == '"' )
			{
				i++;
				var chars = new List<char>();

				while ( i < content.Length && content[i] != '"' )
				{
					if ( content[i] == '\\' && i + 1 < content.Length )
					{
						chars.Add( content[i + 1] );
						i += 2;
					}
					else
					{
						chars.Add( content[i] );
						i++;
					}
				}

				tokens.Add( new string( chars.ToArray() ) );
				if ( i < content.Length ) i++; // skip closing quote
				continue;
			}

			// Unquoted token
			var tokenStart = i;
			while ( i < content.Length && !char.IsWhiteSpace( content[i] ) && content[i] != '"' && content[i] is not '{' and not '}' )
				i++;

			tokens.Add( content[tokenStart..i] );
		}

		return tokens;
	}

	static string UnescapeVdfString( string value )
	{
		return value.Replace( "\\\\", "\\" ).Replace( "\\\"", "\"" );
	}

	static string GetRegistryString( RegistryKey baseKey, string subKeyPath, string valueName )
	{
		try
		{
			using var key = baseKey.OpenSubKey( subKeyPath );
			return key?.GetValue( valueName ) as string;
		}
		catch
		{
			return null;
		}
	}

	static bool IsValidSteamPath( string path )
	{
		return !string.IsNullOrEmpty( path ) && Directory.Exists( path );
	}

	static string NormalizePath( string path )
	{
		return path.Replace( '/', '\\' );
	}

	static void ShowError( string message )
	{
		MessageBoxW( IntPtr.Zero, message, "s&box Editor", 0x00000010 /* MB_ICONERROR */ );
	}

	[LibraryImport( "user32.dll", StringMarshalling = StringMarshalling.Utf16 )]
	private static partial int MessageBoxW( IntPtr hWnd, string text, string caption, uint type );
}
