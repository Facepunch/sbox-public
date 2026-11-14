using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Resident aggregates (<see cref="GpuDevice.DrawsAggregatesResident"/>): every aggregate of the world goes to the device
/// in one list a view kind keeps between frames, which views hand over whole instead of preparing their own, so the
/// device keeps what it makes of them (buckets, records, multi-draws) and the GPU culls every fragment. A list changes only
/// when an aggregate is added, removed or changes how it draws, or its kept transforms move; a view takes the list of its
/// kind (main, or which casters a shadow view takes) and of the tag classes it shows. A shadow view's list holds the casters
/// its static filter takes whether or not they've settled: static ones for a far cascade cache, none for what's drawn over
/// it. Casters whose settling decides, and aggregates a list doesn't hold, go the view's own way.
/// </summary>
internal sealed partial class AggregateRenderFeature
{
	/// <summary>One kind of view's resident aggregates: the same object, and the same batches, while its version is.</summary>
	internal sealed class ResidentList
	{
		public readonly List<Batch> Batches = new();

		/// <summary>By world index: whether this list draws the aggregate, so the view's own batches leave it out.</summary>
		public bool[] Holds = [];

		public int Version;

		/// <summary>Triangles and fragments of all its batches, for the stats.</summary>
		public long Triangles;
		public int Fragments;

		/// <summary>The batches as the device takes them, per depth kind, made once a frame (<see cref="DeviceBatches"/>).</summary>
		public readonly GpuContext.GpuDrivenBatch[][] Device = new GpuContext.GpuDrivenBatch[4][];
		public readonly int[] DeviceGeneration = [-1, -1, -1, -1];
	}

	/// <summary>What a resident aggregate was when its lists were made: any change makes them again.</summary>
	struct Member
	{
		public AggregateObject Aggregate;
		public int Index;
		public int DrawVersion;
		public object Fragments;
		public bool Visible, GameLayers, CastShadows, IsStatic, IsWorld, HiddenInCaptures, Overlay, Lightmapped;

		public LayerMatch LayerMatch;
		public uint[] Tags;
		public int SubWorld;
		public ShaderParameters LightmapAttributes;
		public int Class;

		/// <summary>Its first kept transform slot, or -1 while it has frame entries (its first frame): then it isn't resident.</summary>
		public int FirstSlot;

		/// <summary>Its fragments' arguments' first index in the frame's arguments: the same every frame.</summary>
		public int FirstArg;
	}

	/// <summary>An aggregate's tag class: what decides whether a view's root shows it.</summary>
	readonly record struct ClassKey( uint[] Tags, bool IsWorld, int SubWorld, bool HiddenInCaptures, bool Overlay );

	/// <summary>Tag classes a world's resident aggregates may fall in; more and none are resident.</summary>
	const int MaxResidentClasses = 32;

	/// <summary>Resident aggregates are the device's choice; this turns them off for the tests' comparisons.</summary>
	internal static bool ResidentEnabled { get; set; } = true;

	Member[] members = [];
	int memberCount;
	GpuBuffer.IndirectDrawIndexedArguments[] residentArgs = [];
	int residentArgCount;
	RenderWorld residentWorld;
	int residentStructure = -1;
	int residentVersion;
	readonly List<ClassKey> residentClasses = new();
	readonly Dictionary<(ResidentCasters Casters, uint Mask), ResidentList> residentLists = new();

	/// <summary>Which aggregates a view's resident list holds: a main view's, or a shadow view's casters by its <see cref="StaticFilter"/>.</summary>
	enum ResidentCasters
	{
		Main,
		All,
		Static,
		NotStatic,
	}

	/// <summary>Lists made since the feature was made, for the tests.</summary>
	internal int ResidentListsMade { get; private set; }

	/// <summary>A view's resident list after <see cref="Prepare"/>, or null - for the tests.</summary>
	internal ResidentList ResidentFor( ViewPass pass ) => pass.State<Batches>( this ).Resident;

	/// <summary>
	/// Once a frame, before any view prepares: look for added and removed aggregates and changes to how each draws, put
	/// every resident aggregate's arguments first in the frame's (at the same place every frame) and its transforms in the
	/// frame's buffer. A change makes the lists again.
	/// </summary>
	void RefreshResident( RenderWorld world, TransformBuffer transforms )
	{
		if ( !ReferenceEquals( world, residentWorld ) || world.Structure != residentStructure ) Rescan( world );

		// Their arguments, first in the frame's, so each keeps its place
		if ( args.Length < residentArgCount ) Array.Resize( ref args, Math.Max( residentArgCount, args.Length * 2 ) );
		residentArgs.AsSpan( 0, residentArgCount ).CopyTo( args );
		argCount = residentArgCount;

		var drawVersions = world.DrawVersions;
		var entryVersions = world.EntryVersions;
		var changed = false;
		for ( int m = 0; m < memberCount; m++ )
		{
			ref var member = ref members[m];
			var aggregate = member.Aggregate;
			var index = member.Index;

			if ( member.DrawVersion != drawVersions[index] || !ReferenceEquals( member.Fragments, aggregate.FragmentsKey ) || member.Visible != aggregate.Visible
				|| member.GameLayers != aggregate.GameLayers || member.CastShadows != aggregate.CastShadows || member.IsStatic != aggregate.IsStatic
				|| member.IsWorld != aggregate.IsWorld || member.HiddenInCaptures != aggregate.HiddenInCaptures || member.Overlay != aggregate.Overlay
				|| member.LayerMatch != aggregate.LayerMatch || !ReferenceEquals( member.Tags, aggregate.Tags ) || member.SubWorld != aggregate.SubWorld
				|| member.Lightmapped != aggregate.Lightmapped || !ReferenceEquals( member.LightmapAttributes, aggregate.LightmapAttributes ) )
			{
				// Rarely: its arguments may have moved too, so look at everything again
				Rescan( world );
				RefreshResident( world, transforms );
				return;
			}

			// Its transforms, kept between frames, rewritten when its entry version moves
			if ( aggregate.Fragments.Length == 0 ) continue;
			WriteTransforms( aggregate, index, entryVersions[index], transforms );
			var slot = transforms.InstanceSlot( aggregate.FirstInstance );
			if ( slot >= transforms.FrameBase ) slot = -1;
			if ( slot != member.FirstSlot )
			{
				member.FirstSlot = slot;
				changed = true;
			}
		}

		if ( changed ) DropResidentLists();
	}

	/// <summary>Find every aggregate of the world again, its tag class and its arguments' place.</summary>
	void Rescan( RenderWorld world )
	{
		residentWorld = world;
		residentStructure = world.Structure;
		memberCount = 0;
		residentArgCount = 0;
		residentClasses.Clear();

		var objects = world.Objects;
		var drawVersions = world.DrawVersions;
		for ( int i = 0; i < objects.Length; i++ )
		{
			if ( objects[i] is not AggregateObject aggregate ) continue;

			var key = new ClassKey( aggregate.Tags, aggregate.IsWorld, aggregate.SubWorld, aggregate.HiddenInCaptures, aggregate.Overlay );
			var cls = ClassOf( key );
			if ( cls < 0 )
			{
				// Too many tag classes: none resident, every view prepares its own
				memberCount = 0;
				residentArgCount = 0;
				break;
			}

			if ( memberCount == members.Length ) Array.Resize( ref members, Math.Max( 16, members.Length * 2 ) );
			var fragments = aggregate.Fragments;
			if ( residentArgs.Length < residentArgCount + fragments.Length ) Array.Resize( ref residentArgs, Math.Max( residentArgCount + fragments.Length, residentArgs.Length * 2 ) );
			for ( int f = 0; f < fragments.Length; f++ ) residentArgs[residentArgCount + f] = fragments[f].Args;

			members[memberCount++] = new Member
			{
				Aggregate = aggregate,
				Index = i,
				DrawVersion = drawVersions[i],
				Fragments = aggregate.FragmentsKey,
				Visible = aggregate.Visible,
				GameLayers = aggregate.GameLayers,
				CastShadows = aggregate.CastShadows,
				IsStatic = aggregate.IsStatic,
				IsWorld = aggregate.IsWorld,
				HiddenInCaptures = aggregate.HiddenInCaptures,
				Overlay = aggregate.Overlay,
				LayerMatch = aggregate.LayerMatch,
				Tags = aggregate.Tags,
				SubWorld = aggregate.SubWorld,
				Lightmapped = aggregate.Lightmapped,
				LightmapAttributes = aggregate.LightmapAttributes,
				Class = cls,
				FirstSlot = -1,
				FirstArg = residentArgCount,
			};

			residentArgCount += fragments.Length;
		}

		DropResidentLists();
	}

	int ClassOf( in ClassKey key )
	{
		for ( int c = 0; c < residentClasses.Count; c++ )
		{
			var known = residentClasses[c];
			if ( known.IsWorld == key.IsWorld && known.SubWorld == key.SubWorld && known.HiddenInCaptures == key.HiddenInCaptures && known.Overlay == key.Overlay
				&& known.Tags.AsSpan().SequenceEqual( key.Tags ) ) return c;
		}

		if ( residentClasses.Count == MaxResidentClasses ) return -1;
		residentClasses.Add( key );
		return residentClasses.Count - 1;
	}

	void DropResidentLists()
	{
		residentLists.Clear();
		residentVersion++;
	}

	/// <summary>
	/// The resident list a view draws: its kind's (a shadow view's holds static casters, and only for views that take them)
	/// for the tag classes its root shows. Null when it draws none.
	/// </summary>
	ResidentList ResidentListFor( ViewPass pass, int textureSize )
	{
		if ( memberCount == 0 ) return null;

		var shadow = pass.IsShadow;
		var casters = !shadow ? ResidentCasters.Main : pass.StaticFilter switch
		{
			StaticFilter.Any => ResidentCasters.All,
			// A far cascade cache's settled casters that aren't static, and what's drawn over it, depend on when each settled
			StaticFilter.OnlyStatic or StaticFilter.Settled => ResidentCasters.Static,
			StaticFilter.NoStatic => ResidentCasters.NotStatic,
			_ => (ResidentCasters)(-1),
		};
		if ( casters < 0 ) return null;

		// The classes the root shows, as the collect's tag test would have it for any aggregate of each
		var mask = 0u;
		for ( int c = 0; c < residentClasses.Count; c++ )
		{
			var key = residentClasses[c];
			if ( RootShows( pass.Root, key ) ) mask |= 1u << c;
		}

		if ( residentLists.TryGetValue( (casters, mask), out var list ) ) return list;

		list = new ResidentList { Version = residentVersion };
		if ( list.Holds.Length < residentWorld.Count ) list.Holds = new bool[residentWorld.Count];
		for ( int m = 0; m < memberCount; m++ )
		{
			ref readonly var member = ref members[m];
			var aggregate = member.Aggregate;
			if ( member.FirstSlot < 0 || (mask & 1u << member.Class) == 0 || !member.Visible ) continue;
			if ( shadow ? !(member.CastShadows && member.LayerMatch == LayerMatch.None && TakesCaster( casters, member )) : !member.GameLayers ) continue;

			var fragments = aggregate.Fragments;
			long indices = 0;
			var count = 0;
			for ( int i = 0; i < fragments.Length; i++ )
			{
				if ( shadow && (fragments[i].Flags & FragmentFlags.DoNotCastShadows) != 0 ) continue;
				indices += fragments[i].Args.IndexCount;
				count++;
			}

			if ( count == 0 ) continue;

			// Fragments that cast no shadow are culled on the GPU; the batch keeps every argument for the classic way
			list.Batches.Add( new Batch( aggregate, member.FirstArg, fragments.Length, -1, indices / 3, SortKey( aggregate ), textureSize ) );
			list.Holds[member.Index] = true;
			list.Triangles += indices / 3;
			list.Fragments += count;
		}

		list.Batches.Sort( ByKey );
		residentLists[(casters, mask)] = list;
		ResidentListsMade++;
		return list;
	}

	/// <summary>Whether a shadow view's list of these casters holds an aggregate.</summary>
	static bool TakesCaster( ResidentCasters casters, in Member member ) => casters switch
	{
		ResidentCasters.Static => member.IsStatic,
		ResidentCasters.NotStatic => !member.IsStatic,
		_ => true,
	};

	/// <summary>Whether a view whose root is <paramref name="root"/> shows an aggregate of this class (<see cref="RenderView.Shows"/>).</summary>
	static bool RootShows( RenderView root, in ClassKey key )
	{
		var subWorld = key.SubWorld;
		if ( subWorld != 0 && (root.SubWorlds & (1UL << (subWorld - 1))) == 0 ) return false;
		if ( root.Capture && (key.HiddenInCaptures || key.Overlay) ) return false;
		return root.ShowsTags( key.Tags, key.IsWorld );
	}

	/// <summary>
	/// A resident list's batches as the device takes them for a depth kind, made once a frame for every view and worker that
	/// draws the list (each frame's first instances move; nothing else does).
	/// </summary>
	ReadOnlySpan<GpuContext.GpuDrivenBatch> DeviceBatches( ResidentList list, bool depth, bool forceDepthOnly )
	{
		var kind = (depth ? 1 : 0) | (forceDepthOnly ? 2 : 0);
		lock ( list )
		{
			var made = list.Device[kind];
			if ( list.DeviceGeneration[kind] == argGeneration && made is not null ) return made.AsSpan( 0, list.Batches.Count );

			if ( made is null || made.Length < list.Batches.Count ) list.Device[kind] = made = new GpuContext.GpuDrivenBatch[list.Batches.Count];
			var batches = CollectionsMarshal.AsSpan( list.Batches );
			for ( int i = 0; i < batches.Length; i++ ) made[i] = DeviceBatch( batches[i], depth, forceDepthOnly );
			list.DeviceGeneration[kind] = argGeneration;
			return made.AsSpan( 0, batches.Length );
		}
	}
}
