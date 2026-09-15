using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;

namespace WebTests;

[TestClass]
public class HttpTest
{
	// Hostname rows resolve through live DNS (Http.IsAllowed -> Uri.ResolveAddresses -> Dns.GetHostAddresses),
	// so they're excluded from PR runs - the offline address-based rows live in IsUriAllowed below.
	[TestMethod]
	[TestCategory( "LiveBackend" )]
	[DataRow( "http://google.com", true )]
	[DataRow( "https://google.com", true )]
	[DataRow( "https://api.google.com", true )]
	[DataRow( "https://yahoo.com", true )]
	[DataRow( "https://127-0-0-1.mattstevens.co.uk/", false )]
	[DataRow( "https://10-0-0-1.mattstevens.co.uk/", false )]
	[DataRow( "https://192-168-1-1.mattstevens.co.uk/", false )]
	public async Task IsUriAllowed_Hostnames( string uri, bool expected )
	{
		var u = new Uri( uri, UriKind.Absolute );
		Assert.AreEqual( expected, Http.IsAllowed( u ) );
		Assert.AreEqual( expected, await Http.IsAllowedAsync( u ) );
	}

	[TestMethod]
	[DataRow( "http://127.0.0.1", true )]
	[DataRow( "http://127.0.0.1:80", true )]
	[DataRow( "http://127.0.0.1:443", true )]
	[DataRow( "http://127.0.0.1:8080", true )]
	[DataRow( "http://127.0.0.1:8443", true )]
	[DataRow( "http://127.0.0.1:1337", false )]
	[DataRow( "https://localhost/", true )]
	[DataRow( "https://localhost:80/", true )]
	[DataRow( "https://localhost:443/", true )]
	[DataRow( "https://localhost:8080/", true )]
	[DataRow( "https://localhost:8443/", true )]
	[DataRow( "https://localhost:1337/", false )]
	[DataRow( "https://[::1]:8443/", true )]
	[DataRow( "https://[::1]:1337/", false )]
	[DataRow( "https://8.8.8.8/", false )]
	[DataRow( "https://192.168.1.1/", false )]
	[DataRow( "http://0.0.0.0/", false )]
	[DataRow( "http://0.0.0.0:443/", false )]
	[DataRow( "http://0.0.0.0:9200/", false )]
	[DataRow( "http://[::]:443/", false )]
	[DataRow( "https://100.64.0.1/", false )]
	[DataRow( "file://blah", false )]
	public async Task IsUriAllowed( string uri, bool expected )
	{
		var u = new Uri( uri, UriKind.Absolute );
		Assert.AreEqual( expected, Http.IsAllowed( u ) );
		Assert.AreEqual( expected, await Http.IsAllowedAsync( u ) );
	}

	// What a host resolves to decides, not the host text. Fed directly so there's no DNS involved.
	[TestMethod]
	// A name resolving to the unspecified address reaches loopback on Linux.
	[DataRow( "http://elsewhere.example:9200/", "0.0.0.0", false )]
	[DataRow( "http://elsewhere.example/", "0.0.0.0", false )]
	[DataRow( "http://elsewhere.example:8443/", "0.0.0.0", false )]
	[DataRow( "http://elsewhere.example/", "::", false )]
	// The loopback port allowlist must not be reachable through a name.
	[DataRow( "http://elsewhere.example:8443/", "127.0.0.1", false )]
	[DataRow( "http://elsewhere.example/", "127.0.0.1", false )]
	// A host that only claims to be loopback is judged on where it really points.
	[DataRow( "http://localhost:8443/", "8.8.8.8", true )]
	[DataRow( "http://localhost:8443/", "127.0.0.1,8.8.8.8", false )]
	// Other private ranges behind a public-looking name.
	[DataRow( "http://elsewhere.example/", "169.254.169.254", false )]
	[DataRow( "http://elsewhere.example/", "100.64.0.1", false )]
	[DataRow( "http://elsewhere.example/", "8.8.8.8,192.168.1.1", false )]
	// A host resolving to nothing fails closed.
	[DataRow( "http://elsewhere.example/", "", false )]
	// The guard's stated intent still works.
	[DataRow( "http://127.0.0.1:8443/", "127.0.0.1", true )]
	[DataRow( "http://localhost:8443/", "127.0.0.1", true )]
	[DataRow( "http://localhost/", "::1,127.0.0.1", true )]
	[DataRow( "http://localhost:1337/", "127.0.0.1", false )]
	[DataRow( "http://elsewhere.example/", "8.8.8.8", true )]
	public void IsResolvedAllowed( string uri, string addresses, bool expected )
	{
		var parsed = addresses.Split( ',', StringSplitOptions.RemoveEmptyEntries ).Select( IPAddress.Parse ).ToArray();
		Assert.AreEqual( expected, Http.IsResolvedAllowed( new Uri( uri, UriKind.Absolute ), parsed ) );
	}

	// The range table is what actually decides, so pin it directly - no DNS involved.
	[TestMethod]
	[DataRow( "127.0.0.1", true )]
	[DataRow( "127.1.2.3", true )]
	[DataRow( "::1", true )]
	// Connecting to the unspecified address reaches loopback on Linux.
	[DataRow( "0.0.0.0", true )]
	[DataRow( "0.1.2.3", true )]
	[DataRow( "::", true )]
	[DataRow( "::ffff:0.0.0.0", true )]
	[DataRow( "10.0.0.1", true )]
	[DataRow( "172.16.0.1", true )]
	[DataRow( "172.31.255.255", true )]
	[DataRow( "192.168.1.1", true )]
	[DataRow( "169.254.169.254", true )]
	[DataRow( "100.64.0.1", true )]
	[DataRow( "100.127.255.255", true )]
	[DataRow( "fe80::1", true )]
	[DataRow( "fd00::1", true )]
	[DataRow( "::ffff:192.168.1.1", true )]
	[DataRow( "1.1.1.1", false )]
	[DataRow( "8.8.8.8", false )]
	[DataRow( "172.15.0.1", false )]
	[DataRow( "172.32.0.1", false )]
	[DataRow( "100.63.255.255", false )]
	[DataRow( "100.128.0.1", false )]
	[DataRow( "169.253.0.1", false )]
	[DataRow( "2606:4700:4700::1111", false )]
	[DataRow( "::ffff:8.8.8.8", false )]
	public void IpAddressIsPrivate( string address, bool expected )
	{
		Assert.AreEqual( expected, IPAddress.Parse( address ).IsPrivate() );
	}

	[TestMethod]
	[DataRow( "Authorization", true )]
	[DataRow( "Host", false )]
	[DataRow( "X-Test", true )]
	[DataRow( "Proxy-Blah", false )]
	[DataRow( "Sec-Blah", false )]
	public void IsHeaderAllowed( string header, bool expected )
	{
		Assert.AreEqual( expected, Http.IsHeaderAllowed( header ) );
		Assert.AreEqual( expected, Http.IsHeaderAllowed( header.ToUpperInvariant() ) );
		Assert.AreEqual( expected, Http.IsHeaderAllowed( header.ToLowerInvariant() ) );
	}

	[TestMethod]
	public void CreateRequest_Valid_Succeeds()
	{
		var headers = new Dictionary<string, string> { { "X-Test", "1" } };
		using var request = Http.CreateRequest( HttpMethod.Get, "https://google.com/", headers );
		Assert.AreEqual( HttpMethod.Get, request.Method );
		Assert.AreEqual( "https://google.com/", request.RequestUri?.ToString() );
		Assert.AreEqual( 1, request.Headers.Count() );
		Assert.IsTrue( request.Headers.Contains( "X-Test" ) );
		CollectionAssert.AreEqual( new[] { "1" }, request.Headers.GetValues( "X-Test" ).ToArray() );
	}

	[TestMethod]
	public void CreateRequest_BadHeader_Throws()
	{
		var headers = new Dictionary<string, string> { { "Host", "blah" } };
		Assert.ThrowsException<InvalidOperationException>( () => Http.CreateRequest( HttpMethod.Get, "http://google.com", headers ) );
	}

	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task GetString()
	{
		var str = await Http.RequestStringAsync( "https://google.com" );

		Assert.IsFalse( string.IsNullOrEmpty( str ) );
	}


	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task GetBytes()
	{
		var bytes = await Http.RequestBytesAsync( "https://www.google.com/favicon.ico" );

		Assert.IsNotNull( bytes );
		Assert.IsTrue( bytes.Length > 0 );
	}
}
