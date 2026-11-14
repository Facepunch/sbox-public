using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Numerics;

namespace BorealisGpuTests;

/// <summary>
/// The material binding service as the engine's <c>Graphics</c> draws use it (<see cref="ShaderDrawer"/> over material
/// objects): the same pixels as binding by reflection, no sets written once warm, sets freed when what they bind goes,
/// and kept constants bounded. Inconclusive where the shaders aren't built.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MaterialServiceTests : GpuTest
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
	/// What a frame of UI drawing does, on textures that last between frames as pooled targets do: a layer cleared and
	/// composited through a quad, blended by multiply and lighten, and a mip chain filled by <c>downsample_cs</c>
	/// (compute), then the mip composited back. Returns the main target's pixels.
	/// </summary>
	byte[] Frame( GraphicsContext context, TextureHandle main, TextureHandle layer, TextureHandle mipped )
	{
		var filter = library.Load( "shaders/ui_filter.shader" );
		context.SetTarget( layer );
		context.Clear( new Vector4( 0.25f, 0.5f, 0.75f, 1 ) );
		context.SetTarget( mipped );
		context.Clear( new Vector4( 1, 0.5f, 0, 1 ) );
		context.GenerateMips( mipped, library.Load( "shaders/downsample_cs.shader" ), method: 2 );

		context.SetTarget( main );
		context.Clear( new Vector4( 0.5f, 0.25f, 1, 1 ) );
		Assert.IsTrue( context.DrawQuad( filter, Composite( layer, 4, 4, 0 ), new Vector4( 4, 4, 16, 16 ), 0xFFFFFFFF ) );
		Assert.IsTrue( context.DrawQuad( filter, Composite( layer, 24, 4, 1 ), new Vector4( 24, 4, 16, 16 ), 0xFFFFFFFF ) );
		Assert.IsTrue( context.DrawQuad( filter, Composite( layer, 44, 4, 2 ), new Vector4( 44, 4, 16, 16 ), 0xFFFFFFFF ) );
		Assert.IsTrue( context.DrawQuad( filter, Composite( mipped, 4, 32, 0 ), new Vector4( 4, 32, 28, 28 ), 0x80FFFFFF ) );

		var readback = Device.CreateBuffer( new BufferInfo { Size = Size * Size * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		context.CopyToBuffer( main, readback );
		context.FinishReadable();
		Device.Wait( Device.Submit( context.List ) );
		var pixels = Device.Map( readback ).ToArray();
		Device.Destroy( readback );
		return pixels;
	}

	/// <summary>
	/// Draws and dispatches through material objects give exactly the pixels binding by reflection does, and once each
	/// way of drawing has been seen, a frame writes no descriptor sets.
	/// </summary>
	[TestMethod]
	public void DrawTheSamePixelsAsBindingByReflection()
	{
		var context = new GraphicsContext( Device, drawer, null );
		context.Begin( Device.BeginCommandList( QueueType.Graphics, "Targets" ) );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var layer = context.CreateTarget( 16, 16, Format.RGBA8Unorm, name: "Layer" );
		var mipped = context.CreateTarget( 32, 32, Format.RGBA8Unorm, mips: 6, name: "Mipped" );
		context.Finish();
		Device.Submit( context.List );

		byte[] Run( bool objects )
		{
			drawer.MaterialObjects = objects;
			Device.BeginFrame();
			context.Begin( Device.BeginCommandList( QueueType.Graphics, objects ? "Material objects" : "Reflection" ) );
			return Frame( context, main, layer, mipped );
		}

		var reflection = Run( false );
		var objects = Run( true );
		var written = drawer.MaterialObjectStats.SetsWritten;
		var again = Run( true );
		var reflectionAgain = Run( false );

		CollectionAssert.AreEqual( reflection, reflectionAgain, "binding by reflection draws the same frame twice" );
		CollectionAssert.AreEqual( reflection, objects, "material objects draw the pixels binding by reflection does" );
		CollectionAssert.AreEqual( objects, again, "material objects draw the same frame twice" );
		Assert.IsTrue( drawer.MaterialObjectStats.Objects >= 4, "draws and dispatches made objects" );
		Assert.IsTrue( written > 0, "the first frame wrote sets" );
		Assert.AreEqual( written, drawer.MaterialObjectStats.SetsWritten, "a warm frame writes no descriptor sets" );
		Assert.AreEqual( 0, drawer.Fallbacks, "no draw fell back to binding by reflection" );
		context.Release();
	}

	/// <summary>
	/// A set that binds a texture which has gone is freed once nothing has bound it for
	/// <see cref="MaterialObjectCache.EvictAfterFrames"/> frames; a set still in use stays.
	/// </summary>
	[TestMethod]
	public void SetsForGoneTexturesAreFreed()
	{
		var filter = library.Load( "shaders/ui_filter.shader" );
		var context = new GraphicsContext( Device, drawer, null );
		context.Begin( Device.BeginCommandList( QueueType.Graphics, "Targets" ) );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var kept = context.CreateTarget( 8, 8, Format.RGBA8Unorm, name: "Kept" );
		var gone = Device.CreateTexture( TextureInfo.RenderTarget( 8, 8, Format.RGBA8Unorm, name: "Gone" ) );
		context.Track( gone, ImageLayout.Undefined );
		context.ClearTexture( kept, Vector4.One );
		context.ClearTexture( gone, Vector4.One );
		context.FinishReadable();
		Device.Submit( context.List );

		void Draw( params TextureHandle[] textures )
		{
			Device.BeginFrame();
			context.Begin( Device.BeginCommandList( QueueType.Graphics, "Frame" ) );
			context.SetTarget( main );
			foreach ( var texture in textures ) Assert.IsTrue( context.DrawQuad( filter, Composite( texture, 0, 0, 0 ), new Vector4( 0, 0, 8, 8 ), 0xFFFFFFFF ) );
			context.FinishReadable();
			Device.Submit( context.List );
		}

		Draw( kept, gone );
		var both = drawer.MaterialObjectStats.SetsHeld;
		context.Forget( gone );
		Device.Destroy( gone );

		for ( int frame = 0; frame < MaterialObjectCache.EvictAfterFrames + 16; frame++ ) Draw( kept );
		var after = drawer.MaterialObjectStats.SetsHeld;

		Assert.IsTrue( after < both, $"the gone texture's set was freed ({both} sets before, {after} after)" );
		Assert.IsTrue( after > 0, "the set still in use stays" );
		Device.WaitIdle();
		context.Release();
	}

	/// <summary>
	/// Constants kept by content (past the device's dynamic limit, or a material's per attribute value) are freed when
	/// nothing has asked for them for a while, so values that change every frame don't fill the buffer.
	/// </summary>
	[TestMethod]
	public void KeptConstantsStayBounded()
	{
		using var cache = new MaterialObjectCache( Device, defaults );
		var data = new byte[256];
		for ( int frame = 0; frame < 200; frame++ )
		{
			Device.BeginFrame();
			cache.NextEpoch();
			BitConverter.TryWriteBytes( data, frame );
			cache.KeepPassConstants( data, data.Length );
		}

		// At most the frames since the last eviction, plus those not yet freed, plus one eviction period
		var bound = (MaterialObjectCache.EvictAfterFrames + 8 + 6) * 256L;
		Assert.IsTrue( cache.ConstantBytes > 0, "constants were kept" );
		Assert.IsTrue( cache.ConstantBytes <= bound, $"{cache.ConstantBytes} bytes kept after 200 frames of new values, more than {bound}" );
	}

	/// <summary>ui_filter's attributes for drawing <paramref name="texture"/> unfiltered at (x, y) with a blend mode.</summary>
	static ShaderAttributes Composite( TextureHandle texture, float x, float y, int blend )
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
		attributes.SetCombo( "D_BLENDMODE", blend );
		return attributes;
	}
}
