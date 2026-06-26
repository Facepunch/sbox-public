using System;
using System.Collections.Generic;
using System.IO;
using Editor;
using Sandbox.Services;

namespace PackageTests;

/// <summary>
/// Tests for <see cref="CloudAssetDirectory"/> - the on-disk LiteDB directory that remembers which
/// cloud package each loose file under .sbox/cloud belongs to. The headline scenario fetches real
/// packages of every type, stores them, closes the database and re-opens it to confirm they load
/// back. A package carrying a self-referencing latest-news post (which real cloud packages routinely
/// do) forms a Package -> News -> Package reference cycle that overflows LiteDB's nested-document
/// depth limit during serialization, so the news-cycle tests reproduce that crash deterministically.
/// </summary>
[TestClass]
public class CloudAssetDirectoryTest
{
	/// <summary>
	/// A fixed timestamp with second precision, so date fields survive LiteDB's date serialization
	/// without needing tolerance checks.
	/// </summary>
	static readonly DateTimeOffset Stamp = new( 2021, 6, 1, 12, 0, 0, TimeSpan.Zero );

	/// <summary>
	/// Per-test scratch folder for the LiteDB database files.
	/// </summary>
	static string TestDir => Path.Combine( Environment.CurrentDirectory, ".source2", "test_cloud_asset_directory" );

	/// <summary>
	/// Wipes and recreates the scratch folder before each test so on-disk databases never leak
	/// between tests.
	/// </summary>
	[TestInitialize]
	public void TestInitialize()
	{
		if ( Directory.Exists( TestDir ) )
			Directory.Delete( TestDir, true );

		Directory.CreateDirectory( TestDir );
	}

	/// <summary>
	/// Returns a unique database path within the scratch folder for the given test.
	/// </summary>
	static string DbPath( string name ) => Path.Combine( TestDir, $"{name}.db" );

	/// <summary>
	/// Builds a remote package with a single revision and no registered files. The revision's Meta
	/// carries the PrimaryAsset so we can prove the JSON metadata survives the round-trip.
	/// </summary>
	static RemotePackage MakePackage( string org, string ident, string typeName, long versionId, string meta = "{}" )
	{
		return new RemotePackage
		{
			Org = new Package.Organization { Ident = org, Title = $"{org} org", Thumb = $"https://example.com/{org}.png" },
			Ident = ident,
			Title = $"{ident} title",
			Summary = $"{ident} summary",
			Thumb = $"https://example.com/{ident}.png",
			TypeName = typeName,
			Tags = new[] { "alpha", "beta" },
			Public = true,
			Created = Stamp,
			Updated = Stamp,
			Version = new PackageRevision
			{
				AssetVersionId = versionId,
				FileCount = 0,
				TotalSize = 0,
				Created = Stamp,
				EngineVersion = 7,
				ManifestUrl = "https://example.com/manifest.json",
				Meta = meta,
				Changes = "initial",
			},
		};
	}

	/// <summary>
	/// Attaches a latest-news post whose Package points back at the package itself, reproducing the
	/// exact Package -> News -> Package reference cycle real cloud packages carry. News.From resolves
	/// its Package through the static package cache, so the package is cached under its own ident
	/// first; the assertion guards against the cycle silently failing to form.
	/// </summary>
	static RemotePackage WithSelfReferencingNews( RemotePackage package )
	{
		Package.Cache( package, false );

		var dto = new NewsPostDto
		{
			Id = Guid.NewGuid(),
			Created = Stamp,
			Title = "Update",
			Summary = "We changed things",
			Url = "https://sbox.game/news/update",
			Author = null,
			Package = package.FullIdent,
			Media = null,
			Sections = Array.Empty<NewsSectionDto>(),
		};

		var news = News.From( dto );
		Assert.IsNotNull( news.Package, "Setup failed: news did not resolve back to its package, so no reference cycle was formed." );

		package.LatestNewsPost = news;
		return package;
	}

	/// <summary>
	/// The headline scenario: fetch a real package of every asset-party type from the cloud, store
	/// them all in a directory, close it, then re-open from disk and confirm each package loads back
	/// with its identity, type and revision intact. Real packages routinely carry a self-referencing
	/// latest-news post, so storing them trips the LiteDB circular-reference error in AddPackage -
	/// this reproduces it against live data.
	/// </summary>
	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task FetchEveryType_StoreCloseReopen_LoadsBack()
	{
		var idents = new[]
		{
			"facepunch.flatgrass",      // map
			"facepunch.sandbox",        // game
			"facepunch.watermelon",     // model
			"facepunch.glass_a",        // material
			"igrotronika.explosion2",   // sound
			"facepunch.shatterglass",   // library
			"ducksworkshop.mobbosstop", // clothing
			"wmfg.powerkart",           // prefab
		};

		var expected = new List<(string ident, string typeName, long version)>();
		var path = DbPath( "every_type" );

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			foreach ( var ident in idents )
			{
				var package = await Package.FetchAsync( ident, false );

				Assert.IsNotNull( package, $"Failed to fetch {ident} from the cloud" );
				Assert.IsNotNull( package.Revision, $"{ident} came back without a revision" );

				directory.AddPackage( package );

				expected.Add( (package.FullIdent, package.TypeName, package.Revision.VersionId) );
			}
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			foreach ( var (ident, typeName, version) in expected )
			{
				var loaded = directory.FindPackage( ident );

				Assert.IsNotNull( loaded, $"{ident} did not survive the save/reopen round-trip" );
				Assert.AreEqual( ident, loaded.FullIdent );
				Assert.AreEqual( typeName, loaded.TypeName );
				Assert.IsNotNull( loaded.Revision, $"{ident} lost its revision on reopen" );
				Assert.AreEqual( version, loaded.Revision.VersionId );
			}
		}
	}

	/// <summary>
	/// A package carrying a self-referencing latest-news post must still store, persist and load back.
	/// This is the deterministic regression guard for the circular-reference crash: with the bug
	/// present AddPackage throws a LiteDB depth exception here; once the cycle is no longer serialized
	/// the package round-trips and PrimaryAsset is still readable from the stored revision metadata.
	/// </summary>
	[TestMethod]
	public void StorePackageWithSelfReferencingNews_RoundTrips()
	{
		var package = WithSelfReferencingNews(
			MakePackage( "clouddirtest", "withnews", "model", 1234, "{\"PrimaryAsset\":\"models/withnews.vmdl\"}" ) );

		var path = DbPath( "with_news" );

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			directory.AddPackage( package );
			Assert.IsNotNull( directory.FindPackage( package.FullIdent ) );
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			var loaded = directory.FindPackage( "clouddirtest.withnews" );

			Assert.IsNotNull( loaded, "Package with a news post did not survive reopen" );
			Assert.AreEqual( "clouddirtest.withnews", loaded.FullIdent );
			Assert.AreEqual( 1234L, loaded.Revision.VersionId );
			Assert.AreEqual( "models/withnews.vmdl", loaded.PrimaryAsset );
		}
	}

	/// <summary>
	/// A package with no news post survives save/close/reopen with the fields the directory is
	/// contracted to preserve intact. The directory stores a deliberately minimal record, so this
	/// checks exactly that set - identity, the display fields consumers read off installed packages,
	/// the revision, and the metadata JSON behind PrimaryAsset - rather than every field on Package.
	/// </summary>
	[TestMethod]
	public void StorePackageWithoutNews_PreservesStoredFields()
	{
		var package = MakePackage( "clouddirtest", "plain", "material", 99, "{\"PrimaryAsset\":\"materials/plain.vmat\"}" );
		var path = DbPath( "plain" );

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			directory.AddPackage( package );
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			var loaded = directory.FindPackage( package.FullIdent );

			Assert.IsNotNull( loaded );
			Assert.AreEqual( package.FullIdent, loaded.FullIdent );
			Assert.AreEqual( package.Org.Ident, loaded.Org.Ident );
			Assert.AreEqual( package.Org.Title, loaded.Org.Title );
			Assert.AreEqual( package.Ident, loaded.Ident );
			Assert.AreEqual( package.Title, loaded.Title );
			Assert.AreEqual( package.Summary, loaded.Summary );
			Assert.AreEqual( package.Thumb, loaded.Thumb );
			Assert.AreEqual( package.Org.Thumb, loaded.Org.Thumb );
			Assert.AreEqual( package.TypeName, loaded.TypeName );
			Assert.AreEqual( Stamp.ToUnixTimeSeconds(), loaded.Created.ToUnixTimeSeconds() );
			CollectionAssert.AreEqual( package.Tags, loaded.Tags );

			Assert.IsNotNull( loaded.Revision );
			Assert.AreEqual( 99L, loaded.Revision.VersionId );
			Assert.AreEqual( 7, loaded.Revision.EngineVersion );
			Assert.AreEqual( "materials/plain.vmat", loaded.PrimaryAsset );
		}
	}

	/// <summary>
	/// FindPackage(ident) returns the package from the in-memory cache within the same session, and
	/// GetPackages reflects the addition, before any save or reopen.
	/// </summary>
	[TestMethod]
	public void AddPackage_IsFoundInSameSession()
	{
		var package = MakePackage( "clouddirtest", "samesession", "addon", 5 );
		var path = DbPath( "same_session" );

		using var directory = new CloudAssetDirectory( path );
		directory.AddPackage( package );

		var found = directory.FindPackage( package.FullIdent );

		Assert.IsNotNull( found );
		Assert.AreEqual( package.FullIdent, found.FullIdent );
		Assert.AreEqual( 1, directory.GetPackages().Count );
	}

	/// <summary>
	/// RemovePackage drops the package from both the in-memory cache and the on-disk database, so a
	/// later reopen no longer finds it.
	/// </summary>
	[TestMethod]
	public void RemovePackage_RemovesFromCacheAndDisk()
	{
		var package = MakePackage( "clouddirtest", "removable", "addon", 8 );
		var path = DbPath( "removable" );

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			directory.AddPackage( package );
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			directory.RemovePackage( package );
			Assert.IsNull( directory.FindPackage( package.FullIdent ), "Package still present in cache after removal" );
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			Assert.IsNull( directory.FindPackage( package.FullIdent ), "Package came back after reopen, so it was not removed from disk" );
		}
	}

	/// <summary>
	/// AddFile registers files against a package's current revision and GetPackageFiles returns them.
	/// Exercised within a single session so it does not depend on the files existing on disk for the
	/// reopen validation.
	/// </summary>
	[TestMethod]
	public void AddFile_GetPackageFiles_ReturnsRegisteredFiles()
	{
		var package = MakePackage( "clouddirtest", "withfiles", "addon", 3 );
		var path = DbPath( "with_files" );

		using var directory = new CloudAssetDirectory( path );
		directory.AddPackage( package );

		directory.AddFile( "/cloud/withfiles/model.vmdl_c", "crc-a", 100, package.FullIdent, package.Revision.VersionId );
		directory.AddFile( "/cloud/withfiles/texture.vtex_c", "crc-b", 200, package.FullIdent, package.Revision.VersionId );

		var files = directory.GetPackageFiles( package ).ToList();

		Assert.AreEqual( 2, files.Count );
	}

	/// <summary>
	/// Re-adding a package at a newer revision purges the file records tied to the old revision,
	/// keeping the directory in sync with what is actually installed.
	/// </summary>
	[TestMethod]
	public void AddPackage_NewerRevision_PurgesStaleFiles()
	{
		var v1 = MakePackage( "clouddirtest", "versioned", "addon", 1 );
		var path = DbPath( "stale_files" );

		using var directory = new CloudAssetDirectory( path );
		directory.AddPackage( v1 );
		directory.AddFile( "/cloud/versioned/old.vmdl_c", "crc-old", 100, v1.FullIdent, 1 );

		Assert.AreEqual( 1, directory.GetPackageFiles( v1 ).Count() );

		var v2 = MakePackage( "clouddirtest", "versioned", "addon", 2 );
		directory.AddPackage( v2 );

		Assert.AreEqual( 0, directory.GetPackageFiles( v1 ).Count(), "Old-revision files were not purged when the package updated" );
	}

	/// <summary>
	/// FindPackage returns null for idents that are empty or cannot be parsed, rather than throwing.
	/// </summary>
	[TestMethod]
	public void FindPackage_InvalidIdent_ReturnsNull()
	{
		var path = DbPath( "invalid_ident" );
		using var directory = new CloudAssetDirectory( path );

		string nullIdent = null;

		Assert.IsNull( directory.FindPackage( nullIdent ) );
		Assert.IsNull( directory.FindPackage( "" ) );
		Assert.IsNull( directory.FindPackage( "not-a-valid-ident" ) );
	}

	/// <summary>
	/// The path-based lookup used to resolve Asset.Package fast-rejects any absolute path that is not
	/// inside the cloud folder, returning null without touching the database or filesystem.
	/// </summary>
	[TestMethod]
	public void FindPackageByPath_OutsideCloudFolder_ReturnsNull()
	{
		var path = DbPath( "by_path" );
		using var directory = new CloudAssetDirectory( path );

		Assert.IsNull( directory.FindPackage( "C:/projects/mygame/models/thing.vmdl_c", "models/thing.vmdl_c" ) );
	}
}
