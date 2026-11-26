using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BorealisGpuTests;

/// <summary>
/// A shader drawn on its own through <see cref="ShaderDrawer"/> by a draw that's redrawn every frame (a scene's custom
/// object, <see cref="Borealis.RedrawnEveryFrame"/>) doesn't wait for its pipeline: workers make it and the draw is skipped
/// meanwhile, and its variant is made on a worker first. Any other draw of the same shader still waits for its own and draws.
/// </summary>
[TestClass]
public class RedrawnDrawTests : GpuTest
{
	const int Size = 32;

	[StructLayout( LayoutKind.Sequential )]
	struct BoxInstance
	{
		public Vector4 Rect, Color, BorderRadius, BorderRadiusV, BorderSize, BorderColorL, BorderColorT, BorderColorR, BorderColorB;
		public int TextureIndex, SamplerIndex;
		public float BackgroundAngle;
		public Vector4 BackgroundRect, BackgroundTint;
		public int BorderImageIndex, BorderImageSamplerIndex;
		public Vector4 BorderImageSlice, BorderImageTint;
		public uint Flags;
		public int ScissorIndex, TransformIndex, InverseScissorIndex, TextMaskIndex, TextMaskSamplerIndex;
		public Vector4 BackgroundClipRect;
		public int ShapeIndex;
	}

	[TestMethod]
	public void RedrawnDrawsSkipUntilWorkersMakeTheirPipeline()
	{
		using var backend = new BackendDevice( Device, ShaderFileTests.Files );
		using var defaults = new ShaderDefaults( Device );
		var shader = backend.Shaders.Load( "shaders/ui_cssbox_batched.shader" );
		if ( shader is null ) Assert.Inconclusive( "ui_cssbox_batched isn't built" );
		using var drawer = new ShaderDrawer( Device, backend.Shaders, defaults );
		Device.SkipDrawsUntilReady = true;

		BoxInstance[] boxes = [new() { Rect = new Vector4( 0, 0, Size, Size ), Color = new Vector4( 1, 0, 0, 1 ), ScissorIndex = -1, InverseScissorIndex = -1, ShapeIndex = -1 }];
		var instances = Buffer( MemoryMarshal.AsBytes<BoxInstance>( boxes ) );
		var transforms = Buffer( MemoryMarshal.AsBytes<Matrix4x4>( [Matrix4x4.Identity] ) );
		var indices = Device.CreateBuffer( new BufferInfo { Size = 6 * 4, Usage = BufferUsage.Index } );
		Device.WriteBuffer<uint>( indices, [0, 1, 2, 0, 2, 3] );
		var attributes = new ShaderAttributes();
		attributes.Set( "BoxInstances", instances );
		attributes.Set( "TransformBuffer", transforms );
		attributes.Set( "LayerMat", ShaderDefaults.Floats( Matrix4x4.Identity ) );
		attributes.Set( "UIGammaOutput", 1 );
		attributes.SetCombo( "D_BLENDMODE", 0 );

		var color = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA8Unorm, name: "Redrawn" ) );
		var layout = ImageLayout.Undefined;
		void DrawBoxes( CommandList list ) => drawer.DrawIndexedInstanced( list, shader, attributes, Format.RGBA8Unorm, 1, indices, 6, boxes.Length, new Vector4( 0, 0, Size, Size ) );
		(byte, byte, byte, byte) Draw( bool redrawn, out int skipped )
		{
			var list = Device.BeginCommandList( QueueType.Graphics, "Redrawn" );
			list.Barrier( TextureBarrier.Transition( color, layout, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
			layout = ImageLayout.ColorTarget;
			list.BeginRendering( [ColorTarget.Of( color, Vector4.Zero )] );
			if ( redrawn )
			{
				using ( Borealis.RedrawnEveryFrame.Begin() ) DrawBoxes( list );
			}
			else DrawBoxes( list );
			list.EndRendering();
			skipped = list.SkippedDraws;
			Device.Wait( Device.Submit( list ) );
			var pixels = ReadBack( color, ImageLayout.ColorTarget );
			layout = ImageLayout.CopySource;
			return Pixel( pixels, Size, Size / 2, Size / 2 );
		}

		// Redrawn: its variant is made on a worker (ShaderDrawer.MakeVariantsAside), nothing drawn or asked of the device meanwhile
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)0), Draw( redrawn: true, out _ ) );
		Assert.AreEqual( 0, Device.DeferredPipelines.Made + Device.DeferredPipelines.Pending, "the variant wasn't made where it was asked for" );
		Assert.IsTrue( System.Threading.SpinWait.SpinUntil( () => drawer.VariantsBeingMadeAside == 0, TimeSpan.FromSeconds( 60 ) ) );

		// Then the pipeline goes to the workers (very rarely they make it before the draw)
		Draw( redrawn: true, out var skippedFirst );
		var stats = Device.DeferredPipelines;
		Assert.AreEqual( 1, stats.Made + stats.Pending, "the redrawn draw's pipeline was made on the workers, not where it was asked for" );
		Assert.IsTrue( skippedFirst <= 1 );

		// Not redrawn: a pipeline of its own, waited for, drawn now
		Assert.AreEqual( ((byte)255, (byte)0, (byte)0, (byte)255), Draw( redrawn: false, out var skippedOther ) );
		Assert.AreEqual( 0, skippedOther );

		// The redrawn one draws once the workers are done
		Assert.IsTrue( Device.WaitForPendingPipelines( TimeSpan.FromSeconds( 60 ) ) );
		Assert.AreEqual( ((byte)255, (byte)0, (byte)0, (byte)255), Draw( redrawn: true, out var skippedAfter ) );
		Assert.AreEqual( 0, skippedAfter );
		Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ) );
	}

	/// <summary>
	/// A dispatch redrawn every frame (a sprite batch's) whose variant is still made on a worker doesn't run and says so
	/// (<see cref="ShaderDrawer.VariantPending"/>), so the backend skips it as it does such a draw rather than throwing; it runs
	/// once the variant is made.
	/// </summary>
	[TestMethod]
	public void RedrawnDispatchesSkipWhileTheirVariantIsMade()
	{
		using var backend = new BackendDevice( Device );
		using var drawer = new ShaderDrawer( backend );
		var sprite = backend.Shaders.Load( "shaders/sprite/sprite_cs.shader" );
		if ( sprite is null ) Assert.Inconclusive( "sprite_cs isn't compiled" );

		var sprites = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage, Name = "Sprites" } );
		var spritesOut = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage, Name = "Sprites out" } );
		var distances = Device.CreateBuffer( new BufferInfo { Size = 64, Usage = BufferUsage.Storage, Name = "Distances" } );
		var counter = Device.CreateBuffer( new BufferInfo { Size = 4, Usage = BufferUsage.Storage, Name = "Counter" } );
		Device.WriteBuffer<float>( sprites, new float[64] );
		Device.WriteBuffer<float>( distances, new float[16] );
		Device.WriteBuffer<uint>( counter, [0] );
		var attributes = new ShaderAttributes();
		attributes.SetData( "PerViewConstantBuffer_t", new byte[Borealis.Gpu.ViewConstants.NativeSize] );
		attributes.Set( "Sprites", sprites );
		attributes.Set( "SpriteBufferOut", spritesOut );
		attributes.Set( "DistanceBuffer", distances );
		attributes.Set( "AtomicCounter", counter );
		attributes.Set( "SpriteCount", 1 );
		attributes.Set( "CameraPosition", 0, 0, 0 );

		bool Dispatch()
		{
			Device.BeginFrame();
			var list = Device.BeginCommandList( QueueType.Graphics, "Redrawn dispatch" );
			bool done;
			using ( Borealis.RedrawnEveryFrame.Begin() ) done = drawer.Dispatch( list, sprite, attributes, 1, 1, 1 );
			Device.Wait( Device.Submit( list ) );
			return done;
		}

		Assert.IsFalse( Dispatch(), "the variant is made on a worker first" );
		Assert.IsTrue( drawer.VariantPending, "a dispatch skipped while its variant is made says so" );
		Assert.IsTrue( System.Threading.SpinWait.SpinUntil( () => drawer.VariantsBeingMadeAside == 0, TimeSpan.FromSeconds( 60 ) ) );
		Assert.IsTrue( Dispatch(), "it runs once the variant is made" );
		Assert.IsFalse( drawer.VariantPending );
		Device.WaitIdle();

		foreach ( var buffer in (ReadOnlySpan<BufferHandle>)[sprites, spritesOut, distances, counter] ) Device.Destroy( buffer );
	}

	BufferHandle Buffer( ReadOnlySpan<byte> data )
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = data.Length, Usage = BufferUsage.Storage } );
		Device.WriteBuffer( buffer, data );
		return buffer;
	}
}
