using Sandbox.Modals;

namespace UITests;

/// <summary>
/// Game.Overlay.OpenUrl only lets http(s) links through to the menu, normalized, and drops anything else.
/// </summary>
[TestClass]
[DoNotParallelize] // Swaps IModalSystem.Current
public class OverlayOpenUrlTests
{
	[TestMethod]
	[DataRow( "https://discord.gg/abc", "https://discord.gg/abc" )]
	[DataRow( "http://example.com/path?q=1", "http://example.com/path?q=1" )]
	[DataRow( "HTTPS://Discord.GG/abc", "https://discord.gg/abc" )]
	[DataRow( "https://example.com/a b", "https://example.com/a%20b" )]
	[DataRow( "https://example.com/\"onclick", "https://example.com/%22onclick" )]
	[DataRow( "https://b\u00fccher.example/", "https://xn--bcher-kva.example/" )]
	public void AcceptsHttpUrls( string url, string expected )
	{
		Assert.IsTrue( Game.Overlay.TryGetExternalUrl( url, out var safeUrl ) );
		Assert.AreEqual( expected, safeUrl );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	[DataRow( "   " )]
	[DataRow( "javascript:alert(1)" )]
	[DataRow( "file:///C:/Windows/System32/calc.exe" )]
	[DataRow( "steam://run/1" )]
	[DataRow( "C:\\Windows\\notepad.exe" )]
	[DataRow( "\\\\server\\share\\evil.exe" )]
	[DataRow( "/usr/bin/evil" )]
	[DataRow( "discord.gg/abc" )]
	[DataRow( "https://user@evil.com" )]
	[DataRow( "https://discord.gg@evil.com/abc" )]
	[DataRow( "https://user:pass@evil.com/" )]
	[DataRow( "ftp://example.com/file" )]
	[DataRow( "mailto:someone@example.com" )]
	[DataRow( "https://\u200D.com/" )]
	public void RejectsOtherUrls( string url )
	{
		Assert.IsFalse( Game.Overlay.TryGetExternalUrl( url, out var safeUrl ) );
		Assert.IsNull( safeUrl );
	}

	[TestMethod]
	public void RejectsVeryLongUrls()
	{
		var url = "https://example.com/" + new string( 'a', 3000 );
		Assert.IsFalse( Game.Overlay.TryGetExternalUrl( url, out _ ) );
	}

	[TestMethod]
	public void OnlyValidUrlsReachTheModalSystem()
	{
		var previous = IModalSystem.Current;
		var modals = new RecordingModalSystem();
		IModalSystem.Current = modals;

		try
		{
			Game.Overlay.OpenUrl( "file:///C:/Windows/System32/calc.exe" );
			Game.Overlay.OpenUrl( "https://discord.gg@evil.com" );
			Game.Overlay.OpenUrl( null );
			Assert.AreEqual( 0, modals.OpenedUrls.Count );

			Game.Overlay.OpenUrl( "HTTPS://Discord.GG/abc" );
			CollectionAssert.AreEqual( new[] { "https://discord.gg/abc" }, modals.OpenedUrls );
		}
		finally
		{
			IModalSystem.Current = previous;
		}
	}

	[TestMethod]
	public void NoModalSystemIsANoOp()
	{
		var previous = IModalSystem.Current;
		IModalSystem.Current = null;

		try
		{
			Game.Overlay.OpenUrl( "https://discord.gg/abc" );
		}
		finally
		{
			IModalSystem.Current = previous;
		}
	}
}
