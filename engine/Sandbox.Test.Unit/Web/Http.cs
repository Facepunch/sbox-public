using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace Web;

[TestClass]
public class HttpTests
{
	[TestMethod]
	[DataRow( "http://google.com", true )]
	[DataRow( "https://google.com", true )]
	[DataRow( "https://api.google.com", true )]
	[DataRow( "https://yahoo.com", true )]
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
	[DataRow( "https://8.8.8.8/", false )]
	[DataRow( "https://192.168.1.1/", false )]
	[DataRow( "https://127-0-0-1.mattstevens.co.uk/", false )]
	[DataRow( "https://10-0-0-1.mattstevens.co.uk/", false )]
	[DataRow( "https://192-168-1-1.mattstevens.co.uk/", false )]
	[DataRow( "file://blah", false )]
	public void IsUriAllowed( string uri, bool expected )
	{
		Assert.AreEqual( expected, Http.IsAllowed( new Uri( uri, UriKind.Absolute ) ) );
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
		var request = Http.CreateRequest( HttpMethod.Get, "https://google.com/", headers );
		Assert.AreEqual( HttpMethod.Get, request.Method );
		Assert.AreEqual( "https://google.com/", request.RequestUri?.ToString() );
		Assert.AreEqual( 1, request.Headers.Count() );
		Assert.IsTrue( request.Headers.Contains( "X-Test" ) );
		CollectionAssert.AreEqual( new[] { "1" }, request.Headers.GetValues( "X-Test" ).ToArray() );
	}

	[TestMethod]
	public void CreateRequest_BadUri_Throws()
	{
		var headers = new Dictionary<string, string> { { "X-Test", "1" } };
		Assert.ThrowsException<InvalidOperationException>( () => Http.CreateRequest( HttpMethod.Get, "ftp://google.com", headers ) );
	}

	[TestMethod]
	public void CreateRequest_BadHeader_Throws()
	{
		var headers = new Dictionary<string, string> { { "Host", "blah" } };
		Assert.ThrowsException<InvalidOperationException>( () => Http.CreateRequest( HttpMethod.Get, "http://google.com", headers ) );
	}

	[TestMethod]
	public async Task GetString()
	{
		var str = await Http.RequestStringAsync( "https://google.com" );

		Assert.IsFalse( string.IsNullOrEmpty( str ) );
	}


	[TestMethod]
	public async Task GetBytes()
	{
		var bytes = await Http.RequestBytesAsync( "https://google.com/logos/doodles/2023/zofia-nasierowskas-85th-birthday-6753651837109862.2-l.webp" );

		Assert.IsNotNull( bytes );
		Assert.IsTrue( bytes.Length > 0 );
	}
}
