using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Borealis.Gpu;
using Borealis.Source2.Shaders;

namespace Borealis.Backend;

/// <summary>
/// Where a material object's pass bindings come from: what one recording context bound around its draws. Each context
/// has its own (a <see cref="BackendContext"/> its attributes, a <see cref="ShaderDrawer"/> its draw's), so objects keep
/// what they resolved per context and several threads can record with the same objects.
/// </summary>
internal abstract class PassBindings
{
	/// <summary>This context's place in every object's per-context state, given by the cache.</summary>
	public int Slot = -1;

	/// <summary>The pass state this context's attributes last changed in, or -1: a layout resolved in it needs only the material's bindings (<see cref="MaterialLayout.Pass"/>).</summary>
	public long AttributesEpoch = -1;

	/// <summary>Id of the handles this context's attributes bind, stable while they are, or -1: lets an immediate object rebind its sets writing only constants.</summary>
	public long Structure = -1;

	/// <summary>The pass state per layout signature, for this context.</summary>
	public MaterialLayout.PassState[] States = [];

	/// <summary>
	/// The binding the pass makes for <paramref name="resource"/>; a dynamic constant buffer's offset goes in
	/// <paramref name="dynamicOffset"/>.
	/// </summary>
	public abstract Binding Resolve( MaterialLayout layout, int set, int slot, in ShaderResource resource, ShaderStage stage, bool dynamic, out uint dynamicOffset );

	/// <summary>An attribute as floats, for the material constants read from attributes; null when it isn't set.</summary>
	public abstract float[] Floats( string attribute );

	/// <summary>
	/// Which object attributes the next bind reads over the pass's: 0 for none, else a number this context hasn't used
	/// for anything else, new whenever the object's attributes (or their values) change.
	/// </summary>
	public long LayerEpoch;

	/// <summary>The pass state with the object's attributes over it, per layout signature, for this context.</summary>
	public MaterialLayout.PassState[] LayerStates = [];

	/// <summary>
	/// The binding the object's attributes make for <paramref name="resource"/>, if they set it; false leaves the pass's.
	/// A dynamic constant buffer's offset goes in <paramref name="dynamicOffset"/>.
	/// </summary>
	public virtual bool ResolveLayer( in ShaderResource resource, ShaderStage stage, in MaterialLayout.PassInput input, bool dynamic, out Binding binding, out uint dynamicOffset )
	{
		binding = default;
		dynamicOffset = 0;
		return false;
	}

	/// <summary>An attribute the object's attributes set, as floats; null when they don't set it.</summary>
	public virtual float[] LayerFloats( string attribute ) => null;
}

/// <summary>
/// What a variant's sets hold and how often each binding changes, worked out once per variant: the material's own
/// bindings (its constant buffer, textures, samplers and placeholders) and the pass bindings named by attributes
/// (constant buffers, buffers, textures). Its layouts declare pass constant buffers dynamic, so a persistent set can
/// point at constants that move each frame.
/// </summary>
/// <remarks>
/// An immediate layout (<see cref="ShaderDrawer"/>: a shader with no material) takes every named resource from the pass,
/// storage textures and material textures included, and its material constants, written every draw, are dynamic too.
/// </remarks>
internal sealed class MaterialLayout
{
	public readonly ShaderVariant Variant;
	public readonly bool Immediate;
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

	readonly (int Slot, int Resource, int Dynamic)[][] attributeSlots = new (int, int, int)[ShaderAbi.BindlessSet][];
	readonly (int Slot, int Resource, int Dynamic)[][] materialSlots = new (int, int, int)[ShaderAbi.BindlessSet][];

	/// <summary>Bytes of dynamic constants a draw of an immediate layout writes, at most.</summary>
	public readonly int ImmediateBytes;

	/// <summary>Per set, the dynamic pass slots: slot, resource, dynamic offset index, and whether the material decides it.</summary>
	public readonly (int Slot, int Resource, int Dynamic, bool Material)[][] DynamicSlots = new (int, int, int, bool)[ShaderAbi.BindlessSet][];

	/// <summary>True when an immediate draw's constants are all dynamic, so its sets can be reused across values (<see cref="RefreshDynamic"/>).</summary>
	public readonly bool Refreshable;

	/// <summary>
	/// What the pass bound for this layout in one pass state, resolved once per context and shared by every object of
	/// the layout's signature.
	/// </summary>
	public sealed class PassState
	{
		public long Epoch = -1;

		/// <summary>The <see cref="PassBindings.AttributesEpoch"/> these bindings were resolved from.</summary>
		public long Attributes = -1;
		public readonly ulong[] Identity = new ulong[ShaderAbi.BindlessSet];

		/// <summary>Per set, identity of the attribute-decided bindings; the material's are mixed in to make <see cref="Identity"/>.</summary>
		public readonly ulong[] AttributeIdentity = new ulong[ShaderAbi.BindlessSet];
		public readonly Binding[][] Bindings = new Binding[ShaderAbi.BindlessSet][];
		public readonly uint[][] Offsets = new uint[ShaderAbi.BindlessSet][];
		public ulong Constants;

		/// <summary>Per set and dynamic offset, what the attributes alone decide, with the <see cref="PassBindings.AttributesEpoch"/> it was written for.</summary>
		public readonly (long Values, uint Offset)[][] Dynamic = new (long, uint)[ShaderAbi.BindlessSet][];

		/// <summary>With an object's attributes over the pass: which (<see cref="PassBindings.LayerEpoch"/>), and its own
		/// copies of the sets they change.</summary>
		public long Layer;
		internal Binding[][] OwnBindings;
		internal uint[][] OwnOffsets;
	}

	/// <summary>Layouts whose pass bindings are the same (materials of one shader and features) share this.</summary>
	readonly int signature;

	readonly MaterialObjectCache cache;

	/// <summary>Each pass slot's precomputed attribute token and minimum size, when it reads a constant buffer.</summary>
	internal readonly record struct PassInput( StringToken Token, int BufferSize );
	readonly PassInput[][] passInputs = new PassInput[ShaderAbi.BindlessSet][];

	internal ref readonly PassInput Input( int set, int slot ) => ref passInputs[set][slot];

	/// <summary>
	/// The pass slots (set, index into <see cref="PassSlots"/>) and attribute constants objects' attributes can set, as
	/// of <see cref="MaterialObjectCache.LayerNames"/> <c>Version</c>.
	/// </summary>
	sealed record LayerSlots( int Version, (int Set, int Slot)[] Slots, string[] Constants )
	{
		public bool Empty => Slots.Length == 0 && Constants.Length == 0;
	}

	LayerSlots layerSlots;

	LayerSlots SlotsForLayers()
	{
		var version = cache.LayerNames;
		if ( layerSlots is { } found && found.Version == version ) return found;

		var slots = new List<(int, int)>();
		for ( int set = 0; set < Sets.Length; set++ )
		{
			if ( passInputs[set] is not { } inputs ) continue;
			for ( int k = 0; k < inputs.Length; k++ )
			{
				if ( cache.IsLayerName( inputs[k].Token.Value ) ) slots.Add( (set, k) );
			}
		}

		var constants = new List<string>();
		foreach ( var (_, variables) in AttributeConstants )
		{
			foreach ( var v in variables )
			{
				if ( cache.IsLayerName( BackendContext.Token( v.Attribute ).Value ) && !constants.Contains( v.Attribute ) ) constants.Add( v.Attribute );
			}
		}

		return layerSlots = new LayerSlots( version, [.. slots], [.. constants] );
	}

	/// <summary>
	/// The bindings for a draw in pass state <paramref name="epoch"/> with the object attributes of
	/// <paramref name="resolver"/>'s <see cref="PassBindings.LayerEpoch"/> over it: the pass state itself when the object
	/// sets nothing this layout reads, else a copy with what it sets. An object's constant buffers are dynamic like the
	/// pass's, so they change the offsets rather than the identity, and the same sets are bound.
	/// </summary>
	public PassState For( long epoch, PassBindings resolver )
	{
		var pass = Pass( epoch, resolver );
		var layer = resolver.LayerEpoch;
		if ( layer == 0 || Immediate ) return pass;

		var slots = SlotsForLayers();
		if ( slots.Empty ) return pass;

		var states = resolver.LayerStates;
		if ( signature >= states.Length ) resolver.LayerStates = states = [.. states, .. new PassState[Math.Max( signature + 1 - states.Length, 16 )]];
		var state = states[signature] ??= new PassState { OwnBindings = new Binding[ShaderAbi.BindlessSet][], OwnOffsets = new uint[ShaderAbi.BindlessSet][] };
		if ( state.Epoch == epoch && state.Layer == layer ) return state;
		state.Epoch = epoch;
		state.Layer = layer;

		for ( int set = 0; set < Sets.Length; set++ )
		{
			state.Identity[set] = pass.Identity[set];
			state.Bindings[set] = pass.Bindings[set];
			state.Offsets[set] = pass.Offsets[set];
		}

		var changed = 0;
		foreach ( var (set, k) in slots.Slots )
		{
			var i = PassSlots[set][k];
			var dynamic = DynamicIndex[set][i];
			if ( !resolver.ResolveLayer( Resources[set][i], StageOf( set ), Input( set, k ), dynamic >= 0, out var binding, out var dynamicOffset ) ) continue;

			if ( (changed & (1 << set)) == 0 )
			{
				changed |= 1 << set;
				var own = state.OwnBindings[set] ??= new Binding[pass.Bindings[set].Length];
				var ownOffsets = state.OwnOffsets[set] ??= new uint[pass.Offsets[set].Length];
				pass.Bindings[set].CopyTo( own, 0 );
				pass.Offsets[set].CopyTo( ownOffsets, 0 );
				state.Bindings[set] = own;
				state.Offsets[set] = ownOffsets;
			}

			state.Bindings[set][k] = binding;
			if ( dynamic >= 0 ) state.Offsets[set][dynamic] = dynamicOffset;
		}

		for ( int set = 0; set < Sets.Length; set++ )
		{
			if ( (changed & (1 << set)) == 0 ) continue;
			var identity = 14695981039346656037UL;
			foreach ( ref readonly var binding in state.Bindings[set].AsSpan() ) identity = MaterialObject.Mix( identity, binding );
			state.Identity[set] = identity;
		}

		// The attribute constants objects set, over the pass's values: picks the object's constant buffer
		var constants = pass.Constants;
		foreach ( var attribute in slots.Constants )
		{
			if ( resolver.LayerFloats( attribute ) is not { } values ) continue;
			constants = (constants ^ (ulong)attribute.GetHashCode()) * 1099511628211UL;
			foreach ( var v in values ) constants = (constants ^ BitConverter.SingleToUInt32Bits( v )) * 1099511628211UL;
		}

		state.Constants = constants;
		return state;
	}

	/// <summary>The pass's bindings for this layout in pass state <paramref name="epoch"/> of <paramref name="resolver"/>'s context.</summary>
	public PassState Pass( long epoch, PassBindings resolver )
	{
		var states = resolver.States;
		if ( signature >= states.Length ) resolver.States = states = [.. states, .. new PassState[Math.Max( signature + 1 - states.Length, 16 )]];
		var pass = states[signature] ??= new PassState();
		if ( pass.Epoch == epoch ) return pass;

		// Resolved from these attributes already: only the material is new
		var partial = resolver.AttributesEpoch >= 0 && pass.Attributes == resolver.AttributesEpoch;

		for ( int set = 0; set < Sets.Length; set++ )
		{
			var slots = PassSlots[set];
			if ( slots is null ) continue;

			var bindings = pass.Bindings[set] ??= new Binding[slots.Length];
			var offsets = pass.Offsets[set] ??= new uint[DynamicCount[set]];
			var stage = StageOf( set );
			var resources = Resources[set];
			var identity = 14695981039346656037UL;
			if ( partial ) identity = pass.AttributeIdentity[set];
			else
			{
				foreach ( var (k, i, dynamic) in attributeSlots[set] )
				{
					bindings[k] = resolver.Resolve( this, set, k, resources[i], stage, dynamic >= 0, out var dynamicOffset );
					if ( dynamic >= 0 ) offsets[dynamic] = dynamicOffset;
					identity = MaterialObject.Mix( identity, bindings[k] );
				}

				pass.AttributeIdentity[set] = identity;
			}

			foreach ( var (k, i, dynamic) in materialSlots[set] )
			{
				bindings[k] = resolver.Resolve( this, set, k, resources[i], stage, dynamic >= 0, out var dynamicOffset );
				if ( dynamic >= 0 ) offsets[dynamic] = dynamicOffset;
				identity = MaterialObject.Mix( identity, bindings[k] );
			}

			pass.Identity[set] = identity;
		}

		// Set last so a throw (pass constants full) resolves again
		pass.Epoch = epoch;
		pass.Attributes = resolver.AttributesEpoch;
		if ( partial ) return pass;

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

	/// <summary>Rewrites an immediate draw's dynamic constants (the material's and the attribute-decided) into <paramref name="offsets"/>, leaving its other bindings.</summary>
	public void RefreshDynamic( PassBindings resolver, uint[][] offsets )
	{
		var states = resolver.States;
		if ( signature >= states.Length ) resolver.States = states = [.. states, .. new PassState[Math.Max( signature + 1 - states.Length, 16 )]];
		var pass = states[signature] ??= new PassState();
		var values = resolver.AttributesEpoch;

		for ( int set = 0; set < Sets.Length; set++ )
		{
			var dynamic = DynamicSlots[set];
			if ( dynamic is null || dynamic.Length == 0 ) continue;

			var stage = StageOf( set );
			var resources = Resources[set];
			var target = offsets[set];
			foreach ( var (slot, resource, index, material) in dynamic )
			{
				if ( material )
				{
					resolver.Resolve( this, set, slot, resources[resource], stage, true, out var offset );
					target[index] = offset;
					continue;
				}

				var held = pass.Dynamic[set] ??= [.. Enumerable.Repeat( (-1L, 0u), DynamicCount[set] )];
				if ( held[index].Values != values || values < 0 )
				{
					resolver.Resolve( this, set, slot, resources[resource], stage, true, out var offset );
					held[index] = (values, offset);
				}

				target[index] = held[index].Offset;
			}
		}
	}

	// Boxed so a thread reads one whole
	readonly object[][] placeholders = new object[ShaderAbi.BindlessSet][];

	/// <summary>The placeholder binding for a pass slot nothing sets, made once.</summary>
	public Binding Placeholder( int set, int slot )
	{
		var held = placeholders[set] ??= new object[PassSlots[set].Length];
		if ( held[slot] is Binding found ) return found;

		var made = cache.Defaults.For( Resources[set][PassSlots[set][slot]] );
		held[slot] = made;
		return made;
	}

	public MaterialLayout( MaterialObjectCache cache, ShaderVariant variant, bool immediate )
	{
		this.cache = cache;
		Variant = variant;
		Immediate = immediate;

		// Pass constant buffers move every pass, so they're dynamic, as many as the device allows over the layout; the
		// material's own and any past the limit point at constants that stay put. An immediate layout's material
		// constants change every draw, so they come first.
		var budget = cache.Gpu.Capabilities.MaxDynamicUniformBuffers;
		var dynamicMaterial = new bool[Sets.Length];
		if ( immediate )
		{
			foreach ( var r in variant.ResourceSpan )
			{
				if ( r.Kind != ShaderResourceKind.MaterialConstants || budget == 0 ) continue;
				dynamicMaterial[r.Set] = true;
				budget--;
			}
		}

		var immediateBytes = 0;
		var refreshable = immediate;
		for ( int set = 0; set < Sets.Length; set++ )
		{
			if ( !variant.Sets[set].IsValid ) continue;

			var resources = new List<ShaderResource>();
			var slots = new List<BindingSlot>();
			var dynamicIndex = new List<int>();
			foreach ( var r in variant.ResourceSpan )
			{
				if ( r.Set != set ) continue;
				resources.Add( r );
				var dynamic = r.Kind == ShaderResourceKind.MaterialConstants ? dynamicMaterial[set] : r.Kind == ShaderResourceKind.ConstantBuffer && budget > 0;
				if ( dynamic && r.Kind == ShaderResourceKind.ConstantBuffer ) budget--;
				var type = dynamic ? BindingType.UniformBufferDynamic : r.Type;
				dynamicIndex.Add( type == BindingType.UniformBufferDynamic ? DynamicCount[set]++ : -1 );
				slots.Add( new BindingSlot( r.Binding, type, r.Stages, Math.Max( r.Count, 1 ) ) );
			}

			Resources[set] = [.. resources];
			DynamicIndex[set] = [.. dynamicIndex];
			PassSlots[set] = [.. Enumerable.Range( 0, resources.Count ).Where( i => IsPassBinding( resources[i], immediate ) )];
			var inputs = passInputs[set] = new PassInput[PassSlots[set].Length];
			var attributes = new List<(int, int, int)>();
			var material = new List<(int, int, int)>();
			var stage = StageOf( set );

			// Non-dynamic constants (past the device limit) are kept by content, so values change their set
			var dynamicSlots = new List<(int, int, int, bool)>();
			for ( int k = 0; k < PassSlots[set].Length; k++ )
			{
				var i = PassSlots[set][k];
				var resource = resources[i];
				var isMaterial = FromMaterial( resource );
				var dynamic = DynamicIndex[set][i];
				(isMaterial ? material : attributes).Add( (k, i, dynamic) );
				inputs[k] = new PassInput( resource.Name is { } name ? BackendContext.Token( name ) : default, DeclaredBufferSize( stage, resource ) );
				if ( dynamic >= 0 ) dynamicSlots.Add( (k, i, dynamic, isMaterial) );
				else if ( resource.Kind is ShaderResourceKind.MaterialConstants or ShaderResourceKind.ConstantBuffer ) refreshable = false;
			}

			DynamicSlots[set] = [.. dynamicSlots];
			attributeSlots[set] = attributes.ToArray();
			materialSlots[set] = material.ToArray();
			Sets[set] = cache.Layout( slots );
			if ( immediate && stage is not null ) immediateBytes += stage.MaterialBuffer.Size + 256;
		}

		ImmediateBytes = immediateBytes;
		Refreshable = refreshable;

		var attributeConstants = new List<(ShaderStage, ShaderVariable[])>();
		foreach ( var stage in immediate ? [] : new[] { variant.Vertex, variant.Pixel, variant.Compute, variant.Task, variant.Mesh } )
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

		// Immediate layouts resolve their own pass state every draw. Others that bind the same pass resources the same
		// way resolve them once per pass state between them.
		if ( immediate )
		{
			signature = cache.Signature( null );
			return;
		}

		var key = new System.Text.StringBuilder();
		for ( int set = 0; set < Sets.Length; set++ )
		{
			if ( PassSlots[set] is not { } slots ) continue;
			key.Append( set ).Append( ':' ).Append( Sets[set].Index ).Append( ';' );
			for ( int k = 0; k < slots.Length; k++ )
			{
				var i = slots[k];
				var r = Resources[set][i];
				key.Append( r.Name ).Append( '/' ).Append( (int)r.Kind ).Append( '/' ).Append( (int)r.Dimension ).Append( '/' ).Append( r.Variable?.RenderStateId ?? 0 ).Append( '/' ).Append( DynamicIndex[set][i] ).Append( '/' ).Append( r.Binding ).Append( ';' );
				if ( r.Kind == ShaderResourceKind.ConstantBuffer ) key.Append( Input( set, k ).BufferSize ).Append( ';' );
			}
		}

		foreach ( var (_, variables) in AttributeConstants )
		{
			foreach ( var v in variables ) key.Append( v.Attribute ).Append( ',' );
		}

		signature = cache.Signature( key.ToString() );
	}

	static int DeclaredBufferSize( ShaderStage stage, in ShaderResource resource )
	{
		var size = 0;
		if ( resource.Kind == ShaderResourceKind.ConstantBuffer && stage is not null )
		{
			foreach ( var layout in stage.Program.ConstantBuffers )
			{
				if ( layout.Name == resource.Name ) size = Math.Max( size, layout.Size );
			}
		}
		return size;
	}

	static bool IsPassBinding( in ShaderResource resource, bool immediate ) => resource.Kind switch
	{
		ShaderResourceKind.ConstantBuffer or ShaderResourceKind.Buffer or ShaderResourceKind.Counter => true,
		ShaderResourceKind.Texture => immediate || resource.Variable.Source != VariableSource.Material,
		ShaderResourceKind.MaterialConstants or ShaderResourceKind.StorageTexture => immediate,
		_ => false,
	};

	// What an immediate draw binds for these comes from its material rather than the attributes
	static bool FromMaterial( in ShaderResource resource ) => resource.Kind == ShaderResourceKind.MaterialConstants || resource.Kind == ShaderResourceKind.Texture && resource.Variable?.Source == VariableSource.Material;

	/// <summary>The stage whose material constants a set holds, as <see cref="BackendContext"/> binds them (a mesh stage's in set 0 where there's no vertex stage).</summary>
	public ShaderStage StageOf( int set ) => set == ShaderAbi.FragmentSet ? Variant.Pixel : Variant.Compute ?? Variant.Vertex ?? Variant.Mesh;
}

/// <summary>A persistent set an object made, and the last frame anything bound it.</summary>
internal sealed class SetEntry( BindingSetHandle handle )
{
	public readonly BindingSetHandle Handle = handle;
	public long LastUsed;
	public volatile bool Dead;
}

/// <summary>An object's material constant buffers for one value of the constants it reads from attributes.</summary>
internal sealed class ConstantVariant( long[] offsets, int[] sizes )
{
	public readonly long[] Offsets = offsets;
	public readonly int[] Sizes = sizes;
	public long LastUsed;
	public volatile bool Dead;

	/// <summary>The key it holds the constants of now: a variant idle long enough is written again for another key.</summary>
	public ulong Key;
}

/// <summary>
/// A material drawn one way: a material's variant (or a shader's, with no material) for a mode and dynamic combos, a
/// vertex layout and target formats. It holds its pipeline, its constant buffers (one per value of the constants it
/// reads from attributes, written once), its resolved material bindings, and persistent descriptor sets keyed by what
/// the pass bound around it. Binding it is a pipeline bind and a set bind per stage with dynamic offsets; sets are
/// written only when a pass binds resources this object hasn't seen with it before. See docs/reference/borealis-materials.md.
/// </summary>
/// <remarks>
/// Sets and constants are shared between recording contexts, under the object's lock when one is made; what a context
/// resolved for its pass state is its own, so contexts on several threads can bind one object.
/// </remarks>
internal sealed class MaterialObject
{
	readonly MaterialObjectCache cache;
	public readonly BackendMaterial Material;
	public readonly int MaterialVersion;
	public readonly MaterialLayout Layout;
	public readonly PipelineHandle Pipeline;
	internal bool OwnsPipeline { get; init; }
	public readonly bool DepthOnly;
	public readonly bool UncompressedTangentFrame;

	/// <summary>Per set, the bindings that never change for this object (pass bindings left default until resolved).</summary>
	readonly Binding[][] template = new Binding[ShaderAbi.BindlessSet][];

	/// <summary>Per set, which binding is the material constant buffer, when the object writes it once per value.</summary>
	readonly int[] materialSlot = [-1, -1, -1, -1];
	readonly int[] constantSize = new int[ShaderAbi.BindlessSet];
	readonly bool hasMaterialConstants;

	/// <summary>Material constant buffers per value of the attribute constants.</summary>
	readonly Dictionary<ulong, ConstantVariant> constantVariants = new();

	/// <summary>Persistent sets per set index, by a hash of what the pass bound in them.</summary>
	readonly Dictionary<(int Set, ulong Identity), SetEntry> sets = new();

	/// <summary>What one context resolved for its last pass state: the sets to bind and their dynamic offsets.</summary>
	sealed class Resolved
	{
		public long Epoch = -1;
		public long Layer;
		public readonly SetEntry[] Bound = new SetEntry[ShaderAbi.BindlessSet];
		public readonly uint[][] Offsets = [noOffsets, noOffsets, noOffsets, noOffsets];

		/// <summary>
		/// Per set index, the last few sets this context bound and their identities: Borealis's per-frame buffers go round
		/// a ring, so a pass binds one of a few sets frame after frame, found here without the object's lock.
		/// </summary>
		public readonly ulong[] LastIdentity = new ulong[ShaderAbi.BindlessSet * Remembered];
		public readonly SetEntry[] LastSets = new SetEntry[ShaderAbi.BindlessSet * Remembered];
		public readonly int[] Next = new int[ShaderAbi.BindlessSet];
		public ulong LastConstantsKey;
		public int ValuesVersion;
		public ConstantVariant LastConstants;

		/// <summary>Per material state of an immediate object: the structure, material version and values its last resolve was for.</summary>
		public long Structure = -1, Values = -1;
		public int MaterialVersion = -1;
		ConditionalWeakTable<BackendMaterial, Resolved> materials;
		BackendMaterial lastMaterial;
		Resolved lastState;

		public Resolved ForMaterial( BackendMaterial material )
		{
			if ( ReferenceEquals( lastMaterial, material ) ) return lastState;

			materials ??= new();
			var found = materials.GetValue( material, static _ => new Resolved() );
			(lastMaterial, lastState) = (material, found);
			return found;
		}

		/// <summary>The structures an object's draws of one material come back to: a pass binds the same few frame after frame.</summary>
		public const int Leaves = 8;

		/// <summary>
		/// The structures its draws without a material come back to. A drawer's draws and dispatches of one shader share them:
		/// a skinning dispatch a mesh's vertex buffer, a frame buffer copy's mip a dispatch, each a structure of its own, come
		/// to more than <see cref="Leaves"/> a frame, and going round fewer resolves every bind again.
		/// </summary>
		public const int ImmediateLeaves = 64;

		Resolved[] leaves;
		long[] leafKeys;
		int nextLeaf;

		/// <summary>This state's under <paramref name="structure"/>, else a new one, of the last <paramref name="count"/> kept.</summary>
		public Resolved ForStructure( long structure, int count )
		{
			var held = leaves ??= new Resolved[count];
			var keys = leafKeys ??= [.. Enumerable.Repeat( -1L, count )];
			var at = Array.IndexOf( keys, structure );
			if ( at >= 0 ) return held[at];

			var made = held[nextLeaf] ??= new Resolved();
			keys[nextLeaf] = structure;
			nextLeaf = (nextLeaf + 1) % held.Length;
			made.Structure = -1;
			return made;
		}
	}

	const int Remembered = 4;
	Resolved[] contexts = [];
	static readonly uint[] noOffsets = [];
	[ThreadStatic] static Binding[] scratch;

	/// <summary>Sets written so far, for the benchmark and tests.</summary>
	public int SetsWritten => setsWritten;
	int setsWritten;

	/// <summary>Sets this object holds now.</summary>
	public int SetCount
	{
		get
		{
			lock ( sets ) return sets.Count;
		}
	}

	public MaterialObject( MaterialObjectCache cache, BackendMaterial material, MaterialLayout layout, PipelineHandle pipeline, bool depthOnly, bool uncompressedTangentFrame )
	{
		this.cache = cache;
		Material = material;
		MaterialVersion = material?.LayoutVersion ?? 0;
		Layout = layout;
		Pipeline = pipeline;
		DepthOnly = depthOnly;
		UncompressedTangentFrame = uncompressedTangentFrame;

		var defaults = cache.Defaults;
		for ( int set = 0; set < template.Length; set++ )
		{
			var resources = layout.Resources[set];
			if ( resources is null ) continue;

			var bindings = new Binding[resources.Length];
			for ( int i = 0; i < resources.Length; i++ )
			{
				ref readonly var resource = ref resources[i];
				if ( Array.IndexOf( layout.PassSlots[set], i ) >= 0 ) continue;

				switch ( resource.Kind )
				{
					case ShaderResourceKind.MaterialConstants:
						materialSlot[set] = i;
						constantSize[set] = layout.StageOf( set ).MaterialBuffer.Size;
						bindings[i] = Binding.Uniform( resource.Binding, cache.MaterialBuffer, 0, constantSize[set] );
						break;

					case ShaderResourceKind.Texture when resource.Variable.Source == VariableSource.Material:
						bindings[i] = MaterialTexture( resource );
						break;

					default:
						bindings[i] = defaults.For( resource );
						break;
				}
			}

			template[set] = bindings;
		}

		hasMaterialConstants = Array.Exists( materialSlot, s => s >= 0 );
	}

	/// <summary>Whether the material changed since this object was made, so it has to be made again.</summary>
	public bool Stale => Material is not null && Material.LayoutVersion != MaterialVersion;

	// The material's values version (BackendMaterial.ValuesVersion): values set in code reach the object's constants, which a
	// context resolves again when it moves on
	int ValuesVersion => Material?.ValuesVersion ?? 0;

	Binding MaterialTexture( in ShaderResource resource )
	{
		BackendTexture texture = null;
		Material?.Textures.TryGetValue( resource.Variable.Name, out texture );
		return BackendContext.TextureBinding( cache.Gpu, cache.Defaults, texture, texture?.Layout ?? ImageLayout.Undefined, resource );
	}

	Resolved ContextState( int slot )
	{
		var states = contexts;
		if ( slot < states.Length && states[slot] is { } found ) return found;

		lock ( sets )
		{
			if ( slot >= contexts.Length ) contexts = [.. contexts, .. new Resolved[Math.Max( slot + 1 - contexts.Length, 4 )]];
			return contexts[slot] ??= new Resolved();
		}
	}

	/// <summary>
	/// Bind the pipeline and sets for a draw in pass state <paramref name="passEpoch"/>; pass bindings are resolved through
	/// <paramref name="pass"/> only when the epoch changed since this object last drew in its context.
	/// </summary>
	public void Bind( CommandList list, ref PipelineHandle bound, long passEpoch, PassBindings pass )
	{
		if ( bound != Pipeline )
		{
			list.SetPipeline( Pipeline );
			bound = Pipeline;
		}

		var state = ContextState( pass.Slot );
		if ( passEpoch != state.Epoch || pass.LayerEpoch != state.Layer || state.ValuesVersion != ValuesVersion || AnyDead( state ) )
		{
			Resolve( state, passEpoch, pass );
		}

		for ( int set = 0; set < state.Bound.Length; set++ )
		{
			if ( state.Bound[set] is not { } entry ) continue;
			list.SetBindingSet( set, entry.Handle, state.Offsets[set] );
		}
	}

	/// <summary>Binds an immediate draw; sets are reused per attribute structure and only constants rewritten, so pass bindings resolve whole only when structure, material or a bound set changed.</summary>
	public void BindImmediate( CommandList list, ref PipelineHandle bound, BackendMaterial material, int materialVersion, PassBindings pass )
	{
		if ( bound != Pipeline )
		{
			list.SetPipeline( Pipeline );
			bound = Pipeline;
		}

		var context = ContextState( pass.Slot );
		var node = material is null ? context : context.ForMaterial( material );
		var structure = pass.Structure;
		var state = structure >= 0 ? node.ForStructure( structure, material is null ? Resolved.ImmediateLeaves : Resolved.Leaves ) : node;
		var values = pass.AttributesEpoch;
		var current = structure >= 0 && state.Structure == structure && state.MaterialVersion == materialVersion && !AnyDead( state );
		if ( !current || (state.Values != values && !Layout.Refreshable) )
		{
			Resolve( state, cache.NextEpoch(), pass );
			(state.Structure, state.MaterialVersion, state.Values) = (structure, materialVersion, values);
		}
		else if ( state.Values != values )
		{
			Layout.RefreshDynamic( pass, state.Offsets );
			state.Values = values;
		}

		var frame = cache.Frame;
		for ( int set = 0; set < state.Bound.Length; set++ )
		{
			if ( state.Bound[set] is not { } entry ) continue;
			entry.LastUsed = frame;
			list.SetBindingSet( set, entry.Handle, state.Offsets[set] );
		}
	}

	static bool AnyDead( Resolved state )
	{
		foreach ( var entry in state.Bound )
		{
			if ( entry is { Dead: true } ) return true;
		}

		return false;
	}

	void Resolve( Resolved state, long passEpoch, PassBindings resolver )
	{
		cache.CountResolve();
		state.Epoch = passEpoch;
		state.Layer = resolver.LayerEpoch;
		state.ValuesVersion = ValuesVersion;
		var frame = cache.Frame;
		var pass = Layout.For( passEpoch, resolver );
		// A variant per value of the attribute constants and of the material's own values
		var constantsKey = pass.Constants ^ (ulong)(uint)state.ValuesVersion * 0x9E3779B97F4A7C15UL;
		var constants = hasMaterialConstants ? ConstantVariantFor( state, constantsKey, resolver, frame ) : null;

		for ( int set = 0; set < template.Length; set++ )
		{
			state.Bound[set] = null;
			var resources = Layout.Resources[set];
			if ( resources is null || (set == ShaderAbi.FragmentSet && DepthOnly) ) continue;

			// Identity: what the pass bound, as handles, and which constant buffer variant; dynamic offsets are given at bind time
			var identity = pass.Identity[set];
			if ( materialSlot[set] >= 0 && constants is not null ) identity = (identity ^ (ulong)constants.Offsets[set]) * 1099511628211UL;
			var passOffsets = pass.Offsets[set];
			if ( passOffsets is null || passOffsets.Length == 0 ) state.Offsets[set] = noOffsets;
			else
			{
				// Its own copy: the layout's resolves of other pass states change theirs in place
				var own = state.Offsets[set];
				if ( own.Length != passOffsets.Length ) state.Offsets[set] = own = new uint[passOffsets.Length];
				passOffsets.CopyTo( own, 0 );
			}

			// A set this context bound lately, if the pass bound the same: the usual case from frame to frame
			if ( RememberedSet( state, set, identity ) is { } last )
			{
				last.LastUsed = frame;
				state.Bound[set] = last;
				continue;
			}

			SetEntry entry;
			lock ( sets )
			{
				if ( !sets.TryGetValue( (set, identity), out entry ) || entry.Dead )
				{
					var bindings = (scratch ??= new Binding[128]).AsSpan( 0, resources.Length );
					template[set].CopyTo( bindings );
					var slots = Layout.PassSlots[set];
					for ( int k = 0; k < slots.Length; k++ ) bindings[slots[k]] = pass.Bindings[set][k];
					if ( materialSlot[set] >= 0 && constants is not null ) bindings[materialSlot[set]].Offset = constants.Offsets[set];

					entry = new SetEntry( cache.Gpu.CreateBindingSet( Layout.Sets[set], bindings ) );
					sets[(set, identity)] = entry;
					setsWritten++;
					cache.CountSetWritten();
				}
			}

			entry.LastUsed = frame;
			state.Bound[set] = entry;
			var slot = set * Remembered + state.Next[set];
			state.Next[set] = (state.Next[set] + 1) % Remembered;
			state.LastSets[slot] = entry;
			state.LastIdentity[slot] = identity;
		}
	}

	static SetEntry RememberedSet( Resolved state, int set, ulong identity )
	{
		for ( int i = set * Remembered, end = i + Remembered; i < end; i++ )
		{
			if ( state.LastIdentity[i] == identity && state.LastSets[i] is { Dead: false } entry ) return entry;
		}

		return null;
	}

	internal static ulong Mix( ulong hash, in Binding binding )
	{
		hash = (hash ^ (ulong)binding.Type ^ ((ulong)binding.Layout << 8) ^ ((ulong)binding.ArrayElement << 16)) * 1099511628211UL;
		hash = (hash ^ binding.Buffer.Index ^ ((ulong)binding.Buffer.Generation << 32)) * 1099511628211UL;
		hash = (hash ^ (ulong)binding.Size) * 1099511628211UL;
		hash = (hash ^ binding.Texture.Index ^ ((ulong)binding.Texture.Generation << 32)) * 1099511628211UL;
		hash = (hash ^ binding.View.Index ^ ((ulong)binding.View.Generation << 32)) * 1099511628211UL;
		hash = (hash ^ binding.Sampler.Index ^ ((ulong)binding.Sampler.Generation << 32)) * 1099511628211UL;
		hash = (hash ^ (binding.Srgb ? 1UL : 0UL) ^ ((ulong)binding.Offset << 1)) * 1099511628211UL;
		return hash;
	}

	/// <summary>
	/// The material constant buffers for the attribute constants' current values: filled and kept the first time each
	/// value is seen.
	/// </summary>
	ConstantVariant ConstantVariantFor( Resolved state, ulong key, PassBindings pass, long frame )
	{
		if ( state.LastConstants is { Dead: false } last && key == state.LastConstantsKey && last.Key == key )
		{
			last.LastUsed = frame;
			return last;
		}

		ConstantVariant variant;
		lock ( constantVariants )
		{
			if ( !constantVariants.TryGetValue( key, out variant ) || variant.Dead )
			{
				variant = ReuseConstantVariant( key, pass, frame ) ?? MakeConstantVariant( pass );
				variant.Key = key;
				constantVariants[key] = variant;
			}
		}

		variant.LastUsed = frame;
		state.LastConstantsKey = key;
		state.LastConstants = variant;
		return variant;
	}

	// Under the variants' lock: the pass a variant's constants are written for, read through one delegate made once
	PassBindings writingPass;
	Func<ShaderVariable, float[]> constantValue;
	float[] shaderVersionValue, tangentFrameValue;

	float[] ConstantValue( ShaderVariable variable ) => variable.Source switch
	{
		VariableSource.Material or VariableSource.MaterialAndExpression => Material?.Value( variable.Name ),
		VariableSource.ShaderVersion => shaderVersionValue ??= [Material?.ShaderVersion ?? 0],
		VariableSource.Attribute when variable.Attribute == "UncompressedTangentFrame" => tangentFrameValue ??= [UncompressedTangentFrame ? 1 : 0],
		VariableSource.Attribute => (writingPass.LayerEpoch != 0 ? writingPass.LayerFloats( variable.Attribute ) : null) ?? writingPass.Floats( variable.Attribute ),
		_ => null,
	};

	void WriteConstants( ConstantVariant variant, PassBindings pass )
	{
		writingPass = pass;
		for ( int set = 0; set < template.Length; set++ )
		{
			if ( materialSlot[set] < 0 ) continue;
			Layout.StageOf( set ).WriteMaterialConstants( cache.MaterialConstantsAt( variant.Offsets[set], variant.Sizes[set] ), constantValue ??= ConstantValue, Material is null ? default : Material.Features );
		}

		writingPass = null;
	}

	/// <summary>
	/// A variant nothing has used since before the frames in flight, written again with this pass's constants: a material
	/// whose values code sets every frame, or attributes that change every frame, take one from a few kept rather than a
	/// new one each frame, and their sets (which point at the same constants) with it. Null when there's none. Hold the
	/// variants' lock.
	/// </summary>
	ConstantVariant ReuseConstantVariant( ulong key, PassBindings pass, long frame )
	{
		var idle = frame - cache.Gpu.FramesInFlight - 1;
		foreach ( var (oldKey, old) in constantVariants )
		{
			if ( old.Dead || old.LastUsed > idle || oldKey == key ) continue;
			constantVariants.Remove( oldKey );
			WriteConstants( old, pass );
			return old;
		}

		return null;
	}

	ConstantVariant MakeConstantVariant( PassBindings pass )
	{
		var offsets = new long[template.Length];
		var sizes = new int[template.Length];
		for ( int set = 0; set < template.Length; set++ )
		{
			if ( materialSlot[set] < 0 ) continue;
			sizes[set] = constantSize[set];
			offsets[set] = cache.AllocateMaterialConstants( constantSize[set], out _ );
		}

		var variant = new ConstantVariant( offsets, sizes );
		WriteConstants( variant, pass );
		return variant;
	}

	/// <summary>
	/// Drop sets and constants nothing has bound since <paramref name="before"/>: they're marked dead at once, so no
	/// context binds them again, and handed to <paramref name="retireSet"/> and <paramref name="retireConstants"/> to free once the frames that may have used them
	/// are done.
	/// </summary>
	public int Evict( long before, Action<SetEntry> retireSet, Action<ConstantVariant> retireConstants )
	{
		var evicted = 0;
		lock ( sets )
		{
			// Removing while enumerating is allowed for a dictionary, so nothing is listed first
			foreach ( var (key, entry) in sets )
			{
				if ( entry.LastUsed >= before ) continue;
				entry.Dead = true;
				retireSet( entry );
				sets.Remove( key );
				evicted++;
			}
		}

		lock ( constantVariants )
		{
			foreach ( var (key, variant) in constantVariants )
			{
				if ( variant.LastUsed >= before ) continue;
				variant.Dead = true;
				retireConstants( variant );
				constantVariants.Remove( key );
			}
		}

		return evicted;
	}

	/// <summary>Retire every set and constant buffer: the object is going away.</summary>
	public void Retire( Action<SetEntry> retireSet, Action<ConstantVariant> retireConstants ) => Evict( long.MaxValue, retireSet, retireConstants );
}

/// <summary>
/// The device's material objects: their layouts, a persistent buffer of material constants written once per object and
/// attribute value, and a buffer of pass constants with a region per frame in flight, which dynamic offsets point
/// into. Objects are made the first time a material draws a way and kept until the material changes; sets and
/// constants nothing has bound for <see cref="EvictAfterFrames"/> frames are freed, so the textures and buffers they
/// point at can go and the constants stay bounded. Thread-safe.
/// </summary>
internal sealed class MaterialObjectCache : IDisposable
{
	public readonly Device Gpu;
	public readonly ShaderDefaults Defaults;
	internal readonly object Identity = new();
	readonly Dictionary<string, BindingLayoutHandle> layouts = new();
	readonly Dictionary<(ShaderVariant Variant, bool Immediate), MaterialLayout> variantLayouts = new();
	// Found without a lock by every recording thread; changed under its own lock
	readonly System.Collections.Concurrent.ConcurrentDictionary<Key, MaterialObject> objects = new( new KeyComparer() );
	readonly Dictionary<string, int> signatures = new();
	int signatureCount, slotCount;

	/// <summary>Frames a set or constant buffer stays after the last frame that bound it.</summary>
	public const int EvictAfterFrames = 32;

	/// <summary>Material constants, written once per object and attribute value; bound with dynamic offsets.</summary>
	public readonly BufferHandle MaterialBuffer;
	const long MaterialBufferSize = 32 << 20;
	readonly ConstantHeap materialHeap;

	/// <summary>Pass constants (view, lighting...), copied in once per pass state, a region per frame in flight.</summary>
	public readonly BufferHandle PassBuffer;
	const long PassRegionSize = 16 << 20;
	long passUsed;
	ulong passFrame = ulong.MaxValue;
	readonly Lock passLock = new();

	readonly int alignment;
	long epochs;

	// Sweeping: done once a frame, on the first epoch of it
	long sweptFrame = -1;
	readonly Lock sweepLock = new();
	readonly List<(long Frame, SetEntry Set, ConstantVariant Constants, long Kept, int KeptSize)> retired = new();
	Queue<(long Frame, PipelineHandle Pipeline)> retiredPipelines;

	/// <summary>The device frame now, as sets and constants record their last use.</summary>
	public long Frame => (long)Gpu.FrameNumber;

	/// <summary>A layout signature's index into each context's pass states; every immediate layout gets its own.</summary>
	internal int Signature( string key )
	{
		lock ( signatures )
		{
			if ( key is null ) return signatureCount++;
			if ( signatures.TryGetValue( key, out var index ) ) return index;
			return signatures[key] = signatureCount++;
		}
	}

	/// <summary>A new recording context's slot in every object.</summary>
	public int NewSlot() => Interlocked.Increment( ref slotCount ) - 1;

	readonly HashSet<uint> layerNames = new();
	int layerNamesVersion;

	/// <summary>
	/// Moves on whenever an object's attributes set a name none had before: layouts then look again for the pass
	/// bindings and attribute constants objects can change.
	/// </summary>
	public int LayerNames => Volatile.Read( ref layerNamesVersion );

	/// <summary>Whether an object's attributes have set this name (a <see cref="StringToken"/>'s value).</summary>
	public bool IsLayerName( uint name )
	{
		lock ( layerNames ) return layerNames.Contains( name );
	}

	internal void AddLayerNames( HashSet<uint> names )
	{
		lock ( layerNames )
		{
			var added = false;
			foreach ( var name in names ) added |= layerNames.Add( name );
			if ( added ) Interlocked.Increment( ref layerNamesVersion );
		}
	}

	/// <summary>Descriptor sets written, for the benchmark and tests.</summary>
	public int SetsWritten => setsWritten;
	int setsWritten;

	internal void CountSetWritten() => Interlocked.Increment( ref setsWritten );

	/// <summary>Times an object resolved what a context's pass binds for it, for tests.</summary>
	public long Resolves => Interlocked.Read( ref resolves );
	long resolves;

	internal void CountResolve() => Interlocked.Increment( ref resolves );

	/// <summary>Sets and constant buffers freed for not being bound, for tests.</summary>
	public int Evicted { get; private set; }

	/// <summary>Objects made so far, including those made again for a changed material.</summary>
	public int Made => Volatile.Read( ref objectsMade );
	int objectsMade;

	/// <summary>Objects made.</summary>
	public int Count
	{
		get
		{
			lock ( objects ) return objects.Count;
		}
	}

	/// <summary>Sets held by every object now.</summary>
	public int SetCount
	{
		get
		{
			lock ( objects ) return objects.Values.Sum( o => o.SetCount );
		}
	}

	/// <summary>Each object's material, layout and whether it draws depth only, for reporting what draws bind.</summary>
	internal (BackendMaterial Material, MaterialLayout Layout, bool DepthOnly)[] Describe()
	{
		lock ( objects ) return [.. objects.Values.Select( o => (o.Material, o.Layout, o.DepthOnly) )];
	}

	/// <summary>Bytes of material and kept pass constants in use.</summary>
	public long ConstantBytes => materialHeap.Used;

	/// <summary>The materials objects were made for.</summary>
	public BackendMaterial[] Materials
	{
		get
		{
			lock ( objects ) return [.. objects.Keys.Select( k => k.Material ).Where( m => m is not null ).Distinct()];
		}
	}

	public MaterialObjectCache( Device gpu, ShaderDefaults defaults )
	{
		Gpu = gpu;
		Defaults = defaults;
		alignment = Math.Max( gpu.Capabilities.UniformAlignment, 16 );
		MaterialBuffer = gpu.CreateBuffer( new BufferInfo { Size = MaterialBufferSize, Usage = BufferUsage.Uniform, Memory = MemoryLocation.Upload, Name = "Material constants" } );
		PassBuffer = gpu.CreateBuffer( new BufferInfo { Size = PassRegionSize * gpu.FramesInFlight, Usage = BufferUsage.Uniform, Memory = MemoryLocation.Upload, Name = "Pass constants" } );
		materialHeap = new ConstantHeap( MaterialBufferSize, alignment );
	}

	/// <summary>
	/// A new pass state: every object resolves its pass bindings again the next time it binds. The first of a frame also
	/// frees what's been retired long enough, and now and then evicts what nothing binds any more.
	/// </summary>
	public long NextEpoch()
	{
		if ( sweptFrame != Frame ) Sweep();
		return Interlocked.Increment( ref epochs );
	}

	void Sweep()
	{
		lock ( sweepLock )
		{
			var frame = Frame;
			if ( sweptFrame == frame ) return;
			sweptFrame = frame;

			// Freed two frames after they were retired: a context that bound one just before it was marked dead submitted
			// within its frame, and the device's own deferral covers the GPU from there
			var due = frame - 2;
			while ( retiredPipelines is not null && retiredPipelines.TryPeek( out var pipeline ) && pipeline.Frame <= due )
			{
				Gpu.Destroy( retiredPipelines.Dequeue().Pipeline );
			}

			var i = 0;
			for ( ; i < retired.Count && retired[i].Frame <= due; i++ )
			{
				var (_, set, constants, kept, keptSize) = retired[i];
				if ( set is not null ) Gpu.Destroy( set.Handle );
				if ( constants is not null )
				{
					for ( int s = 0; s < constants.Offsets.Length; s++ )
					{
						if ( constants.Sizes[s] > 0 ) materialHeap.Free( constants.Offsets[s], constants.Sizes[s] );
					}
				}

				if ( keptSize > 0 ) materialHeap.Free( kept, keptSize );
			}

			retired.RemoveRange( 0, i );

			if ( frame % 8 != 0 ) return;

			var before = frame - EvictAfterFrames;
			if ( before <= 0 ) return;

			lock ( objects ) sweeping.AddRange( allObjects );
			retireFrame = frame;
			foreach ( var o in sweeping ) Evicted += o.Evict( before, retireSet ??= RetireSet, retireConstants ??= RetireConstants );
			sweeping.Clear();

			lock ( keptConstants )
			{
				foreach ( var (key, entry) in keptConstants )
				{
					if ( !entry.TryRetire( before ) ) continue;
					retired.Add( (frame, null, null, entry.Offset, key.Size) );
					keptConstants.Remove( key );
				}
			}
		}
	}

	internal BindingLayoutHandle Layout( List<BindingSlot> slots )
	{
		var key = string.Join( ';', slots.ConvertAll( s => $"{s.Binding},{(int)s.Type},{(int)s.Stages},{s.Count}" ) );
		lock ( layouts )
		{
			if ( layouts.TryGetValue( key, out var layout ) ) return layout;
			return layouts[key] = Gpu.CreateBindingLayout( [.. slots] );
		}
	}

	/// <summary>
	/// How a material object draws: the material (none for a shader drawn on its own), its variant, the vertex layout
	/// (Borealis's <see cref="VertexLayout"/> or a <see cref="VertexStreamLayout"/>), the targets' formats, the topology,
	/// whether the opaque pass's normals target follows colour, the tools and overlay stencil overrides, and whether its
	/// draws are redrawn every frame (<see cref="RedrawnEveryFrame"/>: its pipeline may still be being made).
	/// </summary>
	internal readonly record struct Key( BackendMaterial Material, ShaderVariant Variant, object Layout, Format Color, Format Depth, int Samples, bool DepthOnly, bool UncompressedTangentFrame, PrimitiveTopology Topology = PrimitiveTopology.TriangleList, bool Normals = false, ToolsPass Tools = ToolsPass.None, bool Redrawn = false, OverlayStencil Stencil = OverlayStencil.None )
	{
		/// <summary>Equal by reference for the material, variant and layout (layouts are made once each).</summary>
		public bool Same( in Key other ) => ReferenceEquals( Material, other.Material ) && ReferenceEquals( Variant, other.Variant ) && ReferenceEquals( Layout, other.Layout )
			&& Color == other.Color && Depth == other.Depth && Samples == other.Samples && DepthOnly == other.DepthOnly && UncompressedTangentFrame == other.UncompressedTangentFrame && Topology == other.Topology && Normals == other.Normals
			&& Tools == other.Tools && Redrawn == other.Redrawn && Stencil == other.Stencil;
	}

	sealed class KeyComparer : IEqualityComparer<Key>
	{
		public bool Equals( Key a, Key b ) => a.Same( b );

		public int GetHashCode( Key k ) => HashCode.Combine( RuntimeHelpers.GetHashCode( k.Material ), RuntimeHelpers.GetHashCode( k.Variant ), RuntimeHelpers.GetHashCode( k.Layout ),
			(int)k.Color, (int)k.Depth, k.Samples, (k.DepthOnly ? 1 : 0) | (k.UncompressedTangentFrame ? 2 : 0) | (k.Normals ? 4 : 0) | (int)k.Tools << 4 | (k.Redrawn ? 8 : 0) | (int)k.Stencil << 8, (int)k.Topology );
	}

	/// <summary>The layout a variant's objects share, made once.</summary>
	public MaterialLayout LayoutOf( ShaderVariant variant, bool immediate )
	{
		lock ( variantLayouts )
		{
			if ( variantLayouts.TryGetValue( (variant, immediate), out var layout ) ) return layout;
			return variantLayouts[(variant, immediate)] = new MaterialLayout( this, variant, immediate );
		}
	}

	/// <summary>
	/// The object for a material drawn this way, made the first time (or again, after its material changed), with the
	/// pipeline <paramref name="pipeline"/> makes for the layout's sets. A pipeline made only for this object must pass
	/// <paramref name="ownsPipeline"/> so the cache can destroy it; shared device pipelines are owned by the device cache.
	/// </summary>
	public MaterialObject Get<TState>( in Key key, bool immediate, TState state, Func<TState, MaterialLayout, PipelineHandle> pipeline, bool ownsPipeline = false )
	{
		if ( objects.TryGetValue( key, out var found ) && !found.Stale ) return found;

		var layout = LayoutOf( key.Variant, immediate );
		var handle = pipeline( state, layout );
		MaterialObject made;
		try
		{
			made = new MaterialObject( this, key.Material, layout, handle, key.DepthOnly, key.UncompressedTangentFrame ) { OwnsPipeline = ownsPipeline };
		}
		catch
		{
			if ( ownsPipeline ) Gpu.Destroy( handle );
			throw;
		}

		Interlocked.Increment( ref objectsMade );
		MaterialObject replaced;
		lock ( objects )
		{
			if ( objects.TryGetValue( key, out replaced ) && !replaced.Stale )
			{
				found = replaced;
			}
			else
			{
				found = made;
				objects[key] = made;
				allObjects.Add( made );
				if ( replaced is not null ) allObjects.Remove( replaced );
			}
		}

		if ( !ReferenceEquals( found, made ) )
		{
			if ( ownsPipeline ) Gpu.Destroy( handle );
			return found;
		}

		if ( replaced is not null ) Retire( replaced );
		return made;
	}

	/// <summary>Drop a material's objects, after it changed: they're made again, with its new values, when it next draws.</summary>
	public void Invalidate( GpuMaterial material )
	{
		List<MaterialObject> dropped = null;
		lock ( objects )
		{
			List<Key> stale = null;
			foreach ( var (key, value) in objects )
			{
				if ( !ReferenceEquals( key.Material, material ) ) continue;
				(dropped ??= new()).Add( value );
				(stale ??= new()).Add( key );
			}

			foreach ( var key in stale ?? [] ) objects.TryRemove( key, out _ );
			foreach ( var value in dropped ?? [] ) allObjects.Remove( value );
		}

		foreach ( var value in dropped ?? [] ) Retire( value );
	}

	/// <summary>Retire an object's resources; never called holding the objects' lock, which a sweep takes after its own.</summary>
	void Retire( MaterialObject value )
	{
		lock ( sweepLock )
		{
			retireFrame = Frame;
			value.Retire( retireSet ??= RetireSet, retireConstants ??= RetireConstants );
			if ( value.OwnsPipeline ) (retiredPipelines ??= new()).Enqueue( (retireFrame, value.Pipeline) );
		}
	}

	// Retiring under sweepLock, for the frame set before: delegates made once, so a sweep or retire allocates none
	long retireFrame;
	Action<SetEntry> retireSet;
	Action<ConstantVariant> retireConstants;

	void RetireSet( SetEntry set ) => retired.Add( (retireFrame, set, null, 0, 0) );

	void RetireConstants( ConstantVariant constants ) => retired.Add( (retireFrame, null, constants, 0, 0) );

	// Every object in objects, in a list a sweep copies under the objects' lock (a dictionary's values allocate), and that copy
	readonly HashSet<MaterialObject> allObjects = new( ReferenceEqualityComparer.Instance );
	readonly List<MaterialObject> sweeping = new();

	/// <summary>A material constant buffer's space, written again (a reused <see cref="ConstantVariant"/>).</summary>
	public Span<byte> MaterialConstantsAt( long offset, int size ) => Gpu.Map( MaterialBuffer ).Slice( (int)offset, size );

	/// <summary>Space for one material constant buffer, until it's evicted.</summary>
	public long AllocateMaterialConstants( int size, out Span<byte> data )
	{
		var offset = materialHeap.Allocate( size );
		data = Gpu.Map( MaterialBuffer ).Slice( (int)offset, size );
		return offset;
	}

	internal sealed class Kept( long offset, long frame )
	{
		public readonly long Offset = offset;
		long lastUsed = frame;

		// A version hit needs no shared dictionary lock, but cannot revive an offset that a sweep reclaimed.
		public bool TryTouch( long frame )
		{
			long last;
			do
			{
				last = Volatile.Read( ref lastUsed );
				if ( last < 0 ) return false;
				if ( last >= frame ) return true;
			}
			while ( Interlocked.CompareExchange( ref lastUsed, frame, last ) != last );
			return true;
		}

		public bool TryRetire( long before )
		{
			var last = Volatile.Read( ref lastUsed );
			return last < before && Interlocked.CompareExchange( ref lastUsed, -1, last ) == last;
		}
	}

	readonly Dictionary<(ulong Hash, int Size), Kept> keptConstants = new();

	/// <summary>
	/// Pass constants for a binding that isn't dynamic: kept by content, so the same values land at the same place every
	/// frame and the sets pointing at them stay valid. Evicted like sets, when nothing has asked for them for a while.
	/// </summary>
	public long KeepPassConstants( ReadOnlySpan<byte> data, int size ) => KeepPassConstantsEntry( data, size ).Offset;

	internal Kept KeepPassConstantsEntry( ReadOnlySpan<byte> data, int size )
	{
		// Hash meaningful bytes only: explicit and implicit zero padding share one entry.
		data = data[..(data.LastIndexOfAnyExcept( (byte)0 ) + 1)];
		var hash = 14695981039346656037UL;
		foreach ( var b in data ) hash = (hash ^ b) * 1099511628211UL;
		lock ( keptConstants )
		{
			if ( keptConstants.TryGetValue( (hash, size), out var kept ) )
			{
				kept.TryTouch( Frame );
				return kept;
			}

			var offset = AllocateMaterialConstants( size, out var target );
			data.CopyTo( target );
			target[data.Length..].Clear();
			return keptConstants[(hash, size)] = new Kept( offset, Frame );
		}
	}

	/// <summary>Space for pass constants in this frame's region.</summary>
	public long AllocatePassConstants( int size, out Span<byte> data ) => TryAllocatePassConstants( size, out data ) is var offset and >= 0
		? offset
		: throw new InvalidOperationException( "Pass constant region is full" );

	/// <summary>Space for pass constants in this frame's region, or -1 when it's full.</summary>
	public long TryAllocatePassConstants( int size, out Span<byte> data )
	{
		long start;
		lock ( passLock )
		{
			var frame = Gpu.FrameNumber;
			if ( frame != passFrame )
			{
				passFrame = frame;
				passUsed = 0;
			}

			var offset = (passUsed + alignment - 1) / alignment * alignment;
			if ( offset + size > PassRegionSize )
			{
				data = default;
				return -1;
			}

			passUsed = offset + size;
			start = Gpu.FrameSlot * PassRegionSize + offset;
		}

		data = Gpu.Map( PassBuffer ).Slice( (int)start, size );
		return start;
	}

	/// <summary>Bytes left in this frame's pass region.</summary>
	public long PassSpace
	{
		get
		{
			lock ( passLock ) return Gpu.FrameNumber == passFrame ? PassRegionSize - passUsed : PassRegionSize;
		}
	}

	public void Dispose()
	{
		lock ( objects )
		{
			foreach ( var value in objects.Values )
			{
				value.Retire( s => Gpu.Destroy( s.Handle ), _ => { } );
				if ( value.OwnsPipeline ) Gpu.Destroy( value.Pipeline );
			}
			objects.Clear();
			allObjects.Clear();
		}

		lock ( sweepLock )
		{
			foreach ( var (_, set, _, _, _) in retired )
			{
				if ( set is not null ) Gpu.Destroy( set.Handle );
			}

			retired.Clear();
			while ( retiredPipelines is not null && retiredPipelines.TryDequeue( out var pipeline ) )
			{
				Gpu.Destroy( pipeline.Pipeline );
			}
		}

		lock ( layouts )
		{
			foreach ( var layout in layouts.Values ) Gpu.Destroy( layout );
			layouts.Clear();
		}

		Gpu.Destroy( MaterialBuffer );
		Gpu.Destroy( PassBuffer );
	}
}

/// <summary>
/// Space in the material constant buffer: power-of-two blocks from the alignment up, freed blocks reused by size, the
/// rest taken from the top. Thread-safe.
/// </summary>
internal sealed class ConstantHeap( long size, int alignment )
{
	readonly Dictionary<int, Stack<long>> free = new();
	long top;

	/// <summary>Bytes handed out and not freed.</summary>
	public long Used { get; private set; }

	int Block( int bytes )
	{
		var block = Math.Max( alignment, 16 );
		while ( block < bytes ) block <<= 1;
		return block;
	}

	public long Allocate( int bytes )
	{
		var block = Block( bytes );
		lock ( free )
		{
			Used += block;
			if ( free.TryGetValue( block, out var stack ) && stack.Count > 0 ) return stack.Pop();

			var offset = (top + block - 1) / block * block;
			if ( offset + block > size ) throw new InvalidOperationException( "Material constant buffer is full" );
			top = offset + block;
			return offset;
		}
	}

	public void Free( long offset, int bytes )
	{
		var block = Block( bytes );
		lock ( free )
		{
			Used -= block;
			if ( !free.TryGetValue( block, out var stack ) ) free[block] = stack = new();
			stack.Push( offset );
		}
	}
}
