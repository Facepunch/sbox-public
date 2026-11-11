using System.Net;
using System.Net.Http;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Credentials for one backend login. A replay must remain on the same player's account.
/// </summary>
internal readonly record struct BackendSession( string Token, long UserId );

/// <summary>
/// Refreshes expired game sessions once per rejected request, sharing login work across callers.
/// Wraps the backend retry handler so authentication is retried only once, even across HTTP retries.
/// </summary>
internal sealed class BackendAuthenticationHandler : DelegatingHandler
{
	private readonly Func<BackendSession> getSession;
	private readonly Func<Task> refreshSession;

	internal BackendAuthenticationHandler( HttpMessageHandler transport, Func<BackendSession> getSession, Func<Task> refreshSession )
	{
		InnerHandler = transport;
		this.getSession = getSession;
		this.refreshSession = refreshSession;
	}

	/// <inheritdoc />
	protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		// Login uses a Steam ticket, and may itself return 401. Never wait on its own refresh.
		if ( request.RequestUri.AbsolutePath.Contains( "/account/login/", StringComparison.OrdinalIgnoreCase ) )
			return await base.SendAsync( request, cancellationToken );

		var session = getSession();
		SetHeaders( request, session );

		var response = await base.SendAsync( request, cancellationToken );
		if ( response.StatusCode != HttpStatusCode.Unauthorized || session.UserId == 0 ) return response;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			// AccountInformation.Update shares the complete login across concurrent callers.
			if ( getSession() == session )
			{
				await refreshSession().WaitAsync( cancellationToken );
			}
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
			response.Dispose();
			throw;
		}
		catch ( Exception exception )
		{
			// Preserve the original service error if login could not be refreshed.
			Log.Warning( exception, "Failed to refresh the backend session." );
			return response;
		}

		var renewed = getSession();
		if ( string.IsNullOrEmpty( renewed.Token ) || renewed == session || renewed.UserId != session.UserId ) return response;

		response.Dispose();
		cancellationToken.ThrowIfCancellationRequested();
		SetHeaders( request, renewed );
		// Backend JSON bodies are already buffered by Refit. Replay them without changing request IDs.
		// A second 401 is returned directly, never refreshed recursively.
		return await base.SendAsync( request, cancellationToken );
	}

	private static void SetHeaders( HttpRequestMessage request, BackendSession session )
	{
		request.Headers.Remove( "Authorization" );
		request.Headers.Remove( "X-User-Id" );
		if ( string.IsNullOrEmpty( session.Token ) ) return;
		request.Headers.Add( "Authorization", $"session {session.Token}" );
		request.Headers.Add( "X-User-Id", session.UserId.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
	}
}
