using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BasaltGpuTests;

/// <summary>
/// The engine's UI shader (<c>ui_cssbox_batched.shader</c>) drawing through Basalt.Gpu with <see cref="ShaderDrawer"/>,
/// fed the instance tables the engine's painter uploads. Inconclusive where the shaders aren't built.
/// </summary>
[TestClass]
public class UiShaderTests : GpuTest
{
	const int Size = 128;

	/// <summary>
	/// Mirrors <c>UICssBoxBatched.BoxInstance</c> (Sandbox.Engine) and <c>BoxInstanceData</c> in the shader.
	/// </summary>
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

	static BoxInstance Box( float x, float y, float w, float h, Vector4 color ) => new()
	{
		Rect = new Vector4( x, y, w, h ),
		Color = color,
		ScissorIndex = -1,
		InverseScissorIndex = -1,
		ShapeIndex = -1,
	};

	/// <summary>
	/// A rounded box with a border and a plain box, drawn as one instanced batch into an 8-bit target the way the
	/// painter draws into a texture (gamma output on): fills, borders and rounded corners land where the CSS says.
	/// </summary>
	[TestMethod]
	public void BatchedBoxesDraw()
	{
		using var library = new ShaderLibrary( Device, ShaderFileTests.Files );
		using var defaults = new ShaderDefaults( Device );
		var shader = library.Load( "shaders/ui_cssbox_batched.shader" );
		using var drawer = new ShaderDrawer( Device, library, defaults );

		var red = new Vector4( 1, 0, 0, 1 );
		var blue = new Vector4( 0, 0, 1, 1 );
		var rounded = Box( 16, 16, 64, 64, red );
		rounded.BorderRadius = rounded.BorderRadiusV = new Vector4( 16 );
		rounded.BorderSize = new Vector4( 4 );
		rounded.BorderColorL = rounded.BorderColorT = rounded.BorderColorR = rounded.BorderColorB = blue;
		BoxInstance[] boxes = [rounded, Box( 96, 96, 24, 24, new Vector4( 0, 1, 0, 1 ) )];

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

		var color = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA8Unorm, name: "UI" ) );
		var list = Device.BeginCommandList( QueueType.Graphics, "UI" );
		list.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.None, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( color, Vector4.Zero )] );
		Assert.IsTrue( drawer.DrawIndexedInstanced( list, shader, attributes, Format.RGBA8Unorm, 1, indices, 6, boxes.Length, new Vector4( 0, 0, Size, Size ) ) );
		list.EndRendering();
		Device.Wait( Device.Submit( list ) );

		var pixels = ReadBack( color, ImageLayout.ColorTarget );
		Save( pixels, Size, Size, "ui-batched-boxes" );

		Assert.AreEqual( ((byte)255, (byte)0, (byte)0, (byte)255), Pixel( pixels, Size, 48, 48 ), "the fill" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)255, (byte)255), Pixel( pixels, Size, 17, 48 ), "the left border" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)0), Pixel( pixels, Size, 17, 17 ), "outside the rounded corner" );
		Assert.AreEqual( ((byte)0, (byte)255, (byte)0, (byte)255), Pixel( pixels, Size, 108, 108 ), "the second instance" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)0), Pixel( pixels, Size, 90, 40 ), "between the boxes" );
	}

	BufferHandle Buffer( ReadOnlySpan<byte> data )
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = data.Length, Usage = BufferUsage.Storage } );
		Device.WriteBuffer( buffer, data );
		return buffer;
	}
}
