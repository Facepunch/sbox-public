using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Modals;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UITests;

[TestClass]
[DoNotParallelize] // Swaps IModalSystem.Current
public class OverlayContextTests
{
	static readonly List<GlobalContext> Observed = new();

	IModalSystem previous;
	RecordingModalSystem modals;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		Observed.Clear();
		previous = IModalSystem.Current;
		modals = new RecordingModalSystem();
		IModalSystem.Current = modals;
	}

	[TestCleanup]
	public void Cleanup()
	{
		IModalSystem.Current = previous;
	}

	static void AssertNeverRanInMenu()
	{
		Assert.IsFalse( Observed.Contains( GlobalContext.Menu ), "Game code ran in the menu context" );
	}

	[TestMethod]
	public void PackageModalsOnlyTakeEnginePackages()
	{
		var org = new Package.Organization { Ident = "regressiontest" };
		var planted = new GamePackage { Org = org, Ident = "overlay" };
		var engine = new RemotePackage { Org = org, Ident = "overlay" };

		Game.Overlay.CreateGame( new CreateGameOptions( planted, _ => { } ) );

		Assert.AreEqual( 0, modals.Packages.Count, "The menu was handed the game's Package" );

		Game.Overlay.CreateGame( new CreateGameOptions( engine, _ => Observed.Add( GlobalContext.Current ) ) );

		CollectionAssert.AreEqual( new[] { engine }, modals.Packages );
		Assert.AreEqual( 1, modals.CompletedGames );
		AssertNeverRanInMenu();
	}

	/// <summary>
	/// Existing addons can call the retired review APIs and still read package votes independently.
	/// </summary>
	[TestMethod]
	public async Task LegacyReviewsReturnEmptyResultsWithoutOpeningAModal()
	{
		var package = new RemotePackage { VotesUp = 7, VotesDown = 2 };
		const string ident = "regressiontest.overlay";

#pragma warning disable CS0618 // Exercise the signatures used by existing addons.
		Game.Overlay.ShowReviewModal( package );
		await MenuUtility.PostReview( ident, Sandbox.Services.Review.ReviewScore.Positive, "test", default, default );

		Assert.IsNull( await Sandbox.Services.Review.Get( ident, default ) );
		Assert.AreEqual( 0, (await Sandbox.Services.Review.Fetch( ident )).Length );
		Assert.AreEqual( 0, (await Sandbox.Services.Review.FetchEx( ident, 10, 0 )).Posts.Length );
		Assert.AreEqual( 0, package.Reviews.Total );
		Assert.AreEqual( 0, package.Reviews.PositiveTags.Count );
		Assert.AreEqual( 0, package.Reviews.NegativeTags.Count );
#pragma warning restore CS0618

		Assert.AreEqual( 0, modals.Packages.Count );
		Assert.AreEqual( 7, package.VotesUp );
		Assert.AreEqual( 2, package.VotesDown );
	}

	[TestMethod]
	public void CallbacksRunInTheCallersContext()
	{
		var origin = GlobalContext.Current;
		static void Record<T>( T _ ) => Observed.Add( GlobalContext.Current );

		Game.Overlay.ShowPackageSelector( "type:map", Record, Record );
		Game.Overlay.ShowMapSelector( Record );
		Game.Overlay.WorkshopPublish( new WorkshopPublishOptions { OnComplete = Record } );

		Assert.AreEqual( 4, Observed.Count );
		Assert.IsTrue( Observed.All( x => x == origin ), "A callback ran outside the caller's context" );
	}

	[TestMethod]
	public void WorkshopCategoriesAreRebuiltByTheEngine()
	{
		var options = new WorkshopPublishOptions();
		options.AddCategory<Storage.Visibility>( "kind" );
		options.AddCategory<Storage.Visibility>( "Movement", "Mouvement" );
		options.Categories["planted"] = GlobalContext.Current.TypeLibrary.CreateProperty<Storage.Visibility>( "planted",
			() => { Observed.Add( GlobalContext.Current ); return default; },
			_ => Observed.Add( GlobalContext.Current ) );

		Game.Overlay.WorkshopPublish( options );

		CollectionAssert.AreEquivalent( new[] { "kind", "Mouvement" }, modals.CategoryNames );
		modals.PublishOptions.Categories["Mouvement"].SetValue( Storage.Visibility.Private );
		Assert.AreEqual( "Private", modals.PublishOptions.KeyValues["Movement"] );
		Assert.IsFalse( modals.PublishOptions.KeyValues.ContainsKey( "Mouvement" ) );
		AssertNeverRanInMenu();
	}

	[TestMethod]
	public void WorkshopCategoriesSkipEnumsWithGameAttributes()
	{
		const string source = """
			public sealed class GameTitleAttribute : System.Attribute { }
			public enum GameKind { [GameTitle] First, Second }
			""";

		var loadContext = new LoadContext( typeof( Game ).Assembly );
		var probe = loadContext.LoadWithEmbeds( Compile( source ) );

		try
		{
			object options = new WorkshopPublishOptions();
			typeof( WorkshopPublishOptions ).GetMethod( nameof( WorkshopPublishOptions.AddCategory ), [typeof( string )] )
				.MakeGenericMethod( probe.GetType( "GameKind" ) )
				.Invoke( options, ["gamekind"] );

			Game.Overlay.WorkshopPublish( (WorkshopPublishOptions)options );
			Game.Overlay.WorkshopPublish( default );

			Assert.AreEqual( 0, modals.CategoryNames.Count, "The menu was handed an enum carrying game attributes" );
		}
		finally
		{
			loadContext.UnloadChild( probe );
			loadContext.Unload();
		}
	}

	static byte[] Compile( string source )
	{
		var references = AppDomain.CurrentDomain.GetAssemblies()
			.Where( x => !x.IsDynamic && !string.IsNullOrEmpty( x.Location ) )
			.GroupBy( x => x.GetName().Name )
			.Select( x => MetadataReference.CreateFromFile( x.First().Location ) );

		var compilation = CSharpCompilation.Create( "OverlayProbe", [CSharpSyntaxTree.ParseText( source )], references, new( OutputKind.DynamicallyLinkedLibrary ) );

		using var stream = new MemoryStream();
		var result = compilation.Emit( stream );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );
		return stream.ToArray();
	}

	[TestMethod]
	public void BenchmarkResultsAreCopiedOutOfTheGamesList()
	{
		Game.Overlay.ShowBenchmarkResults( Guid.NewGuid(), new GameList() );

		Assert.AreEqual( 1, modals.SummaryCount );
		AssertNeverRanInMenu();
	}

	sealed class GamePackage : Package
	{
		public override bool IsRemote
		{
			get
			{
				Observed.Add( GlobalContext.Current );
				return true;
			}
		}

		public override string[] Tags
		{
			get
			{
				Observed.Add( GlobalContext.Current );
				return [];
			}
			set { }
		}
	}

	sealed class GameList : IReadOnlyList<BenchmarkTestSummary>
	{
		public BenchmarkTestSummary this[int index]
		{
			get
			{
				Observed.Add( GlobalContext.Current );
				return new BenchmarkTestSummary { Name = "test" };
			}
		}

		public int Count
		{
			get
			{
				Observed.Add( GlobalContext.Current );
				return 1;
			}
		}

		public IEnumerator<BenchmarkTestSummary> GetEnumerator()
		{
			Observed.Add( GlobalContext.Current );
			yield return new BenchmarkTestSummary { Name = "test" };
		}

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}

/// <summary>
/// Records what the overlay hands the menu, touching packages and running callbacks like the real menu would.
/// </summary>
sealed class RecordingModalSystem : IModalSystem
{
	public List<Package> Packages { get; } = new();
	public List<string> OpenedUrls { get; } = new();
	public int SummaryCount { get; private set; }
	public int CompletedGames { get; private set; }
	public List<string> CategoryNames { get; } = new();

	/// <summary>
	/// The options received by the menu after the overlay rebuilds the category controls.
	/// </summary>
	public WorkshopPublishOptions PublishOptions { get; private set; }

	void Use( Package package )
	{
		if ( package is null ) return;

		Packages.Add( package );
		_ = package.IsRemote;
		_ = package.Tags;
	}

	[Obsolete( "Written package reviews are no longer supported." )]
	public void Review( Package package ) => Use( package );

	public void CreateGame( in CreateGameOptions options )
	{
		Use( options.Package );
		CompletedGames++;
		options.OnComplete?.Invoke( default );
	}

	public void OpenUrl( string url ) => OpenedUrls.Add( url );

	public void PackageSelect( string query, Action<Package> onPackageSelected, Action<string> onFilterChanged = null )
	{
		onPackageSelected?.Invoke( null );
		onFilterChanged?.Invoke( "filter" );
	}

	public void MapSelect( Action<string> onMapSelected, string selected = null ) => onMapSelected?.Invoke( "map" );

	public void WorkshopPublish( in WorkshopPublishOptions options )
	{
		PublishOptions = options;

		foreach ( var (name, property) in options.Categories )
		{
			CategoryNames.Add( name );
			_ = property.GetValue<object>();
		}

		options.OnComplete?.Invoke( 1 );
	}

	public void BenchmarkResults( Guid batchId, IReadOnlyList<BenchmarkTestSummary> summaries )
	{
		foreach ( var _ in summaries ) SummaryCount++;
	}

	public bool HasModalsOpen() => false;
	public void CloseAll( bool immediate = false ) { }
	public void Game( string packageIdent ) { }
	public void Map( string packageIdent ) { }
	public void Package( string packageIdent, string page ) { }
	public void Report( string packageIdent ) { }
	public void FriendsList( in FriendsListModalOptions options ) { }
	public void Server( Sandbox.Network.LobbyInformation lobby ) { }
	public void ServerList( in ServerListConfig config ) { }
	public void Settings( string page = "" ) { }
	public void Player( SteamId steamid, string page = "" ) { }
	public void News( Sandbox.Services.News newsitem ) { }
	public void PlayerList() { }
	public void Notice( string title, string message, string icon ) { }
	public Task<string> ServerPassword( string serverName ) => Task.FromResult<string>( null );
	public void ServiceConnector() { }
	public void PauseMenu() { }
	public bool IsModalOpen => false;
	public bool IsPauseMenuOpen => false;
}
