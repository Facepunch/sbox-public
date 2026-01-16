using System.Diagnostics;
using System.Net.Http.Json;

namespace CrashReporter;

class Program
{
	static async Task<int> Main( string[] args )
	{
		if ( args.Length < 1 )
		{
			Console.WriteLine( "Usage: CrashReporter.exe <path to envelope>" );
			return 1;
		}

		using var stream = File.OpenRead( args[0] );
		var envelope = await Envelope.FromFileStreamAsync( stream );

		var dsn = envelope.TryGetDsn();
		var eventId = envelope.TryGetEventId();

		// Attach NVIDIA Aftermath GPU crash dump files if available
		AttachAftermathFiles( envelope );

		// Attach the most recent game log if available
		AttachLatestLog( envelope );

		// Submit to Sentry
		var sentrySubmitted = false;
		string? sentryError = null;
		try
		{
			await SentryClient.SubmitEnvelopeAsync( dsn!, envelope );
			sentrySubmitted = true;
		}
		catch ( Exception ex )
		{
			sentryError = ex.Message;
			Console.WriteLine( $"Failed to submit to Sentry: {ex.Message}" );
		}

		// Submit to our own API
		var sentryEvent = envelope.TryGetEvent()?.TryParseAsJson();
		var tags = sentryEvent?["tags"];
		var processName = sentryEvent?["contexts"]?["process"]?["name"]?.GetValue<string>();

		var payload = new
		{
			sentry_event_id = eventId,
			sentry_submitted = sentrySubmitted,
			sentry_error = sentryError,
			timestamp = sentryEvent?["timestamp"],
			version = sentryEvent?["release"],
			session_id = tags?["session_id"],
			activity_session_id = tags?["activity_session_id"],
			launch_guid = tags?["launch_guid"],
			gpu = tags?["gpu"],
			cpu = tags?["cpu"],
			mode = tags?["mode"],
			process_name = processName,
		};

		try
		{
			using var client = new HttpClient();
			await client.PostAsJsonAsync( "https://services.facepunch.com/sbox/event/crash/1/", payload );
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Failed to submit to Facepunch: {ex.Message}" );
		}

		// Open browser to crash report page (only if Sentry has the data)
		if ( sentrySubmitted )
		{
			Process.Start( new ProcessStartInfo( $"https://sbox.game/crashes/{eventId}" ) { UseShellExecute = true } );
		}

		return 0;
	}

	/// <summary>
	/// Looks for the most recent NVIDIA Aftermath GPU crash dump files and attaches them to the envelope.
	/// Files are located in game/aftermath_dumps/ and include:
	/// - *.nv-gpudmp - The GPU crash dump file
	/// - *.nv-gpudmp.json - The decoded JSON representation
	/// - *.nvdbg - Shader debug info files
	/// - *.spv - SPIR-V shader binaries
	/// </summary>
	static void AttachAftermathFiles( Envelope envelope )
	{
		try
		{
			var envelopePath = envelope.FilePath;
			if ( string.IsNullOrEmpty( envelopePath ) )
				return;

			// Walk upward to find the game directory that holds aftermath_dumps and the game executable
			var gameDir = FindGameRootWithSubdirectory( envelopePath, "aftermath_dumps" );

			var aftermathDir = Path.Combine( gameDir ?? string.Empty, "aftermath_dumps" );
			if ( !Directory.Exists( aftermathDir ) )
			{
				Console.WriteLine( $"No aftermath_dumps directory found at {aftermathDir}" );
				return;
			}

			// Find the most recent .nv-gpudmp file (the main crash dump)
			var dumpFiles = Directory.GetFiles( aftermathDir, "*.nv-gpudmp" )
				.Select( f => new FileInfo( f ) )
				.OrderByDescending( f => f.LastWriteTimeUtc )
				.ToList();

			if ( dumpFiles.Count == 0 )
			{
				Console.WriteLine( "No aftermath dump files found" );
				return;
			}

			// Only attach files from the most recent dump (within last 120 seconds)
			var latestDump = dumpFiles[0];
			var cutoffTime = DateTime.UtcNow.AddSeconds( -120 );

			if ( latestDump.LastWriteTimeUtc < cutoffTime )
			{
				Console.WriteLine( $"Most recent aftermath dump is too old: {latestDump.Name}" );
				return;
			}

			Console.WriteLine( $"Found aftermath dump: {latestDump.Name}" );

			// Collect all related files (written within 5 seconds of the dump)
			var minTime = latestDump.LastWriteTimeUtc.AddSeconds( -5 );
			var filesToAttach = Directory.GetFiles( aftermathDir )
				.Select( f => new FileInfo( f ) )
				.Where( f => f.LastWriteTimeUtc >= minTime )
				.Where( f => f.Extension is ".nv-gpudmp" or ".json" or ".nvdbg" or ".spv" )
				.Select( f => f.FullName )
				.ToList();

			// Attach each file
			long totalSize = 0;
			const long maxTotalSize = 50 * 1024 * 1024; // 50MB limit for all aftermath files

			foreach ( var filePath in filesToAttach )
			{
				try
				{
					var fileInfo = new FileInfo( filePath );
					if ( totalSize + fileInfo.Length > maxTotalSize )
					{
						Console.WriteLine( $"Skipping {fileInfo.Name} - would exceed size limit" );
						continue;
					}

					var data = File.ReadAllBytes( filePath );
					var filename = Path.GetFileName( filePath );
					var contentType = GetContentType( filename );

					envelope.AddAttachment( filename, data, contentType );
					totalSize += data.Length;

					Console.WriteLine( $"Attached: {filename} ({data.Length:N0} bytes)" );
				}
				catch ( Exception ex )
				{
					Console.WriteLine( $"Failed to attach {filePath}: {ex.Message}" );
				}
			}

			Console.WriteLine( $"Total aftermath attachments: {filesToAttach.Count}, Size: {totalSize:N0} bytes" );
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Error attaching aftermath files: {ex.Message}" );
		}
	}

	/// <summary>
	/// Attaches the most recent s&box log file (sbox.log or sbox-dev.log) if it exists and is under 10MB.
	/// </summary>
	static void AttachLatestLog( Envelope envelope )
	{
		try
		{
			var envelopePath = envelope.FilePath;
			if ( string.IsNullOrEmpty( envelopePath ) )
				return;

			// Walk upward to find the game directory that holds logs and the game executable
			var gameDir = FindGameRootWithSubdirectory( envelopePath, "logs" );

			var logsDir = Path.Combine( gameDir ?? string.Empty, "logs" );
			if ( !Directory.Exists( logsDir ) )
			{
				Console.WriteLine( $"No logs directory found at {logsDir}" );
				return;
			}

			var candidates = new[] { "sbox.log", "sbox-dev.log" }
				.Select( name => new FileInfo( Path.Combine( logsDir, name ) ) )
				.Where( fi => fi.Exists )
				.OrderByDescending( fi => fi.LastWriteTimeUtc )
				.ToList();

			if ( candidates.Count == 0 )
			{
				Console.WriteLine( "No log files found" );
				return;
			}

			var latest = candidates[0];
			const long maxLogSize = 10 * 1024 * 1024; // 10MB

			if ( latest.Length > maxLogSize )
			{
				Console.WriteLine( $"Skipping log {latest.Name} - exceeds 10MB limit" );
				return;
			}

			try
			{
				// Log may still be locked by the game, so open with shared read access
				using var fs = new FileStream( latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete );
				using var ms = new MemoryStream();
				fs.CopyTo( ms );
				var data = ms.ToArray();
				envelope.AddAttachment( latest.Name, data, "text/plain" );
				Console.WriteLine( $"Attached log: {latest.Name} ({data.Length:N0} bytes)" );
			}
			catch ( Exception ex )
			{
				Console.WriteLine( $"Failed to read log {latest.Name}: {ex.Message}" );
			}
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Error attaching logs: {ex.Message}" );
		}
	}

	static string GetContentType( string filename )
	{
		return Path.GetExtension( filename ).ToLowerInvariant() switch
		{
			".log" => "text/plain",
			".json" => "application/json",
			".spv" => "application/x-spirv",
			".nvdbg" => "application/octet-stream",
			_ => "application/octet-stream"
		};
	}

	/// <summary>
	/// Walks upward from the starting path to find the nearest ancestor that contains a given subdirectory
	/// and looks like a game root (sbox.exe or sbox-dev.exe present).
	/// Returns the ancestor path or null if none found.
	/// </summary>
	static string? FindGameRootWithSubdirectory( string startPath, string subdirectory )
	{
		var dir = Path.GetDirectoryName( startPath );
		while ( !string.IsNullOrEmpty( dir ) )
		{
			if ( Directory.Exists( Path.Combine( dir, subdirectory ) ) && IsGameRoot( dir ) )
			{
				return dir;
			}

			var parent = Path.GetDirectoryName( dir );
			if ( parent == dir )
			{
				break;
			}
			dir = parent;
		}

		return null;
	}

	static bool IsGameRoot( string dir )
	{
		return File.Exists( Path.Combine( dir, "sbox.exe" ) ) || File.Exists( Path.Combine( dir, "sbox-dev.exe" ) );
	}
}
