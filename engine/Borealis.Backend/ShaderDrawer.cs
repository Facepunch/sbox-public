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
	readonly Dictionary<(ShaderVariant Variant, Format Color, Format Depth, int Samples, VertexStreamLayout Layout, PrimitiveTopology Topology), PipelineHandle> pipelines = new();
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
		var key = (shader, mode, attributes.ComboKey( shader ));
		if ( variants.TryGetValue( key, out var variant ) ) return variant;

		variant = shaders.Variant( shader, mode, [], attributes.GetCombo, name => attributes.Get( name ) is float[] { Length: > 0 } f ? (int)f[0] : null );
		variants[key] = variant;
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

		BindForDraw( list, variant, attributes, color, depth, samples, null, PrimitiveTopology.TriangleList, viewport );
		list.SetVertexBuffer( 0, defaults.VertexDefaults );
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

		BindForDraw( list, variant, attributes, color, depth, samples, layout, topology, viewport );
		list.SetVertexBuffer( 0, vertices, offset );
		list.SetVertexBuffer( 1, defaults.VertexDefaults );
		if ( !expansion.Expands )
		{
			list.Draw( vertexCount );
			return true;
		}

		for ( int corner = 1; corner < expansion.Corners; corner++ ) list.SetVertexBuffer( 1 + corner, vertices, offset + corner * layout.Stride );
		list.Draw( expansion.Vertices, instances );
		return true;
	}

	void BindForDraw( CommandList list, ShaderVariant variant, ShaderAttributes attributes, Format color, Format depth, int samples, VertexStreamLayout layout, PrimitiveTopology topology, System.Numerics.Vector4 viewport )
	{
		// A pipeline that tests stencil takes its reference as dynamic state, which nothing else in the list may have set
		if ( variant.State.StencilTest && depth.HasStencil() ) list.SetStencilReference( (byte)variant.State.StencilReference );

		if ( Objects( variant ) )
		{
			var key = new MaterialObjectCache.Key( null, variant, layout, color, depth, samples, false, false, topology );
			var materialObject = cache.Get( key, immediate: true, (Drawer: this, Variant: variant, Color: color, Depth: depth, Samples: samples, Layout: layout, Topology: topology),
				static ( s, materialLayout ) => s.Drawer.Pipeline( s.Variant, s.Color, s.Depth, s.Samples, s.Layout, s.Topology, materialLayout.Sets ) );
			SetViewport( list, viewport );
			BindObject( list, materialObject, attributes, viewport );
		}
		else
		{
			list.SetPipeline( Pipeline( variant, color, depth, samples, layout, topology, null ) );
			SetViewport( list, viewport );
			Bind( list, variant, attributes, viewport );
		}

		// The stencil reference is dynamic state: a shader that tests or writes stencil (gizmos, tool overlays) sets its own
		if ( variant.State.StencilTest && depth.HasStencil() ) list.SetStencilReference( (byte)variant.State.StencilReference );
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

	void SetViewport( CommandList list, System.Numerics.Vector4 viewport )
	{
		list.SetViewport( viewport.X, viewport.Y, viewport.Z, viewport.W, DepthRange.Min, DepthRange.Max );
		list.SetScissor( (int)viewport.X, (int)viewport.Y, (int)MathF.Ceiling( viewport.Z ), (int)MathF.Ceiling( viewport.W ) );
	}

	/// <summary>A graphics pipeline for the reflection path (cached here) or for a material object's sets (the object keeps it).</summary>
	PipelineHandle Pipeline( ShaderVariant variant, Format color, Format depth, int samples, VertexStreamLayout layout, PrimitiveTopology topology, BindingLayoutHandle[] sets )
	{
		var key = (variant, color, depth, samples, layout, topology);
		if ( sets is null && pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

		// Inputs the stream lacks (or every input, with no stream) read the default stream. No colour format draws into
		// depth alone
		GraphicsPipelineInfo info;
		Format[] colors = color == Format.Unknown ? [] : [color];
		if ( layout is null )
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, _ => null, defaultBuffer: 0 );
			info = variant.PipelineInfo( [new VertexBufferLayout( 0 )], attributes, colors, depth, samples, depthBias: false, null );
		}
		else if ( variant.Expansion is { Expands: true } expansion )
		{
			// One instance per primitive: its first vertex is stream 0, its others streams 2 and 3, each stepping a
			// primitive's worth of vertices per instance; an input reads its vertex's field at its semantic index less 4 a corner
			var step = new VertexBufferLayout( layout.Stride * expansion.Step( topology ), VertexStepRate.Instance );
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
			{
				var corner = expansion.Corner( input );
				return layout.Find( input.Semantic, expansion.SemanticIndex( input ) ) is { } field ? new VertexAttribute( 0, corner == 0 ? 0 : 1 + corner, field.Format, field.Offset ) : null;
			}, defaultBuffer: 1 );
			VertexBufferLayout[] streams = expansion.Corners switch { 3 => [step, new( 0 ), step, step], 2 => [step, new( 0 ), step], _ => [step, new( 0 )] };
			info = variant.PipelineInfo( streams, attributes, colors, depth, samples, depthBias: false, null );
			topology = expansion.Topology;
		}
		else
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input => layout.Find( input ) is { } field ? new VertexAttribute( 0, 0, field.Format, field.Offset ) : null, defaultBuffer: 1 );
			info = variant.PipelineInfo( [new VertexBufferLayout( layout.Stride ), new VertexBufferLayout( 0 )], attributes, colors, depth, samples, depthBias: false, null );
		}

		info.Topology = topology;
		if ( sets is not null ) info.Sets = sets;
		pipeline = gpu.CreateGraphicsPipeline( info );
		if ( sets is null ) pipelines[key] = pipeline;
		return pipeline;
	}

	/// <summary>The binding a reflected resource gets from a draw's attribute value, or its placeholder.</summary>
	Binding Resource( in ShaderResource resource, object value ) => resource.Kind switch
	{
		ShaderResourceKind.Buffer when value is BufferHandle buffer => Binding.Storage( resource.Binding, buffer ),
		ShaderResourceKind.Texture when value is TextureHandle texture => Binding.Sampled( resource.Binding, texture, resource.Variable.RenderStateId == 1 && gpu.InfoOf( texture ).Format.HasSrgb() ),
		ShaderResourceKind.Texture when value is TextureViewHandle view => Binding.Sampled( resource.Binding, view ),
		ShaderResourceKind.StorageTexture when value is TextureHandle texture => Binding.StorageTexture( resource.Binding, texture ),
		ShaderResourceKind.StorageTexture when value is TextureViewHandle view => Binding.StorageTexture( resource.Binding, view ),
		_ => defaults.For( resource ),
	};

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
				var value = resource.Name is null ? null : attributes.Get( resource.Name );
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
	void WriteConstants( Span<byte> data, ShaderStage stage, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		stage.WriteMaterialConstants( data, variable => variable.Source switch
		{
			VariableSource.Viewport => [viewport.X, viewport.Y, viewport.Z, viewport.W],
			VariableSource.InvViewportSize => [1 / viewport.Z, 1 / viewport.W],
			VariableSource.Attribute => attributes.Get( variable.Attribute ) as float[],
			VariableSource.TextureDimensions or VariableSource.InvTextureDimensions or VariableSource.TextureDimensionsLog2 => TextureSize( stage, variable, attributes ),
			_ => null,
		} );
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
			var value = resource.Name is null ? null : Attributes.Get( resource.Name );
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
