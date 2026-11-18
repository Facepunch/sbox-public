using Borealis.Gpu;
using System.IO;

namespace Borealis.Source2.Shaders;

/// <summary>
/// One drawable variant of a shader: a mode, a material's feature values and dynamic combo values, resolved into what a
/// pipeline and its bindings need. Shader modules and binding layouts are made through a <see cref="ShaderLibrary"/>,
/// which owns them. See docs/managed/borealis-gpu.md.
/// </summary>
public sealed class ShaderVariant
{
	/// <summary>The shader.</summary>
	public ShaderFile Shader { get; }

	/// <summary>The mode it draws.</summary>
	public ShaderMode Mode { get; }

	/// <summary>The vertex stage.</summary>
	public ShaderStage Vertex { get; }

	/// <summary>The pixel stage, or null to draw depth only.</summary>
	public ShaderStage Pixel { get; }

	/// <summary>The compute stage, for compute shaders.</summary>
	public ShaderStage Compute { get; }

	/// <summary>A module's task stage, or null: it launches <see cref="Mesh"/>'s groups.</summary>
	public ShaderStage Task { get; }

	/// <summary>
	/// A module's mesh stage, or null: drawn in place of <see cref="Vertex"/> and vertex input on a device with mesh
	/// shaders (<see cref="MeshPipelineInfo"/>).
	/// </summary>
	public ShaderStage Mesh { get; }

	/// <summary>Layouts of sets 0 to 3; default where a set is empty. Set 4 is the device's bindless set.</summary>
	public BindingLayoutHandle[] Sets { get; } = new BindingLayoutHandle[ShaderAbi.BindlessSet];

	/// <summary>Every binding in sets 0 to 3 and what goes in it.</summary>
	public IReadOnlyList<ShaderResource> Resources => resources;

	/// <summary><see cref="Resources"/> as a span, to go through without allocating an enumerator.</summary>
	public ReadOnlySpan<ShaderResource> ResourceSpan => resources;

	readonly ShaderResource[] resources;

	/// <summary>The vertex shader's inputs, by location, with the semantic each reads.</summary>
	public IReadOnlyList<VertexInput> VertexInputs { get; }

	/// <summary>The resolved render state.</summary>
	public ShaderPipelineState State { get; }

	/// <summary>Whether its stages were compiled from the shader's module (<see cref="IShaderModules"/>) rather than its programs.</summary>
	public bool FromModule { get; }

	/// <summary>
	/// How its vertex stage expands the draw's primitives, where the <c>.shader</c> had a geometry stage; none for a
	/// stage that draws the vertices it's given.
	/// </summary>
	public PrimitiveExpansion Expansion => Vertex?.Expansion ?? default;

	internal ShaderVariant( ShaderLibrary library, ShaderFile shader, ShaderMode mode, ShaderStage vertex, ShaderStage pixel, ShaderStage compute, bool fromModule = false, ShaderStage task = null, ShaderStage mesh = null )
	{
		FromModule = fromModule;
		Shader = shader;
		Mode = mode;
		Vertex = vertex;
		Pixel = pixel;
		Compute = compute;
		Task = task;
		Mesh = mesh;

		// Bindings, each from the SPIR-V and given its meaning by the stage's variables
		var resources = new List<ShaderResource>();
		foreach ( var stage in new[] { vertex, pixel, compute, task, mesh } )
		{
			if ( stage?.Bytecode is null ) continue;
			var flags = stage.Type switch
			{
				ShaderProgramType.Vertex => ShaderStages.Vertex,
				ShaderProgramType.Pixel => ShaderStages.Fragment,
				ShaderProgramType.Task => ShaderStages.Task,
				ShaderProgramType.Mesh => ShaderStages.Mesh,
				_ => ShaderStages.Compute,
			};

			foreach ( var binding in stage.Reflection.Bindings )
			{
				if ( binding.Set >= ShaderAbi.BindlessSet ) continue;
				if ( binding.Dimension is SpirvImageDimension.Buffer or SpirvImageDimension.SubpassData )
					throw new InvalidDataException( $"{shader.Name}: binding {binding.Set}.{binding.Binding} is a texel buffer or input attachment, which Borealis.Gpu doesn't bind" );

				var existing = resources.FindIndex( r => r.Set == binding.Set && r.Binding == binding.Binding );
				if ( existing >= 0 )
				{
					resources[existing] = resources[existing] with { Stages = resources[existing].Stages | flags };
					continue;
				}

				resources.Add( stage.Describe( binding, flags ) );
			}
		}

		resources.Sort( ( a, b ) => a.Set != b.Set ? a.Set.CompareTo( b.Set ) : a.Binding.CompareTo( b.Binding ) );
		this.resources = [.. resources];

		for ( int set = 0; set < Sets.Length; set++ )
		{
			var slots = new List<BindingSlot>();
			foreach ( var r in resources )
			{
				if ( r.Set == set ) slots.Add( new BindingSlot( r.Binding, r.Type, r.Stages, Math.Max( r.Count, 1 ) ) );
			}

			if ( slots.Count > 0 ) Sets[set] = library.Layout( slots );
		}

		var inputs = new List<VertexInput>();
		if ( vertex?.Bytecode is { } vs )
		{
			foreach ( var input in vertex.Reflection.Inputs )
			{
				var semantic = input.Location < vs.AttributeMap.Length ? vs.AttributeMap[input.Location] : (byte)0;
				inputs.Add( new VertexInput( input.Location, (VertexSemantic)(semantic >> 4), semantic & 15, input.Kind, input.Components ) );
			}
		}

		VertexInputs = inputs;
		State = ShaderPipelineState.Resolve( pixel );
	}

	/// <summary>
	/// A graphics pipeline description for this variant into targets of these formats, with vertex inputs fed by
	/// <paramref name="attributes"/> from <paramref name="buffers"/> (the caller maps <see cref="VertexInputs"/> to its
	/// vertex streams; inputs it has no data for need a default stream, as native's input layouts give).
	/// </summary>
	public GraphicsPipelineInfo PipelineInfo( VertexBufferLayout[] buffers, VertexAttribute[] attributes, Format[] colors, Format depth, int samples, bool depthBias, string name )
	{
		var state = State;
		var depthOnly = Pixel?.Bytecode is null;
		return new GraphicsPipelineInfo
		{
			VertexShader = Vertex?.Module ?? default,
			VertexEntry = Vertex?.Bytecode?.EntryPoint( ShaderProgramType.Vertex ),
			VertexConstants = Vertex?.Constants,
			FragmentShader = depthOnly ? default : Pixel.Module,
			FragmentEntry = depthOnly ? null : Pixel.Bytecode.EntryPoint( ShaderProgramType.Pixel ),
			FragmentConstants = depthOnly ? null : Pixel.Constants,
			Sets = Sets,
			VertexBuffers = buffers,
			Attributes = attributes,
			Topology = PrimitiveTopology.TriangleList,
			CullMode = state.Cull,
			FrontFace = FrontFace.CounterClockwise,
			Wireframe = state.Wireframe,
			DepthClamp = !state.DepthClip,
			DepthBias = depthBias,
			DepthTest = depth != Format.Unknown && state.DepthTest,
			DepthWrite = depth != Format.Unknown && state.DepthWrite,
			DepthCompare = state.DepthCompare,
			StencilTest = depth.HasStencil() && state.StencilTest,
			StencilFront = state.StencilFront,
			StencilBack = state.StencilBack,
			StencilReadMask = state.StencilReadMask,
			StencilWriteMask = state.StencilWriteMask,
			Blend = state.Blend,
			AlphaToCoverage = state.AlphaToCoverage && samples > 1,
			ColorFormats = colors,
			DepthFormat = depth,
			Samples = samples,
			Name = name ?? $"{Shader.Name} {Mode?.Name}",
		};
	}

	/// <summary>
	/// A graphics pipeline description that draws with <see cref="Mesh"/> (and <see cref="Task"/>, when there is one) in
	/// place of the vertex stage and vertex input, into targets of these formats; null when it has no mesh stage.
	/// </summary>
	public GraphicsPipelineInfo? MeshPipelineInfo( Format[] colors, Format depth, int samples, bool depthBias, string name )
	{
		if ( Mesh?.Bytecode is null ) return null;
		var info = PipelineInfo( [], [], colors, depth, samples, depthBias, name );
		info.VertexShader = default;
		info.VertexEntry = null;
		info.VertexConstants = null;
		info.MeshShader = Mesh.Module;
		info.MeshEntry = Mesh.Bytecode.EntryPoint( ShaderProgramType.Mesh );
		info.MeshConstants = Mesh.Constants;
		if ( Task?.Bytecode is not null )
		{
			info.TaskShader = Task.Module;
			info.TaskEntry = Task.Bytecode.EntryPoint( ShaderProgramType.Task );
			info.TaskConstants = Task.Constants;
		}

		return info;
	}

	/// <inheritdoc/>
	public override string ToString() => $"{Shader.Name} {Mode?.Name}";
}

/// <summary>
/// One stage of a variant: its program, combos, shader and what its bindings mean.
/// </summary>
public sealed class ShaderStage
{
	readonly ShaderVariable[] variables;
	readonly Dictionary<(int Set, int Register), SamplerInfo> samplers = new();
	readonly Dictionary<(int Set, int Binding), int> named = new();

	// Hidden counters' bindings, to the index of their buffer's variable
	readonly Dictionary<(int Set, int Binding), int> counters = new();

	/// <summary>Which stage.</summary>
	public ShaderProgramType Type { get; }

	/// <summary>The program.</summary>
	public ShaderProgram Program { get; }

	/// <summary>The static combo drawn.</summary>
	public StaticCombo Static { get; }

	/// <summary>The dynamic combo drawn.</summary>
	public DynamicCombo Dynamic { get; }

	/// <summary>Its shader, or null for none (a pixel stage in depth-only combos).</summary>
	public ShaderBytecode Bytecode { get; }

	/// <summary>Its module on the device; default when it has no shader.</summary>
	public ShaderHandle Module { get; }

	/// <summary>Its SPIR-V's bindings and inputs.</summary>
	public SpirvReflection Reflection { get; }

	/// <summary>Specialization constants: the combo's, then any from <c>SpecializedConstant()</c> attributes.</summary>
	public SpecializationConstant[] Constants { get; }

	/// <summary>Where the material constant buffer is and how big; set -1 when there's none.</summary>
	public (int Set, int Binding, int Size) MaterialBuffer { get; }

	/// <summary>The values in the material constant buffer, with their byte offsets.</summary>
	public IReadOnlyList<(ShaderVariable Variable, int Offset)> MaterialConstants { get; }

	/// <summary>Render-state values the stage sets (pixel shaders): (<c>VfxRenderState_t</c>, variable).</summary>
	public IReadOnlyList<(int State, ShaderVariable Variable)> RenderStates { get; }

	/// <summary>A module's vertex stage's expansion of the draw's primitives (<see cref="ModuleStage.Expansion"/>); none otherwise.</summary>
	public PrimitiveExpansion Expansion { get; }

	/// <summary>
	/// A GPU-driven variant's material textures (<c>borealis_gpu_driven.fxc</c>): each one's bindless slot goes in the material
	/// record at this byte offset, after the constants (<see cref="ShaderLibrary.GpuDrivenTextureRegister"/>). Empty otherwise.
	/// </summary>
	public IReadOnlyList<(ShaderVariable Variable, int Offset)> RecordTextures { get; }

	/// <summary>Whether it's from a shader's GPU-driven file (<see cref="ShaderLibrary.GpuDriven"/>).</summary>
	public bool GpuDriven => Program.Shader.Name.EndsWith( ".gpudriven.shader_c", StringComparison.OrdinalIgnoreCase );

	internal ShaderStage( ShaderLibrary library, ShaderProgramType type, ShaderProgram program, StaticCombo combo, DynamicCombo dynamic, Func<string, int?> attributes, ReadOnlySpan<int> features )
	{
		ShaderExpressionContext expressions = null;
		var filters = new Dictionary<(int Set, int Register), int>();
		var evaluatedRenderStates = new Dictionary<ShaderVariable, float>( ReferenceEqualityComparer.Instance );
		EvaluatedRenderStates = evaluatedRenderStates;
		Type = type;
		Program = program;
		Static = combo;
		Dynamic = dynamic;
		Bytecode = dynamic.Shader;
		variables = program.Variables;

		if ( Bytecode is not null )
		{
			Module = library.Module( Bytecode, $"{program.Shader.Name} {type}" );
			library.Remember( program, combo.Id, dynamic.Id );
			Reflection = library.Reflect( Bytecode );
		}

		var constants = new List<SpecializationConstant>( Bytecode?.Constants ?? [] );
		var materialConstants = new List<(ShaderVariable, int)>();
		var renderStates = new List<(int, ShaderVariable)>();
		var recordTextures = new List<(ShaderVariable, int)>();

		foreach ( var binding in dynamic.Variables )
		{
			var variable = variables[binding.Variable];
			if ( variable.Register == RegisterType.Texture && binding.Offset >= ShaderLibrary.GpuDrivenTextureRegister )
			{
				recordTextures.Add( (variable, (binding.Offset - ShaderLibrary.GpuDrivenTextureRegister) * 4) );
				continue;
			}

			switch ( variable.Register )
			{
				case RegisterType.Bool or RegisterType.Int4 or RegisterType.Float4:
					materialConstants.Add( (variable, binding.Offset * 4) );
					break;

				case RegisterType.Specialization:
					var value = variable.Source == VariableSource.Attribute ? attributes?.Invoke( variable.Attribute ) : null;
					if ( value is { } v && !constants.Exists( c => c.Id == (uint)binding.Offset ) ) constants.Add( new SpecializationConstant( (uint)binding.Offset, (uint)v ) );
					break;

				case RegisterType.SamplerState:
					var key = (binding.Set, binding.Offset);
					var state = variable.IntDefault[0];
					if ( variable.Source is VariableSource.Expression or VariableSource.MaterialAndExpression )
					{
						expressions ??= ExpressionContext( combo, dynamic, features );
						state = Evaluate( variable, expressions );
						ExpressionStates += $"{binding.Set}.{binding.Offset}.{variable.RenderStateId}={state};";
					}

					samplers[key] = ApplySampler( samplers.TryGetValue( key, out var sampler ) ? sampler : DefaultSampler, variable, state );
					if ( variable.RenderStateId == 0 ) filters[key] = state;
					break;

				case RegisterType.RenderState:
					renderStates.Add( (variable.RenderStateId, variable) );
					if ( EvaluateRenderState( variable, ref expressions, combo, dynamic, features ) is { } evaluated )
					{
						evaluatedRenderStates[variable] = evaluated;
						ExpressionStates += $"r{variable.RenderStateId}={evaluated};";
					}

					break;

				case RegisterType.Texture:
					named[(binding.Set, ShaderAbi.Texture( binding.Offset ))] = binding.Variable;
					break;

				case RegisterType.Uav:
					named[(binding.Set, ShaderAbi.Storage( binding.Offset ))] = binding.Variable;
					break;

				case RegisterType.ConstantBuffer:
					named[(binding.Set, ShaderAbi.ConstantBuffer( binding.Offset & 0xFF ))] = binding.Variable;
					break;
			}
		}

		// Append and consume buffers' hidden counters: u registers, in the set of the buffer they count
		foreach ( var (buffer, counter) in Bytecode?.HiddenCounters ?? [] )
		{
			foreach ( var ((set, at), index) in named )
			{
				if ( at != ShaderAbi.Storage( buffer ) || variables[index].Register != RegisterType.Uav ) continue;
				counters[(set, ShaderAbi.Storage( counter ))] = index;
				break;
			}
		}

		// MaxAniso only counts with an anisotropic filter, as in D3D: a point or bilinear filter given beside MaxAniso( 8 )
		// (TextureFiltering with F_TEXTURE_FILTERING set) samples without anisotropy, whichever state came first
		foreach ( var key in new List<(int Set, int Register)>( samplers.Keys ) )
		{
			if ( !filters.TryGetValue( key, out var filter ) || (filter & 0x40) != 0 ) continue;
			var sampler = samplers[key];
			sampler.MaxAnisotropy = 1;
			samplers[key] = sampler;
		}

		Constants = [.. constants];
		MaterialConstants = materialConstants;
		RenderStates = renderStates;
		RecordTextures = recordTextures;
		MaterialBuffer = dynamic.ConstantBuffer.Slot >= 0 && combo.ConstantBufferSize > 0
			? (dynamic.ConstantBuffer.Set, ShaderAbi.ConstantBuffer( dynamic.ConstantBuffer.Slot ), combo.ConstantBufferSize)
			: (-1, -1, 0);
	}

	/// <summary>
	/// A stage compiled from <paramref name="shader"/>'s module (<see cref="IShaderModules"/>): one program of its own
	/// variables, one static and one dynamic combo, its <c>$Globals</c> as the material constants, its render states as
	/// literals over the defaults.
	/// </summary>
	internal ShaderStage( ShaderLibrary library, ShaderFile shader, ModuleStage stage )
	{
		EvaluatedRenderStates = new Dictionary<ShaderVariable, float>( ReferenceEqualityComparer.Instance );
		Type = stage.Type;

		var all = new List<ShaderVariable>();
		foreach ( var resource in stage.Resources )
		{
			if ( resource.Variable is null ) continue;
			named[(resource.Set, resource.Binding)] = all.Count;
			all.Add( resource.Variable );
			if ( resource.Sampler is { } sampler ) samplers[(resource.Set, resource.Binding - ShaderAbi.SamplerBase)] = sampler;
		}

		foreach ( var resource in stage.Resources )
		{
			if ( resource.CounterOf is not { } buffer ) continue;
			var index = all.FindIndex( v => v.Name == buffer );
			if ( index >= 0 ) counters[(resource.Set, resource.Binding)] = index;
		}

		foreach ( var (variable, _) in stage.GlobalMembers ) all.Add( variable );
		foreach ( var (_, variable) in stage.RenderStates ) all.Add( variable );
		variables = [.. all];

		Program = new ShaderProgram( shader, stage.Type, variables, [.. stage.ConstantBuffers] );
		Static = new StaticCombo( Program, stage.Globals.Size );
		// A stage with no code (a depth pass's pixel stage that only sets render states) has no bytecode, as a compiled one's
		Bytecode = stage.Spirv is null ? null : ShaderBytecode.FromModule( stage );
		Dynamic = new DynamicCombo { Id = 0, Shader = Bytecode, Variables = [], ConstantBuffer = (-1, -1), RenderState = new ShaderRenderState() };
		if ( Bytecode is not null )
		{
			Module = library.Module( Bytecode, stage.Name ?? $"{shader.Name} {stage.Type}" );
			Reflection = library.Reflect( Bytecode );
		}

		Constants = [.. Bytecode?.Constants ?? []];
		MaterialConstants = stage.GlobalMembers;
		RenderStates = stage.RenderStates;
		Expansion = stage.Expansion;
		RecordTextures = [];
		MaterialBuffer = stage.Globals.Set >= 0 && stage.Globals.Size > 0 ? stage.Globals : (-1, -1, 0);
	}

	/// <summary>
	/// Fill the material constant buffer (<see cref="MaterialBuffer"/>'s size): each value from <paramref name="values"/>
	/// when it has one (floats; ints and bools rounded), feature-derived values from <paramref name="features"/>, and
	/// the shader's defaults for the rest.
	/// </summary>
	public void WriteMaterialConstants( Span<byte> buffer, Func<ShaderVariable, float[]> values = null, ReadOnlySpan<int> features = default )
	{
		buffer.Clear();
		for ( int index = 0; index < MaterialConstants.Count; index++ )
			WriteMaterialConstant( buffer, index, values?.Invoke( MaterialConstants[index].Variable ), features );
	}

	/// <summary>
	/// Write one of <see cref="MaterialConstants"/> into <paramref name="buffer"/>, as <see cref="WriteMaterialConstants"/>
	/// writes each: <paramref name="given"/>, else the feature's value or the variable's default.
	/// </summary>
	public void WriteMaterialConstant( Span<byte> buffer, int index, float[] given, ReadOnlySpan<int> features = default )
	{
		var (variable, offset) = MaterialConstants[index];
		var components = Components( variable.Type );
		if ( components == 0 || offset + components * 4 > buffer.Length ) return;

		if ( given is null && variable.Source is VariableSource.FeatureToInt or VariableSource.FeatureToBool or VariableSource.FeatureToFloat )
		{
			var feature = Program.Shader.FeatureIndex( variable.Name );
			given = [feature >= 0 && feature < features.Length ? features[feature] : 0];
		}

		var integer = variable.Type is >= VariableType.Int and <= VariableType.Bool4;

		// A matrix nothing sets is identity, as native's material parameters default it (CMaterialShaderParams): world text
		// sets neither of ui/vertex.hlsl's LayerMat and TransformMat
		var matrix = components > 4;
		for ( int i = 0; i < components; i++ )
		{
			var target = buffer[(offset + i * 4)..];
			if ( integer )
			{
				var value = given is not null ? (i < given.Length ? (int)MathF.Round( given[i] ) : 0) : i < 4 ? variable.IntDefault[i] : 0;
				if ( variable.Type >= VariableType.Bool ) value = value != 0 ? 1 : 0;
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( target, value );
			}
			else
			{
				var value = given is not null ? (i < given.Length ? given[i] : 0) : matrix ? (i % 5 == 0 ? 1 : 0) : i < 4 ? variable.FloatDefault[i] : 0;
				System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( target, value );
			}
		}
	}

	static int Components( VariableType type ) => type switch
	{
		VariableType.Float or VariableType.Int or VariableType.Bool => 1,
		VariableType.Float2 or VariableType.Int2 or VariableType.Bool2 => 2,
		VariableType.Float3 or VariableType.Int3 or VariableType.Bool3 => 3,
		VariableType.Float4 or VariableType.Int4 or VariableType.Bool4 => 4,
		VariableType.Matrix3x3 or VariableType.Matrix3x4 => 12,
		VariableType.Matrix4x4 => 16,
		_ => 0,
	};

	static SamplerInfo DefaultSampler => SamplerInfo.Linear( AddressMode.Wrap );

	/// <summary><c>RsFilter_t</c> ANISOTROPIC.</summary>
	const int AnisotropicFilter = 0x55;

	/// <summary>
	/// Sampler states set by expressions, with their values (<c>Material.CommonInputs.hlsl</c>'s <c>TextureFiltering</c> picks
	/// its filter by <c>F_TEXTURE_FILTERING</c>); empty when none are. Materials that share combos but evaluate these
	/// differently don't share a variant.
	/// </summary>
	internal string ExpressionStates { get; } = "";

	/// <summary>Render-state variables given by expressions that evaluated for this material, with their values.</summary>
	internal IReadOnlyDictionary<ShaderVariable, float> EvaluatedRenderStates { get; }

	/// <summary>
	/// A render state given by an expression of the material's features (complex.shader's CullMode from
	/// F_RENDER_BACKFACES), evaluated as native's material layer does when it's constant for the material; null for a
	/// literal, or an expression that doesn't evaluate.
	/// </summary>
	static float? EvaluateRenderState( ShaderVariable variable, ref ShaderExpressionContext expressions, StaticCombo combo, DynamicCombo dynamic, ReadOnlySpan<int> features )
	{
		if ( variable.Source is not (VariableSource.Expression or VariableSource.MaterialAndExpression) || variable.Expression is not { } code ) return null;
		expressions ??= ExpressionContext( combo, dynamic, features );
		return ShaderExpression.TryEvaluate( code, expressions, out var evaluated ) ? evaluated.X : null;
	}

	/// <summary>
	/// The render state a pixel stage of these combos would have (<see cref="ShaderVariant.State"/>), without making the
	/// stage: no shader module, no reflection.
	/// </summary>
	internal static ShaderPipelineState PixelState( ShaderProgram program, StaticCombo combo, DynamicCombo dynamic, ReadOnlySpan<int> features )
	{
		ShaderExpressionContext expressions = null;
		List<(int, ShaderVariable)> renderStates = null;
		Dictionary<ShaderVariable, float> evaluated = null;
		foreach ( var binding in dynamic.Variables )
		{
			var variable = program.Variables[binding.Variable];
			if ( variable.Register != RegisterType.RenderState ) continue;
			(renderStates ??= new()).Add( (variable.RenderStateId, variable) );
			if ( EvaluateRenderState( variable, ref expressions, combo, dynamic, features ) is { } value )
				(evaluated ??= new( ReferenceEqualityComparer.Instance ))[variable] = value;
		}

		return ShaderPipelineState.Resolve( dynamic.RenderState, renderStates, evaluated );
	}

	/// <summary>
	/// What a render-state expression reads: the material's feature values, and this stage's static and dynamic combo
	/// values decoded from their ids.
	/// </summary>
	static ShaderExpressionContext ExpressionContext( StaticCombo combo, DynamicCombo dynamic, ReadOnlySpan<int> features )
	{
		static int[] Decode( ShaderCombo[] combos, ulong id )
		{
			var values = new int[combos.Length];
			for ( int i = 0; i < combos.Length; i++ )
			{
				var stride = Math.Max( combos[i].Stride, 1 );
				values[i] = combos[i].Min + (int)(id / stride % (ulong)Math.Max( combos[i].Count, 1 ));
			}

			return values;
		}

		return new ShaderExpressionContext
		{
			Features = features.ToArray(),
			StaticCombos = Decode( combo.Program.StaticCombos, combo.Id ),
			DynamicCombos = Decode( combo.Program.DynamicCombos, dynamic.Id ),
		};
	}

	/// <summary>
	/// A render-state expression's value, truncated to an int as native's material layer does when the expression is
	/// constant for the material. One that reads draw-time values (render attributes, time) can't be burnt in: an
	/// expression filter falls back to anisotropic (<c>F_TEXTURE_FILTERING</c>'s default) and anything else to its literal.
	/// </summary>
	static int Evaluate( ShaderVariable variable, ShaderExpressionContext context )
	{
		if ( variable.Expression is { } code && ShaderExpression.TryEvaluate( code, context, out var value ) ) return (int)value.X;
		return variable.RenderStateId == 0 ? AnisotropicFilter : variable.IntDefault[0];
	}

	/// <summary>
	/// Set one sampler-state field (<c>VfxSamplerState_t</c>) to <paramref name="value"/>; D3D11-style values.
	/// </summary>
	static SamplerInfo ApplySampler( SamplerInfo sampler, ShaderVariable variable, int value )
	{
		switch ( variable.RenderStateId )
		{
			case 0:
				// RsFilter_t: bit 0 mip linear, bit 2 mag linear, bit 4 min linear, 0x40 anisotropic, 0x80 comparison
				var anisotropic = (value & 0x40) != 0;
				sampler.MipFilter = anisotropic || (value & 0x01) != 0 ? Filter.Linear : Filter.Nearest;
				sampler.MagFilter = anisotropic || (value & 0x04) != 0 ? Filter.Linear : Filter.Nearest;
				sampler.MinFilter = anisotropic || (value & 0x10) != 0 ? Filter.Linear : Filter.Nearest;
				if ( anisotropic && sampler.MaxAnisotropy <= 1 ) sampler.MaxAnisotropy = 16;
				if ( !anisotropic ) sampler.MaxAnisotropy = 1;
				if ( (value & 0x80) != 0 && sampler.Compare == CompareFunction.Never ) sampler.Compare = CompareFunction.LessEqual;
				break;
			case 1:
				sampler.AddressU = Address( value );
				break;
			case 2:
				sampler.AddressV = Address( value );
				break;
			case 3:
				sampler.AddressW = Address( value );
				break;
			case 5:
				sampler.MipBias = variable.FloatDefault[0];
				break;
			case 6:
				sampler.MaxLod = variable.FloatDefault[0];
				break;
			case 7:
				sampler.MaxAnisotropy = Math.Max( value, 1 );
				break;
			case 9:
				sampler.MinLod = variable.FloatDefault[0];
				break;
			case 10:
				sampler.Compare = (CompareFunction)Math.Clamp( value, 0, 7 );
				break;
		}

		return sampler;
	}

	static AddressMode Address( int value ) => value switch
	{
		1 or 4 => AddressMode.Mirror,
		2 => AddressMode.Clamp,
		3 => AddressMode.Border,
		_ => AddressMode.Wrap,
	};

	internal ShaderResource Describe( SpirvBinding binding, ShaderStages stages )
	{
		var resource = new ShaderResource( binding.Set, binding.Binding, binding.Type, binding.Count, binding.Dimension, stages, ShaderResourceKind.Unknown, null, default );

		if ( binding.Type == BindingType.Sampler )
		{
			var register = binding.Binding - ShaderAbi.SamplerBase;
			return resource with { Kind = ShaderResourceKind.Sampler, Sampler = samplers.TryGetValue( (binding.Set, register), out var sampler ) ? sampler : DefaultSampler };
		}

		// A GPU-driven variant's material constants are in its material record, not a binding (RecordTextures says it's one)
		if ( binding.Type == BindingType.UniformBuffer && MaterialBuffer.Set == binding.Set && MaterialBuffer.Binding == binding.Binding && !GpuDriven )
			return resource with { Kind = ShaderResourceKind.MaterialConstants };

		if ( binding.Type == BindingType.StorageBuffer && counters.TryGetValue( (binding.Set, binding.Binding), out var buffer ) )
			return resource with { Kind = ShaderResourceKind.Counter, Variable = variables[buffer] };

		if ( !named.TryGetValue( (binding.Set, binding.Binding), out var index ) ) return resource;

		var variable = variables[index];
		var kind = binding.Type switch
		{
			BindingType.UniformBuffer => ShaderResourceKind.ConstantBuffer,
			BindingType.StorageBuffer => ShaderResourceKind.Buffer,
			BindingType.StorageTexture => ShaderResourceKind.StorageTexture,
			_ => ShaderResourceKind.Texture,
		};

		return resource with { Kind = kind, Variable = variable };
	}

	/// <inheritdoc/>
	public override string ToString() => $"{Program} static {Static.Id} dynamic {Dynamic.Id}";
}

/// <summary>
/// What a binding holds.
/// </summary>
public enum ShaderResourceKind
{
	/// <summary>Nothing in the shader's tables names it; bind a placeholder of its type.</summary>
	Unknown,
	/// <summary>The stage's material constant buffer (<see cref="ShaderStage.MaterialConstants"/>).</summary>
	MaterialConstants,
	/// <summary>A constant buffer the engine fills, named by its variable's attribute.</summary>
	ConstantBuffer,
	/// <summary>A structured or byte-address buffer, named by its variable.</summary>
	Buffer,
	/// <summary>A sampled texture: a material texture (by variable name) or an attribute.</summary>
	Texture,
	/// <summary>A storage texture.</summary>
	StorageTexture,
	/// <summary>A sampler made from the shader's sampler states (<see cref="ShaderResource.Sampler"/>).</summary>
	Sampler,
	/// <summary>
	/// The hidden counter of an append, consume or counter buffer: <see cref="ShaderResource.Variable"/> is that buffer's, and
	/// the counter binds under <see cref="ShaderResource.CounterName"/>.
	/// </summary>
	Counter,
}

/// <summary>
/// A binding of a variant and what it's for. <see cref="Variable"/> names it: a material parameter when its source is
/// <see cref="VariableSource.Material"/>, else an engine attribute (<see cref="ShaderVariable.Attribute"/>).
/// </summary>
public readonly record struct ShaderResource( int Set, int Binding, BindingType Type, int Count, SpirvImageDimension Dimension, ShaderStages Stages,
	ShaderResourceKind Kind, ShaderVariable Variable, SamplerInfo Sampler )
{
	/// <summary>The name the engine binds it by: the attribute, or the variable's own name for material values.</summary>
	public string Name => Variable is null ? null : string.IsNullOrEmpty( Variable.Attribute ) ? Variable.Name : Variable.Attribute;

	/// <summary>The name a buffer's hidden counter is bound by: the buffer's, with <see cref="CounterSuffix"/>.</summary>
	public static string CounterName( string buffer ) => buffer + CounterSuffix;

	/// <summary>What a buffer's name takes for its hidden counter (<see cref="ShaderResourceKind.Counter"/>).</summary>
	public const string CounterSuffix = "@counter";

	/// <inheritdoc/>
	public override string ToString() => $"{Set}.{Binding} {Kind} {Type} {Name}";
}

/// <summary>
/// A vertex shader input: its location, the semantic native matches it to a mesh stream by, and its type.
/// </summary>
public readonly record struct VertexInput( int Location, VertexSemantic Semantic, int SemanticIndex, SpirvScalar Kind, int Components );

/// <summary>
/// A vertex input's semantic (<c>SemanticUsageVulkan_t</c>).
/// </summary>
public enum VertexSemantic
{
#pragma warning disable CS1591
	Position = 0,
	BlendWeight,
	BlendIndices,
	Normal,
	PointSize,
	TexCoord,
	Tangent,
	Binormal,
	PositionT,
	Color,
	TessFactor,
#pragma warning restore CS1591
}

/// <summary>
/// A pixel shader's render state as Borealis.Gpu takes it: the dynamic combo's fixed state, or the defaults with the
/// render-state variables' literals applied. Variables whose value is an expression keep the default.
/// </summary>
public sealed class ShaderPipelineState
{
#pragma warning disable CS1591
	public CullMode Cull { get; init; }
	public bool Wireframe { get; init; }
	public bool DepthClip { get; init; }
	public int DepthBiasConstant { get; init; }
	public float DepthBiasSlope { get; init; }
	public bool DepthTest { get; init; }
	public bool DepthWrite { get; init; }
	public CompareFunction DepthCompare { get; init; }
	public bool StencilTest { get; init; }
	public StencilFace StencilFront { get; init; }
	public StencilFace StencilBack { get; init; }
	public byte StencilReadMask { get; init; }
	public byte StencilWriteMask { get; init; }
	public int StencilReference { get; init; }
	public BlendState[] Blend { get; init; }
	public bool AlphaToCoverage { get; init; }
	public bool Translucent => Blend.Length > 0 && Blend[0].Enable;
#pragma warning restore CS1591

	/// <summary>
	/// Whether drawing with this state needs a stencil to be right: it can leave a stencil it starts as zero non-zero, or its
	/// test fails against zero. A test that passes on zero and writes nothing (every standard material's, which skips VR's
	/// checkerboard) draws the same into a depth target with no stencil, where the test is off.
	/// </summary>
	public bool NeedsStencil => StencilTest && (Matters( StencilFront ) || Matters( StencilBack ));

	bool Matters( in StencilFace face )
	{
		var reference = (byte)StencilReference;
		bool Writes( StencilOp op ) => StencilWriteMask != 0 && op switch
		{
			StencilOp.Replace => (reference & StencilWriteMask) != 0,
			StencilOp.IncrementClamp or StencilOp.IncrementWrap or StencilOp.DecrementWrap or StencilOp.Invert => true,
			_ => false,
		};

		// Vulkan compares the masked reference with the masked stencil, here zero
		var masked = reference & StencilReadMask;
		var passesOnZero = face.Compare switch
		{
			CompareFunction.Never => false,
			CompareFunction.Less => false,
			CompareFunction.Equal => masked == 0,
			CompareFunction.LessEqual => masked == 0,
			CompareFunction.Greater => masked != 0,
			CompareFunction.NotEqual => masked != 0,
			_ => true,
		};

		return !passesOnZero || Writes( face.Pass ) || Writes( face.Fail ) || Writes( face.DepthFail );
	}

	internal static ShaderPipelineState Resolve( ShaderStage pixel ) => Resolve( pixel?.Dynamic.RenderState, pixel?.RenderStates, pixel?.EvaluatedRenderStates );

	/// <summary>
	/// A pixel stage's state from its dynamic combo's fixed state (null: every part dynamic), its render-state variables and
	/// the values of those given by expressions that evaluated.
	/// </summary>
	internal static ShaderPipelineState Resolve( ShaderRenderState fixedState, IReadOnlyList<(int State, ShaderVariable Variable)> renderStates, IReadOnlyDictionary<ShaderVariable, float> evaluatedStates )
	{
		var rasterizer = fixedState?.Rasterizer ?? RasterizerDesc.Default;
		var depthStencil = fixedState?.DepthStencil ?? DepthStencilDesc.Default;
		var blend = fixedState?.Blend?.Clone() ?? BlendDesc.Default;
		var stencilReference = 0;

		foreach ( var (state, variable) in renderStates ?? [] )
		{
			// Literals, and expressions of the material's features that evaluated (truncated, as native's are)
			int value;
			float floatValue;
			if ( variable.Source == VariableSource.RenderStateLiteral )
			{
				value = variable.IntDefault[0];
				floatValue = variable.FloatDefault[0];
			}
			else if ( evaluatedStates is not null && evaluatedStates.TryGetValue( variable, out var evaluated ) )
			{
				value = (int)evaluated;
				floatValue = evaluated;
			}
			else
			{
				continue;
			}

			// Only the parts the shader left dynamic take variables, as CVfxStaticComboData's state blocks do
			switch ( state )
			{
				case 0 when fixedState?.Rasterizer is null: rasterizer.FillMode = (byte)value; break;
				case 1 when fixedState?.Rasterizer is null: rasterizer.CullMode = (byte)value; break;
				case 2 when fixedState?.Rasterizer is null: rasterizer.DepthBias = value; break;
				case 3 when fixedState?.Rasterizer is null: rasterizer.SlopeScaledDepthBias = floatValue; break;
				case 5 when fixedState?.Rasterizer is null: rasterizer.DepthClip = value != 0; break;
				case 16 when fixedState?.DepthStencil is null: depthStencil.DepthTest = value != 0; break;
				case 17 when fixedState?.DepthStencil is null: depthStencil.DepthWrite = value != 0; break;
				case 18 when fixedState?.DepthStencil is null: depthStencil.DepthFunc = (byte)value; break;
				case 21 when fixedState?.DepthStencil is null: depthStencil.StencilEnable = value != 0; break;
				case 22 when fixedState?.DepthStencil is null: depthStencil.StencilReadMask = (byte)value; break;
				case 23 when fixedState?.DepthStencil is null: depthStencil.StencilWriteMask = (byte)value; break;
				case 24 when fixedState?.DepthStencil is null: depthStencil.FrontFail = (byte)value; break;
				case 25 when fixedState?.DepthStencil is null: depthStencil.FrontDepthFail = (byte)value; break;
				case 26 when fixedState?.DepthStencil is null: depthStencil.FrontPass = (byte)value; break;
				case 27 when fixedState?.DepthStencil is null: depthStencil.FrontFunc = (byte)value; break;
				case 28 when fixedState?.DepthStencil is null: depthStencil.BackFail = (byte)value; break;
				case 29 when fixedState?.DepthStencil is null: depthStencil.BackDepthFail = (byte)value; break;
				case 30 when fixedState?.DepthStencil is null: depthStencil.BackPass = (byte)value; break;
				case 31 when fixedState?.DepthStencil is null: depthStencil.BackFunc = (byte)value; break;
				case 32: stencilReference = value; break;
				case 37 when fixedState?.Blend is null: blend.AlphaToCoverage = value != 0; break;
				case 41 when fixedState?.Blend is null: blend.Targets[0].Enable = value != 0; break;
				case 43 when fixedState?.Blend is null: blend.Targets[0].Source = (byte)value; break;
				case 44 when fixedState?.Blend is null: blend.Targets[0].Destination = (byte)value; break;
				case 45 when fixedState?.Blend is null: blend.Targets[0].Op = (byte)value; break;
				case 46 when fixedState?.Blend is null: blend.Targets[0].SourceAlpha = (byte)value; break;
				case 47 when fixedState?.Blend is null: blend.Targets[0].DestinationAlpha = (byte)value; break;
				case 48 when fixedState?.Blend is null: blend.Targets[0].OpAlpha = (byte)value; break;
				case 49 when fixedState?.Blend is null: blend.Targets[0].WriteMask = (byte)value; break;
			}
		}

		// Without independent blending every target blends as the first
		var targets = new BlendState[blend.Independent ? 8 : 1];
		for ( int i = 0; i < targets.Length; i++ )
		{
			var t = blend.Targets[i];
			targets[i] = new BlendState
			{
				Enable = t.Enable,
				SourceColor = Factor( t.Source ),
				DestinationColor = Factor( t.Destination ),
				ColorOp = (BlendOp)Math.Min( (int)t.Op, 4 ),
				SourceAlpha = Factor( t.SourceAlpha ),
				DestinationAlpha = Factor( t.DestinationAlpha ),
				AlphaOp = (BlendOp)Math.Min( (int)t.OpAlpha, 4 ),
				WriteMask = t.WriteMask,
			};
		}

		return new ShaderPipelineState
		{
			Cull = rasterizer.CullMode switch { 0 => CullMode.None, 2 => CullMode.Front, _ => CullMode.Back },
			Wireframe = rasterizer.FillMode == 1,
			DepthClip = rasterizer.DepthClip,
			DepthBiasConstant = rasterizer.DepthBias,
			DepthBiasSlope = rasterizer.SlopeScaledDepthBias,
			DepthTest = depthStencil.DepthTest,
			DepthWrite = depthStencil.DepthWrite,
			DepthCompare = (CompareFunction)depthStencil.DepthFunc,
			StencilTest = depthStencil.StencilEnable,
			StencilFront = new StencilFace( (StencilOp)depthStencil.FrontFail, (StencilOp)depthStencil.FrontPass, (StencilOp)depthStencil.FrontDepthFail, (CompareFunction)depthStencil.FrontFunc ),
			StencilBack = new StencilFace( (StencilOp)depthStencil.BackFail, (StencilOp)depthStencil.BackPass, (StencilOp)depthStencil.BackDepthFail, (CompareFunction)depthStencil.BackFunc ),
			StencilReadMask = depthStencil.StencilReadMask,
			StencilWriteMask = depthStencil.StencilWriteMask,
			StencilReference = stencilReference,
			Blend = targets,
			AlphaToCoverage = blend.AlphaToCoverage,
		};
	}

	/// <summary><c>RsBlendMode_t</c> to Borealis.Gpu; dual-source and blend-factor modes aren't there and become one.</summary>
	static BlendFactor Factor( byte mode ) => mode switch
	{
		0 => BlendFactor.Zero,
		1 => BlendFactor.One,
		2 => BlendFactor.SourceColor,
		3 => BlendFactor.OneMinusSourceColor,
		4 => BlendFactor.SourceAlpha,
		5 => BlendFactor.OneMinusSourceAlpha,
		6 => BlendFactor.DestinationAlpha,
		7 => BlendFactor.OneMinusDestinationAlpha,
		8 => BlendFactor.DestinationColor,
		9 => BlendFactor.OneMinusDestinationColor,
		10 => BlendFactor.SourceAlpha,
		_ => BlendFactor.One,
	};
}
