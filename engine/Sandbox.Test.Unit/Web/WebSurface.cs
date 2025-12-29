using System;

namespace Web;

[TestClass]
public class WebSurfaceTests
{
	[TestMethod]
	[DataRow( "http://google.com", true )]
	[DataRow( "https://google.com", true )]
	[DataRow( "https://api.google.com", true )]
	[DataRow( "https://yahoo.com", true )]
	[DataRow( "http://127.0.0.1", false )]
	[DataRow( "http://127.0.0.1:80", false )]
	[DataRow( "http://127.0.0.1:443", false )]
	[DataRow( "http://127.0.0.1:8080", false )]
	[DataRow( "http://127.0.0.1:8443", false )]
	[DataRow( "http://127.0.0.1:1337", false )]
	[DataRow( "https://localhost/", false )]
	[DataRow( "https://localhost:80/", false )]
	[DataRow( "https://localhost:443/", false )]
	[DataRow( "https://localhost:8080/", false )]
	[DataRow( "https://localhost:8443/", false )]
	[DataRow( "https://localhost:1337/", false )]
	[DataRow( "https://8.8.8.8/", false )]
	[DataRow( "https://192.168.1.1/", false )]
	[DataRow( "https://127-0-0-1.mattstevens.co.uk/", false )]
	[DataRow( "https://10-0-0-1.mattstevens.co.uk/", false )]
	[DataRow( "https://192-168-1-1.mattstevens.co.uk/", false )]
	[DataRow( "file://blah", false )]
	[DataRow( "https://store.steampowered.com/", false )]
	public void IsUriAllowed( string uri, bool shouldPass )
	{
		if ( shouldPass )
			WebSurface.CheckUrlIsAllowed( new Uri( uri, UriKind.Absolute ) );
		else
			Assert.ThrowsException<InvalidOperationException>( () => WebSurface.CheckUrlIsAllowed( new Uri( uri, UriKind.Absolute ) ) );
	}
}
