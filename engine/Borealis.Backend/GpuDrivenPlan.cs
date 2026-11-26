using Borealis.Gpu;
using Borealis.Source2.Shaders;

namespace Borealis.Backend;

/// <summary>
/// The plans kept for one resident list (<see cref="Borealis.Gpu.GpuContext.GpuDrivenCull.Plan"/>), one per kind of call:
/// shader mode, targets, and whether it draws ids or lists. Locked while a plan is found or added.
/// </summary>
internal sealed class GpuDrivenPlans
{
	readonly List<GpuDrivenPlan> plans = new();

	/// <summary>
	/// The plan for this kind of call made at <paramref name="version"/> of the list, or null. Plans cannot outlive a
	/// change in the draw and fragment records, or in the visibility runs for a call that names them.
	/// </summary>
	public GpuDrivenPlan Find( in GpuDrivenPlan.CallKey key, int version, int batches, GpuDrivenTables tables )
	{
		lock ( plans )
		{
			foreach ( var plan in plans )
			{
				if ( plan.Key == key ) return plan.Version == version && plan.Batches == batches && plan.RecordEpoch == tables.RecordEpoch
					&& (plan.VisibilityEpoch == tables.VisibilityEpoch || !(key.Ids || key.Lists || key.Visibility)) ? plan : null;
			}

			return null;
		}
	}

	/// <summary>Keep <paramref name="plan"/> for its kind of call, in place of what was kept for it.</summary>
	public void Keep( GpuDrivenPlan plan )
	{
		lock ( plans )
		{
			RetireLocked( plan.Key );
			plans.Add( plan );
		}
	}

	/// <summary>Forget the plan for this kind of call (a check found it stale).</summary>
	public void Drop( in GpuDrivenPlan.CallKey key )
	{
		lock ( plans ) RetireLocked( key );
	}

	/// <summary>Remove the plans for a kind of call, marked retired (<see cref="GpuDrivenPlan.Retired"/>). Hold the list's lock.</summary>
	void RetireLocked( in GpuDrivenPlan.CallKey key )
	{
		for ( int i = plans.Count - 1; i >= 0; i-- )
		{
			if ( plans[i].Key != key ) continue;
			plans[i].Retired = true;
			plans.RemoveAt( i );
		}
	}

	/// <summary>Plans kept, for the tests.</summary>
	public int Count
	{
		get
		{
			lock ( plans ) return plans.Count;
		}
	}
}

/// <summary>
/// What a GPU-culled call of a resident list made, kept for the next call of the same kind while the list's version is
/// the same: its buckets, each batch's 32-byte record, its multi-draws, and which batches it drew. Before it's used, each
/// bucket's variant and attribute hashes are worked out again from its first batch (what the pass's attributes decide), and
/// each material's version is compared; any difference makes the plan again. Read by every worker; never changed once made
/// but for what a check notes of it (<see cref="Reads"/>).
/// </summary>
internal sealed class GpuDrivenPlan
{
	/// <summary>What a call's kind is: the same key, the same buckets and records for the same list.</summary>
	public readonly record struct CallKey( uint Mode, Format Color, Format Depth, int Samples, bool NoColor, bool Ids, bool Lists, bool Visibility, bool Positions, bool KeepsOrder, GpuContext.ResidentFilter Filter, bool Meshlets = false, bool MeshTasks = false, OverlayStencil Stencil = OverlayStencil.None );

	public CallKey Key;
	public int Version;
	public int Batches;

	/// <summary>The visibility epoch its records' runs were named in (<see cref="GpuDrivenTables.VisibilityEpoch"/>).</summary>
	public int VisibilityEpoch;

	/// <summary>The draw and fragment record epoch the plan was made in (<see cref="GpuDrivenTables.RecordEpoch"/>).</summary>
	public int RecordEpoch;

	/// <summary>Its buckets' commands and fragments, its multi-draws' slots, and the compacted slots it uses.</summary>
	public int Commands, Fragments, Written, CompactUsed;

	/// <summary>The call's fragments when it was made, where its compacted multi-draws' slots started.</summary>
	public int CompactBase;

	/// <summary>The buckets, as the context's own were when the plan was made.</summary>
	public List<BackendContext.GpuDrivenBucket> Buckets;

	/// <summary>Per bucket: its first batch's material, object attributes and variant (none for a surface's), and its stages' attribute hashes.</summary>
	public (GpuMaterial Material, ShaderParameters Attributes, ShaderVariant Variant, int VertexAttributes, int PixelAttributes)[] Checks;

	/// <summary>Every material the batches draw with, and its version then.</summary>
	public (BackendMaterial Material, int Version)[] Materials;
	public (SurfaceMaterial Material, int Version)[] SurfaceMaterials;
	public GpuMaterial[] OtherMaterials;

	/// <summary>Every set of object attributes the batches draw with (their lighting records come from them), and its version then.</summary>
	public (BackendParameters Attributes, int Version)[] Attributes;

	/// <summary>Geometry with a lifetime check or a frame-use side effect; kept plans must still call Use.</summary>
	public BackendModel[] Models;
	public IBackendGeometry[] Geometries;

	/// <summary>What its bucket checks read of the pass's attributes, made once a check has passed (<see cref="GpuDrivenPlanReads"/>); null until then.</summary>
	public GpuDrivenPlanReads Reads;

	public uint[] BatchRecords;

	/// <summary>
	/// For a resident filter (<see cref="Borealis.Gpu.GpuContext.GpuDrivenCull.Filter"/>): every record's moved words, one per
	/// fragment or mesh, and by record where its own start, or -1 for a record every view keeps. Null otherwise.
	/// </summary>
	public uint[] Moved;
	public int[] MovedAt;
	public (int Bucket, BufferHandle Page, IndexFormat Format, int First, int Count)[] Runs;

	/// <summary>By multi-draw: whether it draws forward's compacted triangles, from the frame's compacted buffer rather than an index page.</summary>
	public bool[] Compacted;
	public bool[] Drawn;

	/// <summary>Its multi-draws' words in a resident cull's view region (first command slot, count), made the first time.</summary>
	public uint[] ResidentRunWords;

	/// <summary>
	/// For the resident cull (<see cref="ResidentCullItem"/>): a buffer of its batch records and entry map, written the first
	/// time a setup culls it, its bindless slot, its entries and the entry map's byte offset. Destroyed once it's retired.
	/// Its entry records (<see cref="ResidentCullEntryRecord"/>) and resident mesh batches' headers follow, written on the GPU
	/// by the same setup from the tables in host memory, so the culls read only video memory.
	/// </summary>
	public BufferHandle Records;
	public int RecordsSlot, Entries, EntryMap, EntryRecords, Headers;

	/// <summary>Whether a setup has recorded the expand dispatch that writes its entry records.</summary>
	public bool EntryRecordsWritten;

	/// <summary>
	/// A forward plan that compacts: the prepass plan whose ids its records' entries name (<see cref="ResidentCullEntry.PrepassIds"/>).
	/// A view whose prepass draws with that plan lists from them in the same dispatch; any other reads its prepass's stamps.
	/// </summary>
	public GpuDrivenPlan IdsFrom;

	/// <summary>Replaced or dropped from its list's plans: no call finds it again.</summary>
	public volatile bool Retired;

	/// <summary>
	/// The frame (<see cref="BackendDevice.GpuDrivenFrame"/>) its <see cref="Materials"/>' and <see cref="Attributes"/>' versions
	/// were last found as it was made at and its <see cref="Geometries"/> in use; 0 for never.
	/// </summary>
	public uint CheckedFrame;
}

/// <summary>
/// What a plan's bucket checks (<see cref="BackendContext"/>'s PlanCurrent) read that its versions don't tell: the pass's
/// combos its variants' keys and surfaces' passes take, the integers their specialization takes, and the attributes its
/// stages' records read (their hashes, as the records' keys take them), as they were before the last check that passed
/// in full; and each surface bucket's material and program. A pass's attributes are made again each frame, so their
/// version moves on every frame, but the few values the checks read mostly don't: with those the same and the plan's
/// materials and object attributes at their versions, every bucket checks as it did, and isn't worked out again.
/// The first successful fingerprint stays immutable so the usual hit does not take a lock.
/// </summary>
internal sealed class GpuDrivenPlanReads
{
	public StringToken[] Combos, Integers, Hashed;
	public (SurfaceMaterial Material, SurfaceProgram Program)[] Surfaces;

	public int ValueCount => Combos.Length + Integers.Length + Hashed.Length;

	long[] first, recent;

	internal bool Matches( ReadOnlySpan<long> values )
	{
		var baseline = System.Threading.Volatile.Read( ref first );
		if ( baseline is null ) return false;
		if ( values.SequenceEqual( baseline ) ) return true;
		if ( System.Threading.Volatile.Read( ref recent ) is null ) return false;
		lock ( this ) return values.SequenceEqual( recent );
	}

	internal void Keep( ReadOnlySpan<long> values )
	{
		lock ( this )
		{
			if ( first is null )
			{
				System.Threading.Volatile.Write( ref first, values.ToArray() );
			}
			else if ( !values.SequenceEqual( first ) )
			{
				// A different pass can pass the full check too. Reuse this storage if the fingerprint changes again.
				if ( recent is null ) System.Threading.Volatile.Write( ref recent, values.ToArray() );
				else values.CopyTo( recent );
			}
		}
	}
}
