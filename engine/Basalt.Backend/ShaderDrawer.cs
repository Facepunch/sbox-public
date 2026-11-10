using Basalt.Gpu;
using Basalt.Source2.Shaders;

namespace Basalt.Backend;

/// <summary>
/// The values a <see cref="ShaderDrawer"/> draw binds by name, as the engine's render attributes hold them: constants
/// as floats (a matrix as 16, row by row), structured buffers, textures and constant buffers by attribute name, and
/// dynamic combos by combo name.
/// </summary>
public sealed class ShaderAttributes
{
	readonly Dictionary<string, object> values = new( StringComparer.Ordinal );
	readonly Dictionary<string, int> combos = new( StringComparer.Ordinal );

	/// <summary>Constants: a number, a vector or a matrix, as floats.</summary>
	public void Set( string name, params float[] value ) => values[name] = value;

	/// <summary>A structured or byte-address buffer.</summary>
	public void Set( string name, BufferHandle buffer ) => values[name] = buffer;

	/// <summary>A texture, already in <see cref="ImageLayout.ShaderRead"/>.</summary>
	public void Set( string name, TextureHandle texture ) => values[name] = texture;

	/// <summary>A constant buffer's bytes.</summary>
	public void SetData( string name, byte[] data ) => values[name] = data;

	/// <summary>A dynamic combo (<c>D_*</c>).</summary>
	public void SetCombo( string name, int value ) => combos[name] = value;

	/// <summary>The value set under a name, or null.</summary>
	public object Get( string name ) => values.GetValueOrDefault( name );

	/// <summary>A combo's value, or null when it wasn't set.</summary>
	public int? GetCombo( string name ) => combos.TryGetValue( name, out var value ) ? value : null;

	/// <summary>Forget everything.</summary>
	public void Clear()
	{
		values.Clear();
		combos.Clear();
	}

	internal string ComboKey( ShaderFile shader )
	{
		var key = new System.Text.StringBuilder();
		foreach ( var type in (ReadOnlySpan<ShaderProgramType>)[ShaderProgramType.Vertex, ShaderProgramType.Pixel] )
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
/// Draws with one of s&amp;box's compiled shaders and no material, binding every resource the shader reflects by the
/// name the engine sets it under, the way the engine's <c>Graphics</c> draws bind render attributes: what a
/// <c>Graphics</c> or <c>CommandList</c> draw (UI, post processing, debug drawing) lowers to on Basalt.Gpu. Vertices
/// come from the shader (inputs read the default stream), so a draw is an index buffer and an instance count.
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
	readonly Dictionary<(ShaderVariant Variant, Format Color, int Samples), PipelineHandle> pipelines = new();

	/// <summary>
	/// A drawer over a shader library and the placeholders bound for anything a draw doesn't set.
	/// </summary>
	public ShaderDrawer( Device gpu, ShaderLibrary shaders, ShaderDefaults defaults )
	{
		this.gpu = gpu;
		this.shaders = shaders;
		this.defaults = defaults;
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
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		list.SetPipeline( Pipeline( variant, color, samples ) );
		list.SetViewport( viewport.X, viewport.Y, viewport.Z, viewport.W );
		list.SetScissor( (int)viewport.X, (int)viewport.Y, (int)MathF.Ceiling( viewport.Z ), (int)MathF.Ceiling( viewport.W ) );
		Bind( list, variant, attributes, viewport );
		list.SetVertexBuffer( 0, defaults.VertexDefaults );
		list.SetIndexBuffer( indices, IndexFormat.UInt32 );
		list.DrawIndexed( indexCount, instances );
		return true;
	}

	PipelineHandle Pipeline( ShaderVariant variant, Format color, int samples )
	{
		var key = (variant, color, samples);
		if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

		// No mesh: every input reads the default stream
		var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, _ => null, defaultBuffer: 0 );
		var info = variant.PipelineInfo( [new VertexBufferLayout( 0 )], attributes, [color], Format.Unknown, samples, depthBias: false, null );
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
			_ => null,
		} );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
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
