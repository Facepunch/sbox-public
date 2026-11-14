using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Linq;
using System.Runtime.InteropServices;
using Num = System.Numerics;

namespace Borealis.Backend;

/// <summary>
/// Immediate drawing on Borealis.Gpu in the shape the engine's <c>Graphics</c> calls take: bind a colour target, clear
/// it, draw with a compiled shader and named attributes, grab a copy of the target, generate mips. It records into one
/// command list, opens rendering scopes lazily around draws and tracks each texture's layout, so callers never write a
/// barrier. What a <c>Graphics</c> backend on Borealis.Gpu lowers the UI's command lists to (stage 1 of
/// docs/managed/borealis-gpu-path.md).
/// </summary>
/// <remarks>
/// Layouts are tracked per texture, not per mip, and recording order is assumed to be submission order, as
/// <see cref="BackendContext"/> assumes. Textures this context didn't create are taken to be in
/// <see cref="ImageLayout.ShaderRead"/> when it first sees them, unless <see cref="Track"/> says otherwise.
/// </remarks>
public sealed class GraphicsContext
{
	readonly Device gpu;
	readonly ShaderDrawer drawer;
	readonly Dictionary<TextureHandle, ImageLayout> layouts = new();
	readonly Dictionary<(TextureHandle Texture, int Mip), TextureViewHandle> mipViews = new();
	readonly List<TextureHandle> owned = new();
	BufferHandle quadIndices;
	bool rendering;

	/// <summary>The list everything records into.</summary>
	public CommandList List { get; private set; }

	/// <summary>The bound colour target; default when none is.</summary>
	public TextureHandle Target { get; private set; }

	/// <summary>
	/// The depth target bound with it, or default. A draw whose shader tests or writes depth gets it in its scope (read-only
	/// when it only tests); others draw without it.
	/// </summary>
	public TextureHandle Depth { get; private set; }

	// The open scope's depth: none, read-only or written
	enum DepthUse { None, ReadOnly, Write }
	DepthUse scopeDepth;

	/// <summary>Where draws land in the target, and what shaders read as <c>Source( Viewport )</c>: x, y, width, height.</summary>
	public Num.Vector4 Viewport { get; set; }

	/// <summary>Draws issued, for reports.</summary>
	public int Draws { get; private set; }

	/// <summary>A context recording into <paramref name="list"/>.</summary>
	public GraphicsContext( Device gpu, ShaderDrawer drawer, CommandList list )
	{
		this.gpu = gpu;
		this.drawer = drawer;
		List = list;
	}

	/// <summary>The drawer binding shaders.</summary>
	public ShaderDrawer Drawer => drawer;

	/// <summary>
	/// Record into <paramref name="list"/> from now on, with no target bound: the next block of drawing, submitted after
	/// the last. Texture layouts carry over, so the list has to be submitted after the previous one.
	/// </summary>
	public void Begin( CommandList list )
	{
		List = list;
		rendering = false;
		Target = default;
		Depth = default;
		SrgbWrite = false;
		Viewport = default;
	}

	/// <summary>The layout a texture is in at this point of the recording, as tracked.</summary>
	public ImageLayout LayoutOf( TextureHandle texture ) => Layout( texture );

	/// <summary>Say which layout a texture is in now, for textures made or used outside this context.</summary>
	public void Track( TextureHandle texture, ImageLayout layout ) => layouts[texture] = layout;

	/// <summary>Stop tracking a texture that's about to be destroyed, and destroy the views made of it.</summary>
	public void Forget( TextureHandle texture )
	{
		layouts.Remove( texture );
		foreach ( var key in mipViews.Keys.Where( k => k.Texture == texture ).ToArray() )
		{
			gpu.Destroy( mipViews[key] );
			mipViews.Remove( key );
		}
	}

	/// <summary>
	/// A 2D colour target that can also be sampled, copied and written by compute (for its mips), owned by this
	/// context until <see cref="Release"/>. Its contents start undefined.
	/// </summary>
	public TextureHandle CreateTarget( int width, int height, Format format, int mips = 1, string name = null )
	{
		var info = TextureInfo.RenderTarget( width, height, format, name: name );
		info.Mips = Math.Max( mips, 1 );
		info.Usage |= TextureUsage.Storage;
		var texture = gpu.CreateTexture( info );
		layouts[texture] = ImageLayout.Undefined;
		owned.Add( texture );
		return texture;
	}

	/// <summary>
	/// Bind <paramref name="texture"/> as the colour target, with the viewport covering it, as setting
	/// <c>Graphics.RenderTarget</c> does.
	/// </summary>
	public void SetTarget( TextureHandle texture ) => SetTarget( texture, default );

	/// <summary>
	/// Bind <paramref name="texture"/> as the colour target and <paramref name="depth"/> (default for none) as its depth, with
	/// the viewport covering the colour, as setting <c>Graphics.RenderTarget</c> to a target with both does.
	/// </summary>
	public void SetTarget( TextureHandle texture, TextureHandle depth ) => SetTarget( texture, depth, false );

	/// <summary>
	/// <see cref="SetTarget(TextureHandle, TextureHandle)"/>, writing the colour through its sRGB view when
	/// <paramref name="srgb"/> and its format has one: the hardware encodes what's drawn, as a Source 2 target with sRGB
	/// writes does.
	/// </summary>
	public void SetTarget( TextureHandle texture, TextureHandle depth, bool srgb )
	{
		srgb = srgb && gpu.InfoOf( texture ).Format.HasSrgb();
		if ( Target == texture && Depth == depth && SrgbWrite == srgb ) return;
		EndScope();
		Target = texture;
		Depth = depth;
		SrgbWrite = srgb;
		var info = gpu.InfoOf( texture );
		Viewport = new Num.Vector4( 0, 0, info.Width, info.Height );
	}

	/// <summary>Whether the colour target is written through its sRGB view (<see cref="SetTarget(TextureHandle, TextureHandle, bool)"/>).</summary>
	public bool SrgbWrite { get; private set; }

	/// <summary>The colour target's format as drawing sees it: its sRGB twin while <see cref="SrgbWrite"/>.</summary>
	Format TargetFormat => SrgbWrite ? gpu.InfoOf( Target ).Format.Srgb() : gpu.InfoOf( Target ).Format;

	/// <summary>The colour target's samples: a multisampled frame's when drawing inside one (a camera's stage).</summary>
	int TargetSamples => gpu.InfoOf( Target ).Samples;

	/// <summary>Fill the whole target with <paramref name="color"/>.</summary>
	public void Clear( Num.Vector4 color )
	{
		EndScope();
		TransitionForWrite( Target, ImageLayout.CopyDestination );
		List.ClearTexture( Target, color );
	}

	/// <summary>
	/// Draw <paramref name="instances"/> instances of a quad (indices 0, 1, 2, 0, 2, 3) into the target, as
	/// <c>Graphics.DrawIndexedInstanced</c> does with the painter's quad index buffer.
	/// </summary>
	public bool DrawQuadInstances( ShaderFile shader, ShaderAttributes attributes, int instances )
	{
		if ( !quadIndices.IsValid )
		{
			quadIndices = gpu.CreateBuffer( new BufferInfo { Size = 6 * 4, Usage = BufferUsage.Index, Name = "Quad indices" } );
			gpu.WriteBuffer<uint>( quadIndices, [0, 1, 2, 0, 2, 3] );
		}

		return DrawIndexedInstanced( shader, attributes, quadIndices, 6, instances );
	}

	/// <summary>Draw <paramref name="instances"/> instances of <paramref name="indexCount"/> 32-bit indices into the target.</summary>
	public bool DrawIndexedInstanced( ShaderFile shader, ShaderAttributes attributes, BufferHandle indices, int indexCount, int instances )
	{
		var depth = BeginScope( shader, attributes );
		Draws++;
		return drawer.DrawIndexedInstanced( List, shader, attributes, TargetFormat, depth, TargetSamples, indices, indexCount, instances, Viewport );
	}

	/// <summary>
	/// Draw a screen-space rectangle (x, y, width, height) in one colour, as <c>Graphics.DrawQuad</c> does: four
	/// <see cref="VertexStreamLayout.EngineVertex"/>es as a strip, uvs 0..1. <paramref name="color"/> is the vertex
	/// colour's four bytes, red in the lowest, as the engine's <c>Color32</c> holds them.
	/// </summary>
	public bool DrawQuad( ShaderFile shader, ShaderAttributes attributes, Num.Vector4 rect, uint color )
	{
		var c = new Rgba( color );
		ReadOnlySpan<EngineVertex> vertices =
		[
			new( new Num.Vector3( rect.X, rect.Y, 0 ), c, new Num.Vector4( 0, 0, 0, 0 ) ),
			new( new Num.Vector3( rect.X + rect.Z, rect.Y, 0 ), c, new Num.Vector4( 1, 0, 0, 0 ) ),
			new( new Num.Vector3( rect.X, rect.Y + rect.W, 0 ), c, new Num.Vector4( 0, 1, 0, 0 ) ),
			new( new Num.Vector3( rect.X + rect.Z, rect.Y + rect.W, 0 ), c, new Num.Vector4( 1, 1, 0, 0 ) ),
		];

		return DrawVertices( shader, attributes, MemoryMarshal.AsBytes( vertices ), VertexStreamLayout.EngineVertex, 4, PrimitiveTopology.TriangleStrip );
	}

	/// <summary>
	/// Draw <paramref name="count"/> vertices of <paramref name="layout"/> from the CPU into the target, as the engine's
	/// <c>Graphics.Draw</c> does with vertices it's handed.
	/// </summary>
	public bool DrawVertices( ShaderFile shader, ShaderAttributes attributes, ReadOnlySpan<byte> vertices, VertexStreamLayout layout, int count, PrimitiveTopology topology )
	{
		var depth = BeginScope( shader, attributes );
		var upload = List.AllocateUpload( vertices.Length, 16 );
		vertices.CopyTo( upload.Data );
		Draws++;
		return drawer.Draw( List, shader, attributes, TargetFormat, depth, TargetSamples, upload.Buffer, upload.Offset, layout, count, topology, Viewport );
	}

	/// <summary>
	/// Draw vertices into depth alone - a target of the bound colour target's size, which only this draw's scope uses, its
	/// contents never read - counting the samples that pass into occlusion query <paramref name="query"/>, which is reset
	/// first: how native counts the pixels of a frame in a luminance range for auto exposure (<c>tonemap_query</c>, with
	/// colour writes and depth testing off, over the colour it samples).
	/// </summary>
	public bool DrawCounted( ShaderFile shader, ShaderAttributes attributes, ReadOnlySpan<byte> vertices, VertexStreamLayout layout, int count, PrimitiveTopology topology, OcclusionQueries queries, int query )
	{
		if ( !Target.IsValid ) throw new InvalidOperationException( "No colour target is bound." );
		EndScope();

		foreach ( var (_, value) in attributes.Values )
		{
			if ( value is TextureHandle texture ) Transition( texture, ImageLayout.ShaderRead );
		}

		var info = gpu.InfoOf( Target );
		if ( !countDepth.IsValid || gpu.InfoOf( countDepth ).Width != info.Width || gpu.InfoOf( countDepth ).Height != info.Height )
		{
			if ( countDepth.IsValid )
			{
				owned.Remove( countDepth );
				layouts.Remove( countDepth );
				gpu.Destroy( countDepth );
			}

			countDepth = gpu.CreateTexture( TextureInfo.RenderTarget( info.Width, info.Height, Format.D32Float, name: "Counted draws" ) );
			layouts[countDepth] = ImageLayout.Undefined;
			owned.Add( countDepth );
		}

		List.ResetQueries( queries, query, 1 );
		Transition( countDepth, ImageLayout.DepthReadOnly );
		List.BeginRendering( [], new DepthTarget { Texture = countDepth, Load = LoadOp.Load, Store = StoreOp.Store, ReadOnly = true } );
		var upload = List.AllocateUpload( vertices.Length, 16 );
		vertices.CopyTo( upload.Data );
		List.BeginQuery( queries, query );
		Draws++;
		var drawn = drawer.Draw( List, shader, attributes, Format.Unknown, Format.D32Float, 1, upload.Buffer, upload.Offset, layout, count, topology, Viewport );
		List.EndQuery( queries, query );
		List.EndRendering();
		return drawn;
	}

	TextureHandle countDepth;

	/// <summary>Fill a whole texture with <paramref name="color"/>, bound or not.</summary>
	public void ClearTexture( TextureHandle texture, Num.Vector4 color )
	{
		EndScope();
		TransitionForWrite( texture, ImageLayout.CopyDestination );
		List.ClearTexture( texture, color );
	}

	/// <summary>
	/// Copy the viewport's part of the target into mip 0 of <paramref name="destination"/>, from its corner, as
	/// <c>Graphics.GrabFrameTexture</c> resolves the frame into a texture of its own.
	/// </summary>
	public void CopyTarget( TextureHandle destination )
	{
		var width = (int)Viewport.Z;
		var height = (int)Viewport.W;
		EndScope();
		Transition( Target, ImageLayout.CopySource );

		// A destination already in CopyDestination (cleared, or copied into) still needs its last write finished first
		TransitionForWrite( destination, ImageLayout.CopyDestination );
		List.Blit( Target, ((int)Viewport.X, (int)Viewport.Y, width, height), destination, (0, 0, width, height), Filter.Nearest );
	}

	/// <summary>
	/// Run a compute shader outside any rendering scope. Textures it's given whole are put in
	/// <see cref="ImageLayout.General"/>, which it may both read and write.
	/// </summary>
	public bool Dispatch( ShaderFile shader, ShaderAttributes attributes, int x, int y, int z )
	{
		PrepareDispatch( shader, attributes );
		return drawer.Dispatch( List, shader, attributes, x, y, z );
	}

	/// <summary>
	/// As <see cref="Dispatch"/>, over the thread groups <paramref name="arguments"/> holds at <paramref name="offset"/> bytes,
	/// written by an earlier pass (a barrier after that pass makes them visible, as native needs one).
	/// </summary>
	public bool DispatchIndirect( ShaderFile shader, ShaderAttributes attributes, BufferHandle arguments, long offset )
	{
		PrepareDispatch( shader, attributes );
		return drawer.DispatchIndirect( List, shader, attributes, arguments, offset );
	}

	void PrepareDispatch( ShaderFile shader, ShaderAttributes attributes )
	{
		EndScope();

		// Textures it writes go to General, which it may also read; those it only samples to ShaderRead, as their
		// bindings expect
		var variant = drawer.Variant( shader, attributes );
		foreach ( var (_, value) in attributes.Values )
		{
			if ( value is TextureHandle texture ) dispatchLayouts[texture] = ImageLayout.Undefined;
		}

		foreach ( var resource in variant?.Resources ?? [] )
		{
			if ( resource.Name is null || attributes.Get( resource.Name ) is not TextureHandle texture ) continue;
			if ( resource.Kind == ShaderResourceKind.StorageTexture ) dispatchLayouts[texture] = ImageLayout.General;
			else if ( resource.Kind == ShaderResourceKind.Texture && dispatchLayouts[texture] != ImageLayout.General ) dispatchLayouts[texture] = ImageLayout.ShaderRead;
		}

		foreach ( var (texture, layout) in dispatchLayouts ) Transition( texture, layout == ImageLayout.Undefined ? ImageLayout.General : layout );
		dispatchLayouts.Clear();
	}

	readonly Dictionary<TextureHandle, ImageLayout> dispatchLayouts = new();

	/// <summary>Copy bytes into a buffer in order with the rest of the list, visible to everything after it.</summary>
	public void UpdateBuffer( BufferHandle buffer, ReadOnlySpan<byte> data, long offset )
	{
		EndScope();
		List.UpdateBuffer( buffer, data, offset );
		MemoryBarrier();
	}

	/// <summary>
	/// Write timestamp <paramref name="index"/> of <paramref name="queries"/> once the work recorded so far reaches
	/// <paramref name="stage"/>, resetting <paramref name="reset"/> timestamps from it first (each is reset before it's
	/// written again).
	/// </summary>
	public void WriteTimestamp( TimestampQueries queries, int index, PipelineStage stage, int reset = 0 )
	{
		EndScope();
		if ( reset > 0 ) List.ResetTimestamps( queries, index, reset );
		List.WriteTimestamp( queries, index, stage );
	}

	/// <summary>Make every write so far visible to everything after it, keeping layouts (a UAV barrier).</summary>
	public void MemoryBarrier()
	{
		EndScope();
		List.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
	}

	/// <summary>
	/// Copy the target into a new texture with <paramref name="mips"/> mips and generate them with
	/// <paramref name="downsample"/> (<c>downsample_cs.shader</c>, method <paramref name="method"/>), as
	/// <c>Graphics.GrabFrameTexture</c> does. The copy is the viewport's size, read from its corner. It's left readable.
	/// </summary>
	public TextureHandle GrabFrame( int mips, ShaderFile downsample, int method, string name = null )
	{
		var info = gpu.InfoOf( Target );
		var width = (int)Viewport.Z;
		var height = (int)Viewport.W;
		var grab = CreateTarget( width, height, info.Format, mips, name ?? "Frame grab" );

		EndScope();
		Transition( Target, ImageLayout.CopySource );
		Transition( grab, ImageLayout.CopyDestination );
		List.Blit( Target, ((int)Viewport.X, (int)Viewport.Y, width, height), grab, (0, 0, width, height), Filter.Nearest );
		if ( mips > 1 ) GenerateMips( grab, downsample, method );
		Transition( grab, ImageLayout.ShaderRead );
		return grab;
	}

	/// <summary>
	/// Fill mips 1 and down of <paramref name="texture"/> from mip 0 with <c>downsample_cs.shader</c>, one dispatch per mip,
	/// as <c>Graphics.GenerateMipMaps</c> does for its gaussian methods (<paramref name="method"/> is
	/// <c>D_DOWNSAMPLE_METHOD</c>: 1 gaussian blur, 2 gaussian with a transparent border). Mips smaller than a texel on
	/// either side are left as they are, as native leaves them. Mips before <paramref name="initialMip"/> are kept. The
	/// texture ends readable.
	/// </summary>
	public void GenerateMips( TextureHandle texture, ShaderFile downsample, int method, int initialMip = 0 )
	{
		var info = gpu.InfoOf( texture );
		EndScope();
		Transition( texture, ImageLayout.General );

		var attributes = new ShaderAttributes();
		attributes.SetCombo( "D_DOWNSAMPLE_METHOD", method );
		for ( int i = initialMip; i < info.Mips - 1; i++ )
		{
			int width = info.Width >> (i + 1), height = info.Height >> (i + 1);
			if ( width < 1 || height < 1 ) break;

			// Mip i was written (copied, drawn or dispatched) in General; now it's read
			List.Barrier( new TextureBarrier
			{
				Texture = texture,
				OldLayout = ImageLayout.General,
				NewLayout = ImageLayout.ShaderRead,
				SourceStage = PipelineStage.AllCommands,
				SourceAccess = MemoryAccess.MemoryWrite,
				DestinationStage = PipelineStage.ComputeShader,
				DestinationAccess = MemoryAccess.ShaderRead,
				BaseMip = i,
				MipCount = 1,
				LayerCount = 1,
			} );

			attributes.Set( "MipLevel0", MipView( texture, i ) );
			attributes.Set( "MipLevel1", MipView( texture, i + 1 ) );
			attributes.Set( "TextureSize", width, height );
			attributes.Set( "InvTextureSize", 1f / width, 1f / height );
			if ( !drawer.Dispatch( List, downsample, attributes, width, height, 1 ) ) throw new InvalidOperationException( $"{downsample.Name} didn't dispatch" );

			// Put mip i back in General so the whole texture is in one layout again
			List.Barrier( new TextureBarrier
			{
				Texture = texture,
				OldLayout = ImageLayout.ShaderRead,
				NewLayout = ImageLayout.General,
				SourceStage = PipelineStage.ComputeShader,
				SourceAccess = MemoryAccess.ShaderRead,
				DestinationStage = PipelineStage.AllCommands,
				DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
				BaseMip = i,
				MipCount = 1,
				LayerCount = 1,
			} );
		}

		Transition( texture, ImageLayout.ShaderRead );
	}

	/// <summary>
	/// Fill the mips after <paramref name="initialMip"/> with <c>downsample_spd.shader</c> (<paramref name="method"/> is
	/// <c>D_DOWNSAMPLE_METHOD</c>: box, max, min or min and max), six mips a dispatch, down to the smaller side's last
	/// texel, as <c>Graphics.GenerateMipMaps</c> does for those methods. The texture ends readable.
	/// </summary>
	public void DownsampleSpd( TextureHandle texture, ShaderFile spd, int method, int initialMip = 0 )
	{
		var info = gpu.InfoOf( texture );
		EndScope();
		Transition( texture, ImageLayout.General );

		var attributes = new ShaderAttributes();
		attributes.SetCombo( "D_DOWNSAMPLE_METHOD", method );
		attributes.SetCombo( "D_FUSED_DEPTH", 0 );
		var lastMip = Math.Min( info.Mips - 1, System.Numerics.BitOperations.Log2( (uint)Math.Min( info.Width, info.Height ) ) );
		for ( int source = initialMip; source < lastMip; )
		{
			int width = info.Width >> source, height = info.Height >> source;
			var mipCount = Math.Min( lastMip - source, 6 );

			// The source mip is read, the rest written; mips past the count are never written, so the last one fills them
			List.Barrier( MipBarrier( texture, source, ImageLayout.General, ImageLayout.ShaderRead ) );
			attributes.Set( "Mip0", MipView( texture, source ) );
			for ( int i = 1; i <= 6; i++ ) attributes.Set( $"Mip{i}", MipView( texture, source + Math.Min( i, mipCount ) ) );
			attributes.Set( "SourceSize", width, height );
			attributes.Set( "MipCount", mipCount );
			if ( !drawer.Dispatch( List, spd, attributes, (width + 63) / 64 * 256, (height + 63) / 64, 1 ) ) throw new InvalidOperationException( $"{spd.Name} didn't dispatch" );

			List.Barrier( MipBarrier( texture, source, ImageLayout.ShaderRead, ImageLayout.General ) );
			source += mipCount;
		}

		MemoryBarrier();
		Transition( texture, ImageLayout.ShaderRead );
	}

	/// <summary>
	/// Build a view's depth chain (<c>DepthChainDownsample</c>, which <c>Depth::Get</c> reads) from
	/// <paramref name="depth"/> into <paramref name="chain"/>, an RG32F storage texture of the depth's size with mips:
	/// min and max of the depth, resolved into the first mip by the first dispatch of <c>downsample_spd</c> and reduced into
	/// the rest, as Borealis's depth chain layer builds it on Source 2. Both end readable.
	/// </summary>
	public void BuildDepthChain( TextureHandle depth, TextureHandle chain, ShaderFile spd )
	{
		var info = gpu.InfoOf( chain );
		EndScope();
		Transition( depth, ImageLayout.ShaderRead );
		Transition( chain, ImageLayout.General );

		var attributes = new ShaderAttributes();
		attributes.SetCombo( "D_DOWNSAMPLE_METHOD", 5 );
		// A multisampled depth gives its first sample, as native's depth resolve does
		attributes.SetCombo( "D_MSAA", gpu.InfoOf( depth ).Samples > 1 ? 1 : 0 );
		attributes.Set( "SourceDepth", depth );
		var fused = true;
		var lastMip = Math.Min( info.Mips - 1, System.Numerics.BitOperations.Log2( (uint)Math.Min( info.Width, info.Height ) ) );
		for ( int source = 0; source < lastMip; )
		{
			int width = info.Width >> source, height = info.Height >> source;
			var mipCount = Math.Min( lastMip - source, 6 );
			attributes.SetCombo( "D_FUSED_DEPTH", fused ? 1 : 0 );
			if ( fused ) attributes.Set( "Mip0Out", MipView( chain, source ) );
			else List.Barrier( MipBarrier( chain, source, ImageLayout.General, ImageLayout.ShaderRead ) );
			for ( int i = fused ? 1 : 0; i <= 6; i++ ) attributes.Set( $"Mip{i}", MipView( chain, source + Math.Min( i, mipCount ) ) );
			attributes.Set( "SourceSize", width, height );
			attributes.Set( "MipCount", mipCount );
			if ( !drawer.Dispatch( List, spd, attributes, (width + 63) / 64 * 256, (height + 63) / 64, 1 ) ) throw new InvalidOperationException( $"{spd.Name} didn't dispatch" );

			if ( !fused ) List.Barrier( MipBarrier( chain, source, ImageLayout.ShaderRead, ImageLayout.General ) );
			MemoryBarrier();
			source += mipCount;
			fused = false;
		}

		Transition( chain, ImageLayout.ShaderRead );
	}

	static TextureBarrier MipBarrier( TextureHandle texture, int mip, ImageLayout from, ImageLayout to ) => new()
	{
		Texture = texture,
		OldLayout = from,
		NewLayout = to,
		SourceStage = PipelineStage.AllCommands,
		SourceAccess = MemoryAccess.MemoryWrite,
		DestinationStage = PipelineStage.AllCommands,
		DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
		BaseMip = mip,
		MipCount = 1,
		LayerCount = 1,
	};

	/// <summary>
	/// Copy mip <paramref name="mip"/> of <paramref name="source"/> into the same mip of <paramref name="destination"/>, the
	/// same size and format, as <c>Graphics.CopyTexture</c> does. Both are left in the copy's layouts, tracked.
	/// </summary>
	public void CopyTexture( TextureHandle source, TextureHandle destination, int mip = 0 )
	{
		EndScope();
		Transition( source, ImageLayout.CopySource );
		TransitionForWrite( destination, ImageLayout.CopyDestination );
		List.CopyTexture( source, destination, mip, 1 );
	}

	/// <summary>A single mip of a texture as a view, made once, for binding one mip (sampled or as storage).</summary>
	public TextureViewHandle View( TextureHandle texture, int mip ) => MipView( texture, mip );

	/// <summary>
	/// Copy <paramref name="texture"/>'s mip 0 into <paramref name="readback"/> (a <see cref="MemoryLocation.Readback"/>
	/// buffer) and make it visible to the host once the list has finished.
	/// </summary>
	public void CopyToBuffer( TextureHandle texture, BufferHandle readback ) => CopyToBuffer( texture, readback, 0 );

	/// <summary>As <see cref="CopyToBuffer(TextureHandle, BufferHandle)"/>, for one mip.</summary>
	public void CopyToBuffer( TextureHandle texture, BufferHandle readback, int mip )
	{
		EndScope();
		Transition( texture, ImageLayout.CopySource );
		List.CopyTextureToBuffer( texture, mip, 0, readback );
		List.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
	}

	/// <summary>Close any open rendering scope; call before submitting the list.</summary>
	public void Finish() => EndScope();

	/// <summary>
	/// Copy mip 0 of <paramref name="source"/> over the whole of a swap chain image just acquired (its contents are
	/// undefined), converting the format if they differ, and leave the image ready to present. The image isn't tracked
	/// after this.
	/// </summary>
	public void CopyToPresent( TextureHandle source, TextureHandle image )
	{
		var from = gpu.InfoOf( source );
		var to = gpu.InfoOf( image );
		EndScope();
		Transition( source, ImageLayout.CopySource );
		layouts[image] = ImageLayout.Undefined;
		Transition( image, ImageLayout.CopyDestination );
		List.Blit( source, (0, 0, Math.Min( from.Width, to.Width ), Math.Min( from.Height, to.Height )), image, (0, 0, Math.Min( from.Width, to.Width ), Math.Min( from.Height, to.Height )), Filter.Nearest );
		Transition( image, ImageLayout.Present );
		layouts.Remove( image );
	}

	/// <summary>
	/// Close any open rendering scope and leave every texture readable, as a block's last commands: later lists can then
	/// sample what this one drew by bindless index, which no layout tracking sees.
	/// </summary>
	public void FinishReadable()
	{
		EndScope();
		foreach ( var texture in layouts.Keys.ToArray() ) Transition( texture, ImageLayout.ShaderRead );
	}

	/// <summary>Destroy the textures this context created.</summary>
	public void Release()
	{
		foreach ( var view in mipViews.Values ) gpu.Destroy( view );
		foreach ( var texture in owned ) gpu.Destroy( texture );
		if ( quadIndices.IsValid ) gpu.Destroy( quadIndices );
		mipViews.Clear();
		owned.Clear();
		quadIndices = default;
	}

	TextureViewHandle MipView( TextureHandle texture, int mip )
	{
		if ( mipViews.TryGetValue( (texture, mip), out var view ) ) return view;
		view = gpu.CreateView( texture, new TextureViewInfo { Dimension = TextureDimension.Texture2D, BaseMip = mip, MipCount = 1, LayerCount = 1 } );
		mipViews[(texture, mip)] = view;
		return view;
	}

	/// <summary>
	/// Make every texture a draw samples readable, then open a scope on the target - with the bound depth when the shader
	/// tests or writes depth, read-only when it only tests. Returns the depth format the draw's pipeline needs.
	/// </summary>
	Format BeginScope( ShaderFile shader, ShaderAttributes attributes )
	{
		if ( !Target.IsValid ) throw new InvalidOperationException( "No colour target is bound." );

		var variant = Depth.IsValid ? drawer.Variant( shader, attributes ) : null;
		var depth = variant?.State is { } state && (state.DepthTest || state.DepthWrite) ? (state.DepthWrite ? DepthUse.Write : DepthUse.ReadOnly) : DepthUse.None;

		foreach ( var (name, value) in attributes.Values )
		{
			if ( value is not TextureHandle texture || Layout( texture ) == ImageLayout.ShaderRead ) continue;

			// The targets may be among the attributes (a post process's view colour and depth) without the shader reading them
			if ( texture == Target || texture == Depth )
			{
				if ( !Reads( variant ?? drawer.Variant( shader, attributes ), name ) ) continue;
				if ( texture == Target || depth != DepthUse.None ) throw new InvalidOperationException( $"{name} samples a target being drawn to." );
			}

			EndScope();
			Transition( texture, ImageLayout.ShaderRead );
		}

		if ( rendering && scopeDepth == depth ) return depth == DepthUse.None ? Format.Unknown : gpu.InfoOf( Depth ).Format;
		EndScope();

		Transition( Target, ImageLayout.ColorTarget );
		var depthTarget = default( DepthTarget );
		if ( depth != DepthUse.None )
		{
			Transition( Depth, depth == DepthUse.Write ? ImageLayout.DepthTarget : ImageLayout.DepthReadOnly );
			depthTarget = new DepthTarget { Texture = Depth, Load = LoadOp.Load, Store = StoreOp.Store, ReadOnly = depth == DepthUse.ReadOnly };
		}

		List.BeginRendering( [new ColorTarget { Texture = Target, Load = LoadOp.Load, Store = StoreOp.Store, Srgb = SrgbWrite }], depthTarget );
		rendering = true;
		scopeDepth = depth;
		return depth == DepthUse.None ? Format.Unknown : gpu.InfoOf( Depth ).Format;
	}

	/// <summary>Whether a variant binds the texture set under <paramref name="name"/>.</summary>
	static bool Reads( ShaderVariant variant, string name )
	{
		foreach ( var resource in variant?.Resources ?? [] )
		{
			if ( resource.Kind is ShaderResourceKind.Texture or ShaderResourceKind.StorageTexture && string.Equals( resource.Name, name, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	void EndScope()
	{
		if ( !rendering ) return;
		List.EndRendering();
		rendering = false;
	}

	ImageLayout Layout( TextureHandle texture ) => layouts.TryGetValue( texture, out var layout ) ? layout : ImageLayout.ShaderRead;

	/// <summary>
	/// <see cref="Transition"/> for a write: one already in <paramref name="layout"/> still waits for what wrote it last, as
	/// two clears in a row must.
	/// </summary>
	void TransitionForWrite( TextureHandle texture, ImageLayout layout )
	{
		if ( Layout( texture ) != layout )
		{
			Transition( texture, layout );
			return;
		}

		List.Barrier( new TextureBarrier
		{
			Texture = texture,
			OldLayout = layout,
			NewLayout = layout,
			SourceStage = PipelineStage.AllCommands,
			SourceAccess = MemoryAccess.MemoryWrite,
			DestinationStage = PipelineStage.AllCommands,
			DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
			MipCount = 0,
			LayerCount = 0,
		} );
	}

	/// <summary>Move a whole texture to <paramref name="layout"/>, conservatively ordered after everything before it.</summary>
	void Transition( TextureHandle texture, ImageLayout layout )
	{
		var from = Layout( texture );
		if ( from == layout ) return;

		List.Barrier( new TextureBarrier
		{
			Texture = texture,
			OldLayout = from,
			NewLayout = layout,
			SourceStage = PipelineStage.AllCommands,
			SourceAccess = from == ImageLayout.Undefined ? MemoryAccess.None : MemoryAccess.MemoryWrite,
			DestinationStage = PipelineStage.AllCommands,
			DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
			MipCount = 0,
			LayerCount = 0,
		} );
		layouts[texture] = layout;
	}

	/// <summary>Four bytes, red first, for a vertex colour.</summary>
	[StructLayout( LayoutKind.Sequential )]
	readonly struct Rgba( uint packed )
	{
		readonly uint packed = packed;
	}

	/// <summary>Mirrors the engine's <c>Vertex</c>, 76 bytes.</summary>
	[StructLayout( LayoutKind.Sequential )]
	readonly struct EngineVertex( Num.Vector3 position, Rgba color, Num.Vector4 uv )
	{
		readonly Num.Vector3 position = position;
		readonly Rgba color = color;
		readonly Num.Vector3 normal;
		readonly Num.Vector4 uv0 = uv;
		readonly Num.Vector4 uv1;
		readonly Num.Vector4 tangent;
	}
}
