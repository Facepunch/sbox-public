using Sandbox.Mcp.Transport;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Sandbox.Cli;

internal static class Program
{
	const string Help = """
		sbox-cli --project <path>       Open a project in a visible editor and own its lifetime
		sbox-cli --game <path|ident>    Start a game and own its lifetime
		sbox-cli --connect <pid>        Attach to an existing editor or MCP-enabled game
		sbox-cli --list                 Print connected instances as JSON and exit

		Options: --engine <game-directory>, --idle-timeout <seconds> (600),
		         --startup-timeout <seconds> (180), --request-timeout <seconds> (300)

		Connection modes speak MCP: one UTF-8 JSON-RPC message per line on stdin/stdout.
		Send initialize, notifications/initialized, tools/list, then tools/call.
		Initialization returns instructions and a session directory for exchanging files.
		Diagnostics go to stderr. Closing stdin closes the connection and any owned instance.
		An existing game must have been launched with -mcp. Editors follow Project Settings > AI.
		""";

	static async Task<int> Main( string[] args )
	{
		try
		{
			var options = Options.Parse( args );
			if ( options.Mode == "--help" )
			{
				Console.WriteLine( Help );
				Console.WriteLine( $"\nUsage guide: {Path.Combine( AppContext.BaseDirectory, "sbox-cli.md" )}" );
				return 0;
			}

			if ( options.Mode == "--list" )
			{
				await ListInstances();
				return 0;
			}

			using var lifetime = new CancellationTokenSource();
			Console.CancelKeyPress += ( _, e ) => { e.Cancel = true; lifetime.Cancel(); };
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
				process = Process.GetProcessById( int.Parse( options.Target ) );
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
			pipe = CreatePipe( process );
			using ( var startup = CancellationTokenSource.CreateLinkedTokenSource( cancellation ) )
			{
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
				}
				catch ( OperationCanceledException ) when ( !cancellation.IsCancellationRequested )
				{
					throw new TimeoutException( "Timed out waiting for the MCP endpoint. Enable MCP Communication in Project Settings > AI, or start the game with -mcp. Check the engine logs for startup failures." );
				}
				finally
				{
					await startup.CancelAsync();
				}
			}

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

	static async Task ListInstances()
	{
		var instances = new JsonArray();
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
					if ( JsonNode.Parse( response )?["result"] is { } instance )
						instances.Add( instance.DeepClone() );
				}
				catch ( Exception )
				{
					// Exited, inaccessible, not enabled or an older engine build.
				}
			}
		}

		Console.WriteLine( instances.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
	}
}
