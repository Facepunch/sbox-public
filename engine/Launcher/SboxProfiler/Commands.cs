using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace Sandbox;

static class Commands
{
	private static NamedPipeClientStream _pipeClient;
	private static StreamWriter _writer;
	private static StreamReader _reader;
	public static Action<string, string> OnResponse;
	private static bool _listening;
	private static readonly object _writeLock = new();

	public static void Init( string pipeName )
	{
		_pipeClient = new NamedPipeClientStream( ".", pipeName, PipeDirection.InOut );
		_pipeClient.Connect();

		_writer = new StreamWriter( _pipeClient ) { AutoFlush = true };
		_reader = new StreamReader( _pipeClient );
		_listening = true;

		// Start listening in background thread
		Task.Run( ListenForResponses );
	}

	private static void ListenForResponses()
	{
		try
		{
			while ( _listening )
			{
				var line = _reader.ReadLine();
				if ( line == null ) break;

				var split = line.IndexOf( ' ' );
				if ( split == -1 ) continue;

				var commandName = line.Substring( 0, split );
				var contents = line.Substring( split + 1 );

				// Call the response handler
				OnResponse?.Invoke( commandName, contents );
			}
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Pipe listener error: {ex.Message}" );
		}
	}

	public static void Close()
	{
		_listening = false;
		_writer?.Dispose();
		_reader?.Dispose();
		_pipeClient?.Dispose();
		_writer = null;
		_reader = null;
		_pipeClient = null;
	}

	private static void Send( string commandName, string contents )
	{
		if ( _pipeClient == null ) throw new InvalidOperationException( "Pipe not initialized. Call Init first." );

		try
		{
			lock ( _writeLock )
			{
				_writer.WriteLine( commandName + " " + contents );
				_pipeClient.Flush();
			}
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Error sending command: {ex.Message}" );
		}
	}

	public static void Log( string message )
	{
		Console.WriteLine( message );
		Send( "LOG", message );
	}

	public static void Finish( string outputFile ) => Send( "FINISH", outputFile );

	public static void Error( string message )
	{
		Console.WriteLine( message );
		Send( "ERROR", message );
	}
}
