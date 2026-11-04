namespace Sandbox.Diagnostics;

internal static class Profiler
{
	public static bool IsProfiling => _etwLogger?.IsRunning ?? false;

	private static EtwLogger _etwLogger;

	[ConCmd( "profiler", ConVarFlags.None )]
	private static void ProfileCommand()
	{
		if ( IsProfiling )
		{
			StopProfiling();
			return;
		}

		StartProfiling();
	}

	[ConCmd( "profiler_no_upload", ConVarFlags.None )]
	private static void ProfileNoUploadCommand()
	{
		if ( IsProfiling )
		{
			StopProfiling( noUpload: true );
			return;
		}

		StartProfiling( noUpload: true );
	}


	public static void StartProfiling( bool noUpload = false )
	{
		if ( IsProfiling )
		{
			Log.Warning( "Profiling session is already running." );
			return;
		}

		if ( !OperatingSystem.IsWindows() )
		{
			Log.Warning( "The profiler records with ETW, which is only available on Windows." );
			return;
		}

		Log.Info( "Starting Profiler call `profiler` again to stop." );

		if ( noUpload == false )
		{
			Log.Info( "" );
			Log.Info( "Attention:" );
			Log.Info( "The resulting profile file will be uploaded to https://profiler.firefox.com/" );
			Log.Info( "By doing so you agree to the privacy policy and terms of service of Firefox Profiler https://www.mozilla.org/en-US/about/legal/terms/mozilla/." );
			Log.Info( "If you do not wish to upload the profile, terminate the command using `profiler_no_upload` instead." );
			Log.Info( "" );
		}

		_etwLogger = new EtwLogger();
		_etwLogger.Start( noUpload );

		Log.Info( "ETW profiling session started" );
	}

	public static void StopProfiling( bool noUpload = false )
	{
		if ( !IsProfiling )
		{
			Log.Warning( "No profiling session is running." );
			return;
		}

		_etwLogger.Stop( noUpload );
		Log.Info( "ETW profiling session stopping..." );
	}
}
