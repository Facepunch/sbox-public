using Editor;
using System.IO;

namespace FilesystemTests;

/// <summary>
/// Compatibility between the editor's pack writer and the client's read-only lookup.
/// </summary>
[TestClass]
public class BakeCacheTests
{
	/// <summary>
	/// A reader can stay open during a rebake. Only a successful save publishes staged changes.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void RefreshPack()
	{
		var temp = Directory.CreateTempSubdirectory();
		var path = Path.Combine( temp.CreateSubdirectory( "core" ).FullName, "bake.cache" );
		EngineFileSystem.Initialize( temp.FullName, skipBaseFolderInit: true );
		try
		{
			Assert.IsFalse( BakeCache.TryGet( "icon", out _ ) );
			Assert.IsFalse( File.Exists( path ) );

			// Cross multiple copy-buffer boundaries and end with a partial chunk.
			var payload = new byte[150_001];
			new System.Random( 42 ).NextBytes( payload );

			using ( var writer = new BakeCacheWriter() )
			{
				writer.Set( "icon", [1] );
				writer.Set( "other/file", payload );
				writer.Save();
			}

			Assert.IsTrue( BakeCache.TryGet( "icon", out var bytes ) );
			CollectionAssert.AreEqual( new byte[] { 1 }, bytes );

			using ( var writer = new BakeCacheWriter() )
			{
				writer.Set( "icon", [3] );
				writer.Set( "icon", [4] );
				writer.Set( "empty", [] );
				writer.Save();
			}

			Assert.IsTrue( BakeCache.TryGet( "icon", out bytes ) );
			CollectionAssert.AreEqual( new byte[] { 4 }, bytes );
			Assert.IsTrue( BakeCache.TryGet( "other/file", out bytes ) );
			CollectionAssert.AreEqual( payload, bytes );
			Assert.IsTrue( BakeCache.TryGet( "empty", out bytes ) );
			Assert.AreEqual( 0, bytes.Length );

			var committed = File.ReadAllBytes( path );
			using ( var writer = new BakeCacheWriter() )
			{
				writer.Set( "icon", [9] );
			}

			CollectionAssert.AreEqual( committed, File.ReadAllBytes( path ) );
			using ( var writer = new BakeCacheWriter() )
			{
				writer.Set( "icon", [4] );
				writer.Save();
			}

			CollectionAssert.AreEqual( committed, File.ReadAllBytes( path ) );
			Assert.IsTrue( BakeCache.TryGet( "icon", out _ ) );

			// Restart with a different engine root. The static reader must release the previous pack.
			EngineFileSystem.Shutdown();
			EngineFileSystem.Initialize( temp.CreateSubdirectory( "next" ).FullName, skipBaseFolderInit: true );
			Assert.IsFalse( BakeCache.TryGet( "icon", out _ ) );
		}
		finally
		{
			EngineFileSystem.Shutdown();
			temp.Delete( recursive: true );
		}
	}

	/// <summary>
	/// Model aliases share a key while identically named models from different mounts stay distinct.
	/// </summary>
	[TestMethod]
	public void ModelKeys()
	{
		var local = ThumbnailCache.GetBakedModelKey( "/Models\\Crate.VMDL_C" );
		Assert.AreEqual( $"spawnicon/model/local/{"models/crate.vmdl".Md5()}", local );

		var mounted = ThumbnailCache.GetBakedModelKey( "mount://hl2/models/crate.vmdl" );
		Assert.AreEqual( $"spawnicon/model/mount/{"mount://hl2/models/crate.vmdl".Md5()}", mounted );
		Assert.AreNotEqual( mounted, ThumbnailCache.GetBakedModelKey( "mount://css/models/crate.vmdl" ) );
		Assert.IsNull( ThumbnailCache.GetBakedModelKey( "models/crate.prefab" ) );
	}
}
