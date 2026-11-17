using Borealis.Gpu;
using Borealis.Source2.Shaders;

namespace Borealis.Backend;

/// <summary>
/// The values a <see cref="ShaderDrawer"/> draw binds by name, as the engine's render attributes hold them: constants
/// as floats (a matrix as 16, in the engine's memory order: M11, M12 ... M44), structured buffers, textures and
/// constant buffers by attribute name, and dynamic combos by combo name. Names compare ignoring case, as the engine's
/// attribute tokens do.
/// </summary>
public sealed class ShaderAttributes
{
	readonly Dictionary<string, object> values = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, int> combos = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Constants: a number, a vector or a matrix, as floats.</summary>
	public void Set( string name, params float[] value ) => values[name] = value;

	/// <summary>A structured or byte-address buffer.</summary>
	public void Set( string name, BufferHandle buffer ) => values[name] = buffer;

	/// <summary>A texture's default view. Sampled, it has to be in <see cref="ImageLayout.ShaderRead"/>.</summary>
	public void Set( string name, TextureHandle texture ) => values[name] = texture;

	/// <summary>Another view of a texture: one mip, say. Its layout is the caller's to arrange.</summary>
	public void Set( string name, TextureViewHandle view ) => values[name] = view;

	/// <summary>A constant buffer's bytes.</summary>
	public void SetData( string name, byte[] data ) => values[name] = data;

	/// <summary>A dynamic combo (<c>D_*</c>).</summary>
	public void SetCombo( string name, int value ) => combos[name] = value;

	/// <summary>The value set under a name, or null.</summary>
	public object Get( string name ) => values.GetValueOrDefault( name );

	/// <summary>A combo's value, or null when it wasn't set.</summary>
	public int? GetCombo( string name ) => combos.TryGetValue( name, out var value ) ? value : null;

	/// <summary>Every value set, by name.</summary>
	public IEnumerable<KeyValuePair<string, object>> Values => values;

	/// <summary>Forget everything.</summary>
	public void Clear()
	{
		values.Clear();
		combos.Clear();
	}

	/// <summary>Add <paramref name="other"/>'s values and combos, replacing any of the same name.</summary>
	public void Merge( ShaderAttributes other )
	{
		foreach ( var (name, value) in other.values ) values[name] = value;
		foreach ( var (name, value) in other.combos ) combos[name] = value;
	}

	internal string ComboKey( ShaderFile shader )
	{
		var key = new System.Text.StringBuilder();
		foreach ( var type in (ReadOnlySpan<ShaderProgramType>)[ShaderProgramType.Vertex, ShaderProgramType.Pixel, ShaderProgramType.Compute] )
		{
			foreach ( var combo in shader[type]?.DynamicCombos ?? [] )
			{
				if ( GetCombo( combo.Name ) is int value && value != 0 ) key.Append( combo.Name ).Append( '=' ).Append( value ).Append( ';' );
			}
		}

		return key.ToString();
	}
}

/// <summary>
/// One field of a vertex stream, matched to shader inputs by semantic and index as native's input layouts are.
/// </summary>
public readonly record struct VertexField( VertexSemantic Semantic, int Index, VertexFormat Format, int Offset );

/// <summary>
/// The layout of a vertex stream a <see cref="ShaderDrawer"/> draw reads. Inputs it has no field for read the default
/// stream. Pipelines are cached per layout object, so keep one instance per layout.
/// </summary>
public sealed class VertexStreamLayout( int stride, params VertexField[] fields )
{
	/// <summary>Bytes per vertex.</summary>
	public int Stride { get; } = stride;

	/// <summary>Its fields.</summary>
	public IReadOnlyList<VertexField> Fields { get; } = fields;

	/// <summary>
	/// The engine's <c>Vertex</c> (76 bytes): position, colour (bytes), normal, two uvs and a tangent. What
	/// <c>Graphics.DrawQuad</c> draws.
	/// </summary>
	public static VertexStreamLayout EngineVertex { get; } = new( 76,
		new( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
		new( VertexSemantic.Color, 0, VertexFormat.UByte4Norm, 12 ),
		new( VertexSemantic.Normal, 0, VertexFormat.Float3, 16 ),
		new( VertexSemantic.TexCoord, 0, VertexFormat.Float4, 28 ),
		new( VertexSemantic.TexCoord, 1, VertexFormat.Float4, 44 ),
		new( VertexSemantic.Tangent, 0, VertexFormat.Float4, 60 ) );

	internal VertexField? Find( in VertexInput input ) => Find( input.Semantic, input.SemanticIndex );

	internal VertexField? Find( VertexSemantic semantic, int index )
	{
		foreach ( var field in Fields )
		{
			if ( field.Semantic == semantic && field.Index == index ) return field;
		}

		return null;
	}
}

/// <summary>
/// What a <see cref="ShaderDrawer"/> draw reads and how many: vertices of <see cref="Layout"/> from a buffer (or none, every
/// input reading the default stream), indices from a buffer or none, and the counts either here or in an indirect
/// arguments buffer, as the engine's <c>Graphics.Draw</c>, <c>DrawIndexed</c> and their indirect forms take them.
/// </summary>
public struct GeometryDraw
{
	/// <summary>The vertex buffer, read from <see cref="VertexOffset"/> bytes; none reads the default stream.</summary>
	public BufferHandle Vertices;

	/// <summary>Bytes into <see cref="Vertices"/> its vertex 0 is.</summary>
	public long VertexOffset;

	/// <summary>The vertices' layout; needed with <see cref="Vertices"/>.</summary>
	public VertexStreamLayout Layout;

	/// <summary>The index buffer, read from <see cref="IndexOffset"/> bytes; none draws vertices in order.</summary>
	public BufferHandle Indices;

	/// <summary>Bytes into <see cref="Indices"/> its index 0 is.</summary>
	public long IndexOffset;

	/// <summary>16- or 32-bit indices.</summary>
	public IndexFormat IndexFormat;

	/// <summary>What the vertices make.</summary>
	public PrimitiveTopology Topology;

	/// <summary>Vertices (or indices) per instance, from <see cref="First"/>.</summary>
	public int Count;

	/// <summary>The first vertex, or index.</summary>
	public int First;

	/// <summary>Added to each index.</summary>
	public int BaseVertex;

	/// <summary>Instances; 0 draws one.</summary>
	public int Instances;

	/// <summary>
	/// Arguments the counts are read from instead (four 32-bit values a draw, five indexed), <see cref="Stride"/> bytes
	/// apart from <see cref="ArgumentsOffset"/>, <see cref="DrawCount"/> of them.
	/// </summary>
	public BufferHandle Arguments;

	/// <summary>Bytes into <see cref="Arguments"/> the first draw's are.</summary>
	public long ArgumentsOffset;

	/// <summary>Draws read from <see cref="Arguments"/>.</summary>
	public int DrawCount;

	/// <summary>Bytes between draws' arguments; 0 packs them.</summary>
	public int Stride;
}

/// <summary>
/// Draws and dispatches with one of s&amp;box's compiled shaders and no material, binding every resource the shader
/// reflects by the name the engine sets it under, the way the engine's <c>Graphics</c> draws bind render attributes:
/// what a <c>Graphics</c> or <c>CommandList</c> draw (UI, post processing, debug drawing) lowers to on Borealis.Gpu.
/// Draws read vertices from a stream, or none (every input reads the default stream).
/// </summary>
/// <remarks>
/// It binds through the same material objects as Borealis's scene (<see cref="BackendContext"/>): a shader drawn a way is
/// an object holding its pipeline and persistent sets, with every named resource from the draw's attributes and its
/// material constants written each draw into the frame's pass constants. Made over a <see cref="BackendDevice"/>, it
/// shares that device's objects. <see cref="MaterialObjects"/> off binds by reflection every draw instead, which is the
/// reference the tests compare against; a draw also falls back to it when the frame's pass constants are full.
/// </remarks>
public sealed class ShaderDrawer : IDisposable
{
	readonly Device gpu;
	readonly ShaderLibrary shaders;
	readonly ShaderDefaults defaults;
	readonly Dictionary<(ShaderFile Shader, string Mode, string Combos), ShaderVariant> variants = new();
	readonly Dictionary<(ShaderVariant Variant, Format Color, Format Depth, int Samples, VertexStreamLayout Layout, PrimitiveTopology Topology, bool Ids), PipelineHandle> pipelines = new();
	readonly Dictionary<ShaderVariant, PipelineHandle> computePipelines = new();
	MaterialObjectCache cache;
	readonly bool ownsCache;
	readonly AttributeBindings bindings;

	/// <summary>
	/// A drawer over a shader library and the placeholders bound for anything a draw doesn't set, with material objects
	/// of its own.
	/// </summary>
	public ShaderDrawer( Device gpu, ShaderLibrary shaders, ShaderDefaults defaults )
	{
		this.gpu = gpu;
		this.shaders = shaders;
		this.defaults = defaults;
		ownsCache = true;
		bindings = new AttributeBindings( this );
	}

	/// <summary>
	/// A drawer sharing <paramref name="device"/>'s shaders, placeholders and material objects: one material binding
	/// service for the engine's drawing and Borealis's scene on that device.
	/// </summary>
	public ShaderDrawer( BackendDevice device )
	{
		gpu = device.Gpu;
		shaders = device.Shaders;
		defaults = device.Defaults;
		cache = device.MaterialCache;
		bindings = new AttributeBindings( this );
	}

	/// <summary>The library shaders come from.</summary>
	public ShaderLibrary Shaders => shaders;

	/// <summary>
	/// Bind through material objects (the default). Off binds by reflection every draw: a transient set per draw, written
	/// from scratch.
	/// </summary>
	public bool MaterialObjects { get; set; } = true;

	MaterialObjectCache Cache => cache ??= new MaterialObjectCache( gpu, defaults );

	/// <summary>Material objects made, descriptor sets written through them, and sets held now, for tests and benchmarks.</summary>
	public (int Objects, int SetsWritten, int SetsHeld) MaterialObjectStats => cache is null ? default : (cache.Count, cache.SetsWritten, cache.SetCount);

	/// <summary>
	/// The instance id stream draws read <c>TEXCOORD13</c> (<c>InstanceTransformUv</c>) from, per instance, from an offset in
	/// bytes: a frame's, where a custom object draws inside it, whose shaders index <c>g_TransformBuffer</c> with it as
	/// native's <c>CSceneSystem::BindTransformSlot</c> has them do. None (the default) reads the default stream.
	/// </summary>
	public (BufferHandle Buffer, long Offset) InstanceIds { get; set; }

	/// <summary>
	/// The material draws of its shader take their features (<c>F_*</c>, which pick the variant), constants and textures from,
	/// as native binds a material's mode: what a <c>Graphics</c> draw with a material that has a compiled file reads. Null (the
	/// default) draws the shader with its defaults. Draws of another shader ignore it.
	/// </summary>
	internal BackendMaterial Material { get; set; }

	BackendMaterial MaterialOf( ShaderFile shader ) => Material is { } material && ReferenceEquals( material.Shader, shader ) ? material : null;

	/// <summary>A material's texture for a material variable, when it's ready to sample and the shape the shader expects.</summary>
	Binding MaterialTexture( BackendMaterial material, in ShaderResource resource )
	{
		var expected = resource.Dimension switch
		{
			SpirvImageDimension.Cube => TextureDimension.TextureCube,
			SpirvImageDimension.CubeArray => TextureDimension.TextureCubeArray,
			SpirvImageDimension.Texture3D => TextureDimension.Texture3D,
			SpirvImageDimension.Texture2DArray => TextureDimension.Texture2DArray,
			_ => TextureDimension.Texture2D,
		};

		if ( !material.Textures.TryGetValue( resource.Variable.Name, out var texture ) || texture.Dimension != expected || texture.Samples > 1 ) return defaults.For( resource );
		return Binding.Sampled( resource.Binding, texture.Handle, resource.Variable.RenderStateId == 1 && gpu.InfoOf( texture.Handle ).Format.HasSrgb() );
	}

	// Material objects key their pipelines by layout object: a layout with instance ids is another one, so it has a key of its own
	readonly Dictionary<VertexStreamLayout, object> idsLayouts = new();
	readonly object noLayoutIds = new();

	object LayoutKey( VertexStreamLayout layout, bool ids )
	{
		if ( !ids ) return layout;
		if ( layout is null ) return noLayoutIds;
		if ( !idsLayouts.TryGetValue( layout, out var key ) ) idsLayouts[layout] = key = new object();
		return key;
	}

	static bool IsInstanceId( in VertexInput input ) => input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex == 13;

	// Whether a draw of the variant reads the lent instance ids. One that expands primitives reads the first id for every
	// primitive (its instances are primitives): one object's draw, as native binds one transform slot for it
	bool ReadsInstanceIds( ShaderVariant variant )
	{
		if ( !InstanceIds.Buffer.IsValid ) return false;
		foreach ( var input in variant.VertexInputs )
		{
			if ( IsInstanceId( input ) ) return true;
		}

		return false;
	}

	/// <summary>Draws that fell back to binding by reflection because the frame's pass constants were full.</summary>
	public int Fallbacks { get; private set; }

	/// <summary>Free the material objects this drawer made for itself (not those of a device it shares).</summary>
	public void Dispose()
	{
		if ( ownsCache ) cache?.Dispose();
		cache = null;
	}

	/// <summary>
	/// The shader's variant for its first mode (or <paramref name="mode"/>) and the attributes' dynamic combos.
	/// </summary>
	public ShaderVariant Variant( ShaderFile shader, ShaderAttributes attributes, string mode = null )
	{
		mode ??= shader.Modes[0].Name;
		var features = MaterialOf( shader )?.Features ?? [];
		var combos = attributes.ComboKey( shader );
		if ( features.Length > 0 ) combos = string.Concat( combos, "|", string.Join( ',', features ) );
		var key = (shader, mode, combos);
		if ( variants.TryGetValue( key, out var variant ) ) return variant;

		// Not kept while its module compiles: its programs draw until then
		variant = shaders.Variant( shader, mode, features, attributes.GetCombo, name => attributes.Get( name ) is float[] { Length: > 0 } f ? (int)f[0] : null, false, out var pending );
		if ( !pending ) variants[key] = variant;
		return variant;
	}

	/// <summary>
	/// Draw <paramref name="instances"/> instances of <paramref name="indexCount"/> indices with
	/// <paramref name="shader"/> inside the list's open rendering scope, into a colour target of
	/// <paramref name="color"/>. <paramref name="viewport"/> is what the shader reads as <c>Source( Viewport )</c>
	/// (x, y, width, height); it's also set as the viewport and scissor.
	/// </summary>
	public bool DrawIndexedInstanced( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, int samples, BufferHandle indices, int indexCount, int instances, System.Numerics.Vector4 viewport, string mode = null )
		=> DrawIndexedInstanced( list, shader, attributes, color, Format.Unknown, samples, indices, indexCount, instances, viewport, mode );

	/// <summary>
	/// As the overload without <paramref name="depth"/>, in a scope with a depth target of that format
	/// (<see cref="Format.Unknown"/> for none), which the shader's depth state tests and writes.
	/// </summary>
	public bool DrawIndexedInstanced( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, BufferHandle indices, int indexCount, int instances, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, null, PrimitiveTopology.TriangleList, viewport );
		list.SetVertexBuffer( 0, defaults.VertexDefaults );
		if ( ids ) list.SetVertexBuffer( 1, InstanceIds.Buffer, InstanceIds.Offset );
		list.SetIndexBuffer( indices, IndexFormat.UInt32 );
		list.DrawIndexed( indexCount, instances );
		return true;
	}

	/// <summary>
	/// Draw <paramref name="vertexCount"/> vertices of <paramref name="layout"/> from <paramref name="vertices"/> at
	/// <paramref name="offset"/>, as <paramref name="topology"/>, inside the list's open rendering scope. The rest is as
	/// <see cref="DrawIndexedInstanced(CommandList, ShaderFile, ShaderAttributes, Format, int, BufferHandle, int, int, System.Numerics.Vector4, string)"/>.
	/// </summary>
	public bool Draw( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, int samples, BufferHandle vertices, long offset, VertexStreamLayout layout, int vertexCount, PrimitiveTopology topology, System.Numerics.Vector4 viewport, string mode = null )
		=> Draw( list, shader, attributes, color, Format.Unknown, samples, vertices, offset, layout, vertexCount, topology, viewport, mode );

	/// <summary>
	/// As the overload without <paramref name="depth"/>, in a scope with a depth target of that format
	/// (<see cref="Format.Unknown"/> for none), which the shader's depth state tests and writes.
	/// </summary>
	public bool Draw( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, BufferHandle vertices, long offset, VertexStreamLayout layout, int vertexCount, PrimitiveTopology topology, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		// A vertex stage that expands primitives (where the .shader had a geometry stage) runs its vertices once per
		// primitive, an instance each, reading the primitive's vertices as per-instance streams 0, 2 and 3
		var expansion = variant.Expansion;
		var instances = 1;
		if ( expansion.Expands )
		{
			instances = expansion.Primitives( topology, vertexCount );
			if ( instances < 0 || layout is null ) return false;
			if ( instances == 0 ) return true;
		}

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, layout, topology, viewport );
		list.SetVertexBuffer( 0, vertices, offset );
		list.SetVertexBuffer( 1, defaults.VertexDefaults );
		if ( !expansion.Expands )
		{
			if ( ids ) list.SetVertexBuffer( 2, InstanceIds.Buffer, InstanceIds.Offset );
			list.Draw( vertexCount );
			return true;
		}

		for ( int corner = 1; corner < expansion.Corners; corner++ ) list.SetVertexBuffer( 1 + corner, vertices, offset + corner * layout.Stride );
		if ( ids ) list.SetVertexBuffer( 1 + expansion.Corners, InstanceIds.Buffer, InstanceIds.Offset );
		list.Draw( expansion.Vertices, instances );
		return true;
	}

	/// <summary>
	/// Draw <paramref name="instances"/> instances of <paramref name="indexCount"/> 32-bit indices from
	/// <paramref name="firstIndex"/> in <paramref name="indices"/>, reading vertices of <paramref name="layout"/> from
	/// <paramref name="vertices"/> offset by <paramref name="baseVertex"/>, as triangles: a model's draw call, as
	/// <c>Graphics.DrawModelInstanced</c> draws it. False for a variant that expands primitives. The rest is as
	/// <see cref="Draw(CommandList, ShaderFile, ShaderAttributes, Format, Format, int, BufferHandle, long, VertexStreamLayout, int, PrimitiveTopology, System.Numerics.Vector4, string)"/>.
	/// </summary>
	public bool DrawIndexed( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, BufferHandle vertices, VertexStreamLayout layout, BufferHandle indices, int firstIndex, int indexCount, int baseVertex, int instances, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null || variant.Expansion.Expands ) return false;

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, layout, PrimitiveTopology.TriangleList, viewport );
		list.SetVertexBuffer( 0, vertices );
		list.SetVertexBuffer( 1, defaults.VertexDefaults );
		if ( ids ) list.SetVertexBuffer( 2, InstanceIds.Buffer, InstanceIds.Offset );
		list.SetIndexBuffer( indices, IndexFormat.UInt32 );
		list.DrawIndexed( indexCount, instances, firstIndex, baseVertex );
		return true;
	}

	/// <summary>
	/// Draw <paramref name="draw"/>'s geometry inside the list's open rendering scope. A variant that expands primitives
	/// draws only vertices from a buffer, unindexed, one instance, with the counts here: false for the rest. The rest is as
	/// <see cref="Draw(CommandList, ShaderFile, ShaderAttributes, Format, Format, int, BufferHandle, long, VertexStreamLayout, int, PrimitiveTopology, System.Numerics.Vector4, string)"/>.
	/// </summary>
	public bool Draw( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, in GeometryDraw draw, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		var layout = draw.Vertices.IsValid ? draw.Layout : null;
		var indirect = draw.Arguments.IsValid;
		var indexed = draw.Indices.IsValid;
		if ( variant.Expansion.Expands )
		{
			if ( layout is null || indexed || indirect || draw.Instances > 1 ) return false;
			return Draw( list, shader, attributes, color, depth, samples, draw.Vertices, draw.VertexOffset + (long)draw.First * layout.Stride, layout, draw.Count, draw.Topology, viewport, mode );
		}

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, layout, draw.Topology, viewport );
		if ( layout is null )
		{
			list.SetVertexBuffer( 0, defaults.VertexDefaults );
			if ( ids ) list.SetVertexBuffer( 1, InstanceIds.Buffer, InstanceIds.Offset );
		}
		else
		{
			list.SetVertexBuffer( 0, draw.Vertices, draw.VertexOffset );
			list.SetVertexBuffer( 1, defaults.VertexDefaults );
			if ( ids ) list.SetVertexBuffer( 2, InstanceIds.Buffer, InstanceIds.Offset );
		}

		if ( indexed ) list.SetIndexBuffer( draw.Indices, draw.IndexFormat, draw.IndexOffset );

		var instances = Math.Max( draw.Instances, 1 );
		if ( indirect && indexed ) list.DrawIndexedIndirect( draw.Arguments, draw.ArgumentsOffset, draw.DrawCount, draw.Stride > 0 ? draw.Stride : 20 );
		else if ( indirect ) list.DrawIndirect( draw.Arguments, draw.ArgumentsOffset, draw.DrawCount, draw.Stride > 0 ? draw.Stride : 16 );
		else if ( indexed ) list.DrawIndexed( draw.Count, instances, draw.First, draw.BaseVertex );
		else list.Draw( draw.Count, instances, draw.First );
		return true;
	}

	// Bind a draw's pipeline and resources; true when it reads the lent instance ids, which the caller binds after its streams
	bool BindForDraw( CommandList list, ShaderVariant variant, ShaderAttributes attributes, Format color, Format depth, int samples, VertexStreamLayout layout, PrimitiveTopology topology, System.Numerics.Vector4 viewport )
	{
		var ids = ReadsInstanceIds( variant );
		if ( Objects( variant ) )
		{
			var key = new MaterialObjectCache.Key( null, variant, LayoutKey( layout, ids ), color, depth, samples, false, false, topology );
			var materialObject = cache.Get( key, immediate: true, (Drawer: this, Variant: variant, Color: color, Depth: depth, Samples: samples, Layout: layout, Topology: topology, Ids: ids),
				static ( s, materialLayout ) => s.Drawer.Pipeline( s.Variant, s.Color, s.Depth, s.Samples, s.Layout, s.Topology, materialLayout.Sets, s.Ids ) );
			SetViewport( list, viewport );
			BindObject( list, materialObject, attributes, viewport );
		}
		else
		{
			list.SetPipeline( Pipeline( variant, color, depth, samples, layout, topology, null, ids ) );
			SetViewport( list, viewport );
			Bind( list, variant, attributes, viewport );
		}

		// The stencil reference is dynamic state: a shader that tests or writes stencil (gizmos, tool overlays) sets its own
		if ( variant.State.StencilTest && depth.HasStencil() ) list.SetStencilReference( (byte)variant.State.StencilReference );

		// So is the depth bias drawing into depth alone (a shadow map's)
		if ( color == Format.Unknown && depth != Format.Unknown ) list.SetDepthBias( DepthBias.Constant, DepthBias.Slope );
		return ids;
	}

	/// <summary>Whether this draw binds through material objects: they're on, and the frame's pass constants have room for it.</summary>
	bool Objects( ShaderVariant variant )
	{
		if ( !MaterialObjects ) return false;
		if ( Cache.PassSpace >= cache.LayoutOf( variant, immediate: true ).ImmediateBytes ) return true;
		Fallbacks++;
		return false;
	}

	void BindObject( CommandList list, MaterialObject materialObject, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		if ( bindings.Slot < 0 ) bindings.Slot = cache.NewSlot();
		bindings.Attributes = attributes;
		bindings.Viewport = viewport;
		var bound = default( PipelineHandle );
		materialObject.Bind( list, ref bound, cache.NextEpoch(), bindings );
		bindings.Attributes = null;
	}

	/// <summary>
	/// Run a compute shader over <paramref name="x"/> by <paramref name="y"/> by <paramref name="z"/> threads (divided
	/// by its thread group size, as native does), outside any rendering scope. Storage textures bind by attribute too.
	/// </summary>
	public bool Dispatch( CommandList list, ShaderFile shader, ShaderAttributes attributes, int x, int y, int z, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Compute?.Bytecode is null ) return false;

		BindForDispatch( list, variant, attributes );
		var (gx, gy, gz) = variant.Compute.Bytecode.ThreadGroupSize;
		gx = Math.Max( gx, 1 );
		gy = Math.Max( gy, 1 );
		gz = Math.Max( gz, 1 );
		list.Dispatch( (x + gx - 1) / gx, (y + gy - 1) / gy, (z + gz - 1) / gz );
		return true;
	}

	/// <summary>
	/// Run a compute shader over the thread groups (not threads) that <paramref name="arguments"/> holds at
	/// <paramref name="offset"/> bytes - three 32-bit counts, written by an earlier pass - as native's indirect dispatch does.
	/// The rest is as <see cref="Dispatch"/>.
	/// </summary>
	public bool DispatchIndirect( CommandList list, ShaderFile shader, ShaderAttributes attributes, BufferHandle arguments, long offset, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Compute?.Bytecode is null ) return false;

		BindForDispatch( list, variant, attributes );
		list.DispatchIndirect( arguments, offset );
		return true;
	}

	void BindForDispatch( CommandList list, ShaderVariant variant, ShaderAttributes attributes )
	{
		if ( Objects( variant ) )
		{
			var key = new MaterialObjectCache.Key( null, variant, null, Format.Unknown, Format.Unknown, 0, false, false );
			var materialObject = cache.Get( key, immediate: true, (Drawer: this, Variant: variant), static ( s, layout ) => s.Drawer.ComputePipeline( s.Variant, layout.Sets ) );
			BindObject( list, materialObject, attributes, default );
		}
		else
		{
			list.SetPipeline( ComputePipeline( variant, null ) );
			Bind( list, variant, attributes, default );
		}
	}

	/// <summary>
	/// The compute pipeline a dispatch of <paramref name="variant"/> binds by reflection with (made once), for a caller that
	/// fills the variant's transient sets itself.
	/// </summary>
	internal PipelineHandle ReflectedComputePipeline( ShaderVariant variant ) => ComputePipeline( variant, null );

	/// <summary>A compute pipeline for the reflection path (cached here) or for a material object's sets (the object keeps it).</summary>
	PipelineHandle ComputePipeline( ShaderVariant variant, BindingLayoutHandle[] sets )
	{
		if ( sets is null && computePipelines.TryGetValue( variant, out var cached ) ) return cached;

		var pipeline = gpu.CreateComputePipeline( new ComputePipelineInfo
		{
			Shader = variant.Compute.Module,
			Entry = variant.Compute.Bytecode.EntryPoint( ShaderProgramType.Compute ),
			Constants = variant.Compute.Constants,
			Sets = sets ?? variant.Sets,
			Name = variant.ToString(),
		} );

		if ( sets is null ) computePipelines[variant] = pipeline;
		return pipeline;
	}

	/// <summary>
	/// The depth range draws map to, 0 to 1 by default: a frame's own where drawing lands inside it (a custom object in one of
	/// Borealis's layers, whose view may keep part of the range for its 3D skybox).
	/// </summary>
	public (float Min, float Max) DepthRange { get; set; } = (0, 1);

	/// <summary>
	/// The rasterizer depth bias of draws into depth alone, none by default: a shadow map's, as native's shadow layers override
	/// their draws' (<c>RsDepthBiasStateOverride_t</c>). Draws with colour have none.
	/// </summary>
	public (int Constant, float Slope) DepthBias { get; set; }

	void SetViewport( CommandList list, System.Numerics.Vector4 viewport )
	{
		list.SetViewport( viewport.X, viewport.Y, viewport.Z, viewport.W, DepthRange.Min, DepthRange.Max );
		list.SetScissor( (int)viewport.X, (int)viewport.Y, (int)MathF.Ceiling( viewport.Z ), (int)MathF.Ceiling( viewport.W ) );
	}

	/// <summary>A graphics pipeline for the reflection path (cached here) or for a material object's sets (the object keeps it).</summary>
	PipelineHandle Pipeline( ShaderVariant variant, Format color, Format depth, int samples, VertexStreamLayout layout, PrimitiveTopology topology, BindingLayoutHandle[] sets, bool ids = false )
	{
		var key = (variant, color, depth, samples, layout, topology, ids);
		if ( sets is null && pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

		// Inputs the stream lacks (or every input, with no stream) read the default stream. No colour format draws into
		// depth alone. With instance ids, TEXCOORD13 reads them from a stream after the default one, per instance
		GraphicsPipelineInfo info;
		Format[] colors = color == Format.Unknown ? [] : [color];

		// Into depth alone, the depth bias is dynamic (DepthBias)
		var depthBias = color == Format.Unknown && depth != Format.Unknown;
		var idsStream = new VertexBufferLayout( sizeof( uint ), VertexStepRate.Instance );
		if ( layout is null )
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input => ids && IsInstanceId( input ) ? new VertexAttribute( 0, 1, VertexFormat.UInt, 0 ) : null, defaultBuffer: 0 );
			info = variant.PipelineInfo( ids ? [new VertexBufferLayout( 0 ), idsStream] : [new VertexBufferLayout( 0 )], attributes, colors, depth, samples, depthBias, null );
		}
		else if ( variant.Expansion is { Expands: true } expansion )
		{
			// One instance per primitive: its first vertex is stream 0, its others streams 2 and 3, each stepping a
			// primitive's worth of vertices per instance; an input reads its vertex's field at its semantic index less 4 a corner.
			// With instance ids, TEXCOORD13 reads the first from the stream after those, which doesn't step
			var step = new VertexBufferLayout( layout.Stride * expansion.Step( topology ), VertexStepRate.Instance );
			var idStream = 1 + expansion.Corners;
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
			{
				if ( ids && IsInstanceId( input ) ) return new VertexAttribute( 0, idStream, VertexFormat.UInt, 0 );
				var corner = expansion.Corner( input );
				return layout.Find( input.Semantic, expansion.SemanticIndex( input ) ) is { } field && SameKind( field, input ) ? new VertexAttribute( 0, corner == 0 ? 0 : 1 + corner, field.Format, field.Offset ) : null;
			}, defaultBuffer: 1 );
			VertexBufferLayout[] streams = expansion.Corners switch { 3 => [step, new( 0 ), step, step], 2 => [step, new( 0 ), step], _ => [step, new( 0 )] };
			if ( ids ) streams = [.. streams, new VertexBufferLayout( 0, VertexStepRate.Instance )];
			info = variant.PipelineInfo( streams, attributes, colors, depth, samples, depthBias, null );
			topology = expansion.Topology;
		}
		else
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
				ids && IsInstanceId( input ) ? new VertexAttribute( 0, 2, VertexFormat.UInt, 0 )
				: layout.Find( input ) is { } field && SameKind( field, input ) ? new VertexAttribute( 0, 0, Signed( field.Format, input ), field.Offset ) : null, defaultBuffer: 1 );
			info = variant.PipelineInfo( ids ? [new VertexBufferLayout( layout.Stride ), new VertexBufferLayout( 0 ), idsStream] : [new VertexBufferLayout( layout.Stride ), new VertexBufferLayout( 0 )], attributes, colors, depth, samples, depthBias, null );
		}

		info.Topology = topology;
		if ( sets is not null ) info.Sets = sets;
		pipeline = gpu.CreateGraphicsPipeline( info );
		if ( sets is null ) pipelines[key] = pipeline;
		return pipeline;
	}

	// A field the shader reads as the other kind (integers as floats, or the other way round) reads the default stream, as a
	// model's draws do (BackendDevice.Pipeline)
	static bool SameKind( in VertexField field, in VertexInput input )
		=> (field.Format is VertexFormat.UByte4 or VertexFormat.UInt or VertexFormat.UInt2 or VertexFormat.UInt4 or VertexFormat.Int or VertexFormat.UByte or VertexFormat.Byte) == (input.Kind != SpirvScalar.Float);

	// A 32-bit integer field takes the signedness the shader reads it with: the bits are the same
	static VertexFormat Signed( VertexFormat format, in VertexInput input ) => format switch
	{
		VertexFormat.UInt when input.Kind == SpirvScalar.Int => VertexFormat.Int,
		VertexFormat.Int when input.Kind == SpirvScalar.UInt => VertexFormat.UInt,
		_ => format,
	};

	/// <summary>
	/// The binding a reflected resource gets from a draw's attribute value, or for a material variable the material's
	/// texture (<see cref="Material"/>), or its placeholder.
	/// </summary>
	Binding Resource( in ShaderResource resource, object value ) => resource.Kind switch
	{
		ShaderResourceKind.Texture when resource.Variable?.Source == VariableSource.Material && Material is { } material => MaterialTexture( material, resource ),
		ShaderResourceKind.Buffer when value is BufferHandle buffer => Binding.Storage( resource.Binding, buffer ),
		ShaderResourceKind.Texture when value is TextureHandle texture => Binding.Sampled( resource.Binding, texture, resource.Variable.RenderStateId == 1 && gpu.InfoOf( texture ).Format.HasSrgb() ),
		ShaderResourceKind.Texture when value is TextureViewHandle view => Binding.Sampled( resource.Binding, view ),
		ShaderResourceKind.StorageTexture when value is TextureHandle texture => Binding.StorageTexture( resource.Binding, texture ),
		ShaderResourceKind.StorageTexture when value is TextureViewHandle view => Binding.StorageTexture( resource.Binding, view ),
		ShaderResourceKind.Counter when value is BufferHandle counter => Binding.Storage( resource.Binding, counter ),
		_ => defaults.For( resource ),
	};

	/// <summary>
	/// The attribute value a resource binds: the one set under its name, or for a buffer's hidden counter, under the counter's
	/// name (<see cref="ShaderResource.CounterName"/>).
	/// </summary>
	object Value( in ShaderResource resource, ShaderAttributes attributes )
	{
		if ( resource.Name is not { } name ) return null;
		if ( resource.Kind != ShaderResourceKind.Counter ) return attributes.Get( name );
		if ( !counterNames.TryGetValue( name, out var counter ) ) counterNames[name] = counter = ShaderResource.CounterName( name );
		return attributes.Get( counter );
	}

	readonly Dictionary<string, string> counterNames = new();

	void Bind( CommandList list, ShaderVariant variant, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		Span<Binding> bindings = stackalloc Binding[64];
		for ( int set = 0; set < variant.Sets.Length; set++ )
		{
			if ( !variant.Sets[set].IsValid ) continue;

			var count = 0;
			foreach ( var resource in variant.Resources )
			{
				if ( resource.Set != set ) continue;
				if ( count == bindings.Length ) throw new InvalidOperationException( $"{variant} binds more than {bindings.Length} resources in set {set}" );

				var stage = set == ShaderAbi.FragmentSet ? variant.Pixel : variant.Compute ?? variant.Vertex;
				var value = Value( resource, attributes );
				bindings[count++] = resource.Kind switch
				{
					ShaderResourceKind.MaterialConstants => Constants( list, stage, resource.Binding, attributes, viewport ),
					ShaderResourceKind.ConstantBuffer when value is byte[] data => ConstantBuffer( list, resource.Binding, data, stage, resource.Name ),
					_ => Resource( resource, value ),
				};
			}

			list.SetBindings( set, variant.Sets[set], bindings[..count] );
		}
	}

	Binding Constants( CommandList list, ShaderStage stage, int binding, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		var size = stage.MaterialBuffer.Size;
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		WriteConstants( allocation.Data[..size], stage, attributes, viewport );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>A stage's material constants for a draw: the viewport, texture sizes and attributes over the shader's defaults.</summary>
	internal void WriteConstants( Span<byte> data, ShaderStage stage, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		var material = Material;
		stage.WriteMaterialConstants( data, variable => variable.Source switch
		{
			VariableSource.Material or VariableSource.MaterialAndExpression => material?.Value( variable.Name ),
			VariableSource.ShaderVersion => material is null ? null : [material.ShaderVersion],
			VariableSource.Viewport => [viewport.X, viewport.Y, viewport.Z, viewport.W],
			VariableSource.InvViewportSize => [1 / viewport.Z, 1 / viewport.W],
			VariableSource.Attribute => attributes.Get( variable.Attribute ) as float[],
			VariableSource.TextureDimensions or VariableSource.InvTextureDimensions or VariableSource.TextureDimensionsLog2 => TextureSize( stage, variable, attributes ),
			_ => null,
		}, material?.Features ?? default );
	}

	/// <summary>
	/// The size of the texture a <c>Source( TextureDim )</c>-style variable names with <c>SourceArg</c>: (width, height,
	/// depth, mips), its reciprocal, or its log2. Only x and y are read by the shaders drawn so far.
	/// </summary>
	float[] TextureSize( ShaderStage stage, ShaderVariable variable, ShaderAttributes attributes )
	{
		var table = stage.Program.Variables;
		if ( variable.SourceIndex < 0 || variable.SourceIndex >= table.Length ) return null;

		var argument = table[variable.SourceIndex];
		if ( attributes.Get( string.IsNullOrEmpty( argument.Attribute ) ? argument.Name : argument.Attribute ) is not TextureHandle texture ) return null;

		var info = gpu.InfoOf( texture );
		float w = info.Width, h = info.Height, d = Math.Max( info.Depth, 1 ), m = info.Mips;
		return variable.Source switch
		{
			VariableSource.InvTextureDimensions => [1 / w, 1 / h, 1 / d, 1 / m],
			VariableSource.TextureDimensionsLog2 => [MathF.Log2( w ), MathF.Log2( h ), MathF.Log2( d ), m],
			_ => [w, h, d, m],
		};
	}

	Binding ConstantBuffer( CommandList list, int binding, byte[] data, ShaderStage stage, string name )
	{
		HandFilledConstants.CheckSize( stage, name, data.Length );
		var size = (data.Length + 15) & ~15;
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		allocation.Data.Clear();
		data.CopyTo( allocation.Data );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>
	/// A draw's attributes as a material object's pass bindings: every named resource by name, and the material
	/// constants and constant buffers written into the frame's pass constants (or kept by content past the device's
	/// dynamic limit).
	/// </summary>
	sealed class AttributeBindings( ShaderDrawer drawer ) : PassBindings
	{
		public ShaderAttributes Attributes;
		public System.Numerics.Vector4 Viewport;

		public override float[] Floats( string attribute ) => Attributes.Get( attribute ) as float[];

		public override Binding Resolve( in ShaderResource resource, ShaderStage stage, bool dynamic, out uint dynamicOffset )
		{
			dynamicOffset = 0;
			var value = drawer.Value( resource, Attributes );
			switch ( resource.Kind )
			{
				case ShaderResourceKind.MaterialConstants:
					{
						var size = stage.MaterialBuffer.Size;
						Span<byte> target = dynamic ? Space( size, out dynamicOffset ) : new byte[size];
						drawer.WriteConstants( target, stage, Attributes, Viewport );
						return Constants( resource, target, size, dynamic );
					}

				case ShaderResourceKind.ConstantBuffer when value is byte[] data:
					{
						HandFilledConstants.CheckSize( stage, resource.Name, data.Length );
						var size = (data.Length + 15) & ~15;
						Span<byte> target = dynamic ? Space( size, out dynamicOffset ) : new byte[size];
						target.Clear();
						data.CopyTo( target );
						return Constants( resource, target, size, dynamic );
					}

				case ShaderResourceKind.ConstantBuffer:
					{
						var placeholder = drawer.defaults.For( resource );
						if ( dynamic ) placeholder.Type = BindingType.UniformBufferDynamic;
						return placeholder;
					}

				default:
					return drawer.Resource( resource, value );
			}
		}

		Span<byte> Space( int size, out uint offset )
		{
			var start = drawer.cache.TryAllocatePassConstants( size, out var data );
			if ( start < 0 ) throw new InvalidOperationException( "Pass constant region is full" );
			offset = (uint)start;
			return data;
		}

		Binding Constants( in ShaderResource resource, ReadOnlySpan<byte> data, int size, bool dynamic )
		{
			var cache = drawer.cache;
			if ( dynamic ) return new Binding { Slot = resource.Binding, Type = BindingType.UniformBufferDynamic, Buffer = cache.PassBuffer, Size = size };
			return Binding.Uniform( resource.Binding, cache.MaterialBuffer, cache.KeepPassConstants( data, size ), size );
		}
	}
}
