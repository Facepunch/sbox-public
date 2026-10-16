using Sandbox.Mcp.Transport;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;

namespace Editor.Mcp;

/// <summary>
/// Each engine process has a local, same-user MCP endpoint independent of the editor HTTP port.
/// The process start time in its name prevents attaching to a reused PID.
/// </summary>
internal static class McpPipeServer
{
	static CancellationTokenSource lifetime;

	internal static void Start()
	{
		if ( lifetime is not null )
			return;

		lifetime = new CancellationTokenSource();
		var cancellation = lifetime.Token;
		LogBuffer.StartCapture();
		_ = Task.Run( () => Listen( cancellation ) );
	}

	internal static void Stop()
	{
		var stopping = Interlocked.Exchange( ref lifetime, null );
		stopping?.Cancel();
		stopping?.Dispose();
	}

	static async Task Listen( CancellationToken cancellation )
	{
		using var process = Process.GetCurrentProcess();
		using var stopping = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
		cancellation = stopping.Token;
		var name = McpWire.PipeName( process.Id, process.StartTime.ToUniversalTime().Ticks );
		var connections = new List<Task>();

		try
		{
			while ( !cancellation.IsCancellationRequested )
			{
				connections.RemoveAll( connection => connection.IsCompleted );
				if ( connections.Count >= 16 )
				{
					await Task.WhenAny( connections ).WaitAsync( cancellation );
					continue;
				}

				var connected = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
				connections.Add( Serve( name, connected, cancellation ) );
				await connected.Task;
			}
		}
		catch ( OperationCanceledException ) { }
		catch ( Exception e )
		{
			Log.Warning( $"MCP pipe listener stopped: {e.Message}" );
		}
		finally
		{
			// Keep connection resources alive until their pending work has stopped.
			await stopping.CancelAsync();
			await Task.WhenAll( connections );
		}
	}

	static async Task Serve( string name, TaskCompletionSource connected, CancellationToken lifetime )
	{
		using ( var disconnected = CancellationTokenSource.CreateLinkedTokenSource( lifetime ) )
		{
			var token = disconnected.Token;
			try
			{
				using var pipe = new NamedPipeServerStream( name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
					PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly );

				// Disabling MCP closes the pipe even if a running tool ignores cancellation.
				using var closePipe = token.Register( pipe.Dispose );
				await pipe.WaitForConnectionAsync( token );
				connected.SetResult();

				var session = new McpSession( token );
				var messages = Channel.CreateBounded<string>( 32 );
				var reader = Read();
				var writer = Dispatch();
				await Task.WhenAny( reader, writer );
				await disconnected.CancelAsync();
				await Task.WhenAll( reader, writer );

				async Task Read()
				{
					var wire = new McpWire( pipe );
					while ( await wire.ReadAsync( token ) is { } line )
					{
						// Don't let a client flood unbounded work onto the engine's main thread.
						if ( !messages.Writer.TryWrite( line ) )
							throw new IOException( "Too many queued MCP requests." );
					}
				}

				async Task Dispatch()
				{
					try
					{
						using var scope = McpSession.Enter( session );
						await foreach ( var line in messages.Reader.ReadAllAsync( token ) )
						{
							var result = await McpProtocol.HandleLine( line );
							if ( result is not null )
								await McpWire.WriteAsync( pipe, JsonSerializer.Serialize( result ), token );
						}
					}
					catch ( OperationCanceledException ) when ( token.IsCancellationRequested ) { }
					catch ( IOException ) { }
					catch ( ObjectDisposedException ) when ( token.IsCancellationRequested ) { }
					catch ( Exception e )
					{
						Log.Warning( $"MCP dispatch stopped: {e.Message}" );
					}
				}
			}
			catch ( OperationCanceledException )
			{
				connected.TrySetCanceled();
			}
			catch ( IOException e )
			{
				connected.TrySetException( e );
			}
			catch ( ObjectDisposedException ) when ( token.IsCancellationRequested )
			{
				connected.TrySetCanceled();
			}
			catch ( Exception e )
			{
				connected.TrySetException( e );
				Log.Warning( $"MCP connection closed: {e.Message}" );
			}
			finally
			{
				await disconnected.CancelAsync();
			}
		}
	}
}
