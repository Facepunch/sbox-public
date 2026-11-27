using System.Runtime.CompilerServices;

namespace Borealis.Features;

/// <summary>
/// Bins: what a run of instances shares, kept between frames, so a view groups its visible objects by their bins instead of
/// sorting and comparing the objects (<c>docs/reference/borealis.md</c>, *Kept between frames*).
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>
	/// A run's state, kept between frames: everything objects must share to draw as one instanced run, its batch key, and its
	/// draws' sort keys. A custom object has a bin of its own. Bins live as long as something draws with them
	/// (<see cref="DropUnusedBins"/>).
	/// </summary>
	internal sealed class Bin
	{
		public readonly RenderMesh Mesh;
		public readonly int Lod;
		public readonly GpuMaterial Override;
		public readonly bool OverrideBlends;
		public readonly DrawFilter Filter;
		public readonly ShaderParameters Attributes;
		public readonly ShaderParameters Lightmap;
		public readonly ShaderParameters Probe;
		public readonly bool ReadsFrameBuffer;
		public readonly bool Skinned;
		public readonly bool Deformed;
		public readonly bool Morphable;
		public readonly bool Tinted;
		public readonly DrawCallbacks Callbacks;
		public readonly CustomObject Custom;

		/// <summary>
		/// Its objects cast shadows from both faces (<see cref="MeshObject.ShadowTwoSided"/>): shadow views draw them two-sided.
		/// </summary>
		public readonly bool ShadowTwoSided;

		/// <summary>
		/// Its batched lists' sort key (<see cref="OpaqueKey"/>), or a custom object's.
		/// </summary>
		public readonly ulong Key;

		/// <summary>
		/// The draws of its LOD its filter keeps, in order, and each one's sort key above the 24 index bits (<see cref="ItemKey"/>).
		/// </summary>
		public readonly int[] Draws;
		public readonly ulong[] DrawKeys;

		/// <summary>
		/// Whether neighbouring objects in it instance together: not with callbacks, which run around each object's draws, and not
		/// a custom object.
		/// </summary>
		public readonly bool Joins;

		/// <summary>
		/// A mesh whose LOD blends some draws and not others, without an override: the lists that take only some of its draws use
		/// a bin of the same state with that filter (<see cref="ForFilter"/>).
		/// </summary>
		public readonly bool Partial;

		/// <summary>
		/// The grouping a view last counted it in (<see cref="GroupByBin"/>), its visible objects there, and where the next goes.
		/// </summary>
		internal int Stamp;
		internal int Count;
		internal int Fill;

		/// <summary>
		/// The main view prepare that last drew with it, and whether it was dropped from <see cref="bins"/> for not being used.
		/// </summary>
		internal int Used;
		internal bool Dropped;

		Bin opaque, translucent;

		internal Bin( in BinKey key, ulong batchKey )
		{
			Mesh = key.Mesh;
			Lod = key.Lod;
			Override = key.Override;
			OverrideBlends = key.OverrideBlends;
			Filter = key.Filter;
			Attributes = key.Attributes;
			Lightmap = key.Lightmap;
			Probe = key.Probe;
			ReadsFrameBuffer = key.ReadsFrameBuffer;
			Skinned = Mesh.IsSkinned;
			Deformed = key.Deformed;
			Morphable = key.Morphable;
			Tinted = !Skinned && Mesh.HasDrawTints;
			Callbacks = key.Callbacks;
			ShadowTwoSided = key.ShadowTwoSided;
			Key = batchKey;
			Joins = Callbacks is null;
			Partial = Override is null && Mesh.BlendForLod( Lod ) == MeshBlend.Partial;

			// A partially blended object draws only the list's part of its draws (ListDefinition.Draws)
			var draws = Mesh.DrawsForLod( Lod );
			var kept = 0;
			Span<int> scratch = draws.Length <= 64 ? stackalloc int[draws.Length] : new int[draws.Length];
			for ( int d = 0; d < draws.Length; d++ )
			{
				if ( Filter == DrawFilter.Opaque && draws[d].Translucent ) continue;
				if ( Filter == DrawFilter.Translucent && !draws[d].Translucent ) continue;
				scratch[kept++] = d;
			}

			Draws = scratch[..kept].ToArray();
			DrawKeys = new ulong[kept];
			var state = StateBits( Attributes, Lightmap, Probe );
			var hashed = (Material: (GpuMaterial)null, Bits: 0u);
			for ( int i = 0; i < kept; i++ )
				DrawKeys[i] = ItemKey( this, Draws[i], state, ref hashed );
		}

		internal Bin( CustomObject custom )
		{
			Custom = custom;
			Key = (ulong)(uint)RuntimeHelpers.GetHashCode( custom ) << 8;
			Draws = [];
			DrawKeys = [];
		}

		/// <summary>
		/// The bin the lists drawing only <paramref name="filter"/>'s draws put its objects in.
		/// </summary>
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public Bin ForFilter( DrawFilter filter, MeshRenderFeature feature )
		{
			if ( !Partial || filter == DrawFilter.All ) return this;

			ref var bin = ref filter == DrawFilter.Opaque ? ref opaque : ref translucent;
			if ( bin is null || bin.Dropped ) bin = feature.BinFor( new BinKey( Mesh, Lod, Override, OverrideBlends, filter, Attributes, Lightmap, Probe, ReadsFrameBuffer, Deformed, Morphable, Callbacks, ShadowTwoSided ) );
			return bin;
		}
	}

	/// <summary>
	/// What a <see cref="Bin"/> is found by: the state objects must share to instance together, compared by reference.
	/// </summary>
	internal readonly record struct BinKey( RenderMesh Mesh, int Lod, GpuMaterial Override, bool OverrideBlends, DrawFilter Filter, ShaderParameters Attributes,
		ShaderParameters Lightmap, ShaderParameters Probe, bool ReadsFrameBuffer, bool Deformed, bool Morphable, DrawCallbacks Callbacks, bool ShadowTwoSided )
	{
		public bool Equals( BinKey other ) => ReferenceEquals( Mesh, other.Mesh ) && Lod == other.Lod && ReferenceEquals( Override, other.Override ) && OverrideBlends == other.OverrideBlends
			&& Filter == other.Filter && ReferenceEquals( Attributes, other.Attributes ) && ReferenceEquals( Lightmap, other.Lightmap ) && ReferenceEquals( Probe, other.Probe )
			&& ReadsFrameBuffer == other.ReadsFrameBuffer && Deformed == other.Deformed && Morphable == other.Morphable && ReferenceEquals( Callbacks, other.Callbacks ) && ShadowTwoSided == other.ShadowTwoSided;

		public override int GetHashCode() => HashCode.Combine( RuntimeHelpers.GetHashCode( Mesh ), Lod, RuntimeHelpers.GetHashCode( Override ), RuntimeHelpers.GetHashCode( Attributes ),
			RuntimeHelpers.GetHashCode( Lightmap ), RuntimeHelpers.GetHashCode( Probe ), RuntimeHelpers.GetHashCode( Callbacks ),
			(int)Filter | (OverrideBlends ? 4 : 0) | (ReadsFrameBuffer ? 8 : 0) | (Deformed ? 16 : 0) | (Morphable ? 32 : 0) | (ShadowTwoSided ? 64 : 0) );
	}

	/// <summary>
	/// Every bin something drew with lately, by its state. Main thread only.
	/// </summary>
	readonly Dictionary<BinKey, Bin> bins = new();

	/// <summary>
	/// The bin a mesh at a LOD and probe draws in, made when no bin has that state. Main thread only.
	/// </summary>
	Bin BinFor( MeshObject obj, int lod, ShaderParameters probe )
	{
		var skinned = obj.IsSkinned;
		var material = obj.MaterialOverride;
		return BinFor( new BinKey( obj.Mesh, lod, material, material is not null && (obj.OverrideTranslucent || obj.OverrideAlphaTest), DrawFilter.All, obj.Attributes,
			obj.LightmapAttributes, probe, obj.WantsFrameBufferCopy, skinned && obj.IsDeformed, obj.MorphSource is not null, obj.Callbacks, obj.ShadowTwoSided ) );
	}

	Bin BinFor( in BinKey key )
	{
		if ( bins.TryGetValue( key, out var bin ) ) return bin;

		bin = new Bin( key, OpaqueKey( key.Mesh, key.Lod, key.Override, key.Attributes, key.Lightmap, key.Probe ) ) { Used = mainPrepares };
		bins.Add( key, bin );
		keysMade++;
		return bin;
	}

	/// <summary>
	/// Main view prepares so far, which bins are stamped with when they're drawn with.
	/// </summary>
	int mainPrepares;

	/// <summary>
	/// Main view prepares a bin can go undrawn before it's dropped, and how often to look for those. Long enough that a state
	/// objects keep coming back to, as a LOD does while things move about, isn't made again every few seconds: a crowd's walkers
	/// dropped and made three or four bins a frame with an eighth of this.
	/// </summary>
	internal const int BinLifetime = 1024;
	internal const int BinSweepInterval = 64;

	/// <summary>
	/// Bins kept however long they go unused, while there are no more than this: moving about a map comes back to the meshes,
	/// LODs and probes it drew before, and a bin made again is an allocation in the frame that finds it. Past this many, unused
	/// ones go after <see cref="BinLifetime"/>, so objects taking new state all the time don't pile them up.
	/// </summary>
	internal const int KeptBins = 4096;

	/// <summary>
	/// Drop bins nothing drew with for <see cref="BinLifetime"/> main view prepares, once there are more than
	/// <see cref="KeptBins"/>: a kept entry naming one finds its bin again, so objects that come back still share theirs.
	/// </summary>
	void DropUnusedBins()
	{
		if ( ++mainPrepares % BinSweepInterval != 0 || bins.Count <= KeptBins ) return;

		foreach ( var (key, bin) in bins )
		{
			if ( mainPrepares - bin.Used < BinLifetime ) continue;
			bin.Dropped = true;
			bins.Remove( key );
		}
	}

	/// <summary>
	/// Bins kept, for the tests.
	/// </summary>
	internal int BinCount => bins.Count;

	int groupStamp;
	Bin[] touched = new Bin[64];
	ulong[] touchedKeys = new ulong[64];
	int[] grouped = new int[256];

	/// <summary>
	/// Order a batched list's objects (their places in the visible list) by bin: count each bin's objects, sort the bins they
	/// touched by batch key, then place each object after the ones before it in its bin, which keeps visible order within a bin.
	/// No object is sorted or compared.
	/// </summary>
	void GroupByBin( Bucket bucket, DrawFilter filter )
	{
		var count = bucket.Count;
		var items = bucket.Items.AsSpan( 0, count );
		var stamp = ++groupStamp;
		var touchedCount = 0;

		foreach ( var k in items )
		{
			var bin = classified[k].Bin.ForFilter( filter, this );
			if ( bin.Stamp != stamp )
			{
				bin.Stamp = stamp;
				bin.Count = 0;
				if ( touchedCount == touched.Length )
				{
					Array.Resize( ref touched, touchedCount * 2 );
					Array.Resize( ref touchedKeys, touchedCount * 2 );
				}

				touched[touchedCount] = bin;
				touchedKeys[touchedCount++] = bin.Key;
			}

			bin.Count++;
		}

		// One bin, as in a scene of one model, is already in order
		if ( touchedCount == 1 ) return;

		var keys = touchedKeys.AsSpan( 0, touchedCount );
		var order = touched.AsSpan( 0, touchedCount );
		if ( !IsSorted( keys ) )
		{
			keys.Sort( order );
			BucketsSorted++;
		}

		var start = 0;
		foreach ( var bin in order )
		{
			bin.Fill = start;
			start += bin.Count;
		}

		if ( grouped.Length < count ) grouped = new int[Math.Max( count, grouped.Length * 2 )];
		foreach ( var k in items )
			grouped[classified[k].Bin.ForFilter( filter, this ).Fill++] = k;

		grouped.AsSpan( 0, count ).CopyTo( items );
	}
}
