using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Editor;
using Sandbox.Services;
using Sandbox.Tasks;

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
	static DateTimeOffset Stamp = new( 2021, 6, 1, 12, 0, 0, TimeSpan.Zero );

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

	LocalFileSystem verificationFiles;

	LocalFileSystem CreateVerificationFiles()
	{
		var folder = Path.Combine( TestDir, "files" );
		Directory.CreateDirectory( folder );
		return verificationFiles = new LocalFileSystem( folder );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		verificationFiles?.Dispose();
	}

	static ulong Crc( string text ) => Sandbox.Utility.Crc64.FromBytes( Encoding.UTF8.GetBytes( text ) );

	sealed class ReadCallbackFileSystem : Zio.FileSystems.PhysicalFileSystem
	{
		public Action OnRead { get; set; }

		protected override Stream OpenFileImpl( Zio.UPath path, FileMode mode, FileAccess access, FileShare share )
		{
			if ( access == FileAccess.Read )
			{
				var callback = OnRead;
				OnRead = null;
				callback?.Invoke();
			}

			return base.OpenFileImpl( path, mode, access, share );
		}
	}

	[TestMethod]
	public void Download_SupersededDuringVerification_DoesNotFailOrInvalidateNewRecord()
	{
		var folder = Path.Combine( TestDir, "files" );
		Directory.CreateDirectory( folder );
		using var physical = new ReadCallbackFileSystem();
		using var rooted = new Zio.FileSystems.SubFileSystem( physical, physical.ConvertPathFromInternal( folder ) );
		var fs = new BaseFileSystem( rooted );
		using var directory = new CloudAssetDirectory( DbPath( "superseded_download" ), fs );
		var previousDirectory = AssetSystem.CloudDirectory;
		var previousFileSystem = Editor.FileSystem.Cloud;
		AssetSystem.CloudDirectory = directory;
		Editor.FileSystem.Cloud = fs;

		try
		{
			AsyncPump.RunSynchronously( async () =>
			{
				var package = MakePackage( "clouddirtest", "older", "addon", 1 );
				var newer = MakePackage( "clouddirtest", "newer", "addon", 2 );
				((PackageRevision)newer.Revision).Created = Stamp.AddSeconds( 1 );
				var entry = new ManifestSchema.File { Path = $"crc_test_{Guid.NewGuid():N}/shared", Crc = "0", Size = 0 };
				// The missing file is written without a network request. Replace its record when
				// post-download hashing opens it, after the verifier captured the original record.
				physical.OnRead = () =>
				{
					Assert.IsTrue( directory.AddFile( entry.Path, "1", 0, newer ) );
					Assert.IsTrue( directory.TryGetFile( entry.Path, out var replacement ) );
					// Simulate verification already recorded by the concurrent install.
					replacement.VerifiedCrc = "0";
				};

				var download = typeof( AssetSystem ).GetMethod( "DownloadFile", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );
				var written = await (Task<bool>)download.Invoke( null, new object[] { package, entry, CancellationToken.None } );
				Assert.IsFalse( written, "A superseded download must not be registered by the older install." );
				Assert.IsTrue( directory.TryGetFile( entry.Path, out var current ) );
				Assert.AreEqual( "1", current.Crc );
				Assert.AreEqual( "0", current.VerifiedCrc, "The older install must not invalidate the replacement's verification." );
			} );
		}
		finally
		{
			AssetSystem.CloudDirectory = previousDirectory;
			Editor.FileSystem.Cloud = previousFileSystem;
			fs.Dispose();
		}
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 64 )]
	public void Install_FileFailure_IsReportedAfterOtherFilesFinish( int failedIndex )
	{
		var fs = CreateVerificationFiles();
		using var directory = new CloudAssetDirectory( DbPath( "install_failure" ), fs );
		var previousDirectory = AssetSystem.CloudDirectory;
		var previousFileSystem = Editor.FileSystem.Cloud;
		AssetSystem.CloudDirectory = directory;
		Editor.FileSystem.Cloud = fs;

		try
		{
			AsyncPump.RunSynchronously( async () =>
			{
				var package = MakePackage( "clouddirtest", "failedinstall", "addon", 1 );
				((PackageRevision)package.Revision).ManifestUrl = null;
				await package.Revision.DownloadManifestAsync( default );
				var prefix = $"crc_test_{Guid.NewGuid():N}";
				var entries = Enumerable.Range( 0, Package.MaxParallelDownloads + 2 )
					.Select( i => new ManifestSchema.File { Path = $"{prefix}/{i}.dll", Crc = "0", Size = 0 } ).ToArray();
				// Empty files need no network. One deliberately wrong CRC fails after writing, while
				// a valid file beyond the first concurrency window must still finish before we throw.
				entries[failedIndex] = new ManifestSchema.File { Path = $"{prefix}/bad", Crc = "1", Size = 0 };
				entries[^1] = new ManifestSchema.File { Path = $"{prefix}/good", Crc = "0", Size = 0 };
				package.Revision.Manifest.Files = entries;

				var error = await Assert.ThrowsExceptionAsync<AggregateException>( () => AssetSystem.InstallAsync( package, false ) );
				Assert.AreEqual( 1, error.InnerExceptions.Count );
				Assert.IsInstanceOfType<InvalidDataException>( error.InnerExceptions[0] );
				Assert.IsTrue( directory.TryGetFile( $"{prefix}/good", out var good ) );
				Assert.AreEqual( "0", good.VerifiedCrc, "The other download must finish before install failure is reported." );
				Assert.IsNull( directory.FindPackage( package.FullIdent ), "A failed package must not be marked installed." );
			} );
		}
		finally
		{
			AssetSystem.CloudDirectory = previousDirectory;
			Editor.FileSystem.Cloud = previousFileSystem;
		}
	}

	[TestMethod]
	public void DragAsset_FailedInstall_ReturnsNullAndRemainsUninstalled()
	{
		var fs = CreateVerificationFiles();
		using var directory = new CloudAssetDirectory( DbPath( "drag_failure" ), fs );
		var previousDirectory = AssetSystem.CloudDirectory;
		var previousFileSystem = Editor.FileSystem.Cloud;
		AssetSystem.CloudDirectory = directory;
		Editor.FileSystem.Cloud = fs;
		var package = MakePackage( "clouddirtest", "dragfailure", "model", 1, "{\"PrimaryAsset\":\"failed.vmdl\"}" );

		try
		{
			AsyncPump.RunSynchronously( async () =>
			{
				((PackageRevision)package.Revision).ManifestUrl = null;
				await package.Revision.DownloadManifestAsync( default );
				package.Revision.Manifest.Files = new[]
				{
					new ManifestSchema.File { Path = $"crc_test_{Guid.NewGuid():N}/bad", Crc = "1", Size = 0 }
				};
				Package.Cache( package, false );
				var drag = DragAssetData.Parse( package.FullIdent );
				Assert.IsNotNull( drag );
				Assert.IsFalse( drag.IsInstalled );

				Assert.IsNull( await drag.GetAssetAsync() );
				Assert.IsFalse( drag.IsInstalled, "A handled install failure must not make a dragged asset appear installed." );
				Assert.IsTrue( drag.DownloadProgress < 1f, "Failed downloads must not report completion." );
				Assert.IsNull( directory.FindPackage( package.FullIdent ) );
			} );
		}
		finally
		{
			Package.ClearCache( package.FullIdent );
			AssetSystem.CloudDirectory = previousDirectory;
			Editor.FileSystem.Cloud = previousFileSystem;
		}
	}

	[TestMethod]
	public async Task VerifiedCrc_SameSizeUpdate_DoesNotTrustManifestRecord()
	{
		var fs = CreateVerificationFiles();
		fs.WriteAllText( "shader", "old" );
		var package = MakePackage( "clouddirtest", "crc", "addon", 1 );
		using var directory = new CloudAssetDirectory( DbPath( "crc" ), fs );

		// Reproduce an old cache: AddFile already stored the new manifest CRC over the old bytes.
		directory.AddFile( "shader", Crc( "new" ).ToString( "x" ), 3, package );
		Assert.IsFalse( await directory.IsFileCurrentAsync( "shader", Crc( "new" ), 3 ) );
		Assert.IsTrue( directory.TryGetFile( "shader", out var file ) );
		Assert.AreEqual( Crc( "old" ).ToString( "x" ), file.VerifiedCrc );

		// Registering another manifest must not turn its expected CRC into a verified one.
		directory.AddFile( "shader", Crc( "yet" ).ToString( "x" ), 3, package );
		using var locked = System.IO.File.Open( fs.GetFullPath( "shader" ), FileMode.Open, FileAccess.Read, FileShare.None );
		Assert.IsFalse( await directory.IsFileCurrentAsync( "shader", Crc( "yet" ), 3 ) );
	}

	[TestMethod]
	public async Task VerifiedCrc_Reopen_ReusesHashWithoutReadingFile()
	{
		var fs = CreateVerificationFiles();
		fs.WriteAllText( "shader", "old" );
		var fullPath = fs.GetFullPath( "shader" );
		System.IO.File.SetLastWriteTimeUtc( fullPath, new DateTime( 2026, 1, 1, 0, 0, 0, DateTimeKind.Utc ).AddTicks( 1234 ) );
		var package = MakePackage( "clouddirtest", "crc", "addon", 1 );
		var dbPath = DbPath( "persistent_crc" );

		using ( var directory = new CloudAssetDirectory( dbPath, fs ) )
		{
			directory.AddFile( "shader", Crc( "old" ).ToString( "x" ), 3, package );
			directory.AddPackage( package );
			Assert.IsTrue( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
		}

		using ( var directory = new CloudAssetDirectory( dbPath, fs ) )
		{
			// A second read would fail. The cached verification must survive both restart and AddFile.
			using var locked = System.IO.File.Open( fullPath, FileMode.Open, FileAccess.Read, FileShare.None );
			directory.AddFile( "shader", Crc( "old" ).ToString( "x" ), 3, package );
			Assert.IsTrue( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
		}
	}

	[TestMethod]
	public async Task VerifiedCrc_ChangedTimestamp_RehashesSameSizeFile()
	{
		var fs = CreateVerificationFiles();
		fs.WriteAllText( "shader", "old" );
		using var directory = new CloudAssetDirectory( DbPath( "changed_crc" ), fs );
		directory.AddFile( "shader", Crc( "old" ).ToString( "x" ), 3, MakePackage( "clouddirtest", "crc", "addon", 1 ) );
		Assert.IsTrue( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );

		var fullPath = fs.GetFullPath( "shader" );
		var changedTime = System.IO.File.GetLastWriteTimeUtc( fullPath ).AddSeconds( 1 );
		fs.WriteAllText( "shader", "new" );
		System.IO.File.SetLastWriteTimeUtc( fullPath, changedTime );
		Assert.IsFalse( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
		Assert.IsTrue( await directory.IsFileCurrentAsync( "shader", Crc( "new" ), 3 ) );
	}

	[TestMethod]
	public async Task VerifiedCrc_InvalidatedDownload_DoesNotReuseOldHashAfterRestart()
	{
		var fs = CreateVerificationFiles();
		fs.WriteAllText( "shader", "old" );
		var package = MakePackage( "clouddirtest", "crc", "addon", 1 );
		var dbPath = DbPath( "failed_download_crc" );
		var fullPath = fs.GetFullPath( "shader" );
		var originalTime = System.IO.File.GetLastWriteTimeUtc( fullPath );

		using ( var directory = new CloudAssetDirectory( dbPath, fs ) )
		{
			directory.AddFile( "shader", Crc( "old" ).ToString( "x" ), 3, package );
			directory.AddPackage( package );
			Assert.IsTrue( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
			directory.InvalidateFileVerification( "shader" );
			// Simulate a failed replacement that even leaves matching size and timestamp behind.
			fs.WriteAllText( "shader", "bad" );
			System.IO.File.SetLastWriteTimeUtc( fullPath, originalTime );
		}

		using ( var directory = new CloudAssetDirectory( dbPath, fs ) )
		{
			Assert.IsFalse( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
			// A successful retry is hashed and can then use the fast path.
			directory.InvalidateFileVerification( "shader" );
			fs.WriteAllText( "shader", "old" );
			Assert.IsTrue( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
			using var locked = System.IO.File.Open( fullPath, FileMode.Open, FileAccess.Read, FileShare.None );
			Assert.IsTrue( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
		}
	}

	[TestMethod]
	public async Task VerifiedCrc_MissingOrWrongSize_SkipsHashing()
	{
		var fs = CreateVerificationFiles();
		using var directory = new CloudAssetDirectory( DbPath( "size_crc" ), fs );
		directory.AddFile( "shader", Crc( "old" ).ToString( "x" ), 3, MakePackage( "clouddirtest", "crc", "addon", 1 ) );
		Assert.IsFalse( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
		fs.WriteAllText( "shader", "longer" );
		using var locked = System.IO.File.Open( fs.GetFullPath( "shader" ), FileMode.Open, FileAccess.Read, FileShare.None );
		Assert.IsFalse( await directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3 ) );
	}

	[TestMethod]
	public async Task VerifiedCrc_CancelledCheck_DoesNotRecordVerification()
	{
		var fs = CreateVerificationFiles();
		fs.WriteAllText( "shader", "old" );
		using var directory = new CloudAssetDirectory( DbPath( "cancelled_crc" ), fs );
		directory.AddFile( "shader", Crc( "old" ).ToString( "x" ), 3, MakePackage( "clouddirtest", "crc", "addon", 1 ) );
		await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => directory.IsFileCurrentAsync( "shader", Crc( "old" ), 3, new CancellationToken( true ) ) );
		Assert.IsTrue( directory.TryGetFile( "shader", out var file ) );
		Assert.IsNull( file.VerifiedCrc );
	}

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

		directory.AddFile( "/cloud/withfiles/model.vmdl_c", "crc-a", 100, package );
		directory.AddFile( "/cloud/withfiles/texture.vtex_c", "crc-b", 200, package );

		var files = directory.GetPackageFiles( package ).ToList();

		Assert.AreEqual( 2, files.Count );
	}

	/// <summary>
	/// Package lookup from file
	/// </summary>
	[TestMethod]
	public void AddFile_GetPackages()
	{
		var path = DbPath( "get_package" );
		using var directory = new CloudAssetDirectory( path );

		var packageA = MakePackage( "clouddirtest", "one", "material", 99 );
		var packageB = MakePackage( "clouddirtest", "two", "material", 99, "{\"PrimaryAsset\":\"materials/plain.vmat\"}" );
		var packageC = MakePackage( "clouddirtest", "three", "material", 99 );

		directory.AddPackage( packageA );
		directory.AddPackage( packageB );
		directory.AddPackage( packageC );

		directory.AddFile( "materials/plain.vmat", "crc-a", 100, packageA );
		directory.AddPackageLink( "materials/plain.vmat", packageB );
		directory.AddPackageLink( "materials/plain.vmat", packageC );

		// FindPackage - multiple are registed, but should return the one where it's a PrimaryAsset
		var package = directory.FindPackage( ".sbox/cloud/materials/plain.vmat", "materials/plain.vmat" );
		Assert.AreEqual( package, packageB );

		// FindPackages returns all packages that share the file
		var packages = directory.FindPackages( ".sbox/cloud/materials/plain.vmat", "materials/plain.vmat" );
		Assert.AreEqual( packages.Count(), 3 );
	}

	/// <summary>
	/// When encountering a shared file with different versions, AddFile keeps the file from the package with the newer revision
	/// </summary>
	[TestMethod]
	public void AddFile_VersionConflict()
	{
		var path = DbPath( "add_ver_conflict" );
		using var directory = new CloudAssetDirectory( path );

		var packageB = MakePackage( "clouddirtest", "two", "material", 99 );
		Stamp = Stamp.AddSeconds( 1 ); // make sure the revision is newer
		var packageA = MakePackage( "clouddirtest", "one", "material", 99 );

		directory.AddPackage( packageA );
		directory.AddPackage( packageB );

		Assert.IsTrue( directory.AddFile( "materials/plain.vmat", "crc-a", 100, packageA ) );
		Assert.IsFalse( directory.AddFile( "materials/plain.vmat", "crc-b", 200, packageB ) );

		Assert.IsTrue( directory.TryGetFile( "materials/plain.vmat", out var file ) );
		Assert.AreEqual( file.Size, 100 ); // the file from the newer revision is what's kept
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
		directory.AddFile( "/cloud/versioned/old.vmdl_c", "crc-old", 100, v1 );

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

	/// <summary>
	/// A file inside the cloud folder that no package claims is an orphan left behind by an older
	/// revision. The lookup deletes it so the "unable to determine package" warning doesn't repeat
	/// on every scan. The absolute path arrives in native form (e.g. "C:/..."), which has to stay
	/// deletable on Windows after normalization prefixes it with a slash.
	/// </summary>
	[TestMethod]
	public void FindPackageByPath_UnclaimedCloudFile_DeletesFile()
	{
		var cloudFolder = Path.Combine( TestDir, ".sbox", "cloud" );
		Directory.CreateDirectory( Path.Combine( cloudFolder, "jutsu" ) );
		var orphan = Path.Combine( cloudFolder, "jutsu", "orphan.jutsu" );
		System.IO.File.WriteAllText( orphan, "left over from an older revision" );

		using var directory = new CloudAssetDirectory( DbPath( "orphan" ) );

		Assert.IsNull( directory.FindPackage( orphan.Replace( '\\', '/' ), "jutsu/orphan.jutsu" ) );
		Assert.IsFalse( System.IO.File.Exists( orphan ), "Unclaimed cloud file was not deleted" );
	}
}
