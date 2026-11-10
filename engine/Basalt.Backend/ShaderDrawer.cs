using Basalt.Gpu;
using Basalt.Source2.Shaders;

namespace Basalt.Backend;

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

	internal VertexField? Find( in VertexInput input )
	{
		foreach ( var field in Fields )
		{
			if ( field.Semantic == input.Semantic && field.Index == input.SemanticIndex ) return field;
		}

		return null;
	}
}

/// <summary>
/// Draws and dispatches with one of s&amp;box's compiled shaders and no material, binding every resource the shader
/// reflects by the name the engine sets it under, the way the engine's <c>Graphics</c> draws bind render attributes:
/// what a <c>Graphics</c> or <c>CommandList</c> draw (UI, post processing, debug drawing) lowers to on Basalt.Gpu.
/// Draws read vertices from a stream, or none (every input reads the default stream).
/// </summary>
/// <remarks>
/// <see cref="BackendContext"/> binds materials the same way for Basalt's scene draws; both belong in one material
/// binding service once Basalt's renderer services sit on Basalt.Gpu (docs/managed/basalt-gpu-path.md).
/// </remarks>
public sealed class ShaderDrawer
{
	readonly Device gpu;
	readonly ShaderLibrary shaders;
	readonly ShaderDefaults defaults;
	readonly Dictionary<(ShaderFile Shader, string Mode, string Combos), ShaderVariant> variants = new();
	readonly Dictionary<(ShaderVariant Variant, Format Color, int Samples, VertexStreamLayout Layout, PrimitiveTopology Topology), PipelineHandle> pipelines = new();
	readonly Dictionary<ShaderVariant, PipelineHandle> computePipelines = new();

	/// <summary>
	/// A drawer over a shader library and the placeholders bound for anything a draw doesn't set.
	/// </summary>
	public ShaderDrawer( Device gpu, ShaderLibrary shaders, ShaderDefaults defaults )
	{
		this.gpu = gpu;
		this.shaders = shaders;
		this.defaults = defaults;
	}

	/// <summary>The library shaders come from.</summary>
	public ShaderLibrary Shaders => shaders;

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
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		list.SetPipeline( Pipeline( variant, color, samples, null, PrimitiveTopology.TriangleList ) );
		SetViewport( list, viewport );
		Bind( list, variant, attributes, viewport );
		list.SetVertexBuffer( 0, defaults.VertexDefaults );
		list.SetIndexBuffer( indices, IndexFormat.UInt32 );
		list.DrawIndexed( indexCount, instances );
		return true;
	}

	/// <summary>
	/// Draw <paramref name="vertexCount"/> vertices of <paramref name="layout"/> from <paramref name="vertices"/> at
	/// <paramref name="offset"/>, as <paramref name="topology"/>, inside the list's open rendering scope. The rest is as
	/// <see cref="DrawIndexedInstanced"/>.
	/// </summary>
	public bool Draw( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, int samples, BufferHandle vertices, long offset, VertexStreamLayout layout, int vertexCount, PrimitiveTopology topology, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		list.SetPipeline( Pipeline( variant, color, samples, layout, topology ) );
		SetViewport( list, viewport );
		Bind( list, variant, attributes, viewport );
		list.SetVertexBuffer( 0, vertices, offset );
		list.SetVertexBuffer( 1, defaults.VertexDefaults );
		list.Draw( vertexCount );
		return true;
	}

	/// <summary>
	/// Run a compute shader over <paramref name="x"/> by <paramref name="y"/> by <paramref name="z"/> threads (divided
	/// by its thread group size, as native does), outside any rendering scope. Storage textures bind by attribute too.
	/// </summary>
	public bool Dispatch( CommandList list, ShaderFile shader, ShaderAttributes attributes, int x, int y, int z, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Compute?.Bytecode is null ) return false;

		if ( !computePipelines.TryGetValue( variant, out var pipeline ) )
		{
			pipeline = gpu.CreateComputePipeline( new ComputePipelineInfo
			{
				Shader = variant.Compute.Module,
				Entry = variant.Compute.Bytecode.EntryPoint( ShaderProgramType.Compute ),
				Constants = variant.Compute.Constants,
				Sets = variant.Sets,
				Name = variant.ToString(),
			} );
			computePipelines[variant] = pipeline;
		}

		list.SetPipeline( pipeline );
		Bind( list, variant, attributes, default );
		var (gx, gy, gz) = variant.Compute.Bytecode.ThreadGroupSize;
		gx = Math.Max( gx, 1 );
		gy = Math.Max( gy, 1 );
		gz = Math.Max( gz, 1 );
		list.Dispatch( (x + gx - 1) / gx, (y + gy - 1) / gy, (z + gz - 1) / gz );
		return true;
	}

	static void SetViewport( CommandList list, System.Numerics.Vector4 viewport )
	{
		list.SetViewport( viewport.X, viewport.Y, viewport.Z, viewport.W );
		list.SetScissor( (int)viewport.X, (int)viewport.Y, (int)MathF.Ceiling( viewport.Z ), (int)MathF.Ceiling( viewport.W ) );
	}

	PipelineHandle Pipeline( ShaderVariant variant, Format color, int samples, VertexStreamLayout layout, PrimitiveTopology topology )
	{
		var key = (variant, color, samples, layout, topology);
		if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

		// Inputs the stream lacks (or every input, with no stream) read the default stream
		GraphicsPipelineInfo info;
		if ( layout is null )
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, _ => null, defaultBuffer: 0 );
			info = variant.PipelineInfo( [new VertexBufferLayout( 0 )], attributes, [color], Format.Unknown, samples, depthBias: false, null );
		}
		else
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input => layout.Find( input ) is { } field ? new VertexAttribute( 0, 0, field.Format, field.Offset ) : null, defaultBuffer: 1 );
			info = variant.PipelineInfo( [new VertexBufferLayout( layout.Stride ), new VertexBufferLayout( 0 )], attributes, [color], Format.Unknown, samples, depthBias: false, null );
		}

		info.Topology = topology;
		pipeline = gpu.CreateGraphicsPipeline( info );
		pipelines[key] = pipeline;
		return pipeline;
	}

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
					ShaderResourceKind.ConstantBuffer when value is byte[] data => ConstantBuffer( list, resource.Binding, data ),
					ShaderResourceKind.Buffer when value is BufferHandle buffer => Binding.Storage( resource.Binding, buffer ),
					ShaderResourceKind.Texture when value is TextureHandle texture => Binding.Sampled( resource.Binding, texture, resource.Variable.RenderStateId == 1 && gpu.InfoOf( texture ).Format.HasSrgb() ),
					ShaderResourceKind.Texture when value is TextureViewHandle view => Binding.Sampled( resource.Binding, view ),
					ShaderResourceKind.StorageTexture when value is TextureHandle texture => Binding.StorageTexture( resource.Binding, texture ),
					ShaderResourceKind.StorageTexture when value is TextureViewHandle view => Binding.StorageTexture( resource.Binding, view ),
					_ => defaults.For( resource ),
				};
			}

			list.SetBindings( set, variant.Sets[set], bindings[..count] );
		}
	}

	Binding Constants( CommandList list, ShaderStage stage, int binding, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		var size = stage.MaterialBuffer.Size;
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		stage.WriteMaterialConstants( allocation.Data[..size], variable => variable.Source switch
		{
			VariableSource.Viewport => [viewport.X, viewport.Y, viewport.Z, viewport.W],
			VariableSource.InvViewportSize => [1 / viewport.Z, 1 / viewport.W],
			VariableSource.Attribute => attributes.Get( variable.Attribute ) as float[],
			VariableSource.TextureDimensions or VariableSource.InvTextureDimensions or VariableSource.TextureDimensionsLog2 => TextureSize( stage, variable, attributes ),
			_ => null,
		} );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
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

	Binding ConstantBuffer( CommandList list, int binding, byte[] data )
	{
		var size = (data.Length + 15) & ~15;
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		allocation.Data.Clear();
		data.CopyTo( allocation.Data );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}
}
