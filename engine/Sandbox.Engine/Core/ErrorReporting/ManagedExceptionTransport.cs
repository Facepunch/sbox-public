using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Sandbox.Engine;

/// <summary>The independent Facepunch copy, with bounded attempts including disk retries.</summary>
internal sealed class ManagedExceptionTransport : ITransport
{
	readonly Func<object, Task> submit;
	readonly Func<bool> hasAccount;
	readonly ExceptionReportLimiter uploads = new( 20, 20 );
	internal ManagedExceptionTransport( Func<object, Task> submit = null, Func<bool> hasAccount = null )
	{
		this.submit = submit ?? (batch => Sandbox.Backend.Account.SubmitEvents( batch ));
		this.hasAccount = hasAccount ?? (() => ManagedExceptionReporting.IsLoggedIn);
	}

	public async Task SendEnvelopeAsync( Envelope envelope, CancellationToken cancellationToken = default )
	{
		if ( !AccountInformation.UseAnalytics ) return;
		// New reports are captured only after login. Preserve previously cached reports until login.
		if ( !hasAccount() ) throw new InvalidOperationException( "Exception upload requires an account." );
		foreach ( var item in envelope.Items.Where( x => x.TryGetType() == "event" ) )
		{
			// Deliberately shed excess backlog instead of replaying an error storm later.
			if ( !uploads.TryAccept( "upload", out _ ) ) continue;
			using var stream = new MemoryStream();
			await item.Payload.SerializeAsync( stream, null, cancellationToken ).ConfigureAwait( false );
			if ( stream.Length > 256 * 1024 ) continue;
			stream.Position = 0;
			using var json = await JsonDocument.ParseAsync( stream, cancellationToken: cancellationToken ).ConfigureAwait( false );
			if ( json.RootElement.TryGetProperty( "level", out var level ) && level.GetString() is not ("error" or "fatal") ) continue;
			var record = new Api.Events.EventRecord( "managed.exception" )
			{
				Version = Application.Version,
				Mode = Application.IsEditor ? "editor" : "game"
			};
			if ( json.RootElement.TryGetProperty( "timestamp", out var timestamp ) ) record.Created = timestamp.GetDateTimeOffset();
			record.SetValue( "report", json.RootElement.Clone() );
			// Use the existing authenticated API, but await delivery so the SDK can retry failures.
			await submit( new { Events = new[] { record } } ).ConfigureAwait( false );
		}
	}
}
