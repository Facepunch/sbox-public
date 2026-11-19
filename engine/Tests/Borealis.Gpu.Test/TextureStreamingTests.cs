using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using System.IO;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>
/// Texture streaming in the game window (<see cref="TextureStreamer"/>): a texture's tail read from the start of its file,
/// its image swapped for the mips its draws ask for, and mips given back when the budget runs short.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TextureStreamingTests : GpuTest
{
	/// <summary>A few of the core content's textures that stream: 2D, one slice, mips above a 64 KiB tail.</summary>
	static List<string> StreamingTextures( int count )
	{
		var root = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( root is null ) Assert.Inconclusive( "No game content" );

		var found = new List<string>();
		foreach ( var file in Directory.EnumerateFiles( Path.Combine( root, "materials" ), "*.vtex_c", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
		{
			if ( new FileInfo( file ).Length < 256 << 10 ) continue;
			if ( !TextureStreamer.Streams( TextureFile.Load( file ) ) ) continue;
			found.Add( Path.GetRelativePath( root, file ).Replace( '\\', '/' )[..^2] );
			if ( found.Count == count ) break;
		}

		if ( found.Count == 0 ) Assert.Inconclusive( "No streaming textures in the core content" );
		return found;
	}

	/// <summary>
	/// A tail read takes only the start of the file, decodes only the tail's mips (within 64 KiB), and they're the same as a
	/// whole read's; a read from a finer mip reads on to it.
	/// </summary>
	[TestMethod]
	public void TailReadsTheStartOfTheFile()
	{
		var content = ContentFiles.FindGame();
		foreach ( var path in StreamingTextures( 4 ) )
		{
			var whole = content.TryRead( path );
			var full = TextureFile.Read( whole );
			var tail = TextureStreamer.ReadMips( content, path, TextureFile.Tail );

			Assert.IsNotNull( tail, path );
			Assert.AreEqual( full.TailTop, tail.TopMip, path );
			Assert.IsTrue( tail.TopMip > 0, path );
			Assert.IsTrue( TextureFile.PrefixNeeded( whole, TextureFile.Tail ) < whole.Length, $"{path}: the tail is the whole file" );
			Assert.IsNull( tail.MipData[tail.TopMip - 1], path );

			long bytes = 0;
			for ( int mip = tail.TopMip; mip < tail.Mips; mip++ )
			{
				Assert.IsTrue( full.MipData[mip].AsSpan().SequenceEqual( tail.Mip( mip ) ), $"{path} mip {mip}" );
				bytes += tail.Mip( mip ).Length;
			}

			Assert.IsTrue( bytes <= TextureFile.TailBytes || tail.TopMip == tail.Mips - 1, $"{path}: tail of {bytes} bytes" );

			var finer = TextureStreamer.ReadMips( content, path, 1 );
			Assert.AreEqual( 1, finer.TopMip, path );
			Assert.IsTrue( full.MipData[1].AsSpan().SequenceEqual( finer.Mip( 1 ) ), path );
			tail.Release();
			finer.Release();
		}
	}

	/// <summary>
	/// Streaming a texture's mips in makes next to no garbage: its file is read into, and its mips decoded into, pooled
	/// arrays that go back once they're written, and the Zstandard decoder keeps its tables between mips. Before, a 2 MiB
	/// texture's build made its size again in garbage, much of it on the large object heap.
	/// </summary>
	[TestMethod]
	public void StreamedReadsAllocateLittle()
	{
		var content = ContentFiles.FindGame();
		var paths = StreamingTextures( 4 );

		// Once each first: the pool fills and the decoder's tables are made
		foreach ( var path in paths ) TextureStreamer.ReadMips( content, path, 0 ).Release();

		foreach ( var path in paths )
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			var file = TextureStreamer.ReadMips( content, path, 0 );
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			var decoded = TextureStreamer.Bytes( file, 0 );
			file.Release();

			Assert.IsTrue( allocated < 4096, $"{path}: reading {decoded} bytes of mips allocated {allocated} bytes" );
		}

		Assert.IsTrue( ContentBuffers.KeptBytes > 0, "released mips went back to the pool" );
	}

	/// <summary>
	/// A full pool lets go of the sizes least lately asked for to keep one asked for since, so it follows what streams (a
	/// map's coarse mips given back, then fine ones read) rather than holding on to what nothing asks for any more.
	/// </summary>
	[TestMethod]
	public void FullPoolKeepsTheSizesAskedForLately()
	{
		var most = ContentBuffers.MaxKeptBytes;
		try
		{
			ContentBuffers.Clear();
			ContentBuffers.MaxKeptBytes = 1 << 20;
			var small = Enumerable.Range( 0, 4 ).Select( _ => ContentBuffers.Rent( 256 << 10 ) ).ToArray();
			foreach ( var array in small ) ContentBuffers.Return( array );
			Assert.AreEqual( 1L << 20, ContentBuffers.KeptBytes, "four quarter-MiB arrays fill it" );

			var large = ContentBuffers.Rent( 512 << 10 );
			ContentBuffers.Return( large );
			Assert.AreSame( large, ContentBuffers.Rent( 512 << 10 ), "the size asked for lately is kept" );
			Assert.IsTrue( ContentBuffers.KeptBytes <= 1 << 20, "within the most it keeps" );

			// Nothing asked for since: a size asked for longer ago doesn't push it out
			ContentBuffers.Return( large );
			var made = ContentBuffers.MadeBytes;
			ContentBuffers.Return( new byte[256 << 10] );
			Assert.AreSame( large, ContentBuffers.Rent( 512 << 10 ) );
			Assert.AreEqual( made, ContentBuffers.MadeBytes );
		}
		finally
		{
			ContentBuffers.MaxKeptBytes = most;
			ContentBuffers.Clear();
		}
	}

	/// <summary>
	/// A streamed material texture starts as its tail; a draw asking for every mip gets a new image of them all, the material
	/// re-keyed; a budget too small for it gives them back, a mip a build, down to the tail.
	/// </summary>
	[TestMethod]
	public void AskedMipsSwapInAndGoBackUnderBudget()
	{
		var path = StreamingTextures( 1 )[0];
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var texture = (BackendTexture)backend.MaterialTextureFile( path );
		var stream = texture.Stream;
		Assert.IsNotNull( stream );
		Assert.AreEqual( stream.TailTop, stream.ResidentTop );
		Assert.AreEqual( Math.Max( texture.Width >> stream.TailTop, 1 ), Device.InfoOf( texture.Handle ).Width );

		using var material = new BackendMaterial( "streamed", null, null, new( StringComparer.OrdinalIgnoreCase ) { ["g_tColor"] = texture } );
		var version = material.Version;
		var tail = texture.Handle;

		// Asked for in each frame, as draws ask
		for ( int frame = 0; frame < 200 && stream.ResidentTop > 0; frame++ )
		{
			backend.AskTextures( material, TextureSizes.All );
			backend.BeginFrame();
			Thread.Sleep( 5 );
		}

		Assert.AreEqual( 0, stream.ResidentTop );
		Assert.AreEqual( texture.Width, Device.InfoOf( texture.Handle ).Width );
		Assert.AreEqual( texture.Mips, Device.InfoOf( texture.Handle ).Mips );
		Assert.AreNotEqual( tail, texture.Handle );
		Assert.AreNotEqual( version, material.Version );
		Assert.IsFalse( backend.StreamingTextures );

		// A budget of a byte: mips given back down to the tail, which always stays
		backend.TextureBudgetBytes = 1;
		for ( int frame = 0; frame < 400 && stream.ResidentTop < stream.TailTop; frame++ )
		{
			backend.AskTextures( material, TextureSizes.All );
			backend.BeginFrame();
			Thread.Sleep( 5 );
		}

		Assert.AreEqual( stream.TailTop, stream.ResidentTop );
		Assert.AreEqual( Math.Max( texture.Width >> stream.TailTop, 1 ), Device.InfoOf( texture.Handle ).Width );
		Device.WaitIdle();
	}

	/// <summary>
	/// Behind a loading screen (<see cref="BackendDevice.Loading"/>) a build's mips go up together once read, where a frame's
	/// upload budget otherwise spreads them over frames.
	/// </summary>
	[TestMethod]
	public void WhileLoadingBuildsUploadInOneFrame()
	{
		foreach ( var loading in new[] { false, true } )
		{
			var path = StreamingTextures( 1 )[0];
			using var backend = new BackendDevice( Device ) { StreamTextures = true, Loading = loading };
			var texture = (BackendTexture)backend.MaterialTextureFile( path );
			var stream = texture.Stream;
			backend.Streamer.UploadBytesPerFrame = 1;
			using var material = new BackendMaterial( "streamed", null, null, new( StringComparer.OrdinalIgnoreCase ) { ["g_tColor"] = texture } );

			// Asked for, its build started, and its file read
			backend.AskTextures( material, TextureSizes.All );
			backend.BeginFrame();
			Assert.AreEqual( 1, backend.Streamer.BuildsInFlight, $"loading {loading}" );
			for ( int i = 0; i < 400 && stream.Building is { Read.IsCompleted: false }; i++ ) Thread.Sleep( 5 );

			backend.AskTextures( material, TextureSizes.All );
			backend.BeginFrame();
			Assert.AreEqual( loading, stream.ResidentTop == 0, $"loading {loading}: resident from mip {stream.ResidentTop} after one upload" );
			Device.WaitIdle();
		}
	}

	/// <summary>
	/// A file that gives its tail but not its finer mips (cut short after the tail was read) is read for them once, not again
	/// every frame: the texture keeps its tail.
	/// </summary>
	[TestMethod]
	public void UnreadableMipsAreNotReadEveryFrame()
	{
		var path = StreamingTextures( 1 )[0];
		var game = ContentFiles.FindGame();
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-stream-{Guid.NewGuid():N}" );
		var copy = Path.Combine( folder, path + "_c" );
		Directory.CreateDirectory( Path.GetDirectoryName( copy ) );
		File.Copy( game.Find( path ), copy );
		try
		{
			using var backend = new BackendDevice( Device, new ContentFiles( [folder, .. game.Roots] ) ) { StreamTextures = true };
			var texture = (BackendTexture)backend.MaterialTextureFile( path );
			var stream = texture.Stream;
			Assert.IsNotNull( stream );

			// Only the tail's bytes are left
			var tailBytes = TextureFile.PrefixNeeded( File.ReadAllBytes( copy ), TextureFile.Tail );
			using ( var file = new FileStream( copy, FileMode.Open ) ) file.SetLength( tailBytes );
			Assert.IsNull( TextureStreamer.ReadMips( backend.Content, path, 0 ) );

			using var material = new BackendMaterial( "streamed", null, null, new( StringComparer.OrdinalIgnoreCase ) { ["g_tColor"] = texture } );
			for ( int frame = 0; frame < 60; frame++ )
			{
				backend.AskTextures( material, TextureSizes.All );
				backend.BeginFrame();
				Thread.Sleep( 2 );
			}

			Assert.AreEqual( stream.TailTop, stream.ResidentTop );
			Assert.IsTrue( backend.Streamer.BuildsStarted <= stream.TailTop, $"{backend.Streamer.BuildsStarted} builds for {stream.TailTop} mips above the tail" );
			Assert.IsTrue( backend.Streamer.ReadFailures >= 1 );
			Device.WaitIdle();
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A streamed texture nothing asks for gives its mips back down to its tail after <see cref="TextureStreamer.IdleSeconds"/>,
	/// with room to spare: what a map streamed in doesn't stay resident once the map has gone.
	/// </summary>
	[TestMethod]
	public void UnaskedTexturesGoBackToTheirTail()
	{
		var path = StreamingTextures( 1 )[0];
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var texture = (BackendTexture)backend.MaterialTextureFile( path );
		var stream = texture.Stream;
		using var material = new BackendMaterial( "streamed", null, null, new( StringComparer.OrdinalIgnoreCase ) { ["g_tColor"] = texture } );

		for ( int frame = 0; frame < 200 && stream.ResidentTop > 0; frame++ )
		{
			backend.AskTextures( material, TextureSizes.All );
			backend.BeginFrame();
			Thread.Sleep( 5 );
		}

		Assert.AreEqual( 0, stream.ResidentTop );

		// No longer asked for, with no budget: kept while it's been idle less than the idle time, then back to the tail
		backend.Streamer.IdleSeconds = 0.5;
		for ( int frame = 0; frame < 20; frame++ )
		{
			backend.BeginFrame();
			Thread.Sleep( 5 );
		}

		Assert.AreEqual( 0, stream.ResidentTop, "not idle long enough yet" );
		for ( int frame = 0; frame < 400 && stream.ResidentTop < stream.TailTop; frame++ )
		{
			backend.BeginFrame();
			Thread.Sleep( 5 );
		}

		Assert.AreEqual( stream.TailTop, stream.ResidentTop );
		Assert.AreEqual( Math.Max( texture.Width >> stream.TailTop, 1 ), Device.InfoOf( texture.Handle ).Width );
		Assert.IsTrue( backend.Streamer.IdleGivenBack > 0 );
		Device.WaitIdle();
	}

	/// <summary>A draw asking for a size wants the coarsest mip at least that large, and its tail for nothing.</summary>
	[TestMethod]
	public void AskedSizeWantsTheCoarsestMipThatCoversIt()
	{
		var path = StreamingTextures( 1 )[0];
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var stream = ((BackendTexture)backend.MaterialTextureFile( path )).Stream;

		Assert.AreEqual( 0, stream.TopFor( TextureSizes.All ) );
		Assert.AreEqual( 0, stream.TopFor( stream.MaxSide ) );
		Assert.AreEqual( 1, stream.TopFor( stream.MaxSide / 2 ) );
		Assert.AreEqual( 0, stream.TopFor( stream.MaxSide / 2 + 1 ) );
		Assert.AreEqual( stream.TailTop, stream.TopFor( 0 ) );
		Assert.AreEqual( stream.TailTop, stream.TopFor( 1 ) );
	}

	/// <summary>
	/// Video memory full: a build whose image can't be made is dropped and the texture keeps the mips it has, nothing throws,
	/// and once there's room again it grows. A capped budget (<see cref="Device.MemoryBudgetLimit"/>) makes it give mips back
	/// down to its tail.
	/// </summary>
	[TestMethod]
	public void FullVideoMemoryKeepsFewerMips()
	{
		var path = StreamingTextures( 1 )[0];
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var texture = (BackendTexture)backend.MaterialTextureFile( path );
		var stream = texture.Stream;
		using var material = new BackendMaterial( "streamed", null, null, new( StringComparer.OrdinalIgnoreCase ) { ["g_tColor"] = texture } );

		void Ask( int frames, Func<bool> until )
		{
			for ( int frame = 0; frame < frames && !until(); frame++ )
			{
				backend.AskTextures( material, TextureSizes.All );
				backend.BeginFrame();
				Thread.Sleep( 5 );
			}
		}

		try
		{
			Device.Allocator.RefuseDeviceLocal = true;
			Device.Allocator.SystemFallback = false;
			Ask( 100, () => backend.Streamer.OutOfMemoryBuilds > 0 );
			Assert.IsTrue( backend.Streamer.OutOfMemoryBuilds > 0, "a build ran out of memory" );
			Assert.AreEqual( stream.TailTop, stream.ResidentTop, "and the texture kept its tail" );
			Assert.IsTrue( texture.IsValid );
		}
		finally
		{
			Device.Allocator.RefuseDeviceLocal = false;
			Device.Allocator.SystemFallback = true;
		}

		Ask( 400, () => stream.ResidentTop == 0 );
		Assert.AreEqual( 0, stream.ResidentTop, "with room again it grows" );

		try
		{
			Device.MemoryBudgetLimit = 1;
			Ask( 400, () => stream.ResidentTop == stream.TailTop );
			Assert.AreEqual( stream.TailTop, stream.ResidentTop, "a capped budget gives mips back" );
		}
		finally
		{
			Device.MemoryBudgetLimit = 0;
		}

		Device.WaitIdle();
	}
}
