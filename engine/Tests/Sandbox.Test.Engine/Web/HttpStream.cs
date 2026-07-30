using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace WebTests;

/// <summary>
/// Tests that RequestStreamAsync hands back a live stream rather than a buffer. Without the fix
/// (ResponseHeadersRead) it went through RequestAsync, which downloads the whole body before the
/// task resolves - so a response that never ends never resolves.
/// </summary>
[TestClass]
public class HttpStreamTest
{
	// Http.IsAllowed only permits loopback on 80/443/8080/8443, and Http.Client is private, so
	// this goes over a real socket on a real allowed port rather than a stubbed handler.
	private const int Port = 8080;
	private const int Lines = 8;
	private const int IntervalMs = 200;
	private const int BodyMs = Lines * IntervalMs;

	private static readonly string Url = $"http://localhost:{Port}/stream";
	private static HttpListener Listener;

	[ClassInitialize]
	public static void Start( TestContext context )
	{
		Listener = new HttpListener();
		Listener.Prefixes.Add( $"http://localhost:{Port}/" );
		Listener.Start();

		_ = Task.Run( async () =>
		{
			while ( Listener.IsListening )
			{
				HttpListenerContext ctx;
				try { ctx = await Listener.GetContextAsync(); }
				catch { return; }

				_ = Task.Run( () => Serve( ctx ) );
			}
		} );
	}

	[ClassCleanup]
	public static void Stop() => Listener?.Close();

	/// <summary>Paced NDJSON, chunked so there's no Content-Length to buffer against.</summary>
	private static async Task Serve( HttpListenerContext ctx )
	{
		try
		{
			if ( ctx.Request.Url?.AbsolutePath != "/stream" )
			{
				ctx.Response.StatusCode = 404;
				ctx.Response.Close();
				return;
			}

			ctx.Response.ContentType = "application/x-ndjson";
			ctx.Response.SendChunked = true;

			for ( int n = 0; n < Lines; n++ )
			{
				if ( n > 0 ) await Task.Delay( IntervalMs );

				await ctx.Response.OutputStream.WriteAsync( Encoding.UTF8.GetBytes( $"{{\"n\":{n}}}\n" ) );
				await ctx.Response.OutputStream.FlushAsync();
			}

			ctx.Response.Close();
		}
		catch
		{
			try { ctx.Response.Abort(); } catch { }
		}
	}

	[TestMethod]
	public async Task RequestStreamAsync_ReturnsBeforeBodyEnds()
	{
		using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( 30 ) );
		var sw = Stopwatch.StartNew();

		using var stream = await Http.RequestStreamAsync( Url, cancellationToken: cts.Token );

		Assert.IsTrue( sw.ElapsedMilliseconds < BodyMs / 2,
			$"Returned after {sw.ElapsedMilliseconds}ms of a ~{BodyMs}ms body - it buffered." );
	}

	[TestMethod]
	public async Task RequestStreamAsync_DeliversLinesAsTheyArrive()
	{
		using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( 30 ) );
		var sw = Stopwatch.StartNew();

		// Reading after the call returned also covers the disposal half: the old code disposed the
		// HttpResponseMessage before returning its content stream, which a live socket won't survive.
		using var stream = await Http.RequestStreamAsync( Url, cancellationToken: cts.Token );
		using var reader = new StreamReader( stream );

		long first = -1, last = 0;
		var count = 0;

		while ( await reader.ReadLineAsync( cts.Token ) is not null )
		{
			if ( first < 0 ) first = sw.ElapsedMilliseconds;
			last = sw.ElapsedMilliseconds;
			count++;
		}

		Assert.AreEqual( Lines, count );
		Assert.IsTrue( first < BodyMs / 2, $"First line took {first}ms." );
		Assert.IsTrue( last - first >= BodyMs / 2,
			$"All {Lines} lines arrived within {last - first}ms - they were buffered, not streamed." );
	}

	[TestMethod]
	public async Task RequestStreamAsync_NonSuccess_Throws()
	{
		await Assert.ThrowsExceptionAsync<HttpRequestException>(
			() => Http.RequestStreamAsync( $"http://localhost:{Port}/nope" ) );
	}
}
