using Sandbox.Modals;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

	sealed class RecordingModalSystem : IModalSystem
	{
		public List<string> OpenedUrls { get; } = new();

		public void OpenUrl( string url ) => OpenedUrls.Add( url );

		public bool HasModalsOpen() => false;
		public void CloseAll( bool immediate = false ) { }
		public void Game( string packageIdent ) { }
		public void Map( string packageIdent ) { }
		public void Package( string packageIdent, string page ) { }
		public void Review( Package package ) { }
		public void Report( string packageIdent ) { }
		public void PackageSelect( string query, Action<Package> onPackageSelected, Action<string> onFilterChanged = null ) { }
		public void MapSelect( Action<string> onMapSelected, string selected = null ) { }
		public void FriendsList( in FriendsListModalOptions options ) { }
		public void Server( Sandbox.Network.LobbyInformation lobby ) { }
		public void ServerList( in ServerListConfig config ) { }
		public void Settings( string page = "" ) { }
		public void CreateGame( in CreateGameOptions options ) { }
		public void Player( SteamId steamid, string page = "" ) { }
		public void News( Sandbox.Services.News newsitem ) { }
		public void PlayerList() { }
		public void WorkshopPublish( in WorkshopPublishOptions options ) { }
		public void Notice( string title, string message, string icon ) { }
		public Task<string> ServerPassword( string serverName ) => Task.FromResult<string>( null );
		public void ServiceConnector() { }
		public void PauseMenu() { }
		public void BenchmarkResults( Guid batchId, IReadOnlyList<BenchmarkTestSummary> summaries ) { }
		public bool IsModalOpen => false;
		public bool IsPauseMenuOpen => false;
	}
}
