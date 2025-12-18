using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

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

		// Submit to Sentry
		await SentryClient.SubmitEnvelopeAsync( dsn!, envelope );

		// Submit to our own API
		var sentryEvent = envelope.TryGetEvent()?.TryParseAsJson();
		if ( sentryEvent is null )
			return 0;

		var tags = sentryEvent["tags"];
		var processName = sentryEvent["contexts"]?["process"]?["name"]?.GetValue<string>();

		var payload = new
		{
			sentry_event_id = eventId,
			timestamp = sentryEvent["timestamp"],
			version = sentryEvent["release"],
			session_id = tags?["session_id"],
			activity_session_id = tags?["activity_session_id"],
			launch_guid = tags?["launch_guid"],
			gpu = tags?["gpu"],
			cpu = tags?["cpu"],
			mode = tags?["mode"],
			process_name = processName,
		};

		using var client = new HttpClient();
		await client.PostAsJsonAsync( "https://services.facepunch.com/sbox/event/crash/1/", payload );

		// Open browser to crash report page
		Process.Start( new ProcessStartInfo( $"https://sbox.game/crashes/{eventId}" ) { UseShellExecute = true } );

		return 0;
	}
}
