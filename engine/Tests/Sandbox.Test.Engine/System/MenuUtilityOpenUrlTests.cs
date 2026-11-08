using System;
using System.Reflection;

namespace SystemTests;

[TestClass]
public class MenuUtilityOpenUrlTests
{
	static bool TryGetOpenableUrl( string path, out string url )
	{
		var method = typeof( MenuUtility ).GetMethod( "TryGetOpenableUrl", BindingFlags.NonPublic | BindingFlags.Static );
		Assert.IsNotNull( method, "MenuUtility.OpenUrl has no url filter" );

		var args = new object[] { path, null };
		var result = (bool)method.Invoke( null, args );
		url = (string)args[1];
		return result;
	}

	[TestMethod]
	[DataRow( @"C:\Windows\System32\calc.exe" )]
	[DataRow( @"C:\Users\Public\payload.bat" )]
	[DataRow( @"\\server\share\payload.exe" )]
	[DataRow( "file:///C:/Windows/System32/calc.exe" )]
	[DataRow( "calc" )]
	[DataRow( "ms-settings:privacy" )]
	[DataRow( "steam://run/4000//-applaunch" )]
	[DataRow( "steam://rungameid/4000" )]
	[DataRow( "javascript:alert(1)" )]
	[DataRow( "" )]
	[DataRow( null )]
	public void RejectsNonWebTargets( string path )
	{
		Assert.IsFalse( TryGetOpenableUrl( path, out _ ) );
	}

	[TestMethod]
	[DataRow( "https://sbox.game/learn" )]
	[DataRow( "http://example.com/" )]
	[DataRow( "steam://store/4000" )]
	[DataRow( "steam://install/4000" )]
	[DataRow( "steam://url/CommunityFilePage/123456" )]
	public void AllowsWebAndKnownSteamLinks( string path )
	{
		Assert.IsTrue( TryGetOpenableUrl( path, out var url ) );
		Assert.IsFalse( string.IsNullOrEmpty( url ) );
	}

	[TestMethod]
	[DataRow( "/c/notices" )]
	[DataRow( "//evil.example/" )]
	[DataRow( "/@evil.example" )]
	public void NoticeRelativeLinkStaysOnSboxGame( string link )
	{
		Assert.IsTrue( TryGetOpenableUrl( $"https://sbox.game{link}", out var url ) );
		Assert.AreEqual( "sbox.game", new Uri( url ).Host );
	}
}
