using Borealis.Gpu;
using BorealisGpuSample;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BorealisGpuTests;

/// <summary>
/// Rendering, compute, copies, synchronisation and resource lifetime on a real device, offscreen, with the validation
/// layer checking every test.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DeviceTests : GpuTest
{
	TextureHandle Target( int width, int height ) => Device.CreateTexture( TextureInfo.RenderTarget( width, height, Format.RGBA8Unorm, name: "Target" ) );

	/// <summary>
	/// Binding a slot again with the bindings it last wrote binds that set again; other bindings write another, as does
	/// everything with the reuse off.
	/// </summary>
	/// <summary>
	/// Command lists' descriptor pools are the device's spares, given back reset when their frame retires and made ahead on a
	/// worker when few are left: recording takes one rather than making one (that cost ~70 ms in a map's first seconds).
	/// </summary>
	[TestMethod]
	public void DescriptorPoolsAreSharedAndMadeAhead()
	{
		var taken = new List<ulong>();
		void TakeAndGiveBack( int count )
		{
			for ( int i = 0; i < count; i++ ) taken.Add( Device.TakeDescriptorPool() );
			foreach ( var pool in taken ) Device.ReturnDescriptorPool( pool );
			taken.Clear();
		}

		// Warm: the spares made ahead
		TakeAndGiveBack( 1 );
		var clock = System.Diagnostics.Stopwatch.StartNew();
		while ( Device.DescriptorPoolsMade < 8 && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 5 );
		System.Threading.Thread.Sleep( 50 );

		var madeTaking = Device.DescriptorPoolsMadeTaking;
		for ( int round = 0; round < 10; round++ ) TakeAndGiveBack( 4 );
		Assert.AreEqual( madeTaking, Device.DescriptorPoolsMadeTaking, "taken from the spares, not made where asked" );
	}

	/// <summary>
	/// The device lists its live textures with their descriptions and memory (the editor's GPU resource viewer): a new one
	/// is listed with what it was made with and the memory it holds, and isn't once destroyed.
	/// </summary>
	[TestMethod]
	public void ListsLiveTextures()
	{
		var texture = Device.CreateTexture( TextureInfo.RenderTarget( 64, 32, Format.RGBA8Unorm, name: "Listed texture" ) );
		var listed = Device.ListTextures().Where( x => x.Info.Name == "Listed texture" ).ToList();
		Assert.AreEqual( 1, listed.Count );
		Assert.AreEqual( 64, listed[0].Info.Width );
		Assert.AreEqual( 32, listed[0].Info.Height );
		Assert.IsTrue( listed[0].Bytes >= 64 * 32 * 4, $"holds {listed[0].Bytes} bytes" );

		Device.Destroy( texture );
		Device.WaitIdle();
		Assert.IsFalse( Device.ListTextures().Any( x => x.Info.Name == "Listed texture" ) );
	}

	[TestMethod]
	public void SameBindingsReuseTheirSet()
	{
		var layout = Device.CreateBindingLayout( [new BindingSlot( 0, BindingType.UniformBuffer, ShaderStages.Vertex )] );
		var list = Device.BeginCommandList();
		var first = list.AllocateUpload( 256 );
		var (firstBuffer, firstOffset) = (first.Buffer, first.Offset);
		var second = list.AllocateUpload( 256 );
		var (secondBuffer, secondOffset) = (second.Buffer, second.Offset);

		list.SetBindings( 0, layout, [Binding.Uniform( 0, firstBuffer, firstOffset, 256 )] );
		list.SetBindings( 0, layout, [Binding.Uniform( 0, firstBuffer, firstOffset, 256 )] );
		Assert.AreEqual( 1, list.TransientSetsWritten, "the same bindings" );

		list.SetBindings( 0, layout, [Binding.Uniform( 0, secondBuffer, secondOffset, 256 )] );
		list.SetBindings( 0, layout, [Binding.Uniform( 0, secondBuffer, secondOffset, 256 )] );
		Assert.AreEqual( 2, list.TransientSetsWritten, "other bindings, then those again" );

		Device.ReusesTransientSets = false;
		try
		{
			list.SetBindings( 0, layout, [Binding.Uniform( 0, secondBuffer, secondOffset, 256 )] );
			Assert.AreEqual( 3, list.TransientSetsWritten, "reuse off" );
		}
		finally
		{
			Device.ReusesTransientSets = true;
		}

		Device.Wait( Device.Submit( list ) );
	}

	[TestMethod]
	public void BindingsWrittenEarlierInTheListReuseTheirSet()
	{
		var layout = Device.CreateBindingLayout( [new BindingSlot( 0, BindingType.UniformBuffer, ShaderStages.Vertex )] );
		var list = Device.BeginCommandList();
		var first = list.AllocateUpload( 256 );
		var (firstBuffer, firstOffset) = (first.Buffer, first.Offset);
		var second = list.AllocateUpload( 256 );
		var (secondBuffer, secondOffset) = (second.Buffer, second.Offset);

		list.SetBindings( 0, layout, [Binding.Uniform( 0, firstBuffer, firstOffset, 256 )] );
		list.SetBindings( 0, layout, [Binding.Uniform( 0, secondBuffer, secondOffset, 256 )] );
		list.SetBindings( 0, layout, [Binding.Uniform( 0, firstBuffer, firstOffset, 256 )] );
		list.SetBindings( 1, layout, [Binding.Uniform( 0, secondBuffer, secondOffset, 256 )] );
		Assert.AreEqual( 2, list.TransientSetsWritten, "each set of bindings written once, whichever slot binds it" );

		Device.Wait( Device.Submit( list ) );
		list = Device.BeginCommandList();
		var third = list.AllocateUpload( 256 );
		list.SetBindings( 0, layout, [Binding.Uniform( 0, third.Buffer, third.Offset, 256 )] );
		Assert.AreEqual( 1, list.TransientSetsWritten, "a list begun again keeps none" );
		Device.Wait( Device.Submit( list ) );
	}

	/// <summary>
	/// Two moves of one image with nothing recorded between (a block leaving its target readable, and the next drawing into
	/// it again) are one barrier, from where the first started to where the second ends: barriers in one call aren't ordered
	/// among themselves, so apart they'd race (synchronisation validation's write-after-write). A move of another image
	/// stays its own.
	/// </summary>
	[TestMethod]
	public void MovesOfOneImageInABatchJoin()
	{
		var target = Target( 64, 32 );
		var other = Target( 64, 32 );
		var list = Device.BeginCommandList();
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.Barrier( TextureBarrier.Transition( other, ImageLayout.Undefined, ImageLayout.ShaderRead, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.FragmentShader, MemoryAccess.ShaderRead ) );
		list.BeginRendering( [ColorTarget.Of( target, new Vector4( 1, 0, 0, 1 ) )] );
		list.EndRendering();

		var before = list.Counts.Barriers;
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.ColorTarget, ImageLayout.ShaderRead, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.FragmentShader, MemoryAccess.ShaderRead ) );
		list.Barrier( TextureBarrier.Transition( other, ImageLayout.ShaderRead, ImageLayout.ColorTarget, PipelineStage.FragmentShader, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.ShaderRead, ImageLayout.ColorTarget, PipelineStage.FragmentShader, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetRead | MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( target, new Vector4( 0, 1, 0, 1 ) ), ColorTarget.Of( other, new Vector4( 0, 0, 1, 1 ) )] );
		list.EndRendering();
		Assert.AreEqual( before + 1, list.Counts.Barriers, "one call for the batch" );
		Device.Wait( Device.Submit( list ) );

		Assert.AreEqual( ((byte)0, (byte)255, (byte)0, (byte)255), Pixel( ReadBack( target, ImageLayout.ColorTarget ), 64, 10, 10 ) );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)255, (byte)255), Pixel( ReadBack( other, ImageLayout.ColorTarget ), 64, 10, 10 ) );
	}

	/// <summary>
	/// A dispatch of a negative count or past the device's group limit throws before it's recorded: Vulkan would read a
	/// negative count as billions of groups and keep the GPU busy until the device is lost.
	/// </summary>
	[TestMethod]
	public void DispatchOutsideTheGroupRangeThrows()
	{
		var list = Device.BeginCommandList();
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => list.Dispatch( -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => list.Dispatch( 1, int.MaxValue ) );
		Device.Wait( Device.Submit( list ) );
	}

	/// <summary>
	/// A cleared target reads back as exactly the clear colour. The green is 128/255, not 0.5: 0.5 lands halfway between
	/// two 8-bit values, which Vulkan leaves to the driver (NVIDIA stores 127, AMD 128).
	/// </summary>
	[TestMethod]
	public void ClearReadsBack()
	{
		var target = Target( 64, 32 );
		var list = Device.BeginCommandList();
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( target, new Vector4( 1, 128f / 255f, 0, 1 ) )] );
		list.EndRendering();
		Device.Wait( Device.Submit( list ) );

		var pixels = ReadBack( target, ImageLayout.ColorTarget );
		Assert.AreEqual( 64 * 32 * 4, pixels.Length );
		Assert.AreEqual( ((byte)255, (byte)128, (byte)0, (byte)255), Pixel( pixels, 64, 0, 0 ) );
		Assert.AreEqual( ((byte)255, (byte)128, (byte)0, (byte)255), Pixel( pixels, 64, 63, 31 ) );
	}

	/// <summary>
	/// A scope one list suspends and the next resumes is one scope: the second's clear doesn't apply. Lists that don't pair
	/// (something recorded before the resume, or a resume with nothing suspended) get a scope of their own begun or ended
	/// between them, and validation, synchronisation included, stays clean.
	/// </summary>
	[TestMethod]
	public void SuspendedScopeResumesInTheNextList()
	{
		var target = Target( 64, 32 );
		var red = new Vector4( 1, 0, 0, 1 );
		var green = new Vector4( 0, 1, 0, 1 );
		var layout = ImageLayout.Undefined;

		(byte, byte, byte, byte) Draw( bool barrierFirst, bool suspend = true )
		{
			var first = Device.BeginCommandList();
			first.Barrier( TextureBarrier.Transition( target, layout, ImageLayout.ColorTarget, PipelineStage.Transfer, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
			first.BeginRendering( [ColorTarget.Of( target, red )], default, suspend ? RenderingFlags.Suspend : RenderingFlags.None );
			first.EndRendering();
			Device.SubmitLater( first );

			var second = Device.BeginCommandList();
			if ( barrierFirst ) second.Barrier( PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite );
			second.BeginRendering( [ColorTarget.Of( target, green )], default, RenderingFlags.Resume );
			second.EndRendering();
			Assert.AreEqual( barrierFirst ? 0 : 1, second.Counts.Resumed );
			Device.Wait( Device.Submit( second ) );

			var pixels = ReadBack( target, ImageLayout.ColorTarget );
			layout = ImageLayout.CopySource;
			return Pixel( pixels, 64, 10, 10 );
		}

		var pairings = Device.ScopePairings;
		Assert.AreEqual( ((byte)255, (byte)0, (byte)0, (byte)255), Draw( barrierFirst: false ), "the resumed scope kept what the suspended one cleared" );
		Assert.AreEqual( pairings, Device.ScopePairings, "they paired without help" );

		Assert.AreEqual( ((byte)0, (byte)255, (byte)0, (byte)255), Draw( barrierFirst: true ), "a barrier before the resume ends the suspended scope, and the next clears" );
		Assert.AreEqual( pairings + 1, Device.ScopePairings, "the suspended scope was ended by a list of its own" );

		Assert.AreEqual( ((byte)255, (byte)0, (byte)0, (byte)255), Draw( barrierFirst: false, suspend: false ), "a resume with nothing suspended loads what's there" );
		Assert.AreEqual( pairings + 2, Device.ScopePairings, "a scope was begun for it to resume" );
	}

	/// <summary>
	/// A resource destroyed while a frame records waits for the whole frame: a list recorded before the destroy and submitted
	/// after it still reads it, so it isn't freed once only what was submitted before the destroy has finished.
	/// </summary>
	[TestMethod]
	public void DestroyedResourcesWaitForTheirFrame()
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage, Name = "Destroyed mid-frame" } );
		var list = Device.BeginCommandList();
		list.FillBuffer( buffer, 1 );
		Device.Destroy( buffer );
		var point = Device.Submit( list );
		Device.BeginFrame();
		Device.WaitIdle();
		Assert.IsTrue( Device.LastGarbageTag.Graphics >= point.Value, $"the buffer waits for the list that used it (tagged {Device.LastGarbageTag.Graphics}, the list is {point.Value})" );
	}

	/// <summary>
	/// A list still recording when its frame closes (a capture whose recording started a frame) goes with the next frame,
	/// which it's submitted in: recycled with its own frame's slot, its command buffer and upload data would be reused under
	/// it. Once that slot comes round again it still records, submits and uploads what it was given.
	/// </summary>
	[TestMethod]
	public void ListRecordingAcrossAFrameGoesWithTheNext()
	{
		var readback = Device.CreateBuffer( new BufferInfo { Size = 1024, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 1024, Usage = BufferUsage.Storage, Name = "Written across frames" } );
		var others = Device.CreateBuffer( new BufferInfo { Size = 1024, Usage = BufferUsage.Storage, Name = "Written meanwhile" } );
		var data = Enumerable.Range( 0, 256 ).Select( i => (uint)(i * 7 + 1) ).ToArray();

		Device.BeginFrame();
		var carried = Device.ListsCarried;
		var list = Device.BeginCommandList( name: "Across frames" );
		list.UpdateBuffer<uint>( buffer, data );

		// Round to the slot it began in, with other lists' uploads in between
		for ( int i = 0; i < Device.FramesInFlight; i++ )
		{
			Device.BeginFrame();
			var other = Device.BeginCommandList();
			other.UpdateBuffer<uint>( others, new uint[256] );
			Device.Submit( other );
		}

		Assert.AreEqual( carried + 1, Device.ListsCarried, "carried once, into the frame after its own" );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead } );
		list.CopyBuffer( buffer, 0, readback, 0, 1024 );
		list.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );

		CollectionAssert.AreEqual( data, MemoryMarshal.Cast<byte, uint>( Device.Map( readback ) ).ToArray() );
	}

	/// <summary>
	/// A list another thread begins while a frame starts (the game's frame thread starts frames while the main thread
	/// records), after the switch and before the slot's old lists are recycled, belongs to the new frame: it isn't recycled
	/// with the slot's old lists under its recording, and records, submits and uploads what it was given.
	/// </summary>
	[TestMethod]
	public void ListBegunOnAnotherThreadWhileAFrameStartsStays()
	{
		var readback = Device.CreateBuffer( new BufferInfo { Size = 1024, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 1024, Usage = BufferUsage.Storage, Name = "Written by the other thread" } );
		var data = Enumerable.Range( 0, 256 ).Select( i => (uint)(i * 5 + 3) ).ToArray();

		// The slot the next frame takes has lists of its own to recycle
		for ( int i = 0; i < Device.FramesInFlight; i++ )
		{
			Device.BeginFrame();
			Device.Submit( Device.BeginCommandList( name: "Earlier" ) );
		}

		CommandList list = null;
		Device.FrameSwitchedForTests = () => list = System.Threading.Tasks.Task.Run( () => Device.BeginCommandList( name: "Begun meanwhile" ) ).Result;
		try
		{
			Device.BeginFrame();
		}
		finally
		{
			Device.FrameSwitchedForTests = null;
		}

		Assert.IsNotNull( list );
		list.UpdateBuffer<uint>( buffer, data );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead } );
		list.CopyBuffer( buffer, 0, readback, 0, 1024 );
		list.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );

		CollectionAssert.AreEqual( data, MemoryMarshal.Cast<byte, uint>( Device.Map( readback ) ).ToArray() );
		Device.Destroy( buffer );
		Device.Destroy( readback );
	}

	/// <summary>
	/// The validation layer is really checking: a copy into a buffer with no barrier after a fill of it is a write-after-write
	/// hazard, and gets reported. (Every other test passes only if nothing is reported.)
	/// </summary>
	[TestMethod]
	public void ValidationReportsHazards()
	{
		if ( !Device.ValidationEnabled ) Assert.Inconclusive( "The validation layer isn't installed" );

		var a = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage } );
		var b = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage } );
		var list = Device.BeginCommandList();
		list.FillBuffer( b, 1 );
		list.CopyBuffer( a, 0, b, 0, 256 );
		Device.Wait( Device.Submit( list ) );

		var errors = TakeValidationErrors();
		Assert.IsTrue( errors.Any( e => e.Contains( "SYNC-HAZARD" ) ), $"Reported: {string.Join( ", ", errors )}" );
	}

	/// <summary>
	/// A write to a buffer in one submission after a read of it in an earlier one is a hazard (submission order alone doesn't
	/// order them), reported by synchronization validation; the barrier a list of uploads begins with (the engine's, written
	/// outside a block while the last frame's draws may still read the buffer) orders it after the read.
	/// </summary>
	[TestMethod]
	public void WritesAfterAnEarlierSubmissionsReadWaitForIt()
	{
		if ( !Device.ValidationEnabled ) Assert.Inconclusive( "The validation layer isn't installed" );

		var buffer = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination, Name = "Read, then written" } );
		var copy = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Copy" } );
		var setup = Device.BeginCommandList();
		setup.UpdateBuffer<uint>( buffer, new uint[64] );
		setup.UpdateBuffer<uint>( copy, new uint[64] );
		Device.Wait( Device.Submit( setup ) );
		Assert.AreEqual( 0, TakeValidationErrors().Length );

		string[] ReadThenWrite( bool barrier )
		{
			var reads = Device.BeginCommandList( QueueType.Graphics, "Reads" );
			reads.CopyBuffer( buffer, 0, copy, 0, 256 );
			Device.Submit( reads );
			var writes = Device.BeginCommandList( QueueType.Graphics, "Writes" );
			if ( barrier ) writes.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
			writes.UpdateBuffer<uint>( buffer, new uint[64] );
			Device.Wait( Device.Submit( writes ) );
			return TakeValidationErrors();
		}

		var unordered = ReadThenWrite( barrier: false );
		Assert.IsTrue( unordered.Any( e => e.Contains( "SYNC-HAZARD-WRITE-AFTER-READ" ) ), $"Reported: {string.Join( ", ", unordered )}" );
		var ordered = ReadThenWrite( barrier: true );
		Assert.AreEqual( 0, ordered.Length, $"Reported: {string.Join( ", ", ordered )}" );

		Device.Destroy( buffer );
		Device.Destroy( copy );
	}

	/// <summary>
	/// The sample's scene, offscreen: the background is the clear colour, the triangle's corners are its vertex colours
	/// blended toward the middle, and both cubes - one sampling an uploaded checkerboard, one the compute pattern -
	/// cover the middle of their halves, in front of the background.
	/// </summary>
	[TestMethod]
	public void SampleSceneRenders()
	{
		const int width = 640, height = 400;
		var target = Target( width, height );
		using var scene = new SampleScene( Device, Format.RGBA8Unorm );

		var list = Device.BeginCommandList( QueueType.Graphics, "Scene" );
		scene.Render( list, target, ImageLayout.Undefined, ImageLayout.ShaderRead, 0.5f );
		Device.Wait( Device.Submit( list ) );

		var pixels = ReadBack( target, ImageLayout.ShaderRead );
		Save( pixels, width, height, "sample-scene" );

		var clear = ((byte)13, (byte)15, (byte)20, (byte)255);
		Assert.AreEqual( clear, Pixel( pixels, width, 2, 2 ), "the corner is background" );

		// The triangle: centred at (0, 0.55) in clip space, so near the top middle
		var triangle = Pixel( pixels, width, width / 2, (int)(height * (1 - 0.55f) / 2) );
		Assert.AreNotEqual( clear, triangle, "the triangle is drawn" );

		// Each cube covers the middle of its half: lit, and not the background
		var left = Pixel( pixels, width, width / 2 - 125, height / 2 + 10 );
		var right = Pixel( pixels, width, width / 2 + 125, height / 2 + 10 );
		Assert.AreNotEqual( clear, left, "the checkerboard cube is drawn" );
		Assert.AreNotEqual( clear, right, "the pattern cube is drawn" );
	}

	/// <summary>
	/// Compute writes a texture through the bindless storage table, and the texture reads back as written.
	/// </summary>
	[TestMethod]
	public void ComputeWritesThroughBindlessStorage()
	{
		var info = TextureInfo.Texture2D( SampleScene.PatternSize, SampleScene.PatternSize, Format.RGBA8Unorm, name: "Compute target" );
		info.Usage |= TextureUsage.Storage;
		var texture = Device.CreateTexture( info );
		Assert.IsTrue( Device.StorageIndex( texture ) >= 0 );
		Assert.IsTrue( Device.BindlessIndex( texture ) >= 2, "texture index 0 means none to s&box's shaders" );

		var shader = Device.CreateShader( LoadShader( "pattern.cs.spv" ), "pattern" );
		var pipeline = Device.CreateComputePipeline( new ComputePipelineInfo { Shader = shader, Entry = "MainCs" } );

		var list = Device.BeginCommandList( QueueType.Graphics, "Pattern" );
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.General, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite ) );
		list.SetPipeline( pipeline );
		list.PushConstants( new PatternConstants { Target = (uint)Device.StorageIndex( texture ), Width = SampleScene.PatternSize, Height = SampleScene.PatternSize } );
		list.Dispatch( SampleScene.PatternSize / 8, SampleScene.PatternSize / 8 );
		Device.Wait( Device.Submit( list ) );

		var pixels = ReadBack( texture, ImageLayout.General );
		Save( pixels, SampleScene.PatternSize, SampleScene.PatternSize, "compute-pattern" );

		// Opaque everywhere, and not one flat colour
		var distinct = new HashSet<int>();
		for ( int i = 0; i < pixels.Length; i += 4 )
		{
			Assert.AreEqual( 255, pixels[i + 3] );
			distinct.Add( pixels[i] | pixels[i + 1] << 8 | pixels[i + 2] << 16 );
		}

		Assert.IsTrue( distinct.Count > 50, $"{distinct.Count} colours" );
	}

	[StructLayout( LayoutKind.Sequential )]
	struct PatternConstants
	{
		public uint Target;
		public float Time;
		public uint Width, Height;
	}

	static byte[] LoadShader( string name )
	{
		using var stream = typeof( SampleScene ).Assembly.GetManifestResourceStream( name );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}

	/// <summary>
	/// Work on the async compute queue fills a buffer; the graphics queue waits for its sync point and copies it back.
	/// </summary>
	[TestMethod]
	public void QueuesSynchroniseThroughTimelines()
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 4096, Usage = BufferUsage.Storage, Name = "Shared" } );
		var readback = Device.CreateBuffer( new BufferInfo { Size = 4096, Memory = MemoryLocation.Readback, Name = "Readback" } );

		var compute = Device.BeginCommandList( QueueType.Compute, "Fill" );
		compute.FillBuffer( buffer, 0xC0FFEE );
		var filled = Device.Submit( compute );

		var graphics = Device.BeginCommandList( QueueType.Graphics, "Copy" );
		graphics.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead } );
		graphics.CopyBuffer( buffer, 0, readback, 0, 4096 );
		graphics.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		var copied = Device.Submit( graphics, [filled] );

		Device.Wait( copied );
		Assert.IsTrue( Device.IsComplete( filled ) );
		Assert.IsTrue( MemoryMarshal.Cast<byte, uint>( Device.Map( readback ) ).ToArray().All( v => v == 0xC0FFEE ) );
	}

	/// <summary>
	/// Barriers on the async compute queue that name graphics stages and accesses (a texture sampled by pixel and compute
	/// shaders, a buffer read as indices) keep only what that queue has, which validation accepts.
	/// </summary>
	[TestMethod]
	public void ComputeQueueBarriersKeepItsStages()
	{
		if ( !Device.Capabilities.AsyncCompute ) Assert.Inconclusive( "No async compute queue" );

		var texture = Device.CreateTexture( TextureInfo.RenderTarget( 16, 16, Format.RGBA8Unorm, name: "Moved" ) with { Usage = TextureUsage.ColorTarget | TextureUsage.Sampled | TextureUsage.Storage } );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage | BufferUsage.Index, Name = "Read as indices" } );

		var list = Device.BeginCommandList( QueueType.Compute, "Graphics stages" );
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.General, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite,
			PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite ) );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.VertexInput | PipelineStage.ComputeShader, SourceAccess = MemoryAccess.IndexRead | MemoryAccess.ShaderWrite, DestinationStage = PipelineStage.ComputeShader, DestinationAccess = MemoryAccess.ShaderRead } );
		list.Barrier( PipelineStage.AllGraphics, MemoryAccess.ColorTargetWrite, PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetRead );
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.General, ImageLayout.ShaderRead, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite,
			PipelineStage.FragmentShader | PipelineStage.ComputeShader, MemoryAccess.ShaderRead ) );
		Device.Wait( Device.Submit( list ) );

		Device.Destroy( texture );
		Device.Destroy( buffer );
	}

	/// <summary>
	/// Writes in a deferred-uploads scope go with the next submission instead of waiting each, and work submitted after the
	/// scope reads them.
	/// </summary>
	[TestMethod]
	public void DeferredUploadsArriveBeforeLaterWork()
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 4096, Usage = BufferUsage.Storage, Name = "Written" } );
		var readback = Device.CreateBuffer( new BufferInfo { Size = 4096, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var values = Enumerable.Range( 0, 1024 ).Select( i => (uint)(i * 7 + 3) ).ToArray();

		var before = Device.Submissions;
		using ( Device.DeferUploads() ) Device.WriteBuffer<uint>( buffer, values );
		Assert.AreEqual( before, Device.Submissions, "a deferred write submits nothing by itself" );

		var copy = Device.BeginCommandList( QueueType.Graphics, "Copy" );
		copy.CopyBuffer( buffer, 0, readback, 0, 4096 );
		copy.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( copy ) );

		CollectionAssert.AreEqual( values, MemoryMarshal.Cast<byte, uint>( Device.Map( readback ) ).ToArray() );
	}

	/// <summary>
	/// Lists submitted later go to the GPU together, in order, in one submission: with the next list submitted normally,
	/// or when something waits for them.
	/// </summary>
	[TestMethod]
	public void ListsSubmittedLaterGoTogetherInOrder()
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 4096, Usage = BufferUsage.Storage, Name = "Ordered" } );
		var readback = Device.CreateBuffer( new BufferInfo { Size = 4096, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var transfer = new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead | MemoryAccess.TransferWrite };

		uint Run( bool waitForLast )
		{
			var before = Device.Submissions;
			SyncPoint last = default;
			for ( uint i = 1; i <= 3; i++ )
			{
				var list = Device.BeginCommandList( QueueType.Graphics, $"Fill {i}" );
				list.Barrier( transfer );
				list.FillBuffer( buffer, i );
				last = Device.SubmitLater( list );
			}

			Assert.AreEqual( before, Device.Submissions, "nothing reached the GPU yet" );
			Assert.IsFalse( Device.IsComplete( last ) );

			if ( !waitForLast )
			{
				var copy = Device.BeginCommandList( QueueType.Graphics, "Copy" );
				copy.Barrier( transfer );
				copy.CopyBuffer( buffer, 0, readback, 0, 4096 );
				copy.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
				Device.Wait( Device.Submit( copy ) );
				Assert.IsTrue( Device.IsComplete( last ) );
				Assert.AreEqual( before + 1, Device.Submissions, "the three went with the copy, in one submission" );
				return MemoryMarshal.Cast<byte, uint>( Device.Map( readback ) )[0];
			}

			Device.Wait( last );
			Assert.AreEqual( before + 1, Device.Submissions, "waiting sent the three in one submission" );
			return 0;
		}

		Run( waitForLast: true );
		Assert.AreEqual( 3u, Run( waitForLast: false ), "the last fill submitted is the one left" );
	}

	/// <summary>
	/// Per-frame uploads larger than a page, across more frames than are in flight, all arrive.
	/// </summary>
	[TestMethod]
	public void UploadsSpanPagesAndFrames()
	{
		const int count = 3 << 20;
		var buffer = Device.CreateBuffer( new BufferInfo { Size = count * 4L, Usage = BufferUsage.Storage, Name = "Big" } );
		var readback = Device.CreateBuffer( new BufferInfo { Size = count * 4L, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var data = new uint[count];

		for ( int frame = 0; frame < 5; frame++ )
		{
			Device.BeginFrame();
			for ( int i = 0; i < count; i++ ) data[i] = (uint)(i * 3 + frame);

			var list = Device.BeginCommandList();
			list.UpdateBuffer<uint>( buffer, data.AsSpan( 0, count / 2 ) );
			list.UpdateBuffer<uint>( buffer, data.AsSpan( count / 2 ), count / 2 * 4L );
			list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead } );
			list.CopyBuffer( buffer, 0, readback, 0, count * 4L );
			list.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
			Device.Wait( Device.Submit( list ) );

			CollectionAssert.AreEqual( data, MemoryMarshal.Cast<byte, uint>( Device.Map( readback ) ).ToArray(), $"frame {frame}" );
		}
	}

	/// <summary>
	/// Destroyed resources are freed once the frames that used them are done, and their handles stop working at once.
	/// </summary>
	[TestMethod]
	public void DestructionWaitsForTheGpu()
	{
		Device.BeginFrame();
		var baseline = Device.MemoryUsage.Used;
		var texture = Device.CreateTexture( TextureInfo.RenderTarget( 512, 512, Format.RGBA16Float ) );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 1 << 20, Usage = BufferUsage.Storage } );

		var list = Device.BeginCommandList();
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( texture, Vector4.One );
		list.FillBuffer( buffer, 1 );
		Device.Submit( list );

		Device.Destroy( texture );
		Device.Destroy( buffer );
		Assert.IsFalse( Device.IsAlive( texture ) );
		Assert.ThrowsException<ArgumentException>( () => Device.InfoOf( texture ) );
		Assert.IsTrue( Device.MemoryUsage.Used > baseline, "the memory waits for the GPU" );

		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Assert.AreEqual( baseline, Device.MemoryUsage.Used, "freed once its frame finished" );
	}

	/// <summary>
	/// The memory map lists every block the allocator holds with what's allocated in it, in address order: its blocks add up to
	/// what the device reserved and uses, and a new buffer is in one of them.
	/// </summary>
	[TestMethod]
	public void TheMemoryMapListsWhatIsAllocated()
	{
		Device.BeginFrame();
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 1 << 20, Usage = BufferUsage.Storage } );

		var map = Device.MemoryMap();
		var (reserved, used) = Device.MemoryUsage;
		Assert.AreEqual( reserved, map.Sum( x => x.Size ), "the blocks are what's reserved" );
		Assert.AreEqual( used, map.Sum( x => x.Used ), "their allocations are what's used" );
		foreach ( var block in map )
		{
			Assert.AreEqual( block.Used, block.Allocations.Sum( x => x.Size ), "a block's allocations add up to its use" );
			for ( int i = 1; i < block.Allocations.Count; i++ )
				Assert.IsTrue( block.Allocations[i].Offset >= block.Allocations[i - 1].Offset + block.Allocations[i - 1].Size, "in address order, not overlapping" );
		}

		Assert.IsTrue( map.Any( x => !x.Images && x.Allocations.Any( a => a.Size >= 1 << 20 ) ), "the buffer is in a buffer block" );
		Device.Destroy( buffer );
	}

	/// <summary>
	/// Bindless slots are handed out in linear/sRGB pairs and reused once freed.
	/// </summary>
	[TestMethod]
	public void BindlessSlotsComeInPairs()
	{
		var a = Device.CreateTexture( TextureInfo.Texture2D( 4, 4, Format.RGBA8Unorm ) );
		var b = Device.CreateTexture( TextureInfo.Texture2D( 4, 4, Format.RGBA16Float ) );
		Assert.AreEqual( 0, Device.BindlessIndex( a ) % 2 );
		Assert.AreEqual( Device.BindlessIndex( a ) + 2, Device.BindlessIndex( b ) );

		var freed = Device.BindlessIndex( a );
		Device.Destroy( a );
		Device.WaitIdle();
		Device.BeginFrame();

		var c = Device.CreateTexture( TextureInfo.Texture2D( 4, 4, Format.RGBA8Unorm ) );
		Assert.AreEqual( freed, Device.BindlessIndex( c ) );
	}

	/// <summary>
	/// The pipeline cache comes back out non-empty, and a device made with it accepts it.
	/// </summary>
	[TestMethod]
	public void PipelineCacheRoundTrips()
	{
		using ( new SampleScene( Device, Format.RGBA8Unorm ) ) { }
		var data = Device.GetPipelineCacheData();
		Assert.IsTrue( data.Length > 32 );

		using var second = Device.Create( new DeviceOptions { Presentation = false, PipelineCacheData = data } );
		using ( new SampleScene( second, Format.RGBA8Unorm ) ) { }
		Assert.IsTrue( second.GetPipelineCacheData().Length >= data.Length / 2 );
	}

	/// <summary>
	/// A device with a pipeline cache folder saves its cache there when disposed, after making pipelines, and the next
	/// device for the same GPU loads it; a device that made none since loading doesn't write it again.
	/// </summary>
	[TestMethod]
	public void PipelineCacheFolderRoundTrips()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-pipelines-{Guid.NewGuid():N}" );
		try
		{
			string path;
			using ( var first = Device.Create( new DeviceOptions { Presentation = false, PipelineCacheFolder = folder } ) )
			{
				Assert.IsFalse( first.PipelineCacheLoaded, "nothing to load the first time" );
				using ( new SampleScene( first, Format.RGBA8Unorm ) ) { }
				Assert.IsTrue( first.PipelineCreation.Made > 0, "the sample made pipelines" );
				path = first.PipelineCachePath;
			}

			Assert.IsTrue( File.Exists( path ), "saved when the device was disposed" );
			var written = File.GetLastWriteTimeUtc( path );

			using ( var second = Device.Create( new DeviceOptions { Presentation = false, PipelineCacheFolder = folder } ) )
			{
				Assert.IsTrue( second.PipelineCacheLoaded, "the next device loads it" );
				Assert.IsFalse( second.SavePipelineCache(), "nothing new to save" );
			}

			Assert.AreEqual( written, File.GetLastWriteTimeUtc( path ), "not written again" );
		}
		finally
		{
			if ( Directory.Exists( folder ) ) Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Saving the pipeline cache reads it into native memory, not a managed array: a map's cache runs to hundreds of MiB,
	/// and each save in the game put that much on the large object heap. The file is written through its handle, so a save
	/// allocates only its paths, under 4 KiB whatever the cache's size. The part manifest, which the same call writes as
	/// lines of text (about 12 KiB for the sample, against a cache the driver sizes at 26 to 33 KiB), is off here: its
	/// lines are its own allocations, not a copy of the cache.
	/// </summary>
	[TestMethod]
	public void SavingThePipelineCacheAllocatesNoCopyOfIt()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-pipelines-{Guid.NewGuid():N}" );
		try
		{
			// A save on a device of its own first pays for the process's one-time setup (JIT, statics), so the save measured
			// below costs what every save does
			using ( var warm = Device.Create( new DeviceOptions { Presentation = false, PipelineCacheFolder = Path.Combine( folder, "warm" ), PipelinePartManifest = false } ) )
			{
				using ( new SampleScene( warm, Format.RGBA8Unorm ) ) { }
				warm.SavePipelineCache();
			}

			using var device = Device.Create( new DeviceOptions { Presentation = false, PipelineCacheFolder = folder, PipelinePartManifest = false } );
			using ( new SampleScene( device, Format.RGBA8Unorm ) ) { }

			var before = GC.GetAllocatedBytesForCurrentThread();
			Assert.IsTrue( device.SavePipelineCache(), "the sample's pipelines are saved" );
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

			var saved = new FileInfo( device.PipelineCachePath ).Length;
			Assert.IsTrue( allocated < Math.Min( saved / 2, 4096 ), $"saving a {saved} B cache allocated {allocated} B" );
		}
		finally
		{
			if ( Directory.Exists( folder ) ) Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A cache the size of a big map's (Construct's runs to about 300 MiB) is saved whole from native memory without a managed
	/// copy, and loads back in one array.
	/// </summary>
	[TestMethod]
	public unsafe void AMapSizedPipelineCacheSavesWithoutAManagedCopy()
	{
		const int size = 300 << 20;
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-pipelines-{Guid.NewGuid():N}" );
		var data = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc( size );
		try
		{
			var span = new Span<byte>( data, size );
			for ( int i = 0; i < size; i += 4096 ) span[i] = (byte)(i >> 12);

			var path = Path.Combine( folder, "cache.bin" );
			var before = GC.GetAllocatedBytesForCurrentThread();
			Assert.IsTrue( PipelineCacheFile.Save( path, span ), "within the cap" );
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			Assert.IsTrue( allocated < 1 << 20, $"saving {size} B allocated {allocated} B" );

			before = GC.GetAllocatedBytesForCurrentThread();
			var loaded = PipelineCacheFile.Load( path );
			allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			Assert.IsNotNull( loaded, "loads back" );
			Assert.IsTrue( span.SequenceEqual( loaded ), "as saved" );
			Assert.IsTrue( allocated < size + (1 << 20), $"loading {size} B allocated {allocated} B" );
		}
		finally
		{
			System.Runtime.InteropServices.NativeMemory.Free( data );
			if ( Directory.Exists( folder ) ) Directory.Delete( folder, true );
		}
	}

	/// <summary>A pipeline cache file that's truncated, corrupted or not one loads as nothing; a good one as what was saved.</summary>
	[TestMethod]
	public void DamagedPipelineCacheFilesLoadAsNothing()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-pipelines-{Guid.NewGuid():N}" );
		try
		{
			var path = Path.Combine( folder, "cache.bin" );
			var data = new byte[1000];
			new Random( 3 ).NextBytes( data );
			Assert.IsTrue( PipelineCacheFile.Save( path, data ) );
			CollectionAssert.AreEqual( data, PipelineCacheFile.Load( path ), "a good file loads as saved" );

			var bytes = File.ReadAllBytes( path );
			bytes[^1] ^= 1;
			File.WriteAllBytes( path, bytes );
			Assert.IsNull( PipelineCacheFile.Load( path ), "a flipped bit" );

			File.WriteAllBytes( path, bytes[..^10] );
			Assert.IsNull( PipelineCacheFile.Load( path ), "a truncated file" );

			File.WriteAllBytes( path, data );
			Assert.IsNull( PipelineCacheFile.Load( path ), "not a cache file" );
			Assert.IsNull( PipelineCacheFile.Load( Path.Combine( folder, "missing.bin" ) ), "no file" );
		}
		finally
		{
			if ( Directory.Exists( folder ) ) Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Many frames of the sample through the frame ring don't grow memory: command lists, upload pages and descriptor
	/// pools are recycled.
	/// </summary>
	[TestMethod]
	public void FramesRecycle()
	{
		using var scene = new SampleScene( Device, Format.RGBA8Unorm );
		var target = Target( 256, 256 );
		var layout = ImageLayout.Undefined;
		long reserved = 0;

		for ( int frame = 0; frame < 60; frame++ )
		{
			Device.BeginFrame();
			var list = Device.BeginCommandList();
			scene.Render( list, target, layout, ImageLayout.ShaderRead, frame / 60f );
			layout = ImageLayout.ShaderRead;
			Device.Submit( list );
			if ( frame == 10 ) reserved = Device.MemoryUsage.Reserved;
		}

		Device.WaitIdle();
		Assert.AreEqual( reserved, Device.MemoryUsage.Reserved, "steady frames allocate no more memory" );
	}

	/// <summary>
	/// Writes made outside any frame (loading textures before the first one) give their upload memory back as soon as
	/// they've finished, rather than keeping it until the next frame begins.
	/// </summary>
	[TestMethod]
	public void WritesBeforeTheFirstFrameReuseUploadMemory()
	{
		var pixels = new byte[1024 * 1024 * 4];
		var textures = new List<TextureHandle>();
		textures.Add( Device.CreateTexture( TextureInfo.Texture2D( 1024, 1024, Format.RGBA8Unorm, name: "Loaded 0" ) ) );
		Device.WriteTexture( textures[0], pixels );
		var after = Device.MemoryUsage.Reserved;

		for ( int i = 1; i < 32; i++ )
		{
			var texture = Device.CreateTexture( TextureInfo.Texture2D( 1024, 1024, Format.RGBA8Unorm, name: $"Loaded {i}" ) );
			Device.WriteTexture( texture, pixels );
			textures.Add( texture );
		}

		// 31 more textures of 4 MB each; without reuse, each write's 4 MB of upload memory would stay as well
		var grown = Device.MemoryUsage.Reserved - after;
		Assert.IsTrue( grown < 31 * pixels.Length * 3 / 2, $"{grown >> 20} MB reserved for 124 MB of textures" );
		foreach ( var texture in textures ) Device.Destroy( texture );
	}

	/// <summary>
	/// A 1280x720 4x MSAA depth target, the game window's at 4x: without a stencil it takes less video memory than with one.
	/// </summary>
	[TestMethod]
	public void DepthWithoutStencilTakesLessMemory()
	{
		var plain = Device.CreateTexture( TextureInfo.RenderTarget( 1280, 720, Format.D32Float, samples: 4, name: "D32" ) );
		var stencil = Device.CreateTexture( TextureInfo.RenderTarget( 1280, 720, Format.D32FloatS8Uint, samples: 4, name: "D32S8" ) );
		try
		{
			var plainBytes = (long)Device.Get( plain ).Memory.Size;
			var stencilBytes = (long)Device.Get( stencil ).Memory.Size;
			TestContext.WriteLine( $"1280x720 4x: D32Float {plainBytes / 1024.0 / 1024:F2} MiB, D32FloatS8Uint {stencilBytes / 1024.0 / 1024:F2} MiB" );
			Assert.IsTrue( plainBytes < stencilBytes );
		}
		finally
		{
			Device.Destroy( plain );
			Device.Destroy( stencil );
		}
	}
}
