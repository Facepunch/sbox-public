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

	BufferHandle Buffer( ReadOnlySpan<byte> data )
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = data.Length, Usage = BufferUsage.Storage } );
		Device.WriteBuffer( buffer, data );
		return buffer;
	}
}
