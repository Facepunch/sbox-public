using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Facepunch;

internal static class Utility
{
	public static bool RunDotnetCommand( string workingDirectory, string arguments, Dictionary<string, string> environmentVariables = null )
	{
		return RunProcess( "dotnet", arguments, workingDirectory, environmentVariables: environmentVariables );
	}

	/// <summary>
	/// Runs an external process with standard output/error redirection and logging.
	/// </summary>
	/// <param name="executablePath">Path to the executable</param>
	/// <param name="arguments">Command line arguments</param>
	/// <param name="workingDirectory">Working directory for the process</param>
	/// <param name="waitForInput">If true, will pause and wait for user input after process completes</param>
	/// <param name="successExitCode">Exit code that indicates success (default 0)</param>
	/// <returns>True if the process exited with success code, false otherwise</returns>
	public static bool RunProcess(
	   string executablePath,
	   string arguments = "",
	   string workingDirectory = null,
	   Dictionary<string, string> environmentVariables = null,
	   int timeoutMs = 0,
	   int successExitCode = 0,
	   DataReceivedEventHandler onDataReceived = null )
	{
		using Process process = new Process();

		process.StartInfo.FileName = executablePath;
		process.StartInfo.Arguments = arguments;
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.RedirectStandardOutput = true;
		process.StartInfo.RedirectStandardError = true;
		process.StartInfo.CreateNoWindow = false;

		if ( !string.IsNullOrEmpty( workingDirectory ) )
		{
			process.StartInfo.WorkingDirectory = Path.Combine( Directory.GetCurrentDirectory(), workingDirectory );
		}

		// Copy environment variables from the current process
		foreach ( DictionaryEntry entry in Environment.GetEnvironmentVariables() )
		{
			if ( entry.Value is null || entry.Value is not string strValue )
				continue;

			process.StartInfo.EnvironmentVariables[(string)entry.Key] = strValue;
		}

		if ( environmentVariables != null )
		{
			foreach ( var envVar in environmentVariables )
			{
				process.StartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
			}
		}

		process.OutputDataReceived += ( sender, e ) =>
		{
			if ( e.Data != null )
			{
				if ( onDataReceived != null )
				{
					onDataReceived( sender, e );
				}
				else
				{
					Log.Info( e.Data );
				}
			}
		};

		process.ErrorDataReceived += ( sender, e ) =>
		{
			if ( e.Data != null )
			{
				Log.Error( e.Data );
			}
		};

		process.Start();

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		// Wait for process with optional timeout
		bool exited;
		if ( timeoutMs > 0 )
		{
			exited = process.WaitForExit( timeoutMs );
			if ( !exited )
			{
				Log.Error( $"Process timed out after {timeoutMs}ms" );
				try { process.Kill(); } catch { }
				return false;
			}
		}
		else
		{
			process.WaitForExit();
			exited = true;
		}

		bool success = exited && process.ExitCode == successExitCode;

		if ( !success )
		{
			Log.Error( $"Process failed with exit code: {process.ExitCode}" );
		}

		Log.Info( "" );

		return success;
	}

	/// <summary>
	/// Gets the version name from GitHub environment variables
	/// </summary>
	/// <returns>Version name string</returns>
	public static string VersionName()
	{
		var versionHash = Environment.GetEnvironmentVariable( "GITHUB_SHA" ) ?? "";
		versionHash = versionHash[..Math.Min( versionHash.Length, 7 )];

		var versionName = $"{DateTime.Now:yy.MM.dd}-{versionHash}";

		// tagged release, prefer tag name
		if ( Environment.GetEnvironmentVariable( "GITHUB_REF" )?.StartsWith( "refs/tags/" ) == true )
		{
			versionName = Environment.GetEnvironmentVariable( "GITHUB_REF_NAME" ) ?? "";
		}

		return versionName;
	}

	public static bool IsCi()
	{
		return Environment.GetEnvironmentVariable( "GITHUB_ACTIONS" ) != null;
	}

	public static string CalculateSha256( string filePath )
	{
		using var sha256 = SHA256.Create();
		using var stream = File.OpenRead( filePath );
		var hash = sha256.ComputeHash( stream );
		return Convert.ToHexString( hash ).ToLowerInvariant();
	}

	public static string FormatSize( long bytes )
	{
		if ( bytes <= 0 )
		{
			return "0 B";
		}

		string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
		var size = (double)bytes;
		var unitIndex = 0;

		while ( size >= 1024 && unitIndex < units.Length - 1 )
		{
			size /= 1024;
			unitIndex++;
		}

		return $"{size:0.##} {units[unitIndex]}";
	}
}
