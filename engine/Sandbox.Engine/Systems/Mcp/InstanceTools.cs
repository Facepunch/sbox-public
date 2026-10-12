using Sandbox.Engine;

namespace Editor.Mcp;

/// <summary>
/// Tools available in both games and editors. The Editor.Mcp namespace is retained for existing addons.
/// </summary>
[McpToolset( "instance", "The connected game or editor process" )]
internal static class InstanceTools
{
	/// <summary>
	/// Describe this process and the current connection's file exchange directory.
	/// </summary>
	[McpTool.ReadOnly( "instance_status" ), McpListed]
	[Description( "Get this process's PID, kind, project, engine version, active scene, play state and session exchange directory. Files in the exchange directory are retained after disconnect." )]
	internal static object Status() => new
	{
		Pid = Environment.ProcessId,
		Kind = Application.IsEditor ? "editor" : "game",
		Version = Application.Version,
		Project = Project.Current?.ConfigFilePath,
		Game = Application.GameIdent,
		ActiveScene = Sandbox.Game.ActiveScene?.Name,
		IsPlaying = Sandbox.Game.IsPlaying,
		IsLoading = IGameInstance.Current?.IsLoading ?? false,
		ExchangeDirectory = McpSession.Current?.ExchangeDirectory
	};

	/// <summary>
	/// Run a console command with the same privileges as the local engine console.
	/// </summary>
	[McpTool( "instance_console_command" )]
	[Description( "Run a local engine console command. Read the result with read_console. This can change engine state or quit the instance." )]
	internal static string ConsoleCommand( [Description( "One console command, including its arguments." )] string command )
	{
		if ( string.IsNullOrWhiteSpace( command ) )
			throw new ArgumentException( "Give a command to run." );

		ConVarSystem.RunSingle( command, allowProtected: true );
		return "Command submitted. Read output with read_console.";
	}
}
