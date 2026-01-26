using Sandbox.Utility;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Lets your game make async HTTP requests.
/// </summary>
public static partial class Http
{
	internal const string UserAgent = "facepunch-sbox"; // todo: add version?
	internal const string Referrer = "https://sbox.facepunch.com/"; // todo: link to current gamemode?

	private static readonly HttpClient Client;

	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient will dispose handlers." )]
	static Http()
	{
		var socketHttpHandler = new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes( 2 ),
		};

		// Gives us 1 http client per game, so cookies don't persist etc.
		Client = new HttpClient( new SboxHttpHandler( socketHttpHandler ) );
		Client.Timeout = TimeSpan.FromMinutes( 120 );
	}

	/// <summary>
	/// We shouldn't blindly let users opt into local http.
	/// But it's okay for editor, dedicated servers and standalone.
	/// </summary>
	internal static bool IsLocalAllowed => ((Application.IsEditor || Application.IsDedicatedServer) && CommandLine.HasSwitch( "-allowlocalhttp" )) || Application.IsStandalone;

	/// <summary>
	/// Check if the given Uri matches the following requirements:
	/// 1. Scheme is https/http or wss/ws
	/// 2. If it's localhost, only allow ports 80/443/8080/8443
	/// 3. Not an ip address
	/// </summary>
	/// <param name="uri">The Uri to check.</param>
	/// <returns>True if the Uri can be accessed, false if the Uri will be blocked.</returns>
	public static bool IsAllowed( Uri uri )
	{
		if ( uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "wss" && uri.Scheme != "ws" )
			return false;

		if ( IsLocalAllowed )
			return true;

		// Allow specific ports for loopback (localhost) URIs so that people can do local development/testing
		// Only including the obvious development server only ports because nothing should conflict with these
		if ( uri.IsLoopback )
			return uri.IsDefaultPort || uri.Port is 80 or 443 or 8080 or 8443;

		// don't allow ip urls (unless it's covered by loopback above)
		if ( uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6 )
			return false;

		try
		{

			// don't allow any domains that resolve to private or loopback ip addresses
			// shit routers and internet of shit devices are typically vulnerable
			// https://medium.com/@brannondorsey/attacking-private-networks-from-the-internet-with-dns-rebinding-ea7098a2d325
			if ( uri.IsPrivate() )
				return false;

		}
		catch ( System.Net.Sockets.SocketException )
		{
			// No such host is known
			return false;
		}

		return true;
	}

	// https://developer.mozilla.org/en-US/docs/Glossary/Forbidden_header_name
	private static readonly HashSet<string> ForbiddenHeaders = new( StringComparer.InvariantCultureIgnoreCase )
	{
		"Accept-Charset",
		"Accept-Encoding",
		"Access-Control-Request-Headers",
		"Access-Control-Request-Method",
		"Connection",
		"Content-Length",
		//"Cookie", // cookies are necessary for us
		//"Cookie2",
		"Date",
		"DNT",
		"Expect",
		"Feature-Policy",
		"Host",
		"Keep-Alive",
		"Origin", // we should set this (preferably with a way to identify the gamemode)
		"Referer",
		"TE",
		"Trailer",
		"Transfer-Encoding",
		"Upgrade",
		"Via",
		"User-Agent", // not forbidden officially but we'll be setting this to something s&box specific
	};

	/// <summary>
	/// Checks if a given header is allowed to be set.
	/// </summary>
	/// <param name="header">The header name to check.</param>
	/// <returns>True if the header is allowed to be set.</returns>
	public static bool IsHeaderAllowed( string header )
	{
		return !string.IsNullOrWhiteSpace( header ) &&
			   !ForbiddenHeaders.Contains( header ) &&
			   !header.StartsWith( "Proxy-", StringComparison.InvariantCultureIgnoreCase ) &&
			   !header.StartsWith( "Sec-", StringComparison.InvariantCultureIgnoreCase );
	}
}

internal sealed class SboxHttpHandler : DelegatingHandler
{
	public SboxHttpHandler( HttpMessageHandler innerHandler ) : base( innerHandler ) { }

	private static void HandleRequest( HttpRequestMessage request )
	{
		// Check URI here because of redirects
		if ( !Http.IsAllowed( request.RequestUri ) )
		{
			throw new InvalidOperationException( $"Access to '{request.RequestUri}' is not allowed." );
		}

		request.Headers.Remove( "User-Agent" );
		request.Headers.TryAddWithoutValidation( "User-Agent", Http.UserAgent );

		request.Headers.Remove( "Referer" );
		request.Headers.TryAddWithoutValidation( "Referer", Http.Referrer );
	}

	protected override HttpResponseMessage Send( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		HandleRequest( request );
		return base.Send( request, cancellationToken );
	}

	protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		HandleRequest( request );
		return base.SendAsync( request, cancellationToken );
	}
}
