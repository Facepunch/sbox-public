using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BorealisGpuTests;

/// <summary>
/// The engine's UI shader (<c>ui_cssbox_batched.shader</c>) drawing through Borealis.Gpu with <see cref="ShaderDrawer"/>,
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
		var pixels = DrawBoxes( Device );
		Save( pixels, Size, Size, "ui-batched-boxes" );

		Assert.AreEqual( ((byte)255, (byte)0, (byte)0, (byte)255), Pixel( pixels, Size, 48, 48 ), "the fill" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)255, (byte)255), Pixel( pixels, Size, 17, 48 ), "the left border" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)0), Pixel( pixels, Size, 17, 17 ), "outside the rounded corner" );
		Assert.AreEqual( ((byte)0, (byte)255, (byte)0, (byte)255), Pixel( pixels, Size, 108, 108 ), "the second instance" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)0), Pixel( pixels, Size, 90, 40 ), "between the boxes" );
	}

	/// <summary>
	/// The batched boxes as the game window's overlay draws them (gamma output, blended into an 8-bit target with no depth)
	/// draw the same through a fast link of pipeline library parts (no optimised link swapped in) as through a pipeline
	/// made whole, and the validation layer, synchronisation and best practices included, reports no error and nothing
	/// about either pipeline's creation or draw. Inconclusive without pipeline libraries.
	/// </summary>
	[TestMethod]
	public void LinkedUiPipelineDrawsAsWholeAndValidationIsClean()
	{
		if ( !Device.Capabilities.PipelineLibraries ) Assert.Inconclusive( "The device has no fast-linking graphics pipeline libraries" );

		var linked = DrawOnDeviceOfItsOwn( libraries: true, out var linkedMessages );
		var whole = DrawOnDeviceOfItsOwn( libraries: false, out var wholeMessages );
		Save( linked, Size, Size, "ui-batched-boxes-linked" );

		Assert.AreEqual( 0, linkedMessages.Count, "Linked from parts:\n" + string.Join( "\n", linkedMessages.Take( 5 ) ) );
		Assert.AreEqual( 0, wholeMessages.Count, "Made whole:\n" + string.Join( "\n", wholeMessages.Take( 5 ) ) );
		CollectionAssert.AreEqual( whole, linked, "the fast link draws what the pipeline made whole does" );
	}

	/// <summary>The boxes drawn on a device of its own, with the validation layer's errors and pipeline warnings.</summary>
	static byte[] DrawOnDeviceOfItsOwn( bool libraries, out List<string> messages )
	{
		var found = new List<string>();
		var options = new DeviceOptions
		{
			Presentation = false,
			PipelineLibraries = libraries,
			Validation = true,
			SynchronizationValidation = true,
			BestPracticesValidation = true,
			Message = m =>
			{
				if ( Reported( m ) ) lock ( found ) found.Add( $"{m.Id}: {m.Text}" );
			},
		};

		byte[] pixels;
		using ( var device = Device.Create( options ) )
		{
			Assert.AreEqual( libraries, device.Capabilities.PipelineLibraries );
			device.SkipDrawsUntilReady = false;
			device.OptimisesLinks = false;
			pixels = DrawBoxes( device );
		}

		messages = found;
		return pixels;
	}

	// Errors, and warnings about making or drawing with a graphics pipeline (best practices' others are about memory,
	// barriers and the like, which this test doesn't hold)
	static bool Reported( DeviceMessage m ) => m.Severity == MessageSeverity.Error
		|| m.Severity == MessageSeverity.Warning && m.Id is { } id && (id.StartsWith( "BestPractices-Pipeline-", StringComparison.Ordinal )
			|| id.StartsWith( "BestPractices-vkCreateGraphicsPipelines", StringComparison.Ordinal ) || id.StartsWith( "Undefined-Value", StringComparison.Ordinal ));

	/// <summary>The two boxes drawn on <paramref name="device"/> (<see cref="BatchedBoxesDraw"/>), read back.</summary>
	static byte[] DrawBoxes( Device device )
	{
		// ui_cssbox_batched is a Slang module: a backend's library reads it
		using var backend = new BackendDevice( device, ShaderFileTests.Files );
		var library = backend.Shaders;
		using var defaults = new ShaderDefaults( device );
		var shader = library.Load( "shaders/ui_cssbox_batched.shader" );
		using var drawer = new ShaderDrawer( device, library, defaults );

		var red = new Vector4( 1, 0, 0, 1 );
		var blue = new Vector4( 0, 0, 1, 1 );
		var rounded = Box( 16, 16, 64, 64, red );
		rounded.BorderRadius = rounded.BorderRadiusV = new Vector4( 16 );
		rounded.BorderSize = new Vector4( 4 );
		rounded.BorderColorL = rounded.BorderColorT = rounded.BorderColorR = rounded.BorderColorB = blue;
		BoxInstance[] boxes = [rounded, Box( 96, 96, 24, 24, new Vector4( 0, 1, 0, 1 ) )];

		var instances = Buffer( device, MemoryMarshal.AsBytes<BoxInstance>( boxes ) );
		var transforms = Buffer( device, MemoryMarshal.AsBytes<Matrix4x4>( [Matrix4x4.Identity] ) );
		var indices = device.CreateBuffer( new BufferInfo { Size = 6 * 4, Usage = BufferUsage.Index } );
		device.WriteBuffer<uint>( indices, [0, 1, 2, 0, 2, 3] );

		var attributes = new ShaderAttributes();
		attributes.Set( "BoxInstances", instances );
		attributes.Set( "TransformBuffer", transforms );
		attributes.Set( "LayerMat", ShaderDefaults.Floats( Matrix4x4.Identity ) );
		attributes.Set( "UIGammaOutput", 1 );
		attributes.SetCombo( "D_BLENDMODE", 0 );

		var color = device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA8Unorm, name: "UI" ) );
		var list = device.BeginCommandList( QueueType.Graphics, "UI" );
		list.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.None, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( color, Vector4.Zero )] );
		Assert.IsTrue( drawer.DrawIndexedInstanced( list, shader, attributes, Format.RGBA8Unorm, 1, indices, 6, boxes.Length, new Vector4( 0, 0, Size, Size ) ) );
		list.EndRendering();
		device.Wait( device.Submit( list ) );
		return ReadBack( device, color, ImageLayout.ColorTarget );
	}

	static BufferHandle Buffer( Device device, ReadOnlySpan<byte> data )
	{
		var buffer = device.CreateBuffer( new BufferInfo { Size = data.Length, Usage = BufferUsage.Storage } );
		device.WriteBuffer( buffer, data );
		return buffer;
	}
}
