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

	/// <summary>The plan for this kind of call made at <paramref name="version"/> of the list, or null.</summary>
	public GpuDrivenPlan Find( in GpuDrivenPlan.CallKey key, int version, int batches )
	{
		lock ( plans )
		{
			foreach ( var plan in plans )
			{
				if ( plan.Key == key ) return plan.Version == version && plan.Batches == batches ? plan : null;
			}

			return null;
		}
	}

	/// <summary>Keep <paramref name="plan"/> for its kind of call, in place of what was kept for it.</summary>
	public void Keep( GpuDrivenPlan plan )
	{
		lock ( plans )
		{
			plans.RemoveAll( p => Retire( p, p.Key == plan.Key ) );
			plans.Add( plan );
		}
	}

	/// <summary>Forget the plan for this kind of call (a check found it stale).</summary>
	public void Drop( in GpuDrivenPlan.CallKey key )
	{
		var drop = key;
		lock ( plans ) plans.RemoveAll( p => Retire( p, p.Key == drop ) );
	}

	/// <summary>Mark a plan retired when it's being removed (<see cref="GpuDrivenPlan.Retired"/>).</summary>
	static bool Retire( GpuDrivenPlan plan, bool remove )
	{
		if ( remove ) plan.Retired = true;
		return remove;
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
/// each material's version is compared; any difference makes the plan again. Read by every worker; never changed once made.
/// </summary>
internal sealed class GpuDrivenPlan
{
	/// <summary>What a call's kind is: the same key, the same buckets and records for the same list.</summary>
	public readonly record struct CallKey( uint Mode, Format Color, Format Depth, int Samples, bool NoColor, bool Ids, bool Lists, bool Visibility, bool Positions, bool KeepsOrder, bool Meshlets = false );

	public CallKey Key;
	public int Version;
	public int Batches;

	/// <summary>Its buckets' commands and fragments, its multi-draws' slots, and the compacted slots it uses.</summary>
	public int Commands, Fragments, Written, CompactUsed;

	/// <summary>The call's fragments when it was made, where its compacted multi-draws' slots started.</summary>
	public int CompactBase;

	/// <summary>The buckets, as the context's own were when the plan was made.</summary>
	public List<BackendContext.GpuDrivenBucket> Buckets;

	/// <summary>Per bucket: its first batch's material, object attributes and variant (none for a surface's), and its stages' attribute hashes.</summary>
	public (GpuMaterial Material, ShaderParameters Attributes, ShaderVariant Variant, int VertexAttributes, int PixelAttributes)[] Checks;

	/// <summary>Every material the batches draw with, and its version then.</summary>
	public (GpuMaterial Material, int Version)[] Materials;

	/// <summary>Every set of object attributes the batches draw with (their lighting records come from them), and its version then.</summary>
	public (BackendParameters Attributes, int Version)[] Attributes;

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
}
