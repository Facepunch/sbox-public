using Sandbox.Mcp.Transport;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Sandbox.Cli;

internal static class Program
{
	const string Help = """
		sbox-cli --connect [pid|project]    Attach to a running editor, or a game started with -mcp
		sbox-cli --project [project]        Attach to the project's editor, or open and own one
		sbox-cli --game <project|ident>     Start a game and own its lifetime
		sbox-cli --call <tool> [json]       Call one tool on a running instance, print the result, exit
		sbox-cli --list                     Print running instances as JSON and exit

		sbox-cli --new <title>              Create a project in a new folder named after its ident
		         [--template <name>]        Template to start from (default: the launcher's first)
		         [--in <folder>]            Where to create it (default: the current directory)
		sbox-cli --templates                List the project templates as JSON

		sbox-cli --skill [name[/file]]      List the agent skills for this installation, or print one
		sbox-cli --skill-install [target]   Install the s&box bootstrap skill for your agents
		sbox-cli --skill-uninstall [target] Remove it again
		sbox-cli --skill-status             Show where it's installed, as JSON

		Skill targets: claude (~/.claude/skills), agents (~/.agents/skills, used by Codex and
		others). Without one, install covers the agents found on this machine and uninstall
		covers all. The bootstrap tells agents to read --skill, so it never goes out of date;
		only copies carrying its marker are replaced or removed.

		A project is a .sbproj file or its directory. Without one, --connect, --project and --call
		use the project containing the current directory; outside any project, --connect and --call
		attach to the only running instance. --call can be combined with --connect <pid|project>.

		Options: --engine <game-directory>, --idle-timeout <seconds> (600),
		         --startup-timeout <seconds> (180), --request-timeout <seconds> (300)

		--call prints structured results as JSON and text as-is, and saves images to files whose
		paths it prints. Exit code 0 on success, 2 when the tool reports an error, 1 otherwise.

		Connection modes speak MCP: one UTF-8 JSON-RPC message per line on stdin/stdout.
		Send initialize, notifications/initialized, tools/list, then tools/call.
		Initialization returns instructions and a session directory for exchanging files.
		Diagnostics go to stderr. Closing stdin closes the connection and any owned instance.
		Editors accept connections unless disabled in Project Settings > AI.
		""";

	// Output is for people and agents to read, so keep paths like s&box unescaped.
	internal static readonly JsonSerializerOptions Readable = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	static async Task<int> Main( string[] args )
	{
		try
		{
			var options = Options.Parse( args );
			if ( options.Mode == "--help" )
			{
				Console.WriteLine( Help );
				Console.WriteLine( $"\nAgent skill: {Path.Combine( AppContext.BaseDirectory, "skills", "sbox-cli", "SKILL.md" )}" );
				return 0;
			}

			switch ( options.Mode )
			{
				case "--skill": return Skills.Print( options.Target );
				case "--skill-status": return Skills.Status();
				case "--skill-install": return Skills.Install( options.Target );
				case "--skill-uninstall": return Skills.Uninstall( options.Target );
				case "--new": return Projects.Create( options );
				case "--templates": return Projects.ListTemplates( options );
			}

			if ( options.Mode == "--list" )
			{
				var instances = new JsonArray( (await FindInstances()).ToArray() );
				Console.WriteLine( instances.ToJsonString( Readable ) );
				return 0;
			}

			using var lifetime = new CancellationTokenSource();
			Console.CancelKeyPress += ( _, e ) => { e.Cancel = true; lifetime.Cancel(); };

			if ( options.CallTool is not null )
				return await Call( options, lifetime.Token );

			var messages = Channel.CreateBounded<string>( 32 );
			var input = ReadInput();
			var connection = Connect( options, messages.Reader, lifetime.Token );
			var finished = await Task.WhenAny( input, connection );
			await lifetime.CancelAsync();

			try
			{
				await finished;
			}
			finally
			{
				// In particular, wait for Connect's finally to dispose its owned process job.
				try { await connection; } catch ( OperationCanceledException ) when ( lifetime.IsCancellationRequested ) { }
			}

			return 0;

			async Task ReadInput()
			{
				using var stdin = Console.OpenStandardInput();
				var wire = new McpWire( stdin );
				while ( await wire.ReadAsync( lifetime.Token ) is { } line )
				{
					if ( !messages.Writer.TryWrite( line ) )
						throw new IOException( "Too many queued requests (maximum 32)." );
				}
			}
		}
		catch ( OperationCanceledException )
		{
			return 0;
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( $"sbox-cli: {e.Message}" );
			return 1;
		}
	}

	static async Task Connect( Options options, ChannelReader<string> messages, CancellationToken cancellation )
	{
		OwnedProcess owned = null;
		Process process = null;
		NamedPipeClientStream pipe = null;

		try
		{
			cancellation.ThrowIfCancellationRequested();
			if ( options.Mode == "--connect" )
			{
				process = await FindTarget( options );
			}
			else if ( options.Mode == "--project" && await FindRunning( options.Target, required: false ) is { } running )
			{
				// Never open a second editor on a project that's already open; attach to it instead.
				process = running;
			}
			else
			{
				var executable = Path.Combine( options.EngineDirectory, options.Mode == "--project" ? "sbox-dev.exe" : "sbox.exe" );
				if ( !File.Exists( executable ) )
					throw new FileNotFoundException( "Engine executable not found. Use --engine to specify the game directory.", executable );

				string[] arguments = options.Mode == "--project"
					? ["-mcp", "-project", options.Target]
					: ["-mcp", "+game", options.Target];
				owned = new OwnedProcess( executable, options.EngineDirectory, arguments );
				process = owned.Process;
			}

			Console.Error.WriteLine( $"sbox-cli: connecting to PID {process.Id} ({(owned is null ? "attached" : "owned")})." );
			pipe = await OpenPipe( process, options, cancellation );
			Console.Error.WriteLine( "sbox-cli: connected. Send MCP initialize for tools and session instructions; close stdin to disconnect." );
			await Relay( pipe, messages, options, cancellation );
		}
		finally
		{
			if ( owned is not null )
			{
				// Best effort graceful quit, followed by the job's unconditional process-tree cleanup.
				try
				{
					using var shutdown = new CancellationTokenSource( TimeSpan.FromSeconds( 2 ) );
					if ( pipe?.IsConnected == true && !process.HasExited )
					{
						await McpWire.WriteAsync( pipe, """{"jsonrpc":"2.0","id":"sbox-cli-shutdown","method":"tools/call","params":{"name":"instance_console_command","arguments":{"command":"quit"}}}""", shutdown.Token );
						await process.WaitForExitAsync( shutdown.Token );
					}
				}
				catch ( Exception ) { }

			}
			owned?.Dispose();
			process?.Dispose();
			pipe?.Dispose();
		}
	}

	/// <summary>
	/// Connect to a process's MCP pipe, waiting for it to come up if the process is still starting.
	/// </summary>
	static async Task<NamedPipeClientStream> OpenPipe( Process process, Options options, CancellationToken cancellation )
	{
		var pipe = CreatePipe( process );
		using var startup = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
		startup.CancelAfter( options.StartupTimeout );
		var connecting = pipe.ConnectAsync( startup.Token );
		var exited = process.WaitForExitAsync( startup.Token );

		try
		{
			if ( await Task.WhenAny( connecting, exited ) == exited )
			{
				await exited;
				throw new IOException( "The target exited before its MCP endpoint became ready." );
			}

			await connecting;
			VerifyServer( pipe, process.Id );
			return pipe;
		}
		catch ( Exception e )
		{
			pipe.Dispose();
			if ( e is OperationCanceledException && !cancellation.IsCancellationRequested )
				throw new TimeoutException( "Timed out waiting for the MCP endpoint. Enable MCP Communication in Project Settings > AI, or start the game with -mcp. Check the engine logs for startup failures." );

			throw;
		}
		finally
		{
			await startup.CancelAsync();
		}
	}

	/// <summary>
	/// The process --connect names: an explicit PID, the editor for a project, or the only instance running.
	/// </summary>
	static async Task<Process> FindTarget( Options options ) => options.Pid is { } pid
		? Process.GetProcessById( pid )
		: await FindRunning( options.Target, required: true );

	/// <summary>
	/// Find the running instance for a project, or the only running instance when project is null.
	/// Never falls back to a different project's instance.
	/// </summary>
	static async Task<Process> FindRunning( string project, bool required )
	{
		var instances = await FindInstances();
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var matches = project is null ? instances : instances
			.Where( x => x["project"]?.GetValue<string>() is { } path && string.Equals( Path.GetFullPath( path ), project, comparison ) )
			.ToList();

		if ( matches.Count == 1 )
			return Process.GetProcessById( matches[0]["pid"].GetValue<int>() );

		if ( matches.Count > 1 )
		{
			var candidates = string.Join( Environment.NewLine, matches.Select( x => $"  {x["pid"]} {x["kind"]} {x["project"]}" ) );
			throw new IOException( $"Several instances match. Pass a PID from --list:{Environment.NewLine}{candidates}" );
		}

		if ( !required )
			return null;

		throw new IOException( project is null
			? "No running instance accepts connections. Open a project in the editor, or start a game with -mcp."
			: $"No running editor has {project} open. Open it in the editor, or use --project to launch one." );
	}

	/// <summary>
	/// Ask every s&amp;box process for its MCP identity. Processes without a reachable pipe are skipped.
	/// </summary>
	static async Task<List<JsonObject>> FindInstances()
	{
		var instances = new List<JsonObject>();
		foreach ( var process in Process.GetProcesses() )
		{
			using ( process )
			{
				try
				{
					if ( !process.ProcessName.StartsWith( "sbox", StringComparison.OrdinalIgnoreCase ) || process.ProcessName == "sbox-cli" )
						continue;

					using var timeout = new CancellationTokenSource( 300 );
					using var pipe = CreatePipe( process );
					await pipe.ConnectAsync( timeout.Token );
					VerifyServer( pipe, process.Id );
					await McpWire.WriteAsync( pipe, """{"jsonrpc":"2.0","id":1,"method":"sbox/instance"}""", timeout.Token );
					var response = await new McpWire( pipe ).ReadAsync( timeout.Token );
					if ( JsonNode.Parse( response )?["result"] is JsonObject instance )
						instances.Add( (JsonObject)instance.DeepClone() );
				}
				catch ( Exception )
				{
					// Exited, inaccessible, not enabled or an older engine build.
				}
			}
		}

		return instances;
	}

	/// <summary>
	/// One-shot tool call for shells and scripts: handshake, call, print the result, disconnect.
	/// The attached instance keeps running.
	/// </summary>
	static async Task<int> Call( Options options, CancellationToken cancellation )
	{
		using var process = await FindTarget( options );
		using var pipe = await OpenPipe( process, options, cancellation );
		var wire = new McpWire( pipe, 64 * 1024 * 1024 );

		var initialize = await Request( 1, "initialize", new JsonObject
		{
			["protocolVersion"] = "2025-11-25",
			["capabilities"] = new JsonObject(),
			["clientInfo"] = new JsonObject { ["name"] = "sbox-cli --call", ["version"] = "1" }
		} );
		var exchange = initialize?["_meta"]?["exchangeDirectory"]?.GetValue<string>();
		await McpWire.WriteAsync( pipe, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", cancellation );

		var result = await Request( 2, "tools/call", new JsonObject { ["name"] = options.CallTool, ["arguments"] = options.CallArguments } );

		var isError = result?["isError"]?.GetValue<bool>() == true;
		var structured = isError ? null : result?["structuredContent"];
		using var output = new StreamWriter( isError ? Console.OpenStandardError() : Console.OpenStandardOutput(), new UTF8Encoding( false ) );

		if ( structured is not null )
			output.WriteLine( structured.ToJsonString( Readable ) );

		var images = 0;
		foreach ( var block in result?["content"]?.AsArray() ?? [] )
		{
			switch ( block?["type"]?.GetValue<string>() )
			{
				// Structured results repeat themselves as text.
				case "text" when structured is null:
					output.WriteLine( block["text"]?.GetValue<string>() );
					break;
				case "text":
					break;
				case "image":
					output.WriteLine( SaveImage( block, exchange, options.CallTool, ++images ) );
					break;
				default:
					output.WriteLine( block?.ToJsonString() );
					break;
			}
		}

		// Initialization creates the session directory; don't leave an empty one behind for every call.
		try
		{
			if ( exchange is not null && Directory.Exists( exchange ) && !Directory.EnumerateFileSystemEntries( exchange ).Any() )
				Directory.Delete( exchange );
		}
		catch ( IOException ) { }

		return isError ? 2 : 0;

		async Task<JsonNode> Request( int id, string method, JsonObject parameters )
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
			timeout.CancelAfter( options.RequestTimeout );
			var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };

			try
			{
				await McpWire.WriteAsync( pipe, message.ToJsonString(), timeout.Token );
				while ( await wire.ReadAsync( timeout.Token ) is { } line )
				{
					var response = JsonNode.Parse( line );
					var responseId = response?["id"];
					if ( response?["error"] is { } error && (responseId is null || responseId.GetValueKind() == JsonValueKind.Null || responseId.ToJsonString() == id.ToString()) )
						throw new IOException( $"{method} failed: {error["message"]}" );

					if ( responseId?.ToJsonString() == id.ToString() )
						return response["result"];
				}
			}
			catch ( OperationCanceledException ) when ( !cancellation.IsCancellationRequested )
			{
				throw new TimeoutException( $"{method} exceeded --request-timeout. The outcome of an already running tool is unknown; inspect state before retrying." );
			}

			throw new IOException( "The instance closed its MCP connection." );
		}
	}

	/// <summary>
	/// Write an image content block to the session directory and return its path.
	/// </summary>
	static string SaveImage( JsonNode block, string directory, string tool, int index )
	{
		directory ??= Path.Combine( Path.GetTempPath(), "sbox-cli" );
		Directory.CreateDirectory( directory );

		var extension = block["mimeType"]?.GetValue<string>() is "image/jpeg" ? "jpg" : "png";
		var path = Path.Combine( directory, $"{tool}-{DateTime.Now:yyyyMMdd-HHmmss}-{index}.{extension}" );
		File.WriteAllBytes( path, Convert.FromBase64String( block["data"]?.GetValue<string>() ?? "" ) );
		return path;
	}

	static NamedPipeClientStream CreatePipe( Process process ) => new( ".",
		McpWire.PipeName( process.Id, process.StartTime.ToUniversalTime().Ticks ), PipeDirection.InOut,
		PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly );

	static void VerifyServer( NamedPipeClientStream pipe, int pid )
	{
		if ( OperatingSystem.IsWindows() && (!GetNamedPipeServerProcessId( pipe.SafePipeHandle, out var serverPid ) || serverPid != pid) )
			throw new IOException( "The named pipe does not belong to the requested process." );
	}

	[DllImport( "kernel32.dll", SetLastError = true )]
	static extern bool GetNamedPipeServerProcessId( SafePipeHandle pipe, out int serverPid );

	static async Task Relay( NamedPipeClientStream pipe, ChannelReader<string> messages, Options options, CancellationToken cancellation )
	{
		using var stop = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
		var token = stop.Token;
		var pending = new ConcurrentDictionary<string, long>();
		long lastActivity = Environment.TickCount64;
		var input = Send();
		var output = Receive();
		var watchdog = Watchdog();

		try
		{
			await await Task.WhenAny( input, output, watchdog );
		}
		finally
		{
			await stop.CancelAsync();
			// Observe all tasks and finish pending pipe writes before graceful shutdown writes.
			try { await Task.WhenAll( input, output, watchdog ); } catch ( OperationCanceledException ) { }
		}

		async Task Send()
		{
			await foreach ( var line in messages.ReadAllAsync( token ) )
			{
				Interlocked.Exchange( ref lastActivity, Environment.TickCount64 );
				if ( RequestId( line ) is { } id )
				{
					if ( pending.Count >= 32 || !pending.TryAdd( id, Environment.TickCount64 ) )
						throw new InvalidDataException( "Too many outstanding requests or a duplicate request ID." );
				}

				await McpWire.WriteAsync( pipe, line, token );
			}
		}

		async Task Receive()
		{
			// Responses may contain screenshots, so allow a larger limit than requests.
			var wire = new McpWire( pipe, 64 * 1024 * 1024 );
			using var stdout = Console.OpenStandardOutput();
			while ( await wire.ReadAsync( token ) is { } line )
			{
				if ( RequestId( line ) is { } id && pending.TryRemove( id, out _ ) )
					Interlocked.Exchange( ref lastActivity, Environment.TickCount64 );

				await McpWire.WriteAsync( stdout, line, token );
			}

			throw new IOException( "The instance closed its MCP connection." );
		}

		async Task Watchdog()
		{
			while ( true )
			{
				await Task.Delay( 250, token );
				var now = Environment.TickCount64;
				if ( pending.Any( x => now - x.Value >= options.RequestTimeout.TotalMilliseconds ) )
					throw new TimeoutException( "A request exceeded --request-timeout. Disconnecting; the outcome of an already running tool may be unknown. Do not blindly retry mutations." );

				if ( pending.IsEmpty && now - Interlocked.Read( ref lastActivity ) >= options.IdleTimeout.TotalMilliseconds )
					throw new TimeoutException( "No requests received within --idle-timeout. Disconnecting." );
			}
		}
	}

	static string RequestId( string line )
	{
		try { return JsonNode.Parse( line )?["id"]?.ToJsonString(); }
		catch ( Exception ) { return null; }
	}
}
