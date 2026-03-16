using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Web;

/// <summary>
/// Tests that redirect destinations are validated by IsAllowed() on every hop.
/// Without the fix (AllowAutoRedirect=false + manual redirect loop), SocketsHttpHandler
/// follows redirects internally, bypassing SboxHttpHandler entirely. (HackerOne #3519328)
/// </summary>
[TestClass]
public class HttpRedirectValidationTests
{
	/// <summary>
	/// Returns a fixed sequence of responses, in order.
	/// Simulates a server chain without real network traffic.
	/// </summary>
	private sealed class SequenceHandler : HttpMessageHandler
	{
		private readonly HttpResponseMessage[] _responses;
		private int _index;

		public SequenceHandler( params HttpResponseMessage[] responses )
		{
			_responses = responses;
		}

		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
		{
			if ( _index >= _responses.Length )
				throw new InvalidOperationException( "No more queued responses." );

			return Task.FromResult( _responses[_index++] );
		}

		protected override void Dispose( bool disposing )
		{
			if ( disposing )
			{
				foreach ( var r in _responses )
					r.Dispose();
			}

			base.Dispose( disposing );
		}
	}

	private static HttpResponseMessage Redirect( HttpStatusCode status, string location )
	{
		var r = new HttpResponseMessage( status );
		r.Headers.Location = new Uri( location );
		return r;
	}

	private static HttpResponseMessage Ok() => new( HttpStatusCode.OK );

	// Initial URL that passes IsAllowed instantly (loopback, no DNS needed)
	private const string InitialUrl = "http://127.0.0.1/";

	// Core SSRF - redirect to private/reserved IPs must be blocked

	[TestMethod]
	[DataRow( "http://192.168.1.1/" )]        // private LAN (RFC 1918)
	[DataRow( "http://10.0.0.1/" )]            // private LAN (RFC 1918)
	[DataRow( "http://172.16.0.1/" )]          // private LAN (RFC 1918)
	[DataRow( "http://169.254.169.254/" )]     // link-local / cloud metadata (AWS, Azure, GCP)
	[DataRow( "http://127.0.0.1:1337/" )]      // loopback but non-dev port
	public async Task Redirect_ToBlockedTarget_Throws( string redirectTarget )
	{
		using var inner = new SequenceHandler( Redirect( HttpStatusCode.Found, redirectTarget ) );
		using var handler = new SboxHttpHandler( inner );
		using var client = new HttpClient( handler );

		await Assert.ThrowsExceptionAsync<InvalidOperationException>(
		() => client.GetAsync( InitialUrl ),
		$"Expected SSRF redirect to {redirectTarget} to be blocked." );
	}

	[TestMethod]
	[DataRow( HttpStatusCode.MovedPermanently )]  // 301
	[DataRow( HttpStatusCode.Found )]              // 302
	[DataRow( HttpStatusCode.SeeOther )]           // 303
	[DataRow( HttpStatusCode.TemporaryRedirect )]  // 307
	[DataRow( HttpStatusCode.PermanentRedirect )]  // 308
	public async Task Redirect_AllStatusCodes_ToPrivateIp_Throws( HttpStatusCode status )
	{
		// All redirect status codes must be validated, not just 302
		using var inner = new SequenceHandler( Redirect( status, "http://192.168.1.1/" ) );
		using var handler = new SboxHttpHandler( inner );
		using var client = new HttpClient( handler );

		await Assert.ThrowsExceptionAsync<InvalidOperationException>(
		() => client.GetAsync( InitialUrl ) );
	}

	[TestMethod]
	public async Task MultiHop_PrivateIpAtEnd_Throws()
	{
		// Even if first redirect is legitimate, the second must still be validated
		using var inner = new SequenceHandler(
		Redirect( HttpStatusCode.Found, "http://127.0.0.1:8080/" ),
		Redirect( HttpStatusCode.Found, "http://192.168.1.1/" ) );
		using var handler = new SboxHttpHandler( inner );
		using var client = new HttpClient( handler );

		await Assert.ThrowsExceptionAsync<InvalidOperationException>(
		() => client.GetAsync( InitialUrl ) );
	}

	// Silent stop cases - returns the 3xx to caller rather than throwing

	[TestMethod]
	public async Task Redirect_HttpsToHttpDowngrade_ReturnsSilently()
	{
		// Should not throw - silently returns the 3xx to the caller per .NET behaviour
		using var inner = new SequenceHandler( Redirect( HttpStatusCode.Found, "http://127.0.0.1/" ) );
		using var handler = new SboxHttpHandler( inner );
		using var client = new HttpClient( handler );

		var response = await client.GetAsync( "https://127.0.0.1/" );
		Assert.AreEqual( HttpStatusCode.Found, response.StatusCode );
	}

	[TestMethod]
	public async Task Redirect_MissingLocation_ReturnsSilently()
	{
		using var inner = new SequenceHandler( new HttpResponseMessage( HttpStatusCode.Found ) );
		using var handler = new SboxHttpHandler( inner );
		using var client = new HttpClient( handler );

		var response = await client.GetAsync( InitialUrl );
		Assert.AreEqual( HttpStatusCode.Found, response.StatusCode );
	}

	[TestMethod]
	public async Task Redirect_ExceedsMaxRedirects_Returns3xx()
	{
		// 51 redirects should stop at the limit and return the last 3xx
		var responses = new HttpResponseMessage[51];
		for ( int i = 0; i < 51; i++ )
			responses[i] = Redirect( HttpStatusCode.Found, InitialUrl );

		using var inner = new SequenceHandler( responses );
		using var handler = new SboxHttpHandler( inner );
		using var client = new HttpClient( handler );

		var response = await client.GetAsync( InitialUrl );
		Assert.AreEqual( HttpStatusCode.Found, response.StatusCode );
	}

	// Legitimate redirects should be followed normally

	[TestMethod]
	[DataRow( "http://127.0.0.1:8080/" )]
	[DataRow( "http://127.0.0.1:8443/" )]
	[DataRow( "http://127.0.0.1:443/" )]
	[DataRow( "http://127.0.0.1/" )]
	public async Task Redirect_ToAllowedTarget_Follows( string target )
	{
		using var inner = new SequenceHandler( Redirect( HttpStatusCode.Found, target ), Ok() );
		using var handler = new SboxHttpHandler( inner );
		using var client = new HttpClient( handler );

		var response = await client.GetAsync( InitialUrl );
		Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
	}
}
