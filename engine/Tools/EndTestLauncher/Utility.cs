using System.Diagnostics;

namespace Facepunch.EndTestLauncher;

internal static class Utility
{
	/// <summary>
	/// Runs an external process with standard output/error redirection and logging.
	/// </summary>
	/// <param name="executablePath">Path to the executable</param>
	/// <param name="arguments">Command line arguments</param>
	/// <param name="workingDirectory">Working directory for the process</param>
	/// <param name="timeoutMs">Timeout in milliseconds (0 = no timeout)</param>
	/// <param name="successExitCode">Exit code that indicates success (default 0)</param>
	/// <returns>True if the process exited with success code, false otherwise</returns>
	public static bool RunProcess(
		string executablePath,
		string arguments = "",
		string workingDirectory = null,
		int timeoutMs = 0,
		int successExitCode = 0 )
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
			process.StartInfo.WorkingDirectory = workingDirectory;
		}

		// Copy environment variables from the current process
		foreach ( System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables() )
		{
			if ( entry.Value is null || entry.Value is not string strValue )
				continue;

			process.StartInfo.EnvironmentVariables[(string)entry.Key] = strValue;
		}

		process.OutputDataReceived += ( sender, e ) =>
		{
			if ( e.Data != null )
			{
				Log.Info( e.Data );
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
}
