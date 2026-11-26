using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Numerics;

namespace BorealisGpuTests;

/// <summary>
/// <see cref="GraphicsContext"/>: what the engine's <c>Graphics</c> calls lower to on Borealis.Gpu for the UI - quads
/// with vertices, render-to-texture inside a list, mips through <c>downsample_cs</c>, frame grabs, blend modes - drawn
/// with the engine's compiled UI shaders. Inconclusive where the shaders aren't built.
/// </summary>
[TestClass]
public class GraphicsContextTests : GpuTest
{
	const int Size = 64;

	BackendDevice backend;
	ShaderLibrary library;
	ShaderDefaults defaults;
	ShaderDrawer drawer;

	[TestInitialize]
	public void CreateDrawer()
	{
		// The UI shaders are Slang modules: a backend's library reads them
		backend = new BackendDevice( Device, ShaderFileTests.Files );
		library = backend.Shaders;
		defaults = new ShaderDefaults( Device );
		drawer = new ShaderDrawer( Device, library, defaults );
	}

	[TestCleanup]
	public void DestroyDrawer()
	{
		Device?.WaitIdle();
		drawer?.Dispose();
		backend?.Dispose();
		defaults?.Dispose();
	}

	/// <summary>
	/// <c>Source( InvTextureDim ); SourceArg( g_tColor )</c> keeps which texture it measures: ui_filter's
	/// <c>g_vInvTextureDim</c> names <c>g_tColor</c>, bound as the <c>Texture</c> attribute, in its module's pixel stage.
	/// </summary>
	[TestMethod]
	public void TextureSizeSourceNamesItsTexture()
	{
		var shader = library.Load( "shaders/ui_filter.shader" );
		var program = library.Variant( shader, shader.Modes[0].Name, [] ).Pixel.Program;
		var size = program.Variables.Single( v => v.Name == "g_vInvTextureDim" );
		Assert.AreEqual( VariableSource.InvTextureDimensions, size.Source );
		Assert.AreEqual( "g_tColor", program.Variables[size.SourceIndex].Name );
		Assert.AreEqual( "Texture", program.Variables[size.SourceIndex].Attribute );
	}

	/// <summary>
	/// A buffer update waits for what read the buffer before it in the list: a sprite batch's upload for its next view
	/// otherwise overwrites what the last view's draw is still reading (synchronization validation fails the test then).
	/// </summary>
	[TestMethod]
	public void UpdateBufferWaitsForEarlierReads()
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination, Name = "Updated" } );
		var copy = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Copy" } );
		var list = Device.BeginCommandList( QueueType.Graphics, "Update after read" );
		var context = new GraphicsContext( Device, drawer, list );

		context.List.CopyBuffer( buffer, 0, copy, 0, 256 );
		context.UpdateBuffer( buffer, new byte[256], 0 );
		Device.Wait( Device.Submit( list ) );

		Device.Destroy( buffer );
		Device.Destroy( copy );
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
	/// A draw with no target bound (a camera's command list at <c>AfterDepthPrepass</c>, which Borealis gives none: GAMMA's
	/// mirror glass) is skipped and said once, rather than throwing every frame; the list draws on once a target is bound.
	/// </summary>
	[TestMethod]
	public void ADrawWithNoTargetIsSkipped()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "No target" );
		var warnings = new List<string>();
		var context = new GraphicsContext( Device, drawer, list ) { Warn = warnings.Add };
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var layer = context.CreateTarget( 16, 16, Format.RGBA8Unorm, name: "Layer" );
		context.SetTarget( layer );
		context.Clear( new Vector4( 0.25f, 0.5f, 0.75f, 1 ) );
		context.Begin( list );

		var shader = library.Load( "shaders/ui_filter.shader" );
		Assert.IsFalse( context.DrawQuad( shader, Composite( layer, 16, 8 ), new Vector4( 16, 8, 16, 16 ), 0xFFFFFFFF ), "skipped with no target" );
		Assert.IsFalse( context.DrawQuad( shader, Composite( layer, 16, 8 ), new Vector4( 16, 8, 16, 16 ), 0xFFFFFFFF ), "again" );
		Assert.AreEqual( 1, warnings.Count, "said once" );

		context.SetTarget( main );
		context.Clear( new Vector4( 0, 0, 0, 1 ) );
		Assert.IsTrue( context.DrawQuad( shader, Composite( layer, 16, 8 ), new Vector4( 16, 8, 16, 16 ), 0xFFFFFFFF ), "drawn with one" );
		var pixels = Finish( context, main );
		Near( (64, 128, 191, 255), Pixel( pixels, Size, 20, 12 ), "inside the quad" );
		context.Release();
	}

	/// <summary>
	/// A model's draw call drawn from vertex and index buffers, as <c>Graphics.DrawModelInstanced</c> draws one: its indices
	/// start part way into the index buffer and its vertices part way into the vertex buffer, whose first quad (elsewhere)
	/// stays undrawn.
	/// </summary>
	[TestMethod]
	public void IndexedDrawReadsFromItsOffsets()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Indexed" );
		var context = new GraphicsContext( Device, drawer, list );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var layer = context.CreateTarget( 16, 16, Format.RGBA8Unorm, name: "Layer" );
		context.SetTarget( main );
		context.Clear( new Vector4( 0, 0, 0, 1 ) );
		context.SetTarget( layer );
		context.Clear( new Vector4( 0.25f, 0.5f, 0.75f, 1 ) );
		context.SetTarget( main );

		static EngineVertex V( float x, float y, float u, float v ) => new() { Position = new( x, y, 0 ), Color = 0xFFFFFFFF, TexCoord0 = new( u, v, 0, 0 ) };
		EngineVertex[] vertices = [V( 40, 40, 0, 0 ), V( 56, 40, 1, 0 ), V( 40, 56, 0, 1 ), V( 56, 56, 1, 1 ), V( 16, 8, 0, 0 ), V( 32, 8, 1, 0 ), V( 16, 24, 0, 1 ), V( 32, 24, 1, 1 )];
		var vertexBuffer = Device.CreateBuffer( new BufferInfo { Size = 76 * vertices.Length, Usage = BufferUsage.Vertex, Name = "Vertices" } );
		var indexBuffer = Device.CreateBuffer( new BufferInfo { Size = 12 * 4, Usage = BufferUsage.Index, Name = "Indices" } );
		Device.WriteBuffer<EngineVertex>( vertexBuffer, vertices );
		Device.WriteBuffer<uint>( indexBuffer, [7, 7, 7, 7, 7, 7, 0, 1, 2, 2, 1, 3] );

		Assert.IsTrue( context.DrawIndexed( library.Load( "shaders/ui_filter.shader" ), Composite( layer, 16, 8 ), vertexBuffer, VertexStreamLayout.EngineVertex, indexBuffer, 6, 6, 4, 2 ) );

		var pixels = Finish( context, main );
		Save( pixels, Size, Size, "graphics-indexed" );
		Near( (64, 128, 191, 255), Pixel( pixels, Size, 20, 12 ), "inside the drawn quad" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)255), Pixel( pixels, Size, 48, 48 ), "where the buffer's first quad would be" );
		context.Release();
		Device.Destroy( vertexBuffer );
		Device.Destroy( indexBuffer );
	}

	/// <summary>
	/// A texture a dispatch writes is readable when a draw after it in the block runs, though the draw doesn't bind it: the
	/// terrain bakes its normal map and draws in one block, sampling the map by bindless index, which wants it in
	/// <see cref="ImageLayout.ShaderRead"/> (it was left in <see cref="ImageLayout.General"/>).
	/// </summary>
	[TestMethod]
	public void DispatchedTexturesAreReadableForTheNextDraw()
	{
		var bake = library.Load( "shaders/terrain/cs_terrain_normal.shader" );
		if ( bake is null ) Assert.Inconclusive( "cs_terrain_normal isn't compiled" );

		var list = Device.BeginCommandList( QueueType.Graphics, "Dispatch then draw" );
		var context = new GraphicsContext( Device, drawer, list );
		var storage = TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.CopyDestination;
		var height = Device.CreateTexture( TextureInfo.Texture2D( 16, 16, Format.R16Unorm, name: "Height" ) with { Usage = storage } );
		var normals = Device.CreateTexture( TextureInfo.Texture2D( 16, 16, Format.RG16Unorm, name: "Normals" ) with { Usage = storage } );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var layer = context.CreateTarget( 16, 16, Format.RGBA8Unorm, name: "Layer" );
		context.Track( height, ImageLayout.Undefined );
		context.Track( normals, ImageLayout.Undefined );

		var attributes = new ShaderAttributes();
		attributes.Set( "Heightmap", height );
		attributes.Set( "OutputMap", normals );
		attributes.Set( "HeightScale", 100.0f );
		attributes.Set( "UnitsPerTexel", 1.0f );
		Assert.IsTrue( context.Dispatch( bake, attributes, 2, 2, 1 ) );
		Assert.AreEqual( ImageLayout.General, context.LayoutOf( normals ) );

		context.SetTarget( layer );
		context.Clear( new Vector4( 1, 1, 1, 1 ) );
		context.SetTarget( main );
		Assert.IsTrue( context.DrawQuad( library.Load( "shaders/ui_filter.shader" ), Composite( layer, 0, 0 ), new Vector4( 0, 0, 16, 16 ), 0xFFFFFFFF ) );
		Assert.AreEqual( ImageLayout.ShaderRead, context.LayoutOf( normals ), "readable for the draw, which may sample it by bindless index" );
		Assert.AreEqual( ImageLayout.ShaderRead, context.LayoutOf( height ) );

		Finish( context, main );
		context.Release();
		Device.Destroy( height );
		Device.Destroy( normals );
	}

	/// <summary>
	/// A dispatch moves only the textures it can touch: those it binds, and those it can reach through the bindless set. A
	/// texture among its attributes that it neither binds nor reaches (a view's block attributes are under every dispatch's)
	/// keeps its layout, with no barrier. cs_terrain_normal uses no bindless textures; it used to put every texture among its
	/// attributes in <see cref="ImageLayout.General"/>.
	/// </summary>
	[TestMethod]
	public void ADispatchLeavesTexturesItCannotReach()
	{
		var bake = library.Load( "shaders/terrain/cs_terrain_normal.shader" );
		if ( bake is null ) Assert.Inconclusive( "cs_terrain_normal isn't compiled" );

		var list = Device.BeginCommandList( QueueType.Graphics, "Dispatch with an unreachable texture" );
		var context = new GraphicsContext( Device, drawer, list );
		var storage = TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.CopyDestination;
		var height = Device.CreateTexture( TextureInfo.Texture2D( 16, 16, Format.R16Unorm, name: "Height" ) with { Usage = storage } );
		var normals = Device.CreateTexture( TextureInfo.Texture2D( 16, 16, Format.RG16Unorm, name: "Normals" ) with { Usage = storage } );
		var other = context.CreateTarget( 16, 16, Format.RGBA8Unorm, name: "Other" );
		context.Track( height, ImageLayout.Undefined );
		context.Track( normals, ImageLayout.Undefined );
		context.SetTarget( other );
		context.Clear( new Vector4( 1, 1, 1, 1 ) );
		var before = context.LayoutOf( other );

		var attributes = new ShaderAttributes();
		attributes.Set( "Heightmap", height );
		attributes.Set( "OutputMap", normals );
		attributes.Set( "Unbound", other );
		attributes.Set( "HeightScale", 100.0f );
		attributes.Set( "UnitsPerTexel", 1.0f );
		var variant = drawer.Variant( bake, attributes );
		Assert.IsFalse( variant.ReadsBindlessTextures, "cs_terrain_normal samples no bindless textures" );
		Assert.IsFalse( variant.UsesBindlessStorageTextures );

		Assert.IsTrue( context.Dispatch( bake, attributes, 2, 2, 1 ) );
		Assert.AreEqual( ImageLayout.General, context.LayoutOf( normals ), "bound as storage" );
		Assert.AreEqual( ImageLayout.General, context.LayoutOf( height ), "bound as storage too (read with Load)" );
		Assert.AreEqual( before, context.LayoutOf( other ), "neither bound nor reachable: left as it was" );

		context.SetTarget( other );
		context.Clear( new Vector4( 0, 0, 0, 1 ) );
		Finish( context, other );
		context.Release();
		Device.Destroy( height );
		Device.Destroy( normals );
	}

	/// <summary>
	/// Reflection says which bindless arrays a compute variant reaches, as only what its code uses is reflected: screen-space
	/// reflections' intersect pass samples last frame's colour by bindless index, and its other textures are bound.
	/// </summary>
	[TestMethod]
	public void ReflectionSaysWhichVariantsReachBindlessTextures()
	{
		var reflections = library.Load( "shaders/screen_space_reflections_cs.shader" );
		var mips = library.Load( "shaders/downsample_cs.shader" );
		if ( reflections is null || mips is null ) Assert.Inconclusive( "screen_space_reflections_cs or downsample_cs isn't compiled" );

		var attributes = new ShaderAttributes();
		attributes.SetCombo( "D_PASS", 1 );
		var intersect = drawer.Variant( reflections, attributes );
		Assert.IsTrue( intersect.ReadsBindlessTextures, "the intersect pass reads the history by index" );
		Assert.IsFalse( intersect.UsesBindlessStorageTextures );

		var downsample = drawer.Variant( mips, new ShaderAttributes() );
		Assert.IsFalse( downsample.ReadsBindlessTextures, "downsample_cs binds what it reads" );
		Assert.IsFalse( downsample.UsesBindlessStorageTextures );
	}

	// The engine's Vertex, as VertexStreamLayout.EngineVertex lays it out
	struct EngineVertex
	{
		public Vector3 Position;
		public uint Color;
#pragma warning disable CS0649 // laid out for the stream, never set
		public Vector3 Normal;
		public Vector4 TexCoord0, TexCoord1, Tangent;
#pragma warning restore CS0649
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
	/// The GGX kernel's groups each write a 16x16 tile of a mip, so it dispatches a quarter of the threads the mip has
	/// texels: every texel of every mip of an odd-sized texture is still written, a flat colour staying flat.
	/// </summary>
	[TestMethod]
	public void GgxMipsCoverOddSizes()
	{
		const int Width = 37, Height = 23, Mips = 4;
		var list = Device.BeginCommandList( QueueType.Graphics, "GGX mips" );
		var context = new GraphicsContext( Device, drawer, list );
		var texture = context.CreateTarget( Width, Height, Format.RGBA8Unorm, mips: Mips, name: "GGX mipped" );

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		var readbacks = new BufferHandle[Mips];
		for ( int mip = 0; mip < Mips; mip++ )
		{
			int w = Width >> mip, h = Height >> mip;
			// Mip 0 flat grey, the rest red until the downsample writes them
			var fill = new byte[w * h * 4];
			for ( int i = 0; i < fill.Length; i += 4 )
			{
				if ( mip == 0 ) fill.AsSpan( i, 4 ).Fill( 128 );
				else { fill[i] = 255; fill[i + 3] = 255; }
			}
			list.UpdateTexture( texture, fill, mip, 0, w, h );
			readbacks[mip] = Device.CreateBuffer( new BufferInfo { Size = w * h * 4, Memory = MemoryLocation.Readback, Name = $"GGX mip {mip}" } );
		}

		context.Track( texture, ImageLayout.CopyDestination );
		context.GenerateMips( texture, library.Load( "shaders/downsample_cs.shader" ), method: 6 );

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.ShaderRead, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		for ( int mip = 1; mip < Mips; mip++ )
		{
			list.CopyTextureToBuffer( texture, mip, 0, readbacks[mip] );
			list.Barrier( new BufferBarrier { Buffer = readbacks[mip], SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		}
		Device.Wait( Device.Submit( list ) );

		for ( int mip = 1; mip < Mips; mip++ )
		{
			int w = Width >> mip, h = Height >> mip;
			var pixels = Device.Map( readbacks[mip] ).ToArray();
			for ( int y = 0; y < h; y++ )
			{
				for ( int x = 0; x < w; x++ )
				{
					var p = Pixel( pixels, w, x, y );
					Assert.IsTrue( Math.Abs( p.R - 128 ) <= 2 && Math.Abs( p.G - 128 ) <= 2 && Math.Abs( p.B - 128 ) <= 2, $"mip {mip} ({w}x{h}) at {x},{y} is {p}, not the flat grey" );
				}
			}
		}

		foreach ( var readback in readbacks ) if ( readback.IsValid ) Device.Destroy( readback );
		context.Release();
	}

	/// <summary>
	/// <c>borealis_shadow_tiles_cs</c> reduces a cascade's depth to the min and max of each 32x32 texel tile, written at
	/// the cascade's column of the tile atlas, then widens each over its eight neighbours: both match the depths on the CPU.
	/// </summary>
	[TestMethod]
	public void ShadowTilesHoldEachTilesDepthRange()
	{
		var shader = library.Load( "shaders/borealis_shadow_tiles_cs.shader" );
		if ( shader is null ) Assert.Inconclusive( "borealis_shadow_tiles_cs isn't compiled" );

		const int Side = 4, Resolution = Side * 32;
		var random = new Random( 7 );
		var depths = new float[Resolution * Resolution];
		for ( int i = 0; i < depths.Length; i++ ) depths[i] = 0.25f + 0.5f * (float)random.NextDouble();

		var list = Device.BeginCommandList( QueueType.Graphics, "Shadow tiles" );
		var context = new GraphicsContext( Device, drawer, list );
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( Resolution, Resolution, Format.D32Float, name: "Cascade" ) );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.UpdateTexture( depth, System.Runtime.InteropServices.MemoryMarshal.AsBytes( depths.AsSpan() ), 0, 0, Resolution, Resolution );
		context.Track( depth, ImageLayout.CopyDestination );

		// The second cascade's column of a two-cascade atlas
		var raw = context.CreateTarget( Side * 2, Side, Format.RG32Float, name: "Raw tiles" );
		var widened = context.CreateTarget( Side * 2, Side, Format.RG32Float, name: "Tiles" );
		var attributes = new ShaderAttributes();
		attributes.SetCombo( "D_WIDEN", 0 );
		attributes.Set( "ShadowTileDepth", depth );
		attributes.Set( "ShadowTilesOut", raw );
		attributes.Set( "ShadowTiles", Side, Side, 0, 0 );
		Assert.IsTrue( context.Dispatch( shader, attributes, Side * 8, Side * 8, 1 ) );

		attributes = new ShaderAttributes();
		attributes.SetCombo( "D_WIDEN", 1 );
		attributes.Set( "ShadowTilesIn", raw );
		attributes.Set( "ShadowTilesOut", widened );
		attributes.Set( "ShadowTiles", Side, Side, 0, 0 );
		Assert.IsTrue( context.Dispatch( shader, attributes, Side, Side, 1 ) );

		var rawReadback = Device.CreateBuffer( new BufferInfo { Size = Side * 2 * Side * 8, Memory = MemoryLocation.Readback, Name = "Raw tiles" } );
		var readback = Device.CreateBuffer( new BufferInfo { Size = Side * 2 * Side * 8, Memory = MemoryLocation.Readback, Name = "Tiles" } );
		context.CopyToBuffer( raw, rawReadback );
		context.CopyToBuffer( widened, readback );
		context.Finish();
		Device.Wait( Device.Submit( list ) );

		var expected = new (float Min, float Max)[Side, Side];
		for ( int ty = 0; ty < Side; ty++ )
		{
			for ( int tx = 0; tx < Side; tx++ )
			{
				var range = (Min: 1f, Max: 0f);
				for ( int y = ty * 32; y < ty * 32 + 32; y++ )
					for ( int x = tx * 32; x < tx * 32 + 32; x++ )
						range = (Math.Min( range.Min, depths[y * Resolution + x] ), Math.Max( range.Max, depths[y * Resolution + x] ));
				expected[tx, ty] = range;
			}
		}

		var rawTiles = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( rawReadback )[..(Side * 2 * Side * 8)] ).ToArray();
		var tiles = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( readback )[..(Side * 2 * Side * 8)] ).ToArray();
		for ( int ty = 0; ty < Side; ty++ )
		{
			for ( int tx = 0; tx < Side; tx++ )
			{
				int at = (ty * Side * 2 + Side + tx) * 2;
				Assert.AreEqual( expected[tx, ty], (rawTiles[at], rawTiles[at + 1]), $"tile {tx},{ty}'s own range" );

				var wide = (Min: 1f, Max: 0f);
				for ( int y = Math.Max( ty - 1, 0 ); y <= Math.Min( ty + 1, Side - 1 ); y++ )
					for ( int x = Math.Max( tx - 1, 0 ); x <= Math.Min( tx + 1, Side - 1 ); x++ )
						wide = (Math.Min( wide.Min, expected[x, y].Min ), Math.Max( wide.Max, expected[x, y].Max ));
				Assert.AreEqual( wide, (tiles[at], tiles[at + 1]), $"tile {tx},{ty} over its neighbours" );
			}
		}

		context.Release();
		Device.Destroy( rawReadback );
		Device.Destroy( readback );
		Device.Destroy( depth );
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
	/// A draw that samples the target it draws into (a game's feedback effect) reads the target as it was before the draw,
	/// as native draws it: multiplied by itself, the covered half is the square of what was there, the other half untouched,
	/// and the draw's attributes name the target again after it.
	/// </summary>
	[TestMethod]
	public void ADrawSamplesItsOwnTargetAsItWas()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Feedback" );
		var context = new GraphicsContext( Device, drawer, list );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		context.SetTarget( main );
		context.Clear( new Vector4( 0.5f, 0.25f, 1, 1 ) );

		var feedback = Composite( main, 0, 0 );
		feedback.SetCombo( "D_BLENDMODE", 1 );
		Assert.IsTrue( context.DrawQuad( library.Load( "shaders/ui_filter.shader" ), feedback, new Vector4( 0, 0, Size / 2, Size ), 0xFFFFFFFF ) );
		Assert.AreEqual( main, (TextureHandle)feedback.Get( "Texture" ) );

		var pixels = Finish( context, main );
		var squared = Pixel( pixels, Size, 8, 8 );
		Assert.IsTrue( Math.Abs( squared.R - 64 ) <= 2 && Math.Abs( squared.G - 16 ) <= 2 && squared.B >= 253, $"the covered half gave {squared}" );
		Near( (128, 64, 255, 255), Pixel( pixels, Size, 48, 8 ), "the other half" );
		context.Release();
	}

	/// <summary>
	/// A scratch texture kept at the target's size (here the copy a draw sampling its own target reads) stays while it's
	/// used and goes once it has gone unused for a while (the menu after a game), at a block's start.
	/// </summary>
	[TestMethod]
	public void ScratchTexturesGoOnceIdle()
	{
		var (seconds, frames) = (GraphicsContext.ScratchIdleSeconds, GraphicsContext.ScratchIdleFrames);
		try
		{
			var list = Device.BeginCommandList( QueueType.Graphics, "Scratch" );
			var context = new GraphicsContext( Device, drawer, list );
			var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
			context.SetTarget( main );
			context.Clear( new Vector4( 0.5f, 0.25f, 1, 1 ) );
			var feedback = Composite( main, 0, 0 );
			feedback.SetCombo( "D_BLENDMODE", 1 );
			Assert.IsTrue( context.DrawQuad( library.Load( "shaders/ui_filter.shader" ), feedback, new Vector4( 0, 0, Size / 2, Size ), 0xFFFFFFFF ) );
			Finish( context, main );
			Assert.AreEqual( 1, context.ScratchTextures, "the sampled target's copy" );

			context.Begin( Device.BeginCommandList( QueueType.Graphics, "Scratch kept" ) );
			Assert.AreEqual( 1, context.ScratchTextures, "kept while it was used lately" );
			Device.Submit( context.List );

			GraphicsContext.ScratchIdleSeconds = 0;
			GraphicsContext.ScratchIdleFrames = 0;
			context.Begin( Device.BeginCommandList( QueueType.Graphics, "Scratch idle" ) );
			Assert.AreEqual( 0, context.ScratchTextures, "gone once idle" );
			Device.Wait( Device.Submit( context.List ) );
			context.Release();
		}
		finally
		{
			(GraphicsContext.ScratchIdleSeconds, GraphicsContext.ScratchIdleFrames) = (seconds, frames);
		}
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
	/// A deferred depth chain (<see cref="GraphicsContext.DeferDepthChain"/>, a camera's stages after the scene) is built
	/// from the depth as it was when asked, before the depth is next written: the depth at a quarter, the chain deferred,
	/// then the depth cleared to 0, and the chain still holds a quarter. One nothing used isn't built: the block's end drops it.
	/// </summary>
	[TestMethod]
	public void DeferredDepthChainBuildsBeforeItsDepthIsWritten()
	{
		const int size = 64;
		var spd = library.Load( "shaders/downsample_spd.shader" );
		var list = Device.BeginCommandList( QueueType.Graphics, "Depth chain deferred" );
		var context = new GraphicsContext( Device, drawer, list );
		var color = context.CreateTarget( size, size, Format.RGBA8Unorm, name: "Colour" );
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( size, size, Format.D32FloatS8Uint, name: "Depth" ) );
		var chain = context.CreateTarget( size, size, Format.RG32Float, mips: 7, name: "Depth chain" );
		var unused = context.CreateTarget( size, size, Format.RG32Float, mips: 7, name: "Unused depth chain" );

		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( depth, depth: 0.25f, stencil: 7 );
		context.Track( depth, ImageLayout.CopyDestination );
		context.SetTarget( color, depth );
		context.DeferDepthChain( depth, chain, spd );
		Assert.IsTrue( context.DepthChainDeferred, "nothing read it yet" );
		context.ClearDepth();
		Assert.IsFalse( context.DepthChainDeferred, "clearing the depth built it first" );

		var readback = Device.CreateBuffer( new BufferInfo { Size = size * size * 8, Memory = MemoryLocation.Readback, Name = "Chain" } );
		context.CopyToBuffer( chain, readback, 0 );

		context.DeferDepthChain( depth, unused, spd );
		context.FinishReadable();
		Assert.IsFalse( context.DepthChainDeferred, "the block's end drops it" );
		Device.Wait( Device.Submit( list ) );

		var texels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( readback )[..(size * size * 8)] ).ToArray();
		Assert.IsTrue( texels.All( d => d == 0.25f ), $"mip 0 holds {texels.Distinct().Count()} values, first {texels[0]}, not the depth from before the clear" );
		Device.Destroy( depth );
		context.Release();
	}

	/// <summary>
	/// <see cref="GraphicsContext.ClearDepth"/> (<c>Graphics.Clear</c> asked to clear depth) clears the bound depth to the far
	/// plane, 0: a depth at a quarter reads 0 everywhere after, through the depth chain.
	/// </summary>
	[TestMethod]
	public void ClearDepthClearsTheBoundDepth()
	{
		const int size = 64;
		var list = Device.BeginCommandList( QueueType.Graphics, "Depth cleared" );
		var context = new GraphicsContext( Device, drawer, list );
		var color = context.CreateTarget( size, size, Format.RGBA8Unorm, name: "Colour" );
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( size, size, Format.D32FloatS8Uint, name: "Depth" ) );
		var chain = context.CreateTarget( size, size, Format.RG32Float, mips: 7, name: "Depth chain" );

		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( depth, depth: 0.25f, stencil: 7 );
		context.Track( depth, ImageLayout.CopyDestination );
		context.SetTarget( color, depth );
		context.ClearDepth();
		context.BuildDepthChain( depth, chain, library.Load( "shaders/downsample_spd.shader" ) );

		var readback = Device.CreateBuffer( new BufferInfo { Size = size * size * 8, Memory = MemoryLocation.Readback, Name = "Chain" } );
		context.CopyToBuffer( chain, readback, 0 );
		Device.Wait( Device.Submit( list ) );

		var texels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( readback )[..(size * size * 8)] ).ToArray();
		Assert.IsTrue( texels.All( d => d == 0 ), $"mip 0 holds {texels.Distinct().Count()} values, first {texels[0]}, not the cleared depth" );
		Device.Destroy( depth );
		context.Release();
	}

	/// <summary>
	/// Drawing into depth alone at a slice, as a custom object draws into a shadow map on Borealis.Gpu: with the mode
	/// <c>Depth</c> asked for, scene_renderer_attribute_test draws its Depth mode (depth_only.shader) into layer 1 of a
	/// two-layer depth, a shader with no Depth mode (ui_filter) doesn't draw, layer 0 stays cleared, and the depth bias moves
	/// what the draw after it writes by that many of the depth's units.
	/// </summary>
	[TestMethod]
	public void DepthAloneDrawsItsModeIntoTheSlice()
	{
		var color = library.Load( "shaders/scene_renderer_attribute_test.shader" );
		if ( color is null || library.Load( "shaders/depth_only.shader" ) is null ) Assert.Inconclusive( "scene_renderer_attribute_test or depth_only isn't compiled" );

		var list = Device.BeginCommandList( QueueType.Graphics, "Depth alone" );
		var context = new GraphicsContext( Device, drawer, list );
		var info = TextureInfo.RenderTarget( Size, Size, Format.D32Float, name: "Shadow slices" );
		info.Layers = 2;
		info.Dimension = TextureDimension.Texture2DArray;
		var depth = Device.CreateTexture( info );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( depth, depth: 0, stencil: 0 );
		context.Track( depth, ImageLayout.CopyDestination );

		// World space is clip space: x and y -1 to 1 across the target
		var view = new Borealis.Gpu.ViewConstants { InvViewportSize = new Vector2( 1.0f / Size, 1.0f / Size ), ViewportSize = new Vector2( Size, Size ), CameraDirWs = new Vector3( 0, 0, 1 ), CameraUpDirWs = new Vector3( 0, 1, 0 ) };
		System.Runtime.CompilerServices.Unsafe.As<Matrix, Matrix4x4>( ref view.WorldToProjection ) = Matrix4x4.Identity;
		var viewBytes = new byte[Borealis.Gpu.ViewConstants.NativeSize];
		System.Runtime.InteropServices.MemoryMarshal.Write( viewBytes, in view );
		var attributes = new ShaderAttributes();
		attributes.SetData( "PerViewConstantBuffer_t", viewBytes );

		// A quad over the left half at depth 0.5, then over the right half with a constant bias of 1000
		static EngineVertex V( float x, float y ) => new() { Position = new( x, y, 0.5f ), Color = 0xFFFFFFFF };
		EngineVertex[] left = [V( -1, -1 ), V( 0, -1 ), V( -1, 1 ), V( 0, 1 )];
		EngineVertex[] right = [V( 0, -1 ), V( 1, -1 ), V( 0, 1 ), V( 1, 1 )];

		context.SetTarget( default, depth, false, 1 );
		context.Mode = "Depth";
		Assert.IsTrue( context.DrawVertices( color, attributes, System.Runtime.InteropServices.MemoryMarshal.AsBytes( left.AsSpan() ), VertexStreamLayout.EngineVertex, 4, PrimitiveTopology.TriangleStrip ), "scene_renderer_attribute_test draws its Depth mode" );
		Assert.IsFalse( context.DrawQuad( library.Load( "shaders/ui_filter.shader" ), Composite( depth, 0, 0 ), new Vector4( 0, 0, Size, Size ), 0xFFFFFFFF ), "ui_filter has no Depth mode" );
		drawer.DepthBias = (1000, 0);
		Assert.IsTrue( context.DrawVertices( color, attributes, System.Runtime.InteropServices.MemoryMarshal.AsBytes( right.AsSpan() ), VertexStreamLayout.EngineVertex, 4, PrimitiveTopology.TriangleStrip ) );
		drawer.DepthBias = default;

		var slice0 = Device.CreateBuffer( new BufferInfo { Size = Size * Size * 4, Memory = MemoryLocation.Readback, Name = "Slice 0" } );
		var slice1 = Device.CreateBuffer( new BufferInfo { Size = Size * Size * 4, Memory = MemoryLocation.Readback, Name = "Slice 1" } );
		context.CopyToBuffer( depth, slice0, 0, 0 );
		context.CopyToBuffer( depth, slice1, 0, 1 );
		Device.Wait( Device.Submit( list ) );

		var first = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( slice0 )[..(Size * Size * 4)] ).ToArray();
		var second = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( slice1 )[..(Size * Size * 4)] ).ToArray();
		Assert.IsTrue( first.All( d => d == 0 ), $"slice 0 holds {first.Max()}, drawn into" );
		Assert.AreEqual( 0.5f, second[32 * Size + 16], "the left half, unbiased" );

		// A constant bias of 1000 in a float depth near 0.5 (exponent -1) is 1000 * 2^-24
		var biased = second[32 * Size + 48];
		Assert.AreEqual( 0.5f + 1000 * MathF.Pow( 2, -24 ), biased, 1e-6f, $"the right half, biased: {biased}" );
		Device.Destroy( depth );
		context.Release();
	}

	/// <summary>
	/// An indexed draw with its counts in an arguments buffer, as <c>Graphics.DrawIndexedInstancedIndirect</c> takes them: 16-bit
	/// indices bound part way into their buffer, the second draw's arguments (the first's would draw the other quad) picking a
	/// first index and base vertex of their own.
	/// </summary>
	[TestMethod]
	public void IndirectDrawReads16BitIndicesAndItsArguments()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Indirect" );
		var context = new GraphicsContext( Device, drawer, list );
		var (main, layer) = Targets( context );

		static EngineVertex V( float x, float y, float u, float v ) => new() { Position = new( x, y, 0 ), Color = 0xFFFFFFFF, TexCoord0 = new( u, v, 0, 0 ) };
		EngineVertex[] vertices = [V( 40, 40, 0, 0 ), V( 56, 40, 1, 0 ), V( 40, 56, 0, 1 ), V( 56, 56, 1, 1 ), V( 16, 8, 0, 0 ), V( 32, 8, 1, 0 ), V( 16, 24, 0, 1 ), V( 32, 24, 1, 1 )];
		var vertexBuffer = Device.CreateBuffer( new BufferInfo { Size = 76 * vertices.Length, Usage = BufferUsage.Vertex, Name = "Vertices" } );
		var indexBuffer = Device.CreateBuffer( new BufferInfo { Size = 16 * 2, Usage = BufferUsage.Index, Name = "Indices" } );
		var arguments = Device.CreateBuffer( new BufferInfo { Size = 40, Usage = BufferUsage.Indirect, Name = "Arguments" } );
		Device.WriteBuffer<EngineVertex>( vertexBuffer, vertices );
		Device.WriteBuffer<ushort>( indexBuffer, [9, 9, 9, 9, 0, 1, 2, 2, 1, 3, 9, 9, 9, 9, 9, 9] );

		// Index count, instances, first index, base vertex, first instance: the first draw's would draw the quad at 40, 40
		Device.WriteBuffer<int>( arguments, [6, 1, 0, 0, 0, 6, 1, 2, 4, 0] );

		Assert.IsTrue( context.Draw( library.Load( "shaders/ui_filter.shader" ), Composite( layer, 16, 8 ), new GeometryDraw
		{
			Vertices = vertexBuffer,
			Layout = VertexStreamLayout.EngineVertex,
			Indices = indexBuffer,
			IndexOffset = 4,
			IndexFormat = IndexFormat.UInt16,
			Topology = PrimitiveTopology.TriangleList,
			Arguments = arguments,
			ArgumentsOffset = 20,
			DrawCount = 1,
		} ) );

		var pixels = Finish( context, main );
		Save( pixels, Size, Size, "graphics-indirect" );
		Near( (64, 128, 191, 255), Pixel( pixels, Size, 20, 12 ), "inside the drawn quad" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)255), Pixel( pixels, Size, 48, 48 ), "where the first draw's quad would be" );
		context.Release();
		Device.Destroy( vertexBuffer );
		Device.Destroy( indexBuffer );
		Device.Destroy( arguments );
	}

	/// <summary>
	/// Vertices of a layout of their own (position, a two-float uv, a float colour, 36 bytes apart, from a byte offset), drawn
	/// unindexed as <c>Graphics.Draw( GpuBuffer&lt;T&gt; )</c> does, once with its counts here from a first vertex and once with
	/// them in an arguments buffer: both quads land.
	/// </summary>
	[TestMethod]
	public void VertexBufferDrawsReadTheirLayout()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Vertex buffer" );
		var context = new GraphicsContext( Device, drawer, list );
		var (main, layer) = Targets( context );

		var layout = new VertexStreamLayout( 36,
			new VertexField( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
			new VertexField( VertexSemantic.TexCoord, 0, VertexFormat.Float2, 12 ),
			new VertexField( VertexSemantic.Color, 0, VertexFormat.Float4, 20 ) );

		static float[] Quad( float x, float y ) =>
		[
			x, y, 0, 0, 0, 1, 1, 1, 1, x + 16, y, 0, 1, 0, 1, 1, 1, 1, x, y + 16, 0, 0, 1, 1, 1, 1, 1,
			x, y + 16, 0, 0, 1, 1, 1, 1, 1, x + 16, y, 0, 1, 0, 1, 1, 1, 1, x + 16, y + 16, 0, 1, 1, 1, 1, 1, 1,
		];

		// A float of padding first, then a quad that isn't drawn, then the two that are
		float[] floats = [0, .. Quad( 0, 40 ), .. Quad( 16, 8 ), .. Quad( 40, 40 )];
		var vertexBuffer = Device.CreateBuffer( new BufferInfo { Size = floats.Length * 4, Usage = BufferUsage.Vertex, Name = "Vertices" } );
		var arguments = Device.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Indirect, Name = "Arguments" } );
		Device.WriteBuffer<float>( vertexBuffer, floats );

		// Vertex count, instances, first vertex, first instance
		Device.WriteBuffer<int>( arguments, [6, 1, 12, 0] );

		var shader = library.Load( "shaders/ui_filter.shader" );
		var draw = new GeometryDraw { Vertices = vertexBuffer, VertexOffset = 4, Layout = layout, Topology = PrimitiveTopology.TriangleList, First = 6, Count = 6 };
		Assert.IsTrue( context.Draw( shader, Composite( layer, 16, 8 ), draw ) );
		Assert.IsTrue( context.Draw( shader, Composite( layer, 40, 40 ), draw with { Arguments = arguments, DrawCount = 1 } ) );

		var pixels = Finish( context, main );
		Save( pixels, Size, Size, "graphics-vertex-buffer" );
		Near( (64, 128, 191, 255), Pixel( pixels, Size, 20, 12 ), "the quad drawn from its first vertex" );
		Near( (64, 128, 191, 255), Pixel( pixels, Size, 48, 48 ), "the quad drawn from its arguments" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)255), Pixel( pixels, Size, 8, 48 ), "the quad before them" );
		context.Release();
		Device.Destroy( vertexBuffer );
		Device.Destroy( arguments );
	}

	/// <summary>
	/// A texture's mip 0 copied into another's mip 1 (the same size there), as <c>Graphics.CopyTexture</c> copies between
	/// mips; and the bound depth's viewport copied into a texture of its own, as <c>Graphics.GrabDepthTexture</c> resolves it.
	/// </summary>
	[TestMethod]
	public void CopiesBetweenMipsAndOfDepth()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Copies" );
		var context = new GraphicsContext( Device, drawer, list );
		var source = context.CreateTarget( 8, 8, Format.RGBA8Unorm, name: "Source" );
		var destination = context.CreateTarget( 16, 16, Format.RGBA8Unorm, mips: 2, name: "Destination" );
		context.ClearTexture( source, new Vector4( 1, 0, 0, 1 ) );
		context.ClearTexture( destination, new Vector4( 0, 0, 1, 1 ) );
		context.CopyTexture( source, 0, 0, destination, 1, 0 );

		// Depth cleared to a quarter, copied from a viewport part way in
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.D32Float, name: "Depth" ) );
		var grabbed = Device.CreateTexture( TextureInfo.RenderTarget( 16, 16, Format.D32Float, name: "Grabbed depth" ) );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( depth, default, 0.25f );
		context.Track( depth, ImageLayout.CopyDestination );
		context.Track( grabbed, ImageLayout.Undefined );
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		context.SetTarget( main, depth );
		context.Viewport = new Vector4( 8, 8, 16, 16 );
		Assert.IsTrue( context.CopyDepth( grabbed ) );

		var mipReadback = Device.CreateBuffer( new BufferInfo { Size = 8 * 8 * 4, Memory = MemoryLocation.Readback, Name = "Mip" } );
		var depthReadback = Device.CreateBuffer( new BufferInfo { Size = 16 * 16 * 4, Memory = MemoryLocation.Readback, Name = "Depth" } );
		context.CopyToBuffer( destination, mipReadback, 1 );
		context.CopyToBuffer( grabbed, depthReadback );
		context.Finish();
		Device.Wait( Device.Submit( list ) );

		var mip = Device.Map( mipReadback )[..(8 * 8 * 4)].ToArray();
		Assert.AreEqual( ((byte)255, (byte)0, (byte)0, (byte)255), Pixel( mip, 8, 5, 5 ), "mip 1 has the source's pixels" );
		var depths = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( depthReadback )[..(16 * 16 * 4)] ).ToArray();
		Assert.IsTrue( depths.All( d => d == 0.25f ), "the grabbed depth is the bound depth's" );

		context.Release();
		Device.Destroy( mipReadback );
		Device.Destroy( depthReadback );
		Device.Destroy( depth );
		Device.Destroy( grabbed );
	}

	/// <summary>
	/// A depth that isn't bound copied from a region of it, as a depth grab over a frame's output takes the finished frame's
	/// depth while only the output's colour is bound.
	/// </summary>
	[TestMethod]
	public void CopiesDepthThatIsNotBound()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Unbound depth" );
		var context = new GraphicsContext( Device, drawer, list );
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.D32Float, name: "Frame depth" ) );
		var grabbed = Device.CreateTexture( TextureInfo.RenderTarget( 16, 16, Format.D32Float, name: "Grabbed depth" ) );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( depth, default, 0.75f );
		context.Track( depth, ImageLayout.CopyDestination );
		context.Track( grabbed, ImageLayout.Undefined );
		var output = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Output" );
		context.SetTarget( output );
		Assert.IsFalse( context.Depth.IsValid, "no depth is bound" );
		Assert.IsTrue( context.CopyDepth( depth, grabbed, new Vector4( 0, 0, 16, 16 ) ) );

		var readback = Device.CreateBuffer( new BufferInfo { Size = 16 * 16 * 4, Memory = MemoryLocation.Readback, Name = "Depth" } );
		context.CopyToBuffer( grabbed, readback );
		context.Finish();
		Device.Wait( Device.Submit( list ) );

		var depths = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( readback )[..(16 * 16 * 4)] ).ToArray();
		Assert.IsTrue( depths.All( d => d == 0.75f ), "the grabbed depth is the frame's" );

		context.Release();
		Device.Destroy( readback );
		Device.Destroy( depth );
		Device.Destroy( grabbed );
	}

	/// <summary>
	/// Multisampled depth grabbed into a single-sampled texture, as <c>Graphics.GrabDepthTexture</c> into a target without
	/// the frame's samples: resolved whole, sample zero, and validation (synchronisation included) stays clean. A texture
	/// smaller than the depth isn't resolved into.
	/// </summary>
	[TestMethod]
	public void MultisampledDepthResolvesIntoOneSample()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Depth resolve" );
		var context = new GraphicsContext( Device, drawer, list );
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.D32Float, samples: 4, name: "Multisampled depth" ) );
		var grabbed = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.D32Float, name: "Grabbed depth" ) );
		var small = Device.CreateTexture( TextureInfo.RenderTarget( 16, 16, Format.D32Float, name: "Too small" ) );
		var main = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA8Unorm, samples: 4, name: "Main" ) );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( depth, default, 0.25f );
		context.Track( depth, ImageLayout.CopyDestination );
		context.Track( grabbed, ImageLayout.Undefined );
		context.Track( small, ImageLayout.Undefined );
		context.Track( main, ImageLayout.Undefined );
		context.SetTarget( main, depth );
		Assert.IsFalse( context.CopyDepth( small ), "a texture smaller than the depth" );
		Assert.IsTrue( context.CopyDepth( grabbed ) );

		var readback = Device.CreateBuffer( new BufferInfo { Size = Size * Size * 4, Memory = MemoryLocation.Readback, Name = "Depth" } );
		context.CopyToBuffer( grabbed, readback );
		context.Finish();
		Device.Wait( Device.Submit( list ) );

		var depths = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( readback )[..(Size * Size * 4)] ).ToArray();
		Assert.IsTrue( depths.All( d => d == 0.25f ), "the resolved depth is the multisampled one's" );

		context.Release();
		Device.Destroy( readback );
		foreach ( var texture in (TextureHandle[])[depth, grabbed, small, main] ) Device.Destroy( texture );
	}

	/// <summary>A 64-square black main target and a 16-square layer to composite, cleared, with the main one bound.</summary>
	static (TextureHandle Main, TextureHandle Layer) Targets( GraphicsContext context )
	{
		var main = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Main" );
		var layer = context.CreateTarget( 16, 16, Format.RGBA8Unorm, name: "Layer" );
		context.SetTarget( main );
		context.Clear( new Vector4( 0, 0, 0, 1 ) );
		context.SetTarget( layer );
		context.Clear( new Vector4( 0.25f, 0.5f, 0.75f, 1 ) );
		context.SetTarget( main );
		return (main, layer);
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
