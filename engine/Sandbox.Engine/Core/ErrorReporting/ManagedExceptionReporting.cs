using Sentry;
using Sentry.Protocol.Envelopes;
using System.IO;

namespace Sandbox.Engine;

/// <summary>A bounded copy of existing Sentry errors. Does not change Sentry or package reporting.</summary>
[SkipHotload]
internal static class ManagedExceptionReporting
{
	static SentryClient client;
	static readonly ExceptionReportLimiter Limit = new( 20, 3 );
	internal static bool IsLoggedIn => Sandbox.Backend.Account is not null &&
		!string.IsNullOrEmpty( AccountInformation.Session ) && AccountInformation.SteamId != 0;

	internal static void Initialize()
	{
		try
		{
			client = new SentryClient( new SentryOptions
			{
				Dsn = "https://collector@public.facepunch.com/1",
				Transport = new ManagedExceptionTransport(),
				CacheDirectoryPath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "sbox", "managed-exceptions" ),
				MaxCacheItems = 256,
				MaxQueueItems = 100,
				AutoSessionTracking = false,
				TracesSampleRate = 0,
				CaptureFailedRequests = false,
				SendDefaultPii = false
			} );
		}
		catch { /* Local cache failures must not prevent the normal reporter from starting. */ }
	}

	internal static void Flush()
	{
		try { client?.FlushAsync( TimeSpan.FromSeconds( 2 ) ).GetAwaiter().GetResult(); }
		catch { /* Best effort on shutdown. */ }
	}

	internal static bool Accept( SentryEvent report, ExceptionReportLimiter limiter )
	{
		if ( report.Level is { } level && level < SentryLevel.Error ) return false;
		var signature = report.Exception is { } exception ? exception.GetType().FullName + ":" + exception.StackTrace : report.Logger ?? "message";
		if ( !limiter.TryAccept( signature, out var suppressed ) ) return false;
		report.SetTag( "reports_suppressed", suppressed.ToString() );
		return true;
	}

	internal static void Copy( SentryEvent report )
	{
		try
		{
			if ( client is null || !IsLoggedIn || !AccountInformation.UseAnalytics || !Accept( report, Limit ) ) return;
			report.SetTag( "launch_guid", Api.LaunchGuid );
			report.SetTag( "activity_session_id", Api.SessionId.ToString( "N" ) );
			var envelope = Envelope.FromEvent( report );
			if ( !client.CaptureEnvelope( envelope ) ) envelope.Dispose();
		}
		catch { /* A failed copy must not affect Sentry or recurse through logging. */ }
	}
}
