using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Numerics;

namespace BasaltGpuTests;

/// <summary>
/// <see cref="GraphicsContext"/>: what the engine's <c>Graphics</c> calls lower to on Basalt.Gpu for the UI - quads
/// with vertices, render-to-texture inside a list, mips through <c>downsample_cs</c>, frame grabs, blend modes - drawn
/// with the engine's compiled UI shaders. Inconclusive where the shaders aren't built.
/// </summary>
[TestClass]
public class GraphicsContextTests : GpuTest
{
	const int Size = 64;

	ShaderLibrary library;
	ShaderDefaults defaults;
	ShaderDrawer drawer;

	[TestInitialize]
	public void CreateDrawer()
	{
		library = new ShaderLibrary( Device, ShaderFileTests.Files );
		defaults = new ShaderDefaults( Device );
		drawer = new ShaderDrawer( Device, library, defaults );
	}

	[TestCleanup]
	public void DestroyDrawer()
	{
		Device?.WaitIdle();
		drawer?.Dispose();
		library?.Dispose();
		defaults?.Dispose();
	}

	/// <summary>
	/// <c>Source( InvTextureDim ); SourceArg( g_tColor )</c> keeps which texture it measures: ui_filter's
	/// <c>g_vInvTextureDim</c> names <c>g_tColor</c>, bound as the <c>Texture</c> attribute.
	/// </summary>
	[TestMethod]
	public void TextureSizeSourceNamesItsTexture()
	{
		var shader = library.Load( "shaders/ui_filter.shader" );
		var program = shader[ShaderProgramType.Pixel];
		var size = program.Variables.Single( v => v.Name == "g_vInvTextureDim" );
		Assert.AreEqual( VariableSource.InvTextureDimensions, size.Source );
		Assert.AreEqual( "g_tColor", program.Variables[size.SourceIndex].Name );
		Assert.AreEqual( "Texture", program.Variables[size.SourceIndex].Attribute );
	}

	/// <summary>
	/// A layer drawn and composited back in one list, as a painter layer is: boxes into a target of its own, then
	/// ui_filter draws it as a quad (four vertices from a stream, as <c>Graphics.DrawQuad</c> makes them) into the main
	/// target at an offset. The layer's pixels land where the quad is, through the texture's linear view.
	/// </summary>
	[TestMethod]
	public void LayerCompositesThroughQuad()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Layer" );
		var context = new GraphicsContext( Device, drawer, list );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var layer = context.CreateTarget( 16, 16, Format.RGBA8Unorm, name: "Layer" );

		context.SetTarget( main );
		context.Clear( new Vector4( 0, 0, 0, 1 ) );
		context.SetTarget( layer );
		context.Clear( new Vector4( 0.25f, 0.5f, 0.75f, 1 ) );

		context.SetTarget( main );
		var attributes = Composite( layer, 16, 8 );
		Assert.IsTrue( context.DrawQuad( library.Load( "shaders/ui_filter.shader" ), attributes, new Vector4( 16, 8, 16, 16 ), 0xFFFFFFFF ) );

		var pixels = Finish( context, main );
		Save( pixels, Size, Size, "graphics-layer-composite" );
		Near( (64, 128, 191, 255), Pixel( pixels, Size, 20, 12 ), "inside the composited layer" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)255), Pixel( pixels, Size, 40, 40 ), "outside it" );
		context.Release();
	}

	/// <summary>
	/// <c>downsample_cs</c> fills a mip chain in one list: a checker of black and white 2x2 squares averages to grey
	/// in mip 1 (gaussian with a transparent border, as layers use), and the texture ends readable.
	/// </summary>
	[TestMethod]
	public void MipsGenerateWithDownsample()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Mips" );
		var context = new GraphicsContext( Device, drawer, list );
		var texture = context.CreateTarget( 32, 32, Format.RGBA8Unorm, mips: 6, name: "Mipped" );

		var checker = new byte[32 * 32 * 4];
		for ( int y = 0; y < 32; y++ )
		{
			for ( int x = 0; x < 32; x++ )
			{
				var v = (byte)((((x >> 1) ^ (y >> 1)) & 1) != 0 ? 255 : 0);
				checker.AsSpan( (y * 32 + x) * 4, 4 ).Fill( v );
			}
		}

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.UpdateTexture( texture, checker, 0, 0, 32, 32 );
		context.Track( texture, ImageLayout.CopyDestination );
		context.GenerateMips( texture, library.Load( "shaders/downsample_cs.shader" ), method: 2 );

		var readback = Device.CreateBuffer( new BufferInfo { Size = 16 * 16 * 4, Memory = MemoryLocation.Readback, Name = "Mip 1" } );
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.ShaderRead, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.CopyTextureToBuffer( texture, 1, 0, readback );
		list.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );

		var mip = Device.Map( readback ).ToArray();
		var centre = Pixel( mip, 16, 8, 8 );
		Assert.IsTrue( Math.Abs( centre.R - 128 ) <= 2 && Math.Abs( centre.A - 128 ) <= 2, $"mip 1's middle is {centre}, not mid grey" );
		Assert.IsTrue( Pixel( mip, 16, 0, 0 ).A < centre.A, "the border is transparent, so the corner fades" );
		context.Release();
	}

	/// <summary>
	/// A frame grab copies what's drawn so far into a texture of its own, which a later draw samples while the target
	/// keeps being drawn to: the copy has the target's pixels, and drawing after the grab doesn't reach it.
	/// </summary>
	[TestMethod]
	public void GrabFrameCopiesTarget()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Grab" );
		var context = new GraphicsContext( Device, drawer, list );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		context.SetTarget( main );
		context.Clear( new Vector4( 1, 0, 0, 1 ) );
		var grab = context.GrabFrame( 1, library.Load( "shaders/downsample_cs.shader" ), method: 1 );
		context.Clear( new Vector4( 0, 0, 1, 1 ) );

		// Composite the grab back over half the target
		Assert.IsTrue( context.DrawQuad( library.Load( "shaders/ui_filter.shader" ), Composite( grab, 0, 0 ), new Vector4( 0, 0, Size / 2, Size ), 0xFFFFFFFF ) );
		var pixels = Finish( context, main );
		Near( (255, 0, 0, 255), Pixel( pixels, Size, 8, 8 ), "the grab, drawn back" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)255, (byte)255), Pixel( pixels, Size, 48, 8 ), "drawn after the grab" );
		context.Release();
	}

	/// <summary>
	/// <c>D_BLENDMODE</c> picks the pipeline's blend state from the shader's render state: multiply (1) scales what's
	/// there by the source, lighten (2) adds to it.
	/// </summary>
	[TestMethod]
	public void BlendModesSelectState()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Blend" );
		var context = new GraphicsContext( Device, drawer, list );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var source = context.CreateTarget( 4, 4, Format.RGBA8Unorm, name: "Source" );
		context.SetTarget( source );
		context.Clear( new Vector4( 0.5f, 0.5f, 0.5f, 1 ) );
		context.SetTarget( main );
		context.Clear( new Vector4( 0.5f, 0.25f, 1, 1 ) );

		var filter = library.Load( "shaders/ui_filter.shader" );
		var multiply = Composite( source, 0, 0 );
		multiply.SetCombo( "D_BLENDMODE", 1 );
		Assert.IsTrue( context.DrawQuad( filter, multiply, new Vector4( 0, 0, 32, 64 ), 0xFFFFFFFF ) );
		var lighten = Composite( source, 32, 0 );
		lighten.SetCombo( "D_BLENDMODE", 2 );
		Assert.IsTrue( context.DrawQuad( filter, lighten, new Vector4( 32, 0, 32, 64 ), 0xFFFFFFFF ) );

		var pixels = Finish( context, main );
		var product = Pixel( pixels, Size, 8, 8 );
		var sum = Pixel( pixels, Size, 40, 8 );
		Assert.IsTrue( Math.Abs( product.R - 64 ) <= 2 && Math.Abs( product.G - 32 ) <= 2, $"multiply gave {product}" );
		Assert.IsTrue( Math.Abs( sum.R - 255 ) <= 2 && Math.Abs( sum.G - 191 ) <= 2, $"lighten gave {sum}" );
		context.Release();
	}

	/// <summary>
	/// A D32S8 depth target's own texture, bound by attribute, samples as depth alone: cleared to a quarter, then read by
	/// <c>downsample_spd</c>'s fused depth path into a depth chain (<see cref="GraphicsContext.BuildDepthChain"/>, as a
	/// frame's is built; depth of field reads the depth the same way). Every texel of the first mip and the last is the
	/// cleared depth as min and max, and validation has nothing to say about the view.
	/// </summary>
	[TestMethod]
	public void DepthTargetSamplesAsDepth()
	{
		const int size = 64;
		var list = Device.BeginCommandList( QueueType.Graphics, "Depth sampled" );
		var context = new GraphicsContext( Device, drawer, list );
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( size, size, Format.D32FloatS8Uint, name: "Depth" ) );
		var chain = context.CreateTarget( size, size, Format.RG32Float, mips: 7, name: "Depth chain" );

		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( depth, depth: 0.25f, stencil: 7 );
		context.Track( depth, ImageLayout.CopyDestination );
		context.BuildDepthChain( depth, chain, library.Load( "shaders/downsample_spd.shader" ) );
		Assert.AreEqual( ImageLayout.ShaderRead, context.LayoutOf( depth ), "the depth is sampled" );
		Assert.AreEqual( ImageLayout.ShaderRead, context.LayoutOf( chain ), "the chain ends readable" );

		var readback = Device.CreateBuffer( new BufferInfo { Size = size * size * 8, Memory = MemoryLocation.Readback, Name = "Chain" } );
		var last = Device.CreateBuffer( new BufferInfo { Size = 8, Memory = MemoryLocation.Readback, Name = "Chain's last mip" } );
		context.CopyToBuffer( chain, readback, 0 );
		context.CopyToBuffer( chain, last, 6 );
		Device.Wait( Device.Submit( list ) );

		var texels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( readback )[..(size * size * 8)] ).ToArray();
		Assert.IsTrue( texels.All( d => d == 0.25f ), $"mip 0 holds {texels.Distinct().Count()} values, first {texels[0]}, not the cleared depth" );
		var corner = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( last )[..8] ).ToArray();
		Assert.IsTrue( corner.All( d => d == 0.25f ), $"the last mip holds {corner[0]}, {corner[1]}" );
		Device.Destroy( depth );
		context.Release();
	}

	/// <summary>
	/// ui_filter's attributes for drawing <paramref name="texture"/> unfiltered at (<paramref name="x"/>,
	/// <paramref name="y"/>), as <c>Painter.CompositeLayer</c> sets them for a layer in gamma space.
	/// </summary>
	static ShaderAttributes Composite( TextureHandle texture, float x, float y )
	{
		var attributes = new ShaderAttributes();
		attributes.Set( "Texture", texture );
		attributes.Set( "PainterSourceGamma", 1 );
		attributes.Set( "UIGammaOutput", 1 );
		attributes.Set( "PainterScissorIndex", -1 );
		attributes.Set( "LayerMat", ShaderDefaults.Floats( Matrix4x4.Identity ) );
		attributes.Set( "TransformMat", ShaderDefaults.Floats( Matrix4x4.Identity ) );
		attributes.Set( "BoxPosition", x, y );
		attributes.Set( "FilterTint", 1, 1, 1, 1 );
		attributes.SetCombo( "D_BLENDMODE", 0 );
		return attributes;
	}

	static void Near( (int R, int G, int B, int A) expected, (byte R, byte G, byte B, byte A) actual, string what )
	{
		var near = Math.Abs( expected.R - actual.R ) <= 1 && Math.Abs( expected.G - actual.G ) <= 1 && Math.Abs( expected.B - actual.B ) <= 1 && Math.Abs( expected.A - actual.A ) <= 1;
		Assert.IsTrue( near, $"{what}: {actual}, expected {expected}" );
	}

	byte[] Finish( GraphicsContext context, TextureHandle target )
	{
		var readback = Device.CreateBuffer( new BufferInfo { Size = Size * Size * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		context.CopyToBuffer( target, readback );
		Device.Wait( Device.Submit( context.List ) );
		return Device.Map( readback ).ToArray();
	}
}
