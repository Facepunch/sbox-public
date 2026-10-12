using System.IO;
using System.Text.Json.Nodes;
using System.Threading;

namespace Editor.Mcp;

/// <summary>
/// Connection-local context flows through async tools, including calls queued on the main thread.
/// </summary>
internal sealed class McpSession
{
	static readonly AsyncLocal<McpSession> current = new();

	internal static McpSession Current => current.Value;
	internal CancellationToken Cancellation { get; }
	string exchangeDirectory;
	internal string ExchangeDirectory
	{
		get
		{
			if ( exchangeDirectory is null )
			{
				exchangeDirectory = Path.Combine( Path.GetTempPath(), "sbox-cli", Guid.NewGuid().ToString( "N" ) );
				Directory.CreateDirectory( exchangeDirectory );
			}

			return exchangeDirectory;
		}
	}

	internal McpSession( CancellationToken cancellation )
	{
		Cancellation = cancellation;
	}

	internal static IDisposable Enter( McpSession session ) => new Scope( session );

	internal static string Instructions => Current is { } session
		? $"\n\nConnected to process {Environment.ProcessId}. Session exchange directory: {session.ExchangeDirectory}. Use this absolute directory for scripts, screenshots and other file exchange. Files are retained after disconnect; copy useful outputs to your project and remove the directory when finished. Close stdin to disconnect. A CLI-owned instance exits with the CLI; an attached instance stays open."
		: "";

	internal static JsonObject Metadata => new()
	{
		["pid"] = Environment.ProcessId,
		["kind"] = Application.IsEditor ? "editor" : "game",
		["project"] = Project.Current?.ConfigFilePath,
		["exchangeDirectory"] = Current?.exchangeDirectory
	};

	sealed class Scope : IDisposable
	{
		readonly McpSession previous = current.Value;

		internal Scope( McpSession session )
		{
			current.Value = session;
		}

		/// <summary>
		/// Restore the caller's connection context.
		/// </summary>
		public void Dispose() => current.Value = previous;
	}
}
