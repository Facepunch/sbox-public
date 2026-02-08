using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Editor.CodeEditors;

/// <summary></summary>
public abstract class VSCodeBase : ICodeEditor
{
	public abstract string RegistryKey { get; }
	public virtual bool IsPrimary => false;

	public void OpenFile( string path, int? line, int? column )
	{
		var sln = CodeEditor.FindSolutionFromPath( Path.GetDirectoryName( path ) );
		var rootPath = Path.GetDirectoryName( sln );

		var args = $"-r -g \"{path}";

		if ( line.HasValue )
		{
			args += $":{line.Value}";

			if ( column.HasValue )
			{
				args += $":{column.Value}";
			}
		}

		args += $"\" \"{rootPath}\"";

		Launch( args );
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

	private void Launch( string arguments )
	{
		var location = GetLocation();
		if ( string.IsNullOrEmpty( location ) )
		{
			Log.Warning( $"[CodeEditor] Could not find installation for {GetType().Name} (Key: {RegistryKey})" );
			return;
		}

		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			FileName = location,
			Arguments = arguments,
			// CreateNoWindow avoids spawning a console window for GUI processes; do not set WindowStyle.Hidden
			// so the editor UI can appear or be reused normally.
			CreateNoWindow = true
		};

		try
		{
			System.Diagnostics.Process.Start( startInfo );
		}
		catch ( System.Exception e )
		{
			Log.Error( e, $"[CodeEditor] Failed to launch {GetType().Name}" );
		}
	}

	// Static cache to avoid hitting the registry on every frame/widget update
	private static readonly Dictionary<string, string> _cachedLocations = new Dictionary<string, string>();
	private static readonly Regex _commandRegex = new Regex( "\"([^\"]+)\"", RegexOptions.IgnoreCase );

	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Interoperability", "CA1416:Validate platform compatibility", Justification = "Windows Registry required" )]
	private string GetLocation()
	{
		// check for a manual override cookie first
		var cookiePath = EditorCookie.Get( $"CodeEditor.{GetType().Name}.Path", "" );
		if ( !string.IsNullOrEmpty( cookiePath ) && File.Exists( cookiePath ) )
		{
			return cookiePath;
		}

		// check static cache first
		if ( _cachedLocations.TryGetValue( RegistryKey, out var cached ) )
			return cached;

		string value = null;
		try
		{
			using ( var key = Registry.ClassesRoot.OpenSubKey( $@"Applications\\{RegistryKey}\\shell\\open\\command" ) )
			{
				value = key?.GetValue( "" ) as string;
			}

			// fallback: check "app paths" which is standard for many apps in hklm
			if ( string.IsNullOrEmpty( value ) )
			{
				using ( var key = Registry.LocalMachine.OpenSubKey( $@"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{RegistryKey}" ) )
				{
					value = key?.GetValue( "" ) as string;
				}
			}

			// fallback 2: check "app paths" in hkcu (per-user installs)
			if ( string.IsNullOrEmpty( value ) )
			{
				using ( var key = Registry.CurrentUser.OpenSubKey( $@"Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{RegistryKey}" ) )
				{
					value = key?.GetValue( "" ) as string;
				}
			}
		}
		catch
		{
			// registry access failed (permissions, etc.)
			// we continue to see if we found a value before the exception
		}

		if ( string.IsNullOrEmpty( value ) )
			return null;

		// extracts the executable path from the registry command string
		var match = _commandRegex.Match( value );
		// Use a conditional expression to clearly express the two possible values
		string path = match.Success ? match.Groups[1].Value : value;

		// validate that the file actually exists
		if ( !File.Exists( path ) )
			return null;

		_cachedLocations[RegistryKey] = path;
		return path;
	}

	public bool MatchesExecutable( string fileName )
	{
		return string.Equals( RegistryKey, fileName, StringComparison.OrdinalIgnoreCase );
	}
}

[Title( "Visual Studio Code" )]
public class VisualStudioCode : VSCodeBase
{
	public override string RegistryKey => "Code.exe";
	public override bool IsPrimary => true;
}

[Title( "VS Code Insiders" )]
public class VSCodeInsidersEditor : VSCodeBase
{
	public override string RegistryKey => "Code - Insiders.exe";
}

[Title( "Cursor" )]
public class CursorEditor : VSCodeBase
{
	public override string RegistryKey => "Cursor.exe";
}

[Title( "Windsurf" )]
public class WindsurfEditor : VSCodeBase
{
	public override string RegistryKey => "Windsurf.exe";
}

[Title( "Trae" )]
public class TraeEditor : VSCodeBase
{
	public override string RegistryKey => "Trae.exe";
}

[Title( "VSCodium" )]
public class VSCodiumEditor : VSCodeBase
{
	public override string RegistryKey => "codium.exe";
}

[Title( "VSCodium (Alt)" )]
public class VSCodiumEditorAlt : VSCodeBase
{
	public override string RegistryKey => "VSCodium.exe";
}

[Title( "Void" )]
public class VoidEditor : VSCodeBase
{
	public override string RegistryKey => "Void.exe";
}

[Title( "PearAI" )]
public class PearAIEditor : VSCodeBase
{
	public override string RegistryKey => "PearAI.exe";
}

[Title( "Antigravity" )]
public class AntigravityEditor : VSCodeBase
{
	public override string RegistryKey => "Antigravity.exe";
}
