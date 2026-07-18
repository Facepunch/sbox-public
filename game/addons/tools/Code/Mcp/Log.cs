using System;

namespace Editor.Mcp;

[McpToolset( "log", "Write messages to the editor console" )]
public static partial class LogTools
{
	/// <summary>
	/// Write text to the console with severity level information. Multi-line text becomes one console line per line.
	/// </summary>
	/// <param name="message">The text to write.</param>
	[McpTool( "log_info" )]
	public static void LogInfo( string message )
	{
		LogLines( message, x => Log.Info( x ) );
	}

	/// <summary>
	/// Write text to the console with severity level warning. Multi-line text becomes one console line per line.
	/// </summary>
	/// <param name="message">The text to write.</param>
	[McpTool( "log_warning" )]
	public static void LogWarning( string message )
	{
		LogLines( message, x => Log.Warning( x ) );
	}

	/// <summary>
	/// Write text to the console with severity level error. Multi-line text becomes one console line per line.
	/// </summary>
	/// <param name="message">The text to write.</param>
	[McpTool( "log_error" )]
	public static void LogError( string message )
	{
		LogLines( message, x => Log.Error( x ) );
	}

	private static void LogLines( string message, Action<string> log )
	{
		foreach ( var line in message.Split( '\n' ) )
		{
			log( line.TrimEnd( '\r' ) );
		}
	}
}
