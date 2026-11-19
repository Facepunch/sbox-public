using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace ServicesTests;

// Each handler owns its transport; responses passed through the fake transport are disposed by
// the recovery handler or the receiving test. CA2000 cannot follow these ownership transfers.
#pragma warning disable CA2000

/// <summary>
/// Exercises session recovery through the real HTTP handler without Steam or a live backend.
/// </summary>
[TestClass]
public class BackendAuthenticationTest
{
	private sealed class Transport( Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send ) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken ) => send( request, cancellationToken );
	}

	private static HttpResponseMessage Response( HttpStatusCode status ) => new( status );

	/// <summary>
	/// Retry keeps the original POST payload, including the AI idempotency key, and replaces credentials.
	/// </summary>
	[TestMethod]
	public async Task ReplaysExactBodyWithRenewedSession()
	{
		var session = new BackendSession( "expired", 42 );
		var calls = 0;
		var refreshes = 0;
		const string body = "{\"RequestId\":\"retained-request\",\"Input\":\"hello ☃\"}";
		using var handler = new BackendAuthenticationHandler( new Transport( async ( request, token ) =>
		{
			Assert.AreEqual( HttpMethod.Post, request.Method );
			Assert.AreEqual( body, await request.Content.ReadAsStringAsync( token ) );
			Assert.AreEqual( "application/json", request.Content.Headers.ContentType.MediaType );
			Assert.AreEqual( "unchanged", request.Headers.GetValues( "X-Test" ).Single() );
			Assert.AreEqual( "42", request.Headers.GetValues( "X-User-Id" ).Single() );
			Assert.AreEqual( calls++ == 0 ? "session expired" : "session renewed", request.Headers.Authorization.ToString() );
			return Response( calls == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK );
		} ), () => session, () =>
		{
			refreshes++;
			session = new( "renewed", 42 );
			return Task.CompletedTask;
		} );
		using var client = new HttpClient( handler );
		using var request = new HttpRequestMessage( HttpMethod.Post, "https://test/ai/chat/query" ) { Content = new ByteArrayContent( Encoding.UTF8.GetBytes( body ) ) };
		request.Content.Headers.ContentType = new( "application/json" );
		request.Headers.Add( "X-Test", "unchanged" );
		using var response = await client.SendAsync( request );
		Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
		Assert.AreEqual( 2, calls );
		Assert.AreEqual( 1, refreshes );
	}

	/// <summary>
	/// A second rejection is final, even after successfully obtaining a new session.
	/// </summary>
	[TestMethod]
	public async Task RepeatedUnauthorizedRefreshesOnlyOnce()
	{
		var session = new BackendSession( "expired", 42 );
		var calls = 0;
		var refreshes = 0;
		using var handler = new BackendAuthenticationHandler( new Transport( ( _, _ ) =>
		{
			calls++;
			return Task.FromResult( Response( HttpStatusCode.Unauthorized ) );
		} ), () => session, () =>
		{
			refreshes++;
			session = new( "renewed", 42 );
			return Task.CompletedTask;
		} );
		using var client = new HttpClient( handler );
		using var response = await client.GetAsync( "https://test/ai/chat/query" );
		Assert.AreEqual( HttpStatusCode.Unauthorized, response.StatusCode );
		Assert.AreEqual( 2, calls );
		Assert.AreEqual( 1, refreshes );
	}

	/// <summary>
	/// Only authenticated-session 401s can initiate a refresh, and login can never refresh itself.
	/// </summary>
	[TestMethod]
	[DataRow( "/account/login/2", 401, 42L )]
	[DataRow( "/ai/chat/query", 403, 42L )]
	[DataRow( "/ai/chat/query", 500, 42L )]
	[DataRow( "/ai/chat/query", 401, 0L )]
	public async Task DoesNotRefreshUnrelatedResponses( string path, int status, long user )
	{
		var refreshes = 0;
		using var handler = new BackendAuthenticationHandler( new Transport( ( _, _ ) => Task.FromResult( Response( (HttpStatusCode)status ) ) ),
			() => new( user == 0 ? null : "expired", user ), () =>
			{
				refreshes++;
				return Task.CompletedTask;
			} );
		using var client = new HttpClient( handler );
		using var response = await client.GetAsync( "https://test" + path );
		Assert.AreEqual( status, (int)response.StatusCode );
		Assert.AreEqual( 0, refreshes );
	}

	/// <summary>
	/// Failed login and account changes preserve the original rejection without replaying a mutation.
	/// </summary>
	[TestMethod]
	[DataRow( "expired", 42L, false )]
	[DataRow( null, 42L, false )]
	[DataRow( "other-account", 99L, false )]
	[DataRow( "expired", 42L, true )]
	public async Task RequiresNewCredentialsForTheSameAccount( string token, long user, bool failRefresh )
	{
		var session = new BackendSession( "expired", 42 );
		var calls = 0;
		using var handler = new BackendAuthenticationHandler( new Transport( ( _, _ ) =>
		{
			calls++;
			return Task.FromResult( Response( HttpStatusCode.Unauthorized ) );
		} ), () => session, () =>
		{
			if ( failRefresh ) return Task.FromException( new InvalidOperationException( "Login failed" ) );

			session = new( token, user );
			return Task.CompletedTask;
		} );
		using var client = new HttpClient( handler );
		using var response = await client.GetAsync( "https://test/ai/chat/query" );
		Assert.AreEqual( HttpStatusCode.Unauthorized, response.StatusCode );
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// Concurrent rejections await account recovery; a delayed 401 uses its published result.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ConcurrentRequestsUsePublishedCredentials( bool failRefresh )
	{
		var session = new BackendSession( "expired", 42 );
		var login = new TaskCompletionSource();
		var delayed = new TaskCompletionSource<HttpResponseMessage>();
		var refreshes = 0;
		using var handler = new BackendAuthenticationHandler( new Transport( ( request, _ ) =>
		{
			if ( request.Headers.Authorization.Parameter == "renewed" ) return Task.FromResult( Response( HttpStatusCode.OK ) );
			return request.RequestUri.AbsolutePath == "/delayed" ? delayed.Task : Task.FromResult( Response( HttpStatusCode.Unauthorized ) );
		} ), () => session, () =>
		{
			refreshes++;
			return login.Task;
		} );
		using var client = new HttpClient( handler );
		var late = client.GetAsync( "https://test/delayed" );
		var first = client.GetAsync( "https://test/first" );
		var second = client.GetAsync( "https://test/second" );
		Assert.AreEqual( 2, refreshes );
		session = new( failRefresh ? null : "renewed", 42 );
		login.SetResult();
		using var firstResponse = await first;
		using var secondResponse = await second;
		delayed.SetResult( Response( HttpStatusCode.Unauthorized ) );
		using var lateResponse = await late;
		Assert.AreEqual( 2, refreshes );
		var expected = failRefresh ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
		Assert.AreEqual( expected, firstResponse.StatusCode );
		Assert.AreEqual( expected, secondResponse.StatusCode );
		Assert.AreEqual( expected, lateResponse.StatusCode );
	}

	/// <summary>
	/// Cancelling one caller does not cancel the login shared with other requests.
	/// </summary>
	[TestMethod]
	public async Task CancellationDoesNotCancelSharedLogin()
	{
		var session = new BackendSession( "expired", 42 );
		var login = new TaskCompletionSource();
		var calls = 0;
		using var handler = new BackendAuthenticationHandler( new Transport( ( request, _ ) =>
		{
			calls++;
			return Task.FromResult( Response( request.Headers.Authorization.Parameter == "renewed" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized ) );
		} ), () => session, async () =>
		{
			await login.Task;
			session = new( "renewed", 42 );
		} );
		using var client = new HttpClient( handler );
		using var cancellation = new CancellationTokenSource();
		var cancelled = client.GetAsync( "https://test/first", cancellation.Token );
		var continuing = client.GetAsync( "https://test/second" );
		cancellation.Cancel();
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( async () => await cancelled );
		login.SetResult();
		using var response = await continuing;
		Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
		Assert.AreEqual( 3, calls );
	}
}

#pragma warning restore CA2000
