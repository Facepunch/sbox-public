using System.Linq;
using System.Runtime.CompilerServices;
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

	/// <summary>Per set, the bindings the pass fills (constant buffers, buffers and textures named by attributes).</summary>
	public readonly int[][] PassSlots = new int[ShaderAbi.BindlessSet][];

	/// <summary>
	/// What the pass bound for this layout in one pass state, resolved once and shared by every object of the layout.
	/// </summary>
	public sealed class PassState
	{
		public long Epoch = -1;
		public readonly ulong[] Identity = new ulong[ShaderAbi.BindlessSet];
		public readonly Binding[][] Bindings = new Binding[ShaderAbi.BindlessSet][];
		public readonly uint[][] Offsets = new uint[ShaderAbi.BindlessSet][];
		public ulong Constants;
	}

	/// <summary>Shared by every layout whose pass bindings are the same (materials of one shader and features).</summary>
	readonly PassState pass;

	/// <summary>The pass's bindings for this layout in pass state <paramref name="epoch"/>.</summary>
	public PassState Pass( long epoch, BackendContext.PassResolver resolver )
	{
		if ( pass.Epoch == epoch ) return pass;
		pass.Epoch = epoch;

		for ( int set = 0; set < Sets.Length; set++ )
		{
			var slots = PassSlots[set];
			if ( slots is null ) continue;

			var bindings = pass.Bindings[set] ??= new Binding[slots.Length];
			var offsets = pass.Offsets[set] ??= new uint[DynamicCount[set]];
			var identity = 14695981039346656037UL;
			for ( int k = 0; k < slots.Length; k++ )
			{
				var i = slots[k];
				var dynamic = DynamicIndex[set][i];
				bindings[k] = resolver.Resolve( Resources[set][i], StageOf( set ), dynamic >= 0, out var dynamicOffset );
				if ( dynamic >= 0 ) offsets[dynamic] = dynamicOffset;
				identity = MaterialObject.Mix( identity, bindings[k] );
			}

			pass.Identity[set] = identity;
		}

		// The values of the material constants read from attributes, which pick the object's constant buffer
		var constants = 14695981039346656037UL;
		foreach ( var (_, variables) in AttributeConstants )
		{
			foreach ( var variable in variables )
			{
				var values = resolver.Floats( variable.Attribute );
				if ( values is null ) constants = (constants ^ 0xFFFF) * 1099511628211UL;
				else foreach ( var v in values ) constants = (constants ^ BitConverter.SingleToUInt32Bits( v )) * 1099511628211UL;
			}
		}

		pass.Constants = constants;
		return pass;
	}

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
			PassSlots[set] = [.. Enumerable.Range( 0, resources.Count ).Where( i => resources[i].Kind is ShaderResourceKind.ConstantBuffer or ShaderResourceKind.Buffer
				|| (resources[i].Kind == ShaderResourceKind.Texture && resources[i].Variable.Source != VariableSource.Material) )];
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

		// Layouts that bind the same pass resources the same way resolve them once per pass state between them
		var signature = new System.Text.StringBuilder();
		for ( int set = 0; set < Sets.Length; set++ )
		{
			if ( PassSlots[set] is not { } slots ) continue;
			signature.Append( set ).Append( ':' ).Append( Sets[set].Index ).Append( ';' );
			foreach ( var i in slots )
			{
				var r = Resources[set][i];
				signature.Append( r.Name ).Append( '/' ).Append( (int)r.Kind ).Append( '/' ).Append( (int)r.Dimension ).Append( '/' ).Append( r.Variable?.RenderStateId ?? 0 ).Append( '/' ).Append( DynamicIndex[set][i] ).Append( '/' ).Append( r.Binding ).Append( ';' );
				if ( r.Kind == ShaderResourceKind.ConstantBuffer )
				{
					foreach ( var layout in StageOf( set ).Program.ConstantBuffers )
					{
						if ( layout.Name == r.Name ) signature.Append( layout.Size ).Append( ';' );
					}
				}
			}
		}

		foreach ( var (_, variables) in AttributeConstants )
		{
			foreach ( var v in variables ) signature.Append( v.Attribute ).Append( ',' );
		}

		pass = cache.SharedPassState( signature.ToString() );
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

	/// <summary>Per set, which binding is the material constant buffer.</summary>
	readonly int[] materialSlot = [-1, -1, -1, -1];

	/// <summary>The material constant buffer's offset (in the cache's material buffer) per value of the attribute constants.</summary>
	readonly Dictionary<ulong, int> constantVariants = new();
	readonly int[] constantSize = new int[ShaderAbi.BindlessSet];

	/// <summary>Persistent sets per set index, by a hash of what the pass bound in them.</summary>
	readonly Dictionary<(int Set, ulong Identity), BindingSetHandle> sets = new();

	// What the last epoch resolved to: the sets to bind and their dynamic offsets
	long epoch = -1;
	readonly BindingSetHandle[] boundSets = new BindingSetHandle[ShaderAbi.BindlessSet];
	readonly uint[][] offsets = new uint[ShaderAbi.BindlessSet][];
	static readonly uint[] noOffsets = [];
	readonly ulong[] lastIdentity = new ulong[ShaderAbi.BindlessSet];
	readonly BindingSetHandle[] lastSets = new BindingSetHandle[ShaderAbi.BindlessSet];
	ulong lastConstantsKey;
	long[] lastConstants;
	[ThreadStatic] static Binding[] scratch;

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
						break;

					case ShaderResourceKind.Texture when resource.Variable.Source == VariableSource.Material:
						bindings[i] = MaterialTexture( resource );
						break;

					case ShaderResourceKind.Texture:
						break;

					default:
						bindings[i] = defaults.For( resource );
						break;
				}
			}

			template[set] = bindings;
			offsets[set] = noOffsets;
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

	void Resolve( long passEpoch, BackendContext.PassResolver resolver )
	{
		epoch = passEpoch;
		var pass = Layout.Pass( passEpoch, resolver );
		var constants = ConstantVariant( pass.Constants, resolver );

		for ( int set = 0; set < template.Length; set++ )
		{
			boundSets[set] = default;
			var resources = Layout.Resources[set];
			if ( resources is null || (set == ShaderAbi.FragmentSet && DepthOnly) ) continue;

			// Identity: what the pass bound, as handles, and which constant buffer variant; dynamic offsets are given at bind time
			var identity = pass.Identity[set];
			if ( materialSlot[set] >= 0 ) identity = (identity ^ (ulong)constants[set]) * 1099511628211UL;
			offsets[set] = pass.Offsets[set] ?? noOffsets;

			// The set this object bound last time, if the pass bound the same: the usual case from frame to frame
			if ( lastIdentity[set] == identity && lastSets[set].IsValid )
			{
				boundSets[set] = lastSets[set];
				continue;
			}

			if ( !sets.TryGetValue( (set, identity), out var handle ) )
			{
				var bindings = (scratch ??= new Binding[128]).AsSpan( 0, resources.Length );
				template[set].CopyTo( bindings );
				var slots = Layout.PassSlots[set];
				for ( int k = 0; k < slots.Length; k++ ) bindings[slots[k]] = pass.Bindings[set][k];
				if ( materialSlot[set] >= 0 ) bindings[materialSlot[set]].Offset = constants[set];

				handle = cache.Device.Gpu.CreateBindingSet( Layout.Sets[set], bindings );
				sets[(set, identity)] = handle;
				SetsWritten++;
				cache.SetsWritten++;
			}

			boundSets[set] = handle;
			lastSets[set] = handle;
			lastIdentity[set] = identity;
		}
	}

	internal static ulong Mix( ulong hash, in Binding binding )
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
	long[] ConstantVariant( ulong key, BackendContext.PassResolver pass )
	{
		if ( lastConstants is not null && key == lastConstantsKey ) return lastConstants;
		lastConstantsKey = key;
		return lastConstants = FindConstantVariant( key, pass );
	}

	long[] FindConstantVariant( ulong key, BackendContext.PassResolver pass )
	{
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
	readonly Dictionary<Key, MaterialObject> objects = new( new KeyComparer() );

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
	readonly Dictionary<string, MaterialLayout.PassState> passStates = new();

	/// <summary>The pass state shared by layouts with this signature.</summary>
	internal MaterialLayout.PassState SharedPassState( string signature )
	{
		if ( passStates.TryGetValue( signature, out var state ) ) return state;
		return passStates[signature] = new MaterialLayout.PassState();
	}

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

	internal readonly record struct Key( BackendMaterial Material, ShaderVariant Variant, VertexLayout Layout, Format Color, Format Depth, int Samples, bool DepthOnly, bool UncompressedTangentFrame )
	{
		/// <summary>Equal by reference for the material, variant and layout (layouts are made once each).</summary>
		public bool Same( in Key other ) => ReferenceEquals( Material, other.Material ) && ReferenceEquals( Variant, other.Variant ) && ReferenceEquals( Layout, other.Layout )
			&& Color == other.Color && Depth == other.Depth && Samples == other.Samples && DepthOnly == other.DepthOnly && UncompressedTangentFrame == other.UncompressedTangentFrame;
	}

	sealed class KeyComparer : IEqualityComparer<Key>
	{
		public bool Equals( Key a, Key b ) => a.Same( b );

		public int GetHashCode( Key k ) => HashCode.Combine( RuntimeHelpers.GetHashCode( k.Material ), RuntimeHelpers.GetHashCode( k.Variant ), RuntimeHelpers.GetHashCode( k.Layout ),
			(int)k.Color, (int)k.Depth, k.Samples, k.DepthOnly, k.UncompressedTangentFrame );
	}

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
