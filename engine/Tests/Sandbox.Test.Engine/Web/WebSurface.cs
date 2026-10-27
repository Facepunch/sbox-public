using System;

namespace WebTests;

[TestClass]
public class WebSurfaceTest
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

	[TestMethod]
	[DataRow( 86u, KeyboardModifiers.Ctrl, true )] // Ctrl+V
	[DataRow( 67u, KeyboardModifiers.Ctrl, true )] // Ctrl+C
	[DataRow( 88u, KeyboardModifiers.Ctrl, true )] // Ctrl+X
	[DataRow( 45u, KeyboardModifiers.Ctrl, true )] // Ctrl+Insert
	[DataRow( 45u, KeyboardModifiers.Shift, true )] // Shift+Insert
	[DataRow( 46u, KeyboardModifiers.Shift, true )] // Shift+Delete
	[DataRow( 86u, KeyboardModifiers.Ctrl | KeyboardModifiers.Shift, true )]
	[DataRow( 45u, KeyboardModifiers.Ctrl | KeyboardModifiers.Alt, true )]
	[DataRow( 45u, KeyboardModifiers.Shift | KeyboardModifiers.Alt, true )]
	[DataRow( 46u, KeyboardModifiers.Ctrl | KeyboardModifiers.Shift, true )]
	[DataRow( 45u, KeyboardModifiers.None, false )] // Insert
	[DataRow( 46u, KeyboardModifiers.None, false )] // Delete
	[DataRow( 46u, KeyboardModifiers.Ctrl, false )] // Ctrl+Delete
	[DataRow( 86u, KeyboardModifiers.Shift, false )] // Shift+V
	[DataRow( 86u, KeyboardModifiers.None, false )] // V
	[DataRow( 45u, KeyboardModifiers.Alt, false )] // Alt+Insert
	[DataRow( 65u, KeyboardModifiers.Ctrl, false )] // Ctrl+A
	[DataRow( 65u, KeyboardModifiers.Shift, false )] // Shift+A
	public void IsClipboardShortcut( uint virtualKeyCode, KeyboardModifiers modifiers, bool isClipboard )
	{
		Assert.AreEqual( isClipboard, WebSurface.IsClipboardShortcut( virtualKeyCode, modifiers ) );
	}
}
