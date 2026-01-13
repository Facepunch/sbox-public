using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox;

class Launcher
{
	private static TraceEventSession _kernelSession;
	private static TraceEventSession _userSession;
	private static int _targetPid;
	private static ManualResetEventSlim _shutdownEvent = new ManualResetEventSlim( false );

	private static string _baseEtlFileName;
	private static string _kernelEtlFileName;
	private static string _rundownEtlFileName;
	private static string _additionalSymbolPath;

	private static bool _noUpload = false;

	private static void Init( int targetPid )
	{
		_targetPid = targetPid;
		Commands.Log( $"Target PID: {_targetPid}" );

		string baseName = $"../../profiler_captures/sbox_{DateTime.Now:yyyy-MM-dd_HH_mm_ss}";
		Directory.CreateDirectory( Path.GetDirectoryName( baseName ) );
		_baseEtlFileName = $"{baseName}.etl";
		_kernelEtlFileName = $"{baseName}.kernel.etl";
		_rundownEtlFileName = $"{baseName}.clrRundown.etl";

		float cpuSampleIntervalMs = 0.2f;
		int circularBufferMB = 0;
		bool stackCompression = false;

		_kernelSession = new TraceEventSession( _kernelEtlFileName, _kernelEtlFileName );
		_kernelSession.CircularBufferMB = circularBufferMB;
		_kernelSession.CpuSampleIntervalMSec = cpuSampleIntervalMs;
		_kernelSession.StackCompression = stackCompression;

		//// Filter user provider for the target process
		var userProviderOptions = new TraceEventProviderOptions
		{
			StacksEnabled = true,
		};

		userProviderOptions.ProcessIDFilter = new List<int>
		{
			_targetPid
		};

		_userSession = new TraceEventSession( _baseEtlFileName, _baseEtlFileName );
		_userSession.CircularBufferMB = circularBufferMB;
		_userSession.CpuSampleIntervalMSec = cpuSampleIntervalMs;

		var kernelEvents = KernelTraceEventParser.Keywords.Profile
							| KernelTraceEventParser.Keywords.ContextSwitch
							| KernelTraceEventParser.Keywords.ImageLoad
							| KernelTraceEventParser.Keywords.Process
							| KernelTraceEventParser.Keywords.Thread;

		_kernelSession.EnableKernelProvider( kernelEvents, KernelTraceEventParser.Keywords.Profile );
		Commands.Log( $"Kernel ETW session started. Outputting to {_kernelEtlFileName}." );

		// Superluminal events
		_userSession.EnableProvider( "PerformanceAPI", TraceEventLevel.Verbose, ulong.MaxValue, userProviderOptions );

		var jitEvents = ClrTraceEventParser.Keywords.JITSymbols |
						ClrTraceEventParser.Keywords.Exception |
						ClrTraceEventParser.Keywords.GC |
						ClrTraceEventParser.Keywords.GCHeapAndTypeNames |
						ClrTraceEventParser.Keywords.Interop |
						ClrTraceEventParser.Keywords.Binder |
						ClrTraceEventParser.Keywords.Stack;

		_userSession.EnableProvider( ClrTraceEventParser.ProviderGuid, TraceEventLevel.Verbose, (ulong)jitEvents, options: userProviderOptions );

		Commands.Log( $"User ETW session started. Outputting to {_baseEtlFileName}." );
	}

	private static void Shutdown()
	{
		try
		{
			// Stop the sessions and cleanup
			_kernelSession?.Stop();
			_userSession?.Stop();

			_kernelSession?.Dispose();
			_userSession?.Dispose();

			Commands.Log( "Sessions stopped." );

			Commands.Log( "Triggering coreclr rundown..." );

			var rundownProviderOptions = new TraceEventProviderOptions
			{
				StacksEnabled = true,
			};

			rundownProviderOptions.ProcessIDFilter = new List<int>
			{
				_targetPid
			};

			using ( var rundownSession = new TraceEventSession( _rundownEtlFileName, _rundownEtlFileName ) )
			{
				rundownSession.StopOnDispose = true;
				rundownSession.CircularBufferMB = 0;
				var keywords = (ulong)ClrRundownTraceEventParser.Keywords.ForceEndRundown + (ulong)ClrRundownTraceEventParser.Keywords.Jit + (ulong)ClrRundownTraceEventParser.Keywords.SupressNGen + (ulong)ClrRundownTraceEventParser.Keywords.JittedMethodILToNativeMap + (ulong)ClrRundownTraceEventParser.Keywords.Loader + (ulong)ClrRundownTraceEventParser.Keywords.Stack;
				rundownSession.EnableProvider( ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose, keywords, rundownProviderOptions );
				// Poll until time goes by without growth.  
				for ( var prevLength = new FileInfo( _rundownEtlFileName ).Length; ; )
				{
					Thread.Sleep( 500 );
					var newLength = new FileInfo( _rundownEtlFileName ).Length;
					if ( newLength == prevLength ) break;
					prevLength = newLength;
				}
			}

			Commands.Log( "coreclr rundown complete." );

			Commands.Log( "Merging etl files, this may take a while..." );

			TraceEventSession.MergeInPlace( _baseEtlFileName, null );

			Commands.Log( "Merge complete." );

			Commands.Log( "Processing to Firefox Profiler format..." );

			var firefoxFile = SaveFirefoxProfile( _baseEtlFileName, _targetPid, _additionalSymbolPath );

			var firefoxUrl = UploadFirefoxProfile( firefoxFile );

			Commands.Log( "Profiler finished." );

			var result = $"{Path.GetFullPath( _baseEtlFileName )};{Path.GetFullPath( firefoxFile )}";
			if ( firefoxUrl != null ) result += $";{firefoxUrl}";

			Commands.Finish( result );
		}
		finally
		{
			// Signal the main thread to exit
			_shutdownEvent.Set();
		}
	}

	private static string SaveFirefoxProfile( string etlFileName, int targetPid, string symbolPath )
	{
		// Convert ETW data to Firefox profile format
		var profile = EtwConverterToFirefox.Convert( etlFileName, [targetPid], symbolPath );

		// Determine output filename
		var etlFileNameWithoutExtension = Path.GetFileNameWithoutExtension( etlFileName );
		var jsonFinalFileName = $"{Path.GetDirectoryName( etlFileName )}/{etlFileNameWithoutExtension}.json.gz";

		// Save and compress the profile
		using ( var stream = File.Create( jsonFinalFileName ) )
		using ( var gzipStream = new GZipStream( stream, CompressionLevel.Optimal ) )
		{
			JsonSerializer.Serialize( gzipStream, profile, FirefoxProfiler.JsonProfilerContext.Default.Profile );
			gzipStream.Flush();
		}

		Commands.Log( $"Profile saved to {jsonFinalFileName}" );

		return jsonFinalFileName;
	}

	private static string UploadFirefoxProfile( string firefoxProfileFile )
	{
		if ( !_noUpload )
		{
			try
			{
				// Upload the profile to Firefox Profiler
				Commands.Log( "Uploading profile to Firefox Profiler..." );
				byte[] compressedData = File.ReadAllBytes( firefoxProfileFile );

				// Run the upload asynchronously but wait for it to complete
				var uploadTask = UploadProfileAsync( compressedData );
				uploadTask.Wait();

				string jwtToken = uploadTask.Result;
				string profileToken = ExtractProfileToken( jwtToken );
				string profileUrl = $"https://profiler.firefox.com/public/{profileToken}";

				Commands.Log( $"Profile uploaded. Hosted at: {profileUrl}" );

				return profileUrl;
			}
			catch ( Exception ex )
			{
				Commands.Log( $"Error uploading profile: {ex.Message}" );
			}
		}

		return null;
	}

	private static async Task<string> UploadProfileAsync( byte[] compressedData )
	{
		using ( var httpClient = new HttpClient() )
		{
			httpClient.DefaultRequestHeaders.Accept.ParseAdd( "application/vnd.firefox-profiler+json;version=1.0" );

			using var content = new ByteArrayContent( compressedData );
			var response = await httpClient.PostAsync( "https://api.profiler.firefox.com/compressed-store", content );

			response.EnsureSuccessStatusCode();
			return await response.Content.ReadAsStringAsync();
		}
	}

	private static string ExtractProfileToken( string jwtToken )
	{
		// Split the JWT token into its parts
		string[] parts = jwtToken.Split( '.' );
		if ( parts.Length != 3 )
		{
			throw new ArgumentException( "Invalid JWT token format" );
		}

		// Get the payload part (second part)
		string payload = parts[1];

		// Add padding if needed
		int padding = payload.Length % 4;
		if ( padding > 0 )
		{
			payload += new string( '=', 4 - padding );
		}

		// Decode the Base64Url encoded payload
		byte[] payloadBytes = Convert.FromBase64String( payload.Replace( '-', '+' ).Replace( '_', '/' ) );
		string payloadJson = Encoding.UTF8.GetString( payloadBytes );

		// Parse the JSON
		using ( JsonDocument doc = JsonDocument.Parse( payloadJson ) )
		{
			if ( doc.RootElement.TryGetProperty( "profileToken", out JsonElement tokenElement ) )
			{
				return tokenElement.GetString();
			}
			else
			{
				throw new Exception( "Profile token not found in the response" );
			}
		}
	}

	public static int Main()
	{
		Console.WriteLine( "Starting ETW profiler..." );

		var args = Environment.GetCommandLineArgs();

		if ( args.Length < 2 )
		{
			Console.Error.WriteLine( "Missing pipe handle argument." );
			return 1;
		}

		Console.WriteLine( $"Pipe handle: {args[1]}" );

		Commands.OnResponse = ( string commandName, string contents ) =>
		{
			Console.WriteLine( $"Received command: {commandName} {contents}" );

			switch ( commandName )
			{
				case "PID":
					Init( int.Parse( contents ) );
					break;
				case "SHUTDOWN":
					Shutdown();
					break;
				case "SYMBOLPATH":
					_additionalSymbolPath = contents;
					break;
				case "NOUPLOAD":
					_noUpload = true;
					break;
			}
		};

		Commands.Init( args[1] );

		// Wait for the shutdown signal
		Console.WriteLine( "Waiting for commands..." );
		_shutdownEvent.Wait();

		// Clean up resources
		Commands.Close();
		_shutdownEvent.Dispose();

		Console.WriteLine( "Exiting." );
		return 0;
	}
}
