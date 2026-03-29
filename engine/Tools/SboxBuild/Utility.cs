using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Facepunch;

internal static class Utility
{
	public static bool RunDotnetCommand( string workingDirectory, string arguments )
	{
		return RunProcess( "dotnet", arguments, workingDirectory );
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

	/// <summary>
	/// Returns true if the PR touches native C++ code or interop definitions,
	/// meaning a full native rebuild is required.
	/// Falls back to true (safe default) when not in a PR context or if the diff fails.
	/// </summary>
	public static bool PrTouchesNativeCode()
	{
		var baseRef = Environment.GetEnvironmentVariable( "GITHUB_BASE_REF" );
		if ( string.IsNullOrEmpty( baseRef ) )
		{
			Log.Info( "GITHUB_BASE_REF not set; assuming native code is touched." );
			return true;
		}

		var changedFiles = new List<string>();

		// In shallow CI checkouts origin/{baseRef} won't exist until we fetch it.
		// Fetch enough history to find the merge-base; a depth of 50 is sufficient for
		// typical PR branch lengths while keeping the fetch fast.
		RunProcess( "git", $"fetch --depth=128 origin {baseRef}" );

		// Try to compute the merge-base (the true fork point of this PR against the base
		// branch). Diffing HEAD against the merge-base avoids spurious "changes" when the
		// PR branch is behind the base branch — git diff FETCH_HEAD HEAD would include all
		// commits that exist on the base but not on the PR branch, incorrectly forcing a
		// full native rebuild.
		var mergeBaseLines = new List<string>();
		var mergeBaseSuccess = RunProcess(
			"git",
			"merge-base FETCH_HEAD HEAD",
			onDataReceived: ( _, e ) =>
			{
				if ( !string.IsNullOrWhiteSpace( e.Data ) )
					mergeBaseLines.Add( e.Data.Trim() );
			} );

		string diffTarget;
		if ( mergeBaseSuccess && mergeBaseLines.Count > 0 )
		{
			diffTarget = mergeBaseLines[0]; // SHA of the merge-base commit
		}
		else
		{
			// No merge-base found (very shallow clone or unrelated histories).
			// Fall back to FETCH_HEAD — same as the previous behaviour; safe but may
			// over-report changes.
			Log.Warning( "Could not determine merge-base; falling back to FETCH_HEAD for diff." );
			diffTarget = "FETCH_HEAD";
		}

		var success = RunProcess(
			"git",
			$"diff --name-only {diffTarget} HEAD",
			onDataReceived: ( _, e ) =>
			{
				if ( !string.IsNullOrWhiteSpace( e.Data ) )
					changedFiles.Add( e.Data.Trim() );
			} );

		if ( !success )
		{
			Log.Warning( "git diff failed; assuming native code is touched." );
			return true;
		}

		var touchesNative = changedFiles.Any( f =>
			f.StartsWith( "src/", StringComparison.OrdinalIgnoreCase ) ||
			f.StartsWith( "engine/Definitions/", StringComparison.OrdinalIgnoreCase ) );

		if ( touchesNative )
		{
			Log.Info( "PR touches native code or interop definitions; full native build required." );
		}
		else
		{
			Log.Info( $"PR does not touch native code ({changedFiles.Count} file(s) changed); public artifacts will be used." );
		}

		return touchesNative;
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
