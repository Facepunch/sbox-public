using Editor;
using System.IO;
using System.Threading;

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
	/// A thumbnail in the shipped pack loads on a worker: a caller isn't held while the pack is busy (the first lookup reads
	/// its whole index, which took seconds on the game's main thread with the disk under load).
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public async Task ThumbnailLoadsOffTheCaller()
	{
		var temp = Directory.CreateTempSubdirectory();
		temp.CreateSubdirectory( "core" );
		EngineFileSystem.Initialize( temp.FullName, skipBaseFolderInit: true );
		using var release = new ManualResetEventSlim();
		using var held = new ManualResetEventSlim();
		try
		{
			const string model = "mount://hl2/models/crate.vmdl";
			using ( var icon = new Bitmap( 4, 2 ) )
			using ( var writer = new BakeCacheWriter() )
			{
				writer.Set( ThumbnailCache.GetBakedModelKey( model ), icon.ToPng() );
				writer.Save();
			}

			// Stand in for a stalled read: another thread holds the pack's lock.
			var sync = typeof( BakeCache ).GetField( "sync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static ).GetValue( null );
			var holder = new Thread( () => { lock ( sync ) { held.Set(); release.Wait(); } } );
			holder.Start();
			held.Wait();

			Task<Bitmap> load = null;
			var cache = new ThumbnailCache( model );
			var caller = new Thread( () => load = cache.LoadBitmapAsync() );
			caller.Start();
			Assert.IsTrue( caller.Join( 2000 ), "the load waited for the pack on the caller" );
			Assert.IsFalse( load.IsCompleted );

			release.Set();
			using var bitmap = await load;
			Assert.IsNotNull( bitmap );
			Assert.AreEqual( 4, bitmap.Width );
			Assert.AreEqual( 2, bitmap.Height );
			holder.Join();
		}
		finally
		{
			release.Set();
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
