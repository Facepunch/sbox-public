using System;
using System.Linq;
using System.Threading.Tasks;

namespace MenuProject.Modals;

/// <summary>
/// Detects whether known screen recording/streaming software is currently running
/// </summary>
internal static class RecordingSoftware
{
	/// <summary>
	/// Process names of common apps. These are windows process names
	/// </summary>
	static readonly string[] KnownProcessNames =
	{
		"obs64", "obs32", "obs",                // OBS Studio
		"Streamlabs OBS", "Streamlabs Desktop", // Streamlabs
		"XSplit.Core", "XSplit.Broadcaster",    // XSplit
		"TwitchStudio", "Twitch Studio",        // Twitch Studio
	};

	/// <summary>
	/// Returns true if any known screen recording/streaming application is currently running
	/// </summary>
	public static Task<bool> IsRunningAsync()
	{
		return Task.Run( IsRunning );
	}

	/// <summary>
	/// Returns true if any known screen recording/streaming application is currently running
	/// </summary>
	public static bool IsRunning()
	{
		try
		{
			foreach ( var process in System.Diagnostics.Process.GetProcesses() )
			{
				using ( process )
				{
					string name;

					// Accessing ProcessName can throw for exited/protected processes, so we skip those
					try { name = process.ProcessName; }
					catch { continue; }

					if ( KnownProcessNames.Contains( name, StringComparer.OrdinalIgnoreCase ) )
						return true;
				}
			}
		}
		catch ( Exception )
		{
			// This can fail on some systems, so we just assume nothing is running instead of throwing an error
		}

		return false;
	}
}
