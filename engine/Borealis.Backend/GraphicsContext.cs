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
/// docs/reference/borealis-gpu-path.md).
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
	/// when it only tests); others draw without it. Bound with no colour target, every draw's scope has it: drawing into
	/// depth alone, as a shadow map or a depth prepass is drawn.
	/// </summary>
	public TextureHandle Depth { get; private set; }

	/// <summary>The array layer or cube face of <see cref="Depth"/> drawn to: a shadow map's slice.</summary>
	public int DepthSlice { get; private set; }

	/// <summary>
	/// The shader mode draws ask for by name (<c>Depth</c> in a shadow map or a prepass), as a native layer's mode: a shader
	/// without it doesn't draw, as native skips a material with no such mode. Null (the default) draws each shader's first
	/// mode.
	/// </summary>
	public string Mode { get; set; }

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
		DepthSlice = 0;
		SrgbWrite = false;
		Viewport = default;
		dispatched.Clear();
	}

	/// <summary>The layout a texture is in at this point of the recording, as tracked.</summary>
	public ImageLayout LayoutOf( TextureHandle texture ) => Layout( texture );

	/// <summary>Say which layout a texture is in now, for textures made or used outside this context.</summary>
	public void Track( TextureHandle texture, ImageLayout layout ) => layouts[texture] = layout;

	/// <summary>
	/// Before work recorded straight into <see cref="List"/> (a stretch blit, a vendor upscaler through
	/// <see cref="CommandList.BeginNative"/>): end any rendering scope and move <paramref name="texture"/> to
	/// <paramref name="layout"/>, where it's tracked after. One the work writes (<paramref name="written"/>) is made readable
	/// before a later draw samples it, as a dispatch's are.
	/// </summary>
	public void PrepareNative( TextureHandle texture, ImageLayout layout, bool written )
	{
		EndScope();
		Transition( texture, layout );
		if ( written ) dispatched.Add( texture );
	}

	/// <summary>Stop tracking a texture that's about to be destroyed, and destroy the views made of it.</summary>
	public void Forget( TextureHandle texture )
	{
		layouts.Remove( texture );
		dispatched.Remove( texture );
		// Removing while enumerating is allowed; this runs for every texture a block releases, so it allocates nothing
		foreach ( var (key, view) in mipViews )
		{
			if ( key.Texture != texture ) continue;
			gpu.Destroy( view );
			viewMips.Remove( view );
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
	public void SetTarget( TextureHandle texture, TextureHandle depth, bool srgb ) => SetTarget( texture, depth, srgb, 0 );

	/// <summary>
	/// <see cref="SetTarget(TextureHandle, TextureHandle, bool)"/>, drawing into layer <paramref name="depthSlice"/> of the
	/// depth. <paramref name="texture"/> may be default with a depth: drawing into depth alone, the viewport covering it.
	/// </summary>
	public void SetTarget( TextureHandle texture, TextureHandle depth, bool srgb, int depthSlice )
	{
		if ( !texture.IsValid && !depth.IsValid ) throw new ArgumentException( "Neither a colour nor a depth target" );
		srgb = srgb && texture.IsValid && gpu.InfoOf( texture ).Format.HasSrgb();
		if ( Target == texture && Depth == depth && SrgbWrite == srgb && DepthSlice == depthSlice ) return;
		EndScope();
		Target = texture;
		Depth = depth;
		DepthSlice = depthSlice;
		SrgbWrite = srgb;
		var info = gpu.InfoOf( texture.IsValid ? texture : depth );
		Viewport = new Num.Vector4( 0, 0, info.Width, info.Height );
	}

	/// <summary>Whether the colour target is written through its sRGB view (<see cref="SetTarget(TextureHandle, TextureHandle, bool)"/>).</summary>
	public bool SrgbWrite { get; private set; }

	/// <summary>The colour target's format as drawing sees it: its sRGB twin while <see cref="SrgbWrite"/>; none for depth alone.</summary>
	Format TargetFormat => !Target.IsValid ? Format.Unknown : SrgbWrite ? gpu.InfoOf( Target ).Format.Srgb() : gpu.InfoOf( Target ).Format;

	/// <summary>The targets' samples: a multisampled frame's when drawing inside one (a camera's stage).</summary>
	int TargetSamples => gpu.InfoOf( Target.IsValid ? Target : Depth ).Samples;

	/// <summary>Fill the whole target with <paramref name="color"/>. Nothing with depth alone bound.</summary>
	public void Clear( Num.Vector4 color )
	{
		if ( !Target.IsValid ) return;
		EndScope();
		TransitionForWrite( Target, ImageLayout.CopyDestination );
		List.ClearTexture( Target, color );
	}

	/// <summary>
	/// Clear the bound depth to 0 (the far plane: depth is reversed, as native's clears it) and its stencil to 0, as
	/// <c>Graphics.Clear</c> asked to clear depth or stencil does. Nothing without a depth bound.
	/// </summary>
	public void ClearDepth()
	{
		if ( !Depth.IsValid ) return;
		EndScope();
		TransitionForWrite( Depth, ImageLayout.DepthTarget );
		var depth = new DepthTarget { Texture = Depth, Layer = DepthSlice, Load = LoadOp.Clear, Store = StoreOp.Store };
		if ( Target.IsValid )
		{
			Transition( Target, ImageLayout.ColorTarget );
			List.BeginRendering( [new ColorTarget { Texture = Target, Load = LoadOp.Load, Store = StoreOp.Store, Srgb = SrgbWrite }], depth );
		}
		else List.BeginRendering( [], depth );
		List.EndRendering();
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
		if ( !ModeOf( shader, out var mode ) ) return false;
		var depth = BeginScope( shader, attributes, mode );
		Draws++;
		return drawer.DrawIndexedInstanced( List, shader, attributes, TargetFormat, depth, TargetSamples, indices, indexCount, instances, Viewport, mode );
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
		if ( !ModeOf( shader, out var mode ) ) return false;
		var depth = BeginScope( shader, attributes, mode );

		// Padded by a triangle's vertices: a draw that expands primitives reads each through per-instance streams, and a
		// device checking bounds by the stream's whole step would read the last one's other corners as zeros
		var upload = List.AllocateUpload( vertices.Length + 3 * layout.Stride, 16 );
		vertices.CopyTo( upload.Data );
		Draws++;

		// CPU vertices draw with the identity transform unless the draw sets its own, as native's do
		if ( attributes.Get( TransformBufferName ) is null ) attributes.Set( TransformBufferName, IdentityTransform() );
		return drawer.Draw( List, shader, attributes, TargetFormat, depth, TargetSamples, upload.Buffer, upload.Offset, layout, count, topology, Viewport, mode );
	}

	/// <summary>
	/// Draw <paramref name="count"/> vertices of <paramref name="layout"/> from <paramref name="vertices"/> at
	/// <paramref name="offset"/> into the target, as <see cref="DrawVertices"/> draws CPU vertices: for one that expands
	/// primitives, the buffer holds a primitive's worth past the last.
	/// </summary>
	public bool DrawBuffer( ShaderFile shader, ShaderAttributes attributes, BufferHandle vertices, long offset, VertexStreamLayout layout, int count, PrimitiveTopology topology )
	{
		if ( !ModeOf( shader, out var mode ) ) return false;
		var depth = BeginScope( shader, attributes, mode );
		Draws++;
		if ( attributes.Get( TransformBufferName ) is null ) attributes.Set( TransformBufferName, IdentityTransform() );
		return drawer.Draw( List, shader, attributes, TargetFormat, depth, TargetSamples, vertices, offset, layout, count, topology, Viewport, mode );
	}

	/// <summary>
	/// Draw <paramref name="instances"/> instances of a model's draw call into the target: <paramref name="indexCount"/>
	/// 32-bit indices from <paramref name="firstIndex"/>, its vertices of <paramref name="layout"/> in
	/// <paramref name="vertices"/> from <paramref name="baseVertex"/>, as <c>Graphics.DrawModelInstanced</c> draws a model.
	/// With the identity transform unless the draw sets its own, as <see cref="DrawVertices"/>.
	/// </summary>
	public bool DrawIndexed( ShaderFile shader, ShaderAttributes attributes, BufferHandle vertices, VertexStreamLayout layout, BufferHandle indices, int firstIndex, int indexCount, int baseVertex, int instances )
	{
		if ( !ModeOf( shader, out var mode ) ) return false;
		var depth = BeginScope( shader, attributes, mode );
		Draws++;
		if ( attributes.Get( TransformBufferName ) is null ) attributes.Set( TransformBufferName, IdentityTransform() );
		return drawer.DrawIndexed( List, shader, attributes, TargetFormat, depth, TargetSamples, vertices, layout, indices, firstIndex, indexCount, baseVertex, instances, Viewport, mode );
	}

	/// <summary>
	/// Draw <paramref name="draw"/>'s geometry into the target (<see cref="ShaderDrawer.Draw(CommandList, ShaderFile, ShaderAttributes, Format, Format, int, in GeometryDraw, Num.Vector4, string)"/>):
	/// what the engine's vertex-buffer, indexed and indirect draws lower to. With the identity transform unless the draw
	/// sets its own, as <see cref="DrawVertices"/>.
	/// </summary>
	public bool Draw( ShaderFile shader, ShaderAttributes attributes, in GeometryDraw draw )
	{
		if ( !ModeOf( shader, out var mode ) ) return false;
		var depth = BeginScope( shader, attributes, mode );
		Draws++;
		if ( attributes.Get( TransformBufferName ) is null ) attributes.Set( TransformBufferName, IdentityTransform() );
		return drawer.Draw( List, shader, attributes, TargetFormat, depth, TargetSamples, draw, Viewport, mode );
	}

	/// <summary>
	/// The mode a draw of <paramref name="shader"/> draws: its first (null) when no <see cref="Mode"/> is asked for, else that
	/// mode's name as the shader spells it. False when the shader has no such mode, and the draw is skipped.
	/// </summary>
	bool ModeOf( ShaderFile shader, out string mode )
	{
		mode = null;
		if ( Mode is null ) return true;
		foreach ( var candidate in shader.Modes )
		{
			if ( !string.Equals( candidate.Name, Mode, StringComparison.OrdinalIgnoreCase ) ) continue;
			mode = candidate.Name;
			return true;
		}

		// The error shader draws every mode with its one (ShaderFile.Mode)
		if ( !shader.IsErrorShader || shader.Modes.Count == 0 ) return false;
		mode = shader.Modes[0].Name;
		return true;
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

		// A multisampled target is resolved first, into a texture of its own size and format (blits take one sample)
		var source = Target;
		var info = gpu.InfoOf( Target );
		if ( info.Samples > 1 )
		{
			if ( !resolvedTarget.IsValid || gpu.InfoOf( resolvedTarget ) is var kept && (kept.Width != info.Width || kept.Height != info.Height || kept.Format != info.Format) )
			{
				if ( resolvedTarget.IsValid )
				{
					owned.Remove( resolvedTarget );
					layouts.Remove( resolvedTarget );
					gpu.Destroy( resolvedTarget );
				}

				resolvedTarget = gpu.CreateTexture( TextureInfo.RenderTarget( info.Width, info.Height, info.Format, name: "Resolved target" ) );
				layouts[resolvedTarget] = ImageLayout.Undefined;
				owned.Add( resolvedTarget );
			}

			TransitionForWrite( resolvedTarget, ImageLayout.CopyDestination );
			List.ResolveTexture( Target, resolvedTarget );
			Transition( resolvedTarget, ImageLayout.CopySource );
			source = resolvedTarget;
		}

		// A destination already in CopyDestination (cleared, or copied into) still needs its last write finished first
		TransitionForWrite( destination, ImageLayout.CopyDestination );
		List.Blit( source, ((int)Viewport.X, (int)Viewport.Y, width, height), destination, (0, 0, width, height), Filter.Nearest );
	}

	// A multisampled target's colour resolved for CopyTarget
	TextureHandle resolvedTarget;

	/// <summary>
	/// Run a compute shader outside any rendering scope. Textures it's given whole are put in
	/// <see cref="ImageLayout.General"/>, which it may both read and write. A texture it's given a mip of (a view) is put in
	/// General too, with the mips it samples in <see cref="ImageLayout.ShaderRead"/> for the dispatch, as sampled views are
	/// bound, and back in General after it (screen-space reflections read one mip of a texture and write another).
	/// </summary>
	public bool Dispatch( ShaderFile shader, ShaderAttributes attributes, int x, int y, int z )
	{
		PrepareDispatch( shader, attributes );
		var done = drawer.Dispatch( List, shader, attributes, x, y, z );
		EndDispatch();
		return done;
	}

	/// <summary>
	/// As <see cref="Dispatch"/>, over the thread groups <paramref name="arguments"/> holds at <paramref name="offset"/> bytes,
	/// written by an earlier pass (a barrier after that pass makes them visible, as native needs one).
	/// </summary>
	public bool DispatchIndirect( ShaderFile shader, ShaderAttributes attributes, BufferHandle arguments, long offset )
	{
		PrepareDispatch( shader, attributes );
		var done = drawer.DispatchIndirect( List, shader, attributes, arguments, offset );
		EndDispatch();
		return done;
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

		foreach ( var resource in variant is null ? default : variant.ResourceSpan )
		{
			if ( resource.Name is null ) continue;
			var value = attributes.Get( resource.Name );

			// A mip: its texture in General, a sampled one's mip read for the dispatch
			if ( value is TextureViewHandle view && viewMips.TryGetValue( view, out var mip ) )
			{
				dispatchLayouts[mip.Texture] = ImageLayout.General;
				if ( resource.Kind == ShaderResourceKind.Texture ) sampledMips.Add( mip );
				continue;
			}

			if ( value is not TextureHandle texture ) continue;
			if ( resource.Kind == ShaderResourceKind.StorageTexture ) dispatchLayouts[texture] = ImageLayout.General;
			else if ( resource.Kind == ShaderResourceKind.Texture && dispatchLayouts[texture] != ImageLayout.General ) dispatchLayouts[texture] = ImageLayout.ShaderRead;
		}

		foreach ( var (texture, layout) in dispatchLayouts )
		{
			Transition( texture, layout == ImageLayout.Undefined ? ImageLayout.General : layout );
			if ( layout != ImageLayout.ShaderRead ) dispatched.Add( texture );
		}

		foreach ( var (texture, mip) in sampledMips ) List.Barrier( MipBarrier( texture, mip, ImageLayout.General, ImageLayout.ShaderRead ) );
		dispatchLayouts.Clear();
	}

	/// <summary>The mips a dispatch sampled back in General with the rest of their textures.</summary>
	void EndDispatch()
	{
		foreach ( var (texture, mip) in sampledMips ) List.Barrier( MipBarrier( texture, mip, ImageLayout.ShaderRead, ImageLayout.General ) );
		sampledMips.Clear();
	}

	readonly List<(TextureHandle Texture, int Mip)> sampledMips = new();

	// Which texture and mip each mip view is of (MipView)
	readonly Dictionary<TextureViewHandle, (TextureHandle Texture, int Mip)> viewMips = new();

	readonly Dictionary<TextureHandle, ImageLayout> dispatchLayouts = new();

	// What dispatches left in General: a draw after them may sample them by bindless index (the terrain's normal map, baked
	// then drawn in one block), which no attribute shows, so they're readable before it
	readonly HashSet<TextureHandle> dispatched = new();

	/// <summary>Copy bytes into a buffer in order with the rest of the list, visible to everything after it.</summary>
	public void UpdateBuffer( BufferHandle buffer, ReadOnlySpan<byte> data, long offset )
	{
		EndScope();
		List.UpdateBuffer( buffer, data, offset );
		MemoryBarrier();
	}

	/// <summary>
	/// Copy <paramref name="size"/> bytes between buffers in order with the rest of the list: after every write before it,
	/// visible to everything after it.
	/// </summary>
	public void CopyBuffer( BufferHandle source, long sourceOffset, BufferHandle destination, long destinationOffset, long size )
	{
		MemoryBarrier();
		List.CopyBuffer( source, sourceOffset, destination, destinationOffset, size );
		MemoryBarrier();
	}

	/// <summary>
	/// Fill <paramref name="size"/> bytes of a buffer from <paramref name="offset"/> (a multiple of 4) with a repeated 32-bit
	/// value, in order with the rest of the list as <see cref="CopyBuffer"/> is.
	/// </summary>
	public void FillBuffer( BufferHandle buffer, uint value, long offset, long size )
	{
		MemoryBarrier();
		List.FillBuffer( buffer, value, offset, size );
		MemoryBarrier();
	}

	/// <summary>
	/// Copy <paramref name="size"/> bytes of <paramref name="source"/> into the start of <paramref name="readback"/> (a
	/// <see cref="MemoryLocation.Readback"/> buffer) after every write before it, visible to the host once the list has finished.
	/// </summary>
	public void CopyToReadback( BufferHandle source, long offset, BufferHandle readback, long size )
	{
		MemoryBarrier();
		List.CopyBuffer( source, offset, readback, 0, size );
		List.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
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

	// The attributes of this context's own passes (mips, the depth chain), emptied and reused for each: none overlaps another
	ShaderAttributes passAttributes;

	ShaderAttributes PassAttributes()
	{
		passAttributes ??= new ShaderAttributes();
		passAttributes.Clear();
		return passAttributes;
	}

	static readonly string[] MipNames = ["Mip0", "Mip1", "Mip2", "Mip3", "Mip4", "Mip5", "Mip6"];

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

		var attributes = PassAttributes();
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

		var attributes = PassAttributes();
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
			for ( int i = 1; i <= 6; i++ ) attributes.Set( MipNames[i], MipView( texture, source + Math.Min( i, mipCount ) ) );
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

		var attributes = PassAttributes();
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
			for ( int i = fused ? 1 : 0; i <= 6; i++ ) attributes.Set( MipNames[i], MipView( chain, source + Math.Min( i, mipCount ) ) );
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

	/// <summary>
	/// Copy mip <paramref name="sourceMip"/>, layer <paramref name="sourceLayer"/> of <paramref name="source"/> into mip
	/// <paramref name="destinationMip"/>, layer <paramref name="destinationLayer"/> of <paramref name="destination"/>, the
	/// same size and format there, as <c>Graphics.CopyTexture</c> does between mips and slices. Both are left in the copy's
	/// layouts, tracked.
	/// </summary>
	public void CopyTexture( TextureHandle source, int sourceMip, int sourceLayer, TextureHandle destination, int destinationMip, int destinationLayer )
	{
		EndScope();
		Transition( source, ImageLayout.CopySource );
		TransitionForWrite( destination, ImageLayout.CopyDestination );
		List.CopyTextureSubresource( source, sourceMip, sourceLayer, destination, destinationMip, destinationLayer );
	}

	/// <summary>
	/// Copy the viewport's part of the bound depth into <paramref name="destination"/> from its corner, as
	/// <c>Graphics.GrabDepthTexture</c> resolves the depth into a texture of its own. Both have the same format; multisampled
	/// depth into a single-sampled texture at least its size is resolved whole (sample zero, the viewport aside). False
	/// otherwise, and nothing is copied.
	/// </summary>
	public bool CopyDepth( TextureHandle destination )
	{
		if ( !Depth.IsValid ) throw new InvalidOperationException( "No depth is bound." );
		var from = gpu.InfoOf( Depth );
		var to = gpu.InfoOf( destination );
		if ( from.Format != to.Format ) return false;

		// Multisampled into one sample: resolved as a scope on the depth ends (sample zero), the whole of it
		if ( from.Samples > 1 && to.Samples <= 1 )
		{
			if ( to.Width < from.Width || to.Height < from.Height ) return false;
			EndScope();
			Transition( Depth, ImageLayout.DepthTarget );
			TransitionForWrite( destination, ImageLayout.DepthTarget );
			List.BeginRendering( [], new DepthTarget { Texture = Depth, Load = LoadOp.Load, Store = StoreOp.Store, ResolveTarget = destination } );
			List.EndRendering();
			return true;
		}

		if ( from.Samples != to.Samples ) return false;

		int x = (int)Viewport.X, y = (int)Viewport.Y;
		var width = Math.Min( (int)Viewport.Z, Math.Min( from.Width - x, to.Width ) );
		var height = Math.Min( (int)Viewport.W, Math.Min( from.Height - y, to.Height ) );
		EndScope();
		Transition( Depth, ImageLayout.CopySource );
		TransitionForWrite( destination, ImageLayout.CopyDestination );
		List.CopyTextureRegion( Depth, destination, x, y, 0, 0, width, height );
		return true;
	}

	/// <summary>A single mip of a texture as a view, made once, for binding one mip (sampled or as storage).</summary>
	public TextureViewHandle View( TextureHandle texture, int mip ) => MipView( texture, mip );

	/// <summary>
	/// Copy <paramref name="texture"/>'s mip 0 into <paramref name="readback"/> (a <see cref="MemoryLocation.Readback"/>
	/// buffer) and make it visible to the host once the list has finished.
	/// </summary>
	public void CopyToBuffer( TextureHandle texture, BufferHandle readback ) => CopyToBuffer( texture, readback, 0 );

	/// <summary>As <see cref="CopyToBuffer(TextureHandle, BufferHandle)"/>, for one mip.</summary>
	public void CopyToBuffer( TextureHandle texture, BufferHandle readback, int mip ) => CopyToBuffer( texture, readback, mip, 0 );

	/// <summary>As <see cref="CopyToBuffer(TextureHandle, BufferHandle)"/>, for one mip of one array layer.</summary>
	public void CopyToBuffer( TextureHandle texture, BufferHandle readback, int mip, int layer )
	{
		EndScope();
		Transition( texture, ImageLayout.CopySource );
		List.CopyTextureToBuffer( texture, mip, layer, readback );
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
		finishing.Clear();
		finishing.AddRange( layouts.Keys );
		foreach ( var texture in finishing ) Transition( texture, ImageLayout.ShaderRead );
		finishing.Clear();
	}

	// FinishReadable's textures: Transition writes the layouts it walks, every block
	readonly List<TextureHandle> finishing = new();

	/// <summary>Destroy the textures this context created.</summary>
	public void Release()
	{
		foreach ( var view in mipViews.Values ) gpu.Destroy( view );
		foreach ( var texture in owned ) gpu.Destroy( texture );
		if ( quadIndices.IsValid ) gpu.Destroy( quadIndices );
		if ( identityTransform.IsValid ) gpu.Destroy( identityTransform );
		mipViews.Clear();
		viewMips.Clear();
		owned.Clear();
		quadIndices = default;
		identityTransform = default;
	}

	BufferHandle identityTransform;
	const string TransformBufferName = "g_TransformBuffer";

	/// <summary>
	/// One <c>TransformBufferData_t</c> holding the identity (a 3x4 matrix, alpha 1, white tint): the transform a shader that
	/// reads <c>g_TransformBuffer</c> (gizmo lines, tool wireframes) draws CPU vertices with when the draw doesn't set one,
	/// as native's <c>Graphics.Draw</c> does.
	/// </summary>
	BufferHandle IdentityTransform()
	{
		if ( identityTransform.IsValid ) return identityTransform;
		identityTransform = gpu.CreateBuffer( new BufferInfo { Size = 64, Usage = BufferUsage.Storage } );
		gpu.WriteBuffer<uint>( identityTransform,
		[
			BitConverter.SingleToUInt32Bits( 1 ), 0, 0, 0,
			0, BitConverter.SingleToUInt32Bits( 1 ), 0, 0,
			0, 0, BitConverter.SingleToUInt32Bits( 1 ), 0,
			BitConverter.SingleToUInt32Bits( 1 ), 0xFFFFFF, 0, 0,
		] );
		return identityTransform;
	}

	TextureViewHandle MipView( TextureHandle texture, int mip )
	{
		if ( mipViews.TryGetValue( (texture, mip), out var view ) ) return view;
		view = gpu.CreateView( texture, new TextureViewInfo { Dimension = TextureDimension.Texture2D, BaseMip = mip, MipCount = 1, LayerCount = 1 } );
		mipViews[(texture, mip)] = view;
		viewMips[view] = (texture, mip);
		return view;
	}

	/// <summary>
	/// Make every texture a draw samples readable, then open a scope on the target - with the bound depth when the shader
	/// tests or writes depth, read-only when it only tests; always with it when it's bound alone. Returns the depth format the
	/// draw's pipeline needs.
	/// </summary>
	Format BeginScope( ShaderFile shader, ShaderAttributes attributes, string mode )
	{
		if ( !Target.IsValid && !Depth.IsValid ) throw new InvalidOperationException( "No target is bound." );

		var variant = Depth.IsValid ? drawer.Variant( shader, attributes, mode ) : null;
		var depth = variant?.State is { } state && (state.DepthTest || state.DepthWrite) ? (state.DepthWrite ? DepthUse.Write : DepthUse.ReadOnly) : DepthUse.None;
		if ( depth == DepthUse.None && !Target.IsValid ) depth = DepthUse.ReadOnly;

		if ( dispatched.Count > 0 ) ReadableAfterDispatches( attributes );

		foreach ( var (name, value) in attributes.Values )
		{
			if ( value is not TextureHandle texture || Layout( texture ) == ImageLayout.ShaderRead ) continue;

			// The targets may be among the attributes (a post process's view colour and depth) without the shader reading them
			if ( texture == Target || texture == Depth )
			{
				if ( !Reads( variant ?? drawer.Variant( shader, attributes, mode ), name ) ) continue;
				if ( texture == Target || depth != DepthUse.None ) throw new InvalidOperationException( $"{name} samples a target being drawn to." );
			}

			EndScope();
			Transition( texture, ImageLayout.ShaderRead );
		}

		if ( rendering && scopeDepth == depth ) return depth == DepthUse.None ? Format.Unknown : gpu.InfoOf( Depth ).Format;
		EndScope();

		var depthTarget = default( DepthTarget );
		if ( depth != DepthUse.None )
		{
			Transition( Depth, depth == DepthUse.Write ? ImageLayout.DepthTarget : ImageLayout.DepthReadOnly );
			depthTarget = new DepthTarget { Texture = Depth, Layer = DepthSlice, Load = LoadOp.Load, Store = StoreOp.Store, ReadOnly = depth == DepthUse.ReadOnly };
		}

		if ( Target.IsValid )
		{
			Transition( Target, ImageLayout.ColorTarget );
			List.BeginRendering( [new ColorTarget { Texture = Target, Load = LoadOp.Load, Store = StoreOp.Store, Srgb = SrgbWrite }], depthTarget );
		}
		else List.BeginRendering( [], depthTarget );
		rendering = true;
		scopeDepth = depth;
		return depth == DepthUse.None ? Format.Unknown : gpu.InfoOf( Depth ).Format;
	}

	/// <summary>
	/// Before a draw: what dispatches wrote goes to <see cref="ImageLayout.ShaderRead"/>, as a bindless descriptor samples it,
	/// except what the draw binds itself (its attributes' textures take the layouts their bindings want).
	/// </summary>
	void ReadableAfterDispatches( ShaderAttributes attributes )
	{
		foreach ( var texture in dispatched )
		{
			if ( Layout( texture ) == ImageLayout.ShaderRead || Binds( attributes, texture ) ) continue;
			EndScope();
			Transition( texture, ImageLayout.ShaderRead );
		}

		dispatched.Clear();
	}

	static bool Binds( ShaderAttributes attributes, TextureHandle texture )
	{
		foreach ( var (_, value) in attributes.Values )
		{
			if ( value is TextureHandle bound && bound == texture ) return true;
		}

		return false;
	}

	/// <summary>Whether a variant binds the texture set under <paramref name="name"/>.</summary>
	static bool Reads( ShaderVariant variant, string name )
	{
		foreach ( var resource in variant is null ? default : variant.ResourceSpan )
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
