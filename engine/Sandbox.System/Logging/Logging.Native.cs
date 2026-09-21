namespace Sandbox.Diagnostics;

internal static partial class Logging
{
	static Logger nativeLogger;
	static string nativePartial = "";

	internal static void PrintNative( int severity, string logger, string message )
	{
		nativePartial += message;

		var newline = nativePartial.LastIndexOf( '\n' );
		if ( newline < 0 ) return;
		message = nativePartial[..newline].TrimEnd( '\n', '\r' );
		nativePartial = nativePartial[(newline + 1)..];
		NLog.LogLevel level = severity switch
		{
			<= 1 => NLog.LogLevel.Info,
			<= 3 => NLog.LogLevel.Warn,
			4 => NLog.LogLevel.Error,
			5 => NLog.LogLevel.Fatal,
			_ => NLog.LogLevel.Info,
		};

		var logName = $"engine/{logger}";
		nativeLogger ??= GetLogger( "Native" );
		nativeLogger.WriteToTargets( level, null, $"{message}", logName );
	}
}
