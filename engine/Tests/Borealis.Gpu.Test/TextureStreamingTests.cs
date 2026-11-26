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

		// From an empty pool: one that earlier work (other tests' streaming) left full gives this test's sizes up to make room
		ContentBuffers.Clear();

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
	/// A streamed material texture starts as its tail; a draw asking for every mip gets a new image of them all under the same
	/// handle, its materials' versions as they were; a budget too small for it gives them back, a mip a build, down to the tail.
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
		SettleImage( backend, texture, texture.Width );
		Assert.AreEqual( texture.Width, Device.InfoOf( texture.Handle ).Width );
		Assert.AreEqual( texture.Mips, Device.InfoOf( texture.Handle ).Mips );
		Assert.AreEqual( tail, texture.Handle, "the handle stays through the swap" );
		Assert.AreEqual( version, material.Version, "so do its material's version: nothing naming it is made again" );
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
		SettleImage( backend, texture, Math.Max( texture.Width >> stream.TailTop, 1 ) );
		Assert.AreEqual( Math.Max( texture.Width >> stream.TailTop, 1 ), Device.InfoOf( texture.Handle ).Width );
		Device.WaitIdle();
	}

	/// <summary>Frames until a swapped-in image is the texture's (a swap waits for others to join it, <see cref="Device.ReplaceAfterFrames"/>).</summary>
	void SettleImage( BackendDevice backend, BackendTexture texture, int width )
	{
		for ( int frame = 0; frame < Device.ReplaceAfterFrames + 4 && Device.InfoOf( texture.Handle ).Width != width; frame++ ) backend.BeginFrame();
	}

	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
	struct SlotConstants
	{
		public uint Target, First, Second, Third;
	}

	/// <summary>What a draw reads from a bindless slot: texel (0, 0) of mip 0 of the image it names now (the slots compute, which loads it).</summary>
	byte[] ReadSlot( int slot )
	{
		var info = TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: "Slot target" );
		info.Usage |= TextureUsage.Storage;
		var target = Device.CreateTexture( info );
		using var stream = typeof( BorealisGpuSample.SampleScene ).Assembly.GetManifestResourceStream( "slots.cs.spv" );
		var code = new byte[stream.Length];
		stream.ReadExactly( code );
		var shader = Device.CreateShader( code, "slots" );
		var pipeline = Device.CreateComputePipeline( new ComputePipelineInfo { Shader = shader, Entry = "MainCs" } );

		var list = Device.BeginCommandList( QueueType.Graphics, "Read a slot" );
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.Undefined, ImageLayout.General, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite ) );
		list.SetPipeline( pipeline );
		list.PushConstants( new SlotConstants { Target = (uint)Device.StorageIndex( target ), First = (uint)slot } );
		list.Dispatch( 1, 1 );
		Device.Wait( Device.Submit( list ) );
		var bytes = ReadBack( target, ImageLayout.General );
		Device.Destroy( target );
		Device.Destroy( pipeline );
		Device.Destroy( shader );
		return bytes;
	}

	/// <summary>
	/// A streamed texture's new mips are swapped in under its bindless index, and what names it isn't made again: its
	/// material's version stays (a move on forgets and remakes every draw record the material has, and rebuilds plans: a burst
	/// of streaming did that a few hundred times a second), and a read of the slot gets the new image.
	/// </summary>
	[TestMethod]
	public void StreamedMipsKeepTheBindlessIndexAndTheMaterialVersion()
	{
		var path = StreamingTextures( 1 )[0];
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var texture = (BackendTexture)backend.MaterialTextureFile( path );
		var stream = texture.Stream;
		using var material = new BackendMaterial( "streamed", null, null, new( StringComparer.OrdinalIgnoreCase ) { ["g_tColor"] = texture } );
		var (slot, version, layout) = (texture.BindlessIndex, material.Version, material.LayoutVersion);
		Assert.AreEqual( Math.Max( texture.Width >> stream.TailTop, 1 ), Device.InfoOf( texture.Handle ).Width );

		for ( int frame = 0; frame < 200 && stream.ResidentTop > 0; frame++ )
		{
			backend.AskTextures( material, TextureSizes.All );
			backend.BeginFrame();
			Thread.Sleep( 5 );
		}

		Assert.AreEqual( 0, stream.ResidentTop );
		SettleImage( backend, texture, texture.Width );
		Assert.AreEqual( texture.Width, Device.InfoOf( texture.Handle ).Width, "the new image is the texture's" );
		Assert.AreEqual( slot, texture.BindlessIndex, "at the index it had" );
		Assert.AreEqual( version, material.Version );
		Assert.AreEqual( layout, material.LayoutVersion );

		var reference = TextureFile.Read( ContentFiles.FindGame().TryRead( path ) ).Create( Device, "Streamed reference" );
		CollectionAssert.AreEqual( ReadSlot( Device.BindlessIndex( reference ) ), ReadSlot( slot ), "a draw reading the index samples the new image" );
		Device.Destroy( reference );
		Device.WaitIdle();
	}

	/// <summary>
	/// A texture given another image (<see cref="Device.Replace"/>) keeps its handle and bindless slot and samples its old
	/// image until a frame swaps the new one in, when the slot names that; a second image before then takes the first's place,
	/// and a texture destroyed meanwhile takes its image with it.
	/// </summary>
	[TestMethod]
	public void ReplacedTextureKeepsItsSlotAndSwapsInTheNewImage()
	{
		var red = Device.CreateTexture( TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: "Replaced" ) );
		Device.WriteTexture( red, [255, 0, 0, 255] );
		var slot = Device.BindlessIndex( red );

		TextureHandle Made( byte r, byte g, byte b, string name )
		{
			var image = Device.CreateTexture( TextureInfo.Texture2D( 2, 2, Format.RGBA8Unorm, mips: 2, name: name ) );
			Device.WriteTexture( image, [r, g, b, 255, r, g, b, 255, r, g, b, 255, r, g, b, 255], 0, 0 );
			Device.WriteTexture( image, [r, g, b, 255], 1, 0 );
			return image;
		}

		var blue = Made( 0, 0, 255, "Superseded" );
		var green = Made( 0, 255, 0, "New" );
		Device.ReplaceAfterFrames = 0;
		Device.Replace( red, blue );
		Device.Replace( red, green );
		Assert.IsFalse( Device.IsAlive( blue ), "the second image takes the first's place" );
		Assert.IsTrue( Device.IsReplacing( red ) );
		CollectionAssert.AreEqual( new byte[] { 255, 0, 0, 255 }, ReadSlot( slot ), "work before the swap samples the old image" );
		Assert.AreEqual( 1, Device.InfoOf( red ).Width );

		Device.BeginFrame();
		Assert.IsFalse( Device.IsReplacing( red ) );
		Assert.AreEqual( slot, Device.BindlessIndex( red ), "the slot stays" );
		Assert.AreEqual( 2, Device.InfoOf( red ).Width );
		Assert.AreEqual( 2, Device.InfoOf( red ).Mips );
		CollectionAssert.AreEqual( new byte[] { 0, 255, 0, 255 }, ReadSlot( slot ), "work after it samples the new" );

		var gone = Made( 255, 255, 255, "Of a destroyed texture" );
		Device.Replace( red, gone );
		Device.Destroy( red );
		Device.BeginFrame();
		Assert.IsFalse( Device.IsAlive( gone ), "the new image of a destroyed texture goes with it" );
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
	/// A file that holds only its coarser mips (a package's web texture comes without its two finest, though its header lists
	/// them) reads as the mips it has, and loads as a texture of that size: it threw "Texture data ends before mip 1", which
	/// left Resource Haven's terrain with no materials (black).
	/// </summary>
	[TestMethod]
	public void FilesWithOnlyTheirCoarserMipsLoad()
	{
		var path = StreamingTextures( 1 )[0];
		var game = ContentFiles.FindGame();
		var whole = game.TryRead( path );
		var full = TextureFile.Read( whole );
		Assert.IsTrue( full.Mips > 3, path );

		var cut = whole.AsSpan( 0, (int)TextureFile.PrefixNeeded( whole, 2 ) ).ToArray();
		var read = TextureFile.Read( cut );
		Assert.AreEqual( 2, read.TopMip, path );
		Assert.IsNull( read.MipData[1], path );
		for ( int mip = 2; mip < full.Mips; mip++ ) Assert.IsTrue( full.MipData[mip].AsSpan().SequenceEqual( read.Mip( mip ) ), $"{path} mip {mip}" );

		var folder = Path.Combine( Path.GetTempPath(), $"borealis-coarse-{Guid.NewGuid():N}" );
		var copy = Path.Combine( folder, path + "_c" );
		Directory.CreateDirectory( Path.GetDirectoryName( copy ) );
		File.WriteAllBytes( copy, cut );
		try
		{
			using var backend = new BackendDevice( Device, new ContentFiles( [folder, .. game.Roots] ) );
			var texture = backend.LoadTexture( path );
			Assert.IsTrue( texture.IsValid, path );
			Assert.AreEqual( full.Width, texture.Width, $"{path}: described at its file's size, as a streamed texture is" );
			var image = Device.InfoOf( BackendDevice.Unwrap( texture ).Texture );
			Assert.AreEqual( Math.Max( full.Width >> 2, 1 ), image.Width, $"{path}: its image holds the mips from 2" );
			Assert.AreEqual( full.Mips - 2, image.Mips, path );
			Device.WaitIdle();
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A texture file that isn't one (a Git LFS pointer left by a checkout without the object) throws, naming the file, the
	/// first time it's asked for, then draws as the placeholder: it isn't read and thrown again every frame (Flatgrass's
	/// envmaps on a Linux checkout threw 1,900 times a second through the cubemaps asking each frame).
	/// </summary>
	[TestMethod]
	public void UnreadableTextureThrowsOnceThenIsThePlaceholder()
	{
		const string path = "materials/test/lfs_pointer_for_test.vtex";
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-pointer-{Guid.NewGuid():N}" );
		var file = Path.Combine( folder, path + "_c" );
		Directory.CreateDirectory( Path.GetDirectoryName( file ) );
		File.WriteAllText( file, "version https://git-lfs.github.com/spec/v1\noid sha256:3f5ffd9d7e0000000000000000000000000000000000000000000000000000\nsize 1048576\n" );
		try
		{
			using var backend = new BackendDevice( Device, new ContentFiles( folder ) );
			var thrown = Assert.ThrowsException<InvalidDataException>( () => backend.LoadTexture( path ) );
			StringAssert.Contains( thrown.Message, path, "the report names the file" );

			for ( int frame = 0; frame < 3; frame++ )
			{
				var texture = backend.LoadTextureAsync( path, out var pending );
				Assert.IsFalse( pending, "nothing to wait for" );
				Assert.IsTrue( texture.IsValid, "the placeholder" );
				Assert.AreSame( texture, backend.LoadTexture( path ), "the same placeholder each time" );
			}

			Device.WaitIdle();
		}
		finally
		{
			Directory.Delete( folder, true );
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
		backend.Streamer.WindowFrames = int.MaxValue;
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

		// Swapped in at the next frame: waiting for others to join it, the asks of the last window would grow it again
		// first (this test's idle time is far shorter than a game's), and that image would take the tail's place
		var replaceAfter = Device.ReplaceAfterFrames;
		Device.ReplaceAfterFrames = 0;
		try
		{
			SettleImage( backend, texture, Math.Max( texture.Width >> stream.TailTop, 1 ) );
		}
		finally
		{
			Device.ReplaceAfterFrames = replaceAfter;
		}

		Assert.AreEqual( Math.Max( texture.Width >> stream.TailTop, 1 ), Device.InfoOf( texture.Handle ).Width );
		Assert.AreEqual( 0, backend.Streamer.Waiting, "an expired ask doesn't stream the texture back in" );
		Assert.IsTrue( backend.Streamer.IdleGivenBack > 0 );
		Device.WaitIdle();
	}

	/// <summary>
	/// A disposed streamed texture is forgotten at the next frame's start, and frames with nothing of the device's disposed
	/// don't walk the streamed textures looking for disposed ones.
	/// </summary>
	[TestMethod]
	public void DisposedTexturesAreForgottenWithoutLookingEveryFrame()
	{
		var paths = StreamingTextures( 2 );
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var kept = (BackendTexture)backend.MaterialTextureFile( paths[0] );
		var gone = (BackendTexture)backend.MaterialTextureFile( paths[1] );
		backend.BeginFrame();
		Assert.AreEqual( 2, backend.Streamer.Count );

		var scans = backend.Streamer.ForgetScans;
		for ( int frame = 0; frame < 10; frame++ ) backend.BeginFrame();
		Assert.AreEqual( scans, backend.Streamer.ForgetScans, "looked for disposed textures with none disposed" );

		gone.Dispose();
		backend.BeginFrame();
		Assert.AreEqual( 1, backend.Streamer.Count );
		Assert.AreEqual( scans + 1, backend.Streamer.ForgetScans );
		Assert.IsTrue( kept.IsValid );
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

	/// <summary>
	/// The streamer only looks again at textures that may have come to want more mips (asked for finer, built, added,
	/// pinned), not at every texture each frame: one that settled at its tail with nothing asking grows once it's pinned,
	/// with no ask.
	/// </summary>
	[TestMethod]
	public void SettledTextureGrowsWhenPinned()
	{
		var path = StreamingTextures( 1 )[0];
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var texture = (BackendTexture)backend.MaterialTextureFile( path );
		var stream = texture.Stream;

		for ( int frame = 0; frame < 20; frame++ ) backend.BeginFrame();
		if ( stream.ResidentTop == 0 ) Assert.Inconclusive( "The texture started with every mip." );

		stream.Pinned = true;
		for ( int frame = 0; frame < 400 && stream.ResidentTop != 0; frame++ )
		{
			backend.BeginFrame();
			Thread.Sleep( 5 );
		}

		Assert.AreEqual( 0, stream.ResidentTop, "pinned, it grows to every mip" );
		Device.WaitIdle();
	}

	/// <summary>
	/// A streamed texture in a block being emptied (defragmenting) is copied into new memory on the GPU between frames and
	/// swapped in under its handle: an image elsewhere, with every mip, its materials' versions as they were.
	/// </summary>
	[TestMethod]
	public void TexturesMoveOutOfDrainingBlocks()
	{
		var path = StreamingTextures( 1 )[0];
		using var backend = new BackendDevice( Device ) { StreamTextures = true };
		var texture = (BackendTexture)backend.MaterialTextureFile( path );
		var stream = texture.Stream;
		using var material = new BackendMaterial( "streamed", null, null, new( StringComparer.OrdinalIgnoreCase ) { ["g_tColor"] = texture } );
		backend.BeginFrame();

		var before = texture.Handle;
		var block = Device.Get( before ).Memory.Block;
		var version = material.Version;
		Device.Allocator.Drain( block );

		// Room in another block of its kind that's in use (a move never fills an empty block)
		var room = Device.CreateTexture( new TextureInfo { Width = 256, Height = 256, Format = Format.RGBA8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination, Name = "Room", Content = true } );
		try
		{
			for ( int frame = 0; frame < 20 && Device.Get( texture.Handle ).Memory.Block == block; frame++ ) backend.BeginFrame();
		}
		finally
		{
			Device.Allocator.Undrain( block );
			Device.Destroy( room );
		}

		Assert.AreEqual( before, texture.Handle, "the handle stays" );
		Assert.AreNotSame( block, Device.Get( texture.Handle ).Memory.Block, "moved into another block" );
		Assert.AreEqual( stream.TailTop, stream.ResidentTop );
		Assert.AreEqual( version, material.Version, "its materials aren't made again" );
		Assert.IsTrue( backend.Streamer.Moved >= 1 );
		Device.WaitIdle();
	}
	/// <summary>
	/// A big whole texture read ahead (<see cref="BackendDevice.LoadTextureAsync"/>, as a map's lightmaps are) has its mips
	/// staged in an upload buffer by the worker that read it, so the frame that makes it only records copies; the texture
	/// holds the file's mips. Before, the frame copied them into upload memory, and a mip over an upload page took an upload
	/// allocation of its own (Construct's lightmaps: 96 ms of a frame).
	/// </summary>
	[TestMethod]
	public void BigTextureReadAheadIsMadeFromStagedMips()
	{
		var root = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( root is null ) Assert.Inconclusive( "No game content" );

		string path = null;
		TextureFile file = null;
		foreach ( var candidate in Directory.EnumerateFiles( Path.Combine( root, "materials" ), "*.vtex_c", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
		{
			if ( new FileInfo( candidate ).Length < 1 << 20 ) continue;
			var read = TextureFile.Load( candidate );
			if ( read.Dimension == TextureDimension.Texture3D || read.Format == Format.Unknown || read.TopMip != 0 || TextureStreamer.Bytes( read, 0 ) < 1 << 20 ) continue;
			path = Path.GetRelativePath( root, candidate ).Replace( '\\', '/' )[..^2];
			file = read;
			break;
		}

		if ( path is null ) Assert.Inconclusive( "No texture of 1 MiB or more (not a volume) in the core content" );

		var stageAhead = BackendDevice.StageAheadBytes;
		BackendDevice.StageAheadBytes = 1 << 20;
		try
		{
			using var backend = new BackendDevice( Device );
			GpuTexture texture = null;
			for ( int i = 0; i < 500 && texture is null; i++ )
			{
				// A frame each try: what it makes is within each frame's budget
				backend.BeginFrame();
				texture = backend.LoadTextureAsync( path, out var pending );
				if ( texture is null && !pending ) break;
				if ( texture is null ) Thread.Sleep( 10 );
			}

			Assert.IsNotNull( texture, path );
			Assert.AreEqual( 1, backend.StagedTextures, $"{path} is made from staged mips" );

			Device.Wait( Device.Submit( Device.BeginCommandList( QueueType.Graphics, "After the staged copies" ) ) );
			var (handle, layout) = BackendDevice.Unwrap( texture );
			var pixels = ReadBack( handle, layout );
			var expected = file.Mip( 0 )[..(file.MipBytes( 0 ) / file.Slices)];
			CollectionAssert.AreEqual( expected.ToArray(), pixels[..expected.Length], $"{path}: mip 0 as the file has it" );
		}
		finally
		{
			BackendDevice.StageAheadBytes = stageAhead;
		}

		Assert.AreEqual( 0, TakeValidationErrors().Length, "validation errors" );
		Device.WaitIdle();
	}
}
