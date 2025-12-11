using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace Sandbox.Diagnostics;

internal class EtwLogger
{
	private NamedPipeServerStream _pipeServer;
	private StreamWriter _writer;
	private StreamReader _reader;
	private bool _isRunning;

	/// <summary>
	/// Additional symbol path to use for resolving symbols
	/// </summary>
	[ConVar( "profiler_symbol_path", ConVarFlags.Saved | ConVarFlags.Protected | ConVarFlags.Hidden, Help = "Additional symbol path for resolving profile symbols" )]
	public static string SymbolPath { get; set; }

	public bool IsRunning => _isRunning;

	public void Start( bool noUpload = false )
	{
		_isRunning = true;

		var profilerExe = "sbox-profiler.exe";
		string pipeName = "sbox_profiler_pipe_" + Random.Shared.NextInt64();

		Log.Info( $"Starting profiler with pipe: {pipeName}" );

		_pipeServer = new NamedPipeServerStream( pipeName, PipeDirection.InOut, 1,
			PipeTransmissionMode.Byte, PipeOptions.Asynchronous );

		var startInfo = new ProcessStartInfo
		{
			FileName = profilerExe,
			Arguments = pipeName,
			UseShellExecute = true,
			Verb = "runas",
			WorkingDirectory = Path.Combine( Environment.CurrentDirectory, "bin", "managed" )
		};

		try
		{
			Process.Start( startInfo );
		}
		catch
		{
			CleanUp();

			throw;
		}

		_pipeServer.WaitForConnection();

		_writer = new StreamWriter( _pipeServer ) { AutoFlush = true };
		_reader = new StreamReader( _pipeServer );

		// Start a background task to listen for responses
		Task.Run( ListenForResponses );

		// Send the process ID to start profiling
		SendCommand( "PID", Process.GetCurrentProcess().Id.ToString() );

		// Send symbol path if specified
		if ( !string.IsNullOrEmpty( SymbolPath ) )
		{
			SendCommand( "SYMBOLPATH", SymbolPath );
		}

		if ( noUpload )
		{
			SendCommand( "NOUPLOAD", "" );
		}
	}

	public void Stop( bool noUpload = false )
	{
		if ( !_isRunning ) return;

		if ( noUpload )
		{
			SendCommand( "NOUPLOAD", "" );
		}

		SendCommand( "SHUTDOWN", "" );
	}

	private void SendCommand( string command, string data )
	{
		if ( !_isRunning || !_pipeServer.IsConnected ) return;

		try
		{
			_writer.WriteLine( $"{command} {data}" );
			_pipeServer.Flush();
			Log.Info( $"Sent command: {command}" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Error sending command to profiler: {ex.Message}" );
		}
	}

	private void ListenForResponses()
	{
		try
		{
			while ( _isRunning && _pipeServer.IsConnected )
			{
				try
				{
					var line = _reader.ReadLine();
					if ( line == null ) break;

					var split = line.IndexOf( ' ' );
					if ( split == -1 ) continue;

					var command = line.Substring( 0, split );
					var data = line.Substring( split + 1 );

					HandleResponse( command, data );
				}
				catch ( IOException ioEx )
				{
					// Handle pipe read timeout or disconnection
					Log.Warning( $"Pipe read interrupted: {ioEx.Message}" );
					break;
				}
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"Error reading from profiler pipe: {ex.Message}" );
		}
		finally
		{
			CleanUp();
		}
	}

	private void HandleResponse( string command, string data )
	{
		switch ( command )
		{
			case "LOG":
				Log.Info( $"[ETW Profiler] {data}" );
				break;

			case "FINISH":
				ProcessProfilerOutput( data );
				break;
		}
	}

	private void ProcessProfilerOutput( string results )
	{
		try
		{
			var splitResults = results.Split( ';' );

			var etlFilePath = splitResults[0];
			var firefoxJsonFilePath = splitResults[1];
			var url = splitResults.Length > 2 ? splitResults[2] : null;

			_isRunning = false;

			Log.Info( "Profiling finished." );
			Log.Info( "" );
			Log.Info( "You can open the ETL with perfview, Windows Performance Analyzer (WPA) or Superluminal." );
			Log.Info( $"ETL File: {etlFilePath}" );
			Log.Info( "" );
			if ( !string.IsNullOrEmpty( url ) )
			{
				Log.Info( "You can also view the profile online." );
				Log.Info( $"Profile URL: {url}" );
				Log.Info( "Opening in browser..." );
				OpenUrl( url );
				Log.Info( "" );
			}
			else
			{
				Log.Info( "You can open the Firefox Profiler JSON with https://profiler.firefox.com/" );
				Log.Info( $"Firefox Profiler JSON File: {firefoxJsonFilePath}" );
			}
		}
		finally
		{
			CleanUp();
		}
	}

	private static void OpenUrl( string url )
	{
		try
		{
			// Simpler pattern for opening URLs
			Process.Start( new ProcessStartInfo( url ) { UseShellExecute = true } );
		}
		catch
		{
			Log.Info( $"Could not open browser automatically. Please visit: {url}" );
		}
	}

	private void CleanUp()
	{
		_isRunning = false;

		_writer?.Dispose();
		_reader?.Dispose();
		_pipeServer?.Dispose();

		_writer = null;
		_reader = null;
		_pipeServer = null;
	}
}
