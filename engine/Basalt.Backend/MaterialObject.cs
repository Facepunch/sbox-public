using System.Linq;
using Basalt.Gpu;
using Basalt.Source2.Shaders;

namespace Basalt.Backend;

/// <summary>
/// What a variant's sets hold and how often each binding changes, worked out once per variant: the material's own
/// bindings (its constant buffer, textures, samplers and placeholders) and the pass bindings named by attributes
/// (constant buffers, buffers, textures). Its layouts declare every constant buffer dynamic, so a persistent set can
/// point at constants that move each frame.
/// </summary>
internal sealed class MaterialLayout
{
	public readonly ShaderVariant Variant;
	public readonly BindingLayoutHandle[] Sets = new BindingLayoutHandle[ShaderAbi.BindlessSet];

	/// <summary>Per set, its resources in binding order (which is the order of their dynamic offsets).</summary>
	public readonly ShaderResource[][] Resources = new ShaderResource[ShaderAbi.BindlessSet][];

	/// <summary>Per set, how many of its bindings are dynamic constant buffers.</summary>
	public readonly int[] DynamicCount = new int[ShaderAbi.BindlessSet];

	/// <summary>Per set and binding, its place among the set's dynamic offsets, or -1 when it isn't dynamic.</summary>
	public readonly int[][] DynamicIndex = new int[ShaderAbi.BindlessSet][];

	/// <summary>The material constants each stage reads from attributes (other than the draw's tangent format).</summary>
	public readonly (ShaderStage Stage, ShaderVariable[] Variables)[] AttributeConstants;

	public MaterialLayout( MaterialObjectCache cache, ShaderVariant variant )
	{
		Variant = variant;

		// Pass constant buffers move every pass, so they're dynamic, as many as the device allows over the layout; the
		// material's own and any past the limit point at constants that stay put
		var budget = cache.Device.Gpu.Capabilities.MaxDynamicUniformBuffers;
		for ( int set = 0; set < Sets.Length; set++ )
		{
			if ( !variant.Sets[set].IsValid ) continue;

			var resources = new List<ShaderResource>();
			var slots = new List<BindingSlot>();
			var dynamicIndex = new List<int>();
			foreach ( var r in variant.Resources )
			{
				if ( r.Set != set ) continue;
				resources.Add( r );
				var dynamic = r.Kind == ShaderResourceKind.ConstantBuffer && budget > 0;
				if ( dynamic ) budget--;
				var type = dynamic ? BindingType.UniformBufferDynamic : r.Type;
				dynamicIndex.Add( type == BindingType.UniformBufferDynamic ? DynamicCount[set]++ : -1 );
				slots.Add( new BindingSlot( r.Binding, type, r.Stages, Math.Max( r.Count, 1 ) ) );
			}

			Resources[set] = [.. resources];
			DynamicIndex[set] = [.. dynamicIndex];
			Sets[set] = cache.Layout( slots );
		}

		var attributeConstants = new List<(ShaderStage, ShaderVariable[])>();
		foreach ( var stage in new[] { variant.Vertex, variant.Pixel, variant.Compute } )
		{
			if ( stage is null ) continue;
			var variables = new List<ShaderVariable>();
			foreach ( var (variable, _) in stage.MaterialConstants )
			{
				if ( variable.Source == VariableSource.Attribute && variable.Attribute != "UncompressedTangentFrame" ) variables.Add( variable );
			}

			if ( variables.Count > 0 ) attributeConstants.Add( (stage, [.. variables]) );
		}

		AttributeConstants = [.. attributeConstants];
	}

	/// <summary>The stage whose material constants a set holds, as <see cref="BackendContext"/> binds them.</summary>
	public ShaderStage StageOf( int set ) => set == ShaderAbi.FragmentSet ? Variant.Pixel : Variant.Compute ?? Variant.Vertex;
}

/// <summary>
/// A material drawn one way: a material's variant for a mode and dynamic combos, a vertex layout and target formats.
/// It holds its pipeline, its constant buffers (one per value of the constants it reads from attributes, written once),
/// its resolved material bindings, and persistent descriptor sets keyed by what the pass bound around it. Binding it
/// is a pipeline bind and a set bind per stage with dynamic offsets; sets are written only when a pass binds
/// resources this object hasn't seen with it before. See docs/managed/basalt-materials.md.
/// </summary>
internal sealed class MaterialObject
{
	readonly MaterialObjectCache cache;
	public readonly BackendMaterial Material;
	public readonly MaterialLayout Layout;
	public readonly PipelineHandle Pipeline;
	public readonly bool DepthOnly;
	public readonly bool UncompressedTangentFrame;

	/// <summary>Per set, the bindings that never change for this object (pass bindings left default until resolved).</summary>
	readonly Binding[][] template = new Binding[ShaderAbi.BindlessSet][];

	/// <summary>Per set, which of its bindings come from the pass (attributes), and which is the material constant buffer.</summary>
	readonly int[][] passSlots = new int[ShaderAbi.BindlessSet][];
	readonly int[] materialSlot = [-1, -1, -1, -1];

	/// <summary>The material constant buffer's offset (in the cache's material buffer) per value of the attribute constants.</summary>
	readonly Dictionary<ConstantKey, int> constantVariants = new();
	readonly int[] constantSize = new int[ShaderAbi.BindlessSet];

	/// <summary>Persistent sets per set index, by a hash of what the pass bound in them.</summary>
	readonly Dictionary<(int Set, ulong Identity), BindingSetHandle> sets = new();

	// What the last epoch resolved to: the sets to bind and their dynamic offsets
	long epoch = -1;
	readonly BindingSetHandle[] boundSets = new BindingSetHandle[ShaderAbi.BindlessSet];
	readonly uint[][] offsets = new uint[ShaderAbi.BindlessSet][];

	/// <summary>Sets written so far, for the benchmark and tests.</summary>
	public int SetsWritten { get; private set; }

	public MaterialObject( MaterialObjectCache cache, BackendMaterial material, MaterialLayout layout, PipelineHandle pipeline, bool depthOnly, bool uncompressedTangentFrame )
	{
		this.cache = cache;
		Material = material;
		Layout = layout;
		Pipeline = pipeline;
		DepthOnly = depthOnly;
		UncompressedTangentFrame = uncompressedTangentFrame;

		var defaults = cache.Device.Defaults;
		for ( int set = 0; set < template.Length; set++ )
		{
			var resources = layout.Resources[set];
			if ( resources is null ) continue;

			var bindings = new Binding[resources.Length];
			var pass = new List<int>();
			for ( int i = 0; i < resources.Length; i++ )
			{
				ref readonly var resource = ref resources[i];
				switch ( resource.Kind )
				{
					case ShaderResourceKind.MaterialConstants:
						materialSlot[set] = i;
						constantSize[set] = layout.StageOf( set ).MaterialBuffer.Size;
						bindings[i] = Binding.Uniform( resource.Binding, cache.MaterialBuffer, 0, constantSize[set] );
						break;

					case ShaderResourceKind.ConstantBuffer:
					case ShaderResourceKind.Buffer:
						pass.Add( i );
						break;

					case ShaderResourceKind.Texture when resource.Variable.Source == VariableSource.Material:
						bindings[i] = MaterialTexture( resource );
						break;

					case ShaderResourceKind.Texture:
						pass.Add( i );
						break;

					default:
						bindings[i] = defaults.For( resource );
						break;
				}
			}

			template[set] = bindings;
			passSlots[set] = [.. pass];
			offsets[set] = new uint[layout.DynamicCount[set]];
		}
	}

	Binding MaterialTexture( in ShaderResource resource )
	{
		Material.Textures.TryGetValue( resource.Variable.Name, out var texture );
		return BackendContext.TextureBinding( cache.Device, texture, resource );
	}

	/// <summary>
	/// Bind the pipeline and sets for a draw in pass state <paramref name="passEpoch"/>; pass bindings are resolved through
	/// <paramref name="pass"/> only when the epoch changed since this object last drew.
	/// </summary>
	public void Bind( CommandList list, ref PipelineHandle bound, long passEpoch, BackendContext.PassResolver pass )
	{
		if ( bound != Pipeline )
		{
			list.SetPipeline( Pipeline );
			bound = Pipeline;
		}

		if ( passEpoch != epoch ) Resolve( passEpoch, pass );

		for ( int set = 0; set < boundSets.Length; set++ )
		{
			if ( !boundSets[set].IsValid ) continue;
			list.SetBindingSet( set, boundSets[set], offsets[set] );
		}
	}

	void Resolve( long passEpoch, BackendContext.PassResolver pass )
	{
		epoch = passEpoch;
		var constants = ConstantVariant( pass );
		Span<Binding> bindings = stackalloc Binding[64];

		for ( int set = 0; set < template.Length; set++ )
		{
			boundSets[set] = default;
			var resources = Layout.Resources[set];
			if ( resources is null || (set == ShaderAbi.FragmentSet && DepthOnly) ) continue;

			var count = resources.Length;
			template[set].CopyTo( bindings );
			var setOffsets = offsets[set];
			setOffsets.AsSpan().Clear();

			// Identity: what the pass bound, as handles; offsets of dynamic buffers are left out, they're given at bind time
			var identity = 14695981039346656037UL;
			foreach ( var i in passSlots[set] )
			{
				var dynamic = Layout.DynamicIndex[set][i] >= 0;
				bindings[i] = pass.Resolve( resources[i], Layout.StageOf( set ), dynamic, out var dynamicOffset );
				if ( dynamic ) offsets[set][Layout.DynamicIndex[set][i]] = dynamicOffset;
				identity = Mix( identity, bindings[i] );
			}

			// The material's constants: the variant for the attribute constants' values, which is part of the set's identity
			if ( materialSlot[set] >= 0 )
			{
				bindings[materialSlot[set]].Offset = constants[set];
				identity = Mix( identity, bindings[materialSlot[set]] );
			}

			if ( !sets.TryGetValue( (set, identity), out var handle ) )
			{
				handle = cache.Device.Gpu.CreateBindingSet( Layout.Sets[set], bindings[..count] );
				sets[(set, identity)] = handle;
				SetsWritten++;
				cache.SetsWritten++;
			}

			boundSets[set] = handle;
		}
	}

	static ulong Mix( ulong hash, in Binding binding )
	{
		hash = (hash ^ (ulong)binding.Type) * 1099511628211UL;
		hash = (hash ^ binding.Buffer.Index ^ ((ulong)binding.Buffer.Generation << 32)) * 1099511628211UL;
		hash = (hash ^ (ulong)binding.Size) * 1099511628211UL;
		hash = (hash ^ binding.Texture.Index ^ ((ulong)binding.Texture.Generation << 32)) * 1099511628211UL;
		hash = (hash ^ (binding.Srgb ? 1UL : 0UL) ^ ((ulong)binding.Offset << 1)) * 1099511628211UL;
		return hash;
	}

	/// <summary>
	/// The material constant buffers' offsets for the attribute constants' current values: filled and kept the first time
	/// each value is seen.
	/// </summary>
	long[] ConstantVariant( BackendContext.PassResolver pass )
	{
		var key = default( ConstantKey );
		foreach ( var (_, variables) in Layout.AttributeConstants )
		{
			foreach ( var variable in variables ) key = key.Add( pass.Floats( variable.Attribute ) );
		}

		if ( !constantVariants.TryGetValue( key, out var index ) )
		{
			var result = new long[template.Length];
			for ( int set = 0; set < template.Length; set++ )
			{
				if ( materialSlot[set] < 0 ) continue;
				var stage = Layout.StageOf( set );
				var offset = cache.AllocateMaterialConstants( constantSize[set], out var data );
				stage.WriteMaterialConstants( data, variable => variable.Source switch
				{
					VariableSource.Material or VariableSource.MaterialAndExpression => Material.Value( variable.Name ),
					VariableSource.Attribute when variable.Attribute == "UncompressedTangentFrame" => [UncompressedTangentFrame ? 1 : 0],
					VariableSource.Attribute => pass.Floats( variable.Attribute ),
					_ => null,
				}, Material.Features );
				result[set] = offset;
			}

			index = constantVariantOffsets.Count;
			constantVariantOffsets.Add( result );
			constantVariants[key] = index;
		}

		return constantVariantOffsets[index];
	}

	readonly List<long[]> constantVariantOffsets = new();

	/// <summary>Free the persistent sets; the GPU's in-flight frames keep them until they finish.</summary>
	public void Dispose()
	{
		foreach ( var handle in sets.Values ) cache.Device.Gpu.Destroy( handle );
		sets.Clear();
	}

	/// <summary>A small hash of the attribute constants' values.</summary>
	readonly record struct ConstantKey( ulong Hash, int Count )
	{
		public ConstantKey Add( float[] values )
		{
			var hash = Hash == 0 ? 14695981039346656037UL : Hash;
			if ( values is null ) return new ConstantKey( (hash ^ 0xFFFF) * 1099511628211UL, Count + 1 );
			foreach ( var v in values ) hash = (hash ^ BitConverter.SingleToUInt32Bits( v )) * 1099511628211UL;
			return new ConstantKey( hash, Count + 1 );
		}
	}
}

/// <summary>
/// The device's material objects: their layouts, a persistent buffer of material constants written once per object and
/// attribute value, and a buffer of pass constants with a region per frame in flight, which dynamic offsets point
/// into. Objects are made the first time a material draws a way and kept until <see cref="Invalidate"/>.
/// </summary>
internal sealed class MaterialObjectCache : IDisposable
{
	public readonly BackendDevice Device;
	readonly Dictionary<string, BindingLayoutHandle> layouts = new();
	readonly Dictionary<ShaderVariant, MaterialLayout> variantLayouts = new( ReferenceEqualityComparer.Instance );
	readonly Dictionary<Key, MaterialObject> objects = new();

	/// <summary>Material constants, written once per object and attribute value; bound with dynamic offsets.</summary>
	public readonly BufferHandle MaterialBuffer;
	const long MaterialBufferSize = 32 << 20;
	long materialUsed;

	/// <summary>Pass constants (view, lighting...), copied in once per pass state, a region per frame in flight.</summary>
	public readonly BufferHandle PassBuffer;
	const long PassRegionSize = 16 << 20;
	long passUsed;
	ulong passFrame = ulong.MaxValue;

	readonly int alignment;
	long epochs;

	/// <summary>Descriptor sets written, for the benchmark and tests.</summary>
	public int SetsWritten { get; set; }

	/// <summary>Objects made.</summary>
	public int Count => objects.Count;

	/// <summary>The materials objects were made for.</summary>
	public BackendMaterial[] Materials => [.. objects.Keys.Select( k => k.Material ).Distinct()];

	public MaterialObjectCache( BackendDevice device )
	{
		Device = device;
		alignment = Math.Max( device.Gpu.Capabilities.UniformAlignment, 16 );
		MaterialBuffer = device.Gpu.CreateBuffer( new BufferInfo { Size = MaterialBufferSize, Usage = BufferUsage.Uniform, Memory = MemoryLocation.Upload, Name = "Material constants" } );
		PassBuffer = device.Gpu.CreateBuffer( new BufferInfo { Size = PassRegionSize * device.Gpu.FramesInFlight, Usage = BufferUsage.Uniform, Memory = MemoryLocation.Upload, Name = "Pass constants" } );
	}

	/// <summary>A new pass state: every object resolves its pass bindings again the next time it binds.</summary>
	public long NextEpoch() => ++epochs;

	internal BindingLayoutHandle Layout( List<BindingSlot> slots )
	{
		var key = string.Join( ';', slots.ConvertAll( s => $"{s.Binding},{(int)s.Type},{(int)s.Stages},{s.Count}" ) );
		if ( layouts.TryGetValue( key, out var layout ) ) return layout;
		return layouts[key] = Device.Gpu.CreateBindingLayout( [.. slots] );
	}

	internal readonly record struct Key( BackendMaterial Material, ShaderVariant Variant, VertexLayout Layout, Format Color, Format Depth, int Samples, bool DepthOnly, bool UncompressedTangentFrame );

	/// <summary>The object for a material drawn this way, made the first time.</summary>
	public MaterialObject Get( in Key key )
	{
		if ( objects.TryGetValue( key, out var found ) ) return found;

		if ( !variantLayouts.TryGetValue( key.Variant, out var layout ) ) variantLayouts[key.Variant] = layout = new MaterialLayout( this, key.Variant );
		var pipeline = Device.Pipeline( key.Variant, key.Layout, key.Color, key.Depth, key.Samples, key.DepthOnly, layout.Sets );
		var made = new MaterialObject( this, key.Material, layout, pipeline, key.DepthOnly, key.UncompressedTangentFrame );
		objects[key] = made;
		return made;
	}

	/// <summary>Drop a material's objects, after it changed: they're made again, with its new values, when it next draws.</summary>
	public void Invalidate( GpuMaterial material )
	{
		List<Key> stale = null;
		foreach ( var (key, value) in objects )
		{
			if ( !ReferenceEquals( key.Material, material ) ) continue;
			value.Dispose();
			(stale ??= new()).Add( key );
		}

		foreach ( var key in stale ?? [] ) objects.Remove( key );
	}

	/// <summary>Space for one material constant buffer, kept for the device's life.</summary>
	public long AllocateMaterialConstants( int size, out Span<byte> data )
	{
		var offset = (materialUsed + alignment - 1) / alignment * alignment;
		if ( offset + size > MaterialBufferSize ) throw new InvalidOperationException( "Material constant buffer is full" );
		materialUsed = offset + size;
		data = Device.Gpu.Map( MaterialBuffer ).Slice( (int)offset, size );
		return offset;
	}

	readonly Dictionary<(ulong Hash, int Size), long> keptConstants = new();

	/// <summary>
	/// Pass constants for a binding that isn't dynamic: kept by content, so the same values land at the same place every
	/// frame and the sets pointing at them stay valid.
	/// </summary>
	public long KeepPassConstants( ReadOnlySpan<byte> data, int size )
	{
		var hash = 14695981039346656037UL;
		foreach ( var b in data ) hash = (hash ^ b) * 1099511628211UL;
		if ( keptConstants.TryGetValue( (hash, size), out var offset ) ) return offset;

		offset = AllocateMaterialConstants( size, out var target );
		target.Clear();
		data.CopyTo( target );
		keptConstants[(hash, size)] = offset;
		return offset;
	}

	/// <summary>Space for pass constants in this frame's region.</summary>
	public long AllocatePassConstants( int size, out Span<byte> data )
	{
		var frame = Device.Gpu.FrameNumber;
		if ( frame != passFrame )
		{
			passFrame = frame;
			passUsed = 0;
		}

		var offset = (passUsed + alignment - 1) / alignment * alignment;
		if ( offset + size > PassRegionSize ) throw new InvalidOperationException( "Pass constant region is full" );
		passUsed = offset + size;
		var start = Device.Gpu.FrameSlot * PassRegionSize + offset;
		data = Device.Gpu.Map( PassBuffer ).Slice( (int)start, size );
		return start;
	}

	public void Dispose()
	{
		foreach ( var value in objects.Values ) value.Dispose();
		objects.Clear();
		foreach ( var layout in layouts.Values ) Device.Gpu.Destroy( layout );
		layouts.Clear();
		Device.Gpu.Destroy( MaterialBuffer );
		Device.Gpu.Destroy( PassBuffer );
	}
}
