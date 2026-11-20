namespace Borealis;

/// <summary>
/// Shared main/shadow-view culling and object-to-feature lookup.
/// </summary>
public sealed partial class RenderSystem
{
	readonly List<int> candidates = new();

	/// <summary>
	/// Collect visible objects for the main view.
	/// </summary>
	internal void Collect( RenderWorld world, RenderView view, ref RenderStats stats )
	{
		frame.World = world;
		frame.View = view;
		mainPass.Reset( features.Count );
		mainPass.View = view;
		mainPass.Root = view;
		mainPass.World = world;

		// The cull reasons view sees every object in the frustum, so it can tell the small ones from the off-screen ones and
		// draw them anyway
		mainPass.CullReasons = null;
		var reasons = (frame.DebugViews & DebugViews.CullReasons) != 0 && !isSkybox;
		if ( reasons )
		{
			if ( cullReasons.Length < world.Objects.Length ) cullReasons = new byte[world.Objects.Length];
			Array.Clear( cullReasons );
			mainPass.CullReasons = cullReasons;
		}

		// What the view's resident list draws (the GPU culls it) is left out: the frame's transforms begin here, as the list is
		// worked out from them
		var occlusion = RenderHost.Current.OcclusionCulling && DepthPrepass && !isSkybox && !view.Capture;
		collectedResident = CollectSkipsResident && !reasons && frame.DebugViews == DebugViews.None && !occlusion;
		if ( collectedResident )
		{
			transforms.World = world;
			transforms.Clear();
			mainPass.ResidentHolds = aggregates.CollectHolds( world, mainPass, transforms );
			mainPass.TreeKept = aggregates.CollectTreeKept( world, mainPass );
		}

		var inFrustum = FindCandidates( world, mainPass, candidates, out var inTree, out var rejected, sizeCull: !reasons );
		stats.ObjectsSizeCulled += rejected;
		if ( reasons ) stats.ObjectsFrustumCulled += world.Count - candidates.Count;

		if ( ParallelRecording && candidates.Count >= MinParallelCullCandidates && JobDispatcher.MaxDegreeOfParallelism > 1 )
			CullInChunks( world, mainPass, candidates, inTree, inFrustum, ref stats );
		else
			Cull( world, mainPass, candidates, inTree, inFrustum, ref stats );
	}

	/// <summary>
	/// Leave out of the main view's collect what its resident list draws (<see cref="ViewPass.ResidentHolds"/>). Never with
	/// debug views or occlusion culling. Off for the tests' comparisons, and with borealislab's <c>-collect-resident</c>.
	/// </summary>
	internal static bool CollectSkipsResident { get; set; } = true;

	/// <summary>Whether this frame's collect left resident objects out, having begun the frame's transforms.</summary>
	bool collectedResident;

	/// <summary>
	/// Candidates a main view has before culling splits them across threads. 2,500 broke even; 5,000 took 20-30% off
	/// collecting, the serial tree query being the rest.
	/// </summary>
	internal static int MinParallelCullCandidates { get; set; } = 4096;

	// The main view's cull reasons, by world index, while the cull reasons view is on
	byte[] cullReasons = [];

	// Candidates in each chunk of a split cull; enough chunks that threads finishing early can take another
	const int CullChunkCandidates = 1024;

	// Each chunk's visible objects by feature, and its counts, merged in candidate order after
	List<int>[][] chunkVisible = [];
	RenderStats[] chunkStats = [];
	(RenderWorld World, ViewPass Pass, List<int> Candidates, int InTree, bool InFrustum) chunkCull;

	/// <summary>
	/// <see cref="Cull"/> split into chunks of candidates across the job dispatcher's threads, each into its own lists,
	/// then joined in candidate order - the same visible lists and counts as culling them in one go.
	/// </summary>
	void CullInChunks( RenderWorld world, ViewPass pass, List<int> candidates, int inTree, bool inFrustum, ref RenderStats stats )
	{
		// Feature lookups that miss write the shared cache
		WarmFeatureTypes( world );

		var chunks = (candidates.Count + CullChunkCandidates - 1) / CullChunkCandidates;
		if ( chunkVisible.Length < chunks )
		{
			var old = chunkVisible.Length;
			Array.Resize( ref chunkVisible, chunks );
			Array.Resize( ref chunkStats, chunks );
			for ( int c = old; c < chunks; c++ ) chunkVisible[c] = [];
		}

		for ( int c = 0; c < chunks; c++ )
		{
			if ( chunkVisible[c].Length < features.Count )
			{
				var old = chunkVisible[c].Length;
				Array.Resize( ref chunkVisible[c], features.Count );
				for ( int f = old; f < features.Count; f++ ) chunkVisible[c][f] = new List<int>();
			}

			for ( int f = 0; f < features.Count; f++ ) chunkVisible[c][f].Clear();
			chunkStats[c] = default;
		}

		chunkCull = (world, pass, candidates, inTree, inFrustum);
		JobDispatcher.For( chunks, this, static ( system, c ) =>
		{
			var (world, pass, candidates, inTree, inFrustum) = system.chunkCull;
			var start = c * CullChunkCandidates;
			var end = Math.Min( start + CullChunkCandidates, candidates.Count );
			system.CullRange( world, pass, candidates, start, end, inTree, inFrustum, system.chunkVisible[c], ref system.chunkStats[c] );
		}, itemsPerBatch: 1 );
		chunkCull = default;

		for ( int f = 0; f < features.Count; f++ )
		{
			var visible = pass.Visible( features[f] );
			for ( int c = 0; c < chunks; c++ ) visible.AddRange( chunkVisible[c][f] );
		}

		for ( int c = 0; c < chunks; c++ )
		{
			stats.ObjectsSizeCulled += chunkStats[c].ObjectsSizeCulled;
			stats.ObjectsVisible += chunkStats[c].ObjectsVisible;
			stats.ObjectsTagCulled += chunkStats[c].ObjectsTagCulled;
			stats.ObjectsFrustumCulled += chunkStats[c].ObjectsFrustumCulled;
		}
	}

	/// <summary>
	/// Query the frustum or light sphere, then append non-tree objects. Entries encode (world index &lt;&lt; 1) | contained.
	/// Returns true when containment means frustum containment. Only the first <paramref name="inTree"/> entries are size-culled;
	/// <paramref name="rejected"/> counts objects in size-rejected subtrees, including possible off-screen objects. Without
	/// <paramref name="sizeCull"/>, no subtree is rejected, and <see cref="Cull"/> size culls each object.
	/// </summary>
	internal static bool FindCandidates( RenderWorld world, ViewPass pass, List<int> candidates, out int inTree, out int rejected, bool sizeCull = true )
	{
		candidates.Clear();
		if ( pass.StaticFilter == StaticFilter.Nothing )
		{
			(inTree, rejected) = (0, 0);
			return true;
		}

		var size = sizeCull && !KeepsSmallCasters( pass ) ? SizeCull.ForPass( pass ) : default;

		// Avoid traversing static objects for dynamic-only shadows.
		var tree = pass.StaticFilter is StaticFilter.NoStatic or StaticFilter.SettledNoStatic or StaticFilter.Unsettled ? world.DynamicTree : world.Tree;

		using var zone = Zones.TreeQuery.Start();
		bool inFrustum;
		if ( pass.LightSphere is { } sphere )
		{
			var query = new SpatialTree.SphereQuery { Center = sphere.Center, Radius = sphere.Radius, Size = size };
			tree.QueryClassified( ref query, candidates, out rejected, pass.TreeKept );
			inFrustum = false;
		}
		else
		{
			var query = new SpatialTree.FrustumQuery { Frustum = pass.View.Frustum, Size = size };
			tree.QueryClassified( ref query, candidates, out rejected, pass.TreeKept );
			inFrustum = true;
		}

		// Lights, custom objects and anything else that's never size culled
		inTree = candidates.Count;
		foreach ( var i in world.Unculled )
			candidates.Add( i << 1 );

		return inFrustum;
	}

	/// <summary>
	/// Filter candidates by frustum, root-view size and tags, then feature support.
	/// Shadow views also apply caster and static filters. With <see cref="ViewPass.CullReasons"/>, meshes culled for their
	/// size or tags are kept with their reason, and not counted visible.
	/// </summary>
	internal void Cull( RenderWorld world, ViewPass pass, List<int> candidates, int inTree, bool inFrustum, ref RenderStats stats )
		=> CullRange( world, pass, candidates, 0, candidates.Count, inTree, inFrustum, pass.VisibleByFeature, ref stats );

	/// <summary>
	/// <see cref="Cull"/> candidates [<paramref name="start"/>, <paramref name="end"/>) into <paramref name="visible"/>, by
	/// feature index.
	/// </summary>
	void CullRange( RenderWorld world, ViewPass pass, List<int> candidates, int start, int end, int inTree, bool inFrustum, List<int>[] visible, ref RenderStats stats )
	{
		var frustum = pass.View.Frustum;
		var root = pass.Root;
		var sizeCull = KeepsSmallCasters( pass ) ? default : SizeCull.ForPass( pass );
		var shadow = pass.IsShadow;
		var filter = pass.StaticFilter;
		var objects = world.Objects;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;
		var subWorlds = world.SubWorlds;
		var reasons = pass.CullReasons;
		var members = pass.ResidentMembers;
		var holds = pass.ResidentHolds;

		// Neighbours are mostly of one type: look its feature up again only when the type changes
		Type lastType = null;
		var lastFeature = -1;

		var entries = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( candidates );
		for ( int k = start; k < end; k++ )
		{
			var entry = entries[k];
			var i = entry >> 1;

			// Drawn from the main view's resident list
			if ( holds is not null && i < holds.Length && holds[i] ) continue;

			// Drawn from the view's resident list (its query leaves most of these out already)
			if ( members is not null && i < members.Length && members[i] != 0 ) continue;

			if ( !(inFrustum && (entry & 1) != 0) && !frustum.Intersects( centers[i], extents[i] ) )
			{
				if ( reasons is not null ) stats.ObjectsFrustumCulled++;
				continue;
			}

			// Size-cull tree entries before accessing objects.
			var reason = CullReason.None;
			if ( k < inTree && sizeCull.Culls( centers[i], extents[i] ) )
			{
				stats.ObjectsSizeCulled++;
				if ( reasons is null ) continue;
				reason = CullReason.Size;
			}

			// Another world's objects that this view doesn't draw aren't culled: they aren't in its view at all
			if ( !root.ShowsSubWorld( subWorlds[i] ) ) continue;
			var obj = objects[i];
			if ( reason == CullReason.None && !root.ShowsShown( obj ) )
			{
				stats.ObjectsTagCulled++;
				if ( reasons is null ) continue;
				reason = CullReason.Tags;
			}

			var type = obj.GetType();
			if ( type != lastType )
			{
				lastType = type;
				lastFeature = FeatureFor( type );
			}

			var feature = lastFeature;
			if ( feature < 0 ) continue;
			var f = features[feature];

			// Only meshes are drawn anyway: a culled light or probe would change what the visible ones look like
			if ( reason != CullReason.None )
			{
				if ( obj is not MeshObject ) continue;
				reasons[i] = (byte)reason;
				visible[feature].Add( i );
				continue;
			}

			if ( shadow )
			{
				if ( !f.DrawsShadows || !f.CastsShadow( obj ) ) continue;
				if ( filter == StaticFilter.OnlyStatic && !obj.IsStatic ) continue;
				if ( filter == StaticFilter.NoStatic && obj.IsStatic ) continue;
				if ( filter >= StaticFilter.Settled && !TakesSettled( pass, filter, obj.IsStatic, world.MovedStamps[i] ) ) continue;
			}

			visible[feature].Add( i );
			stats.ObjectsVisible++;
		}
	}

	/// <summary>
	/// Whether the sun's cached far cascade (<see cref="StaticFilter.Settled"/>, <see cref="StaticFilter.SettledNoStatic"/>) or
	/// what's drawn on top of it (<see cref="StaticFilter.Unsettled"/>) takes a caster that last moved at <paramref name="moved"/>.
	/// The one on top counts those it takes that have settled since its cache was drawn.
	/// </summary>
	static bool TakesSettled( ViewPass pass, StaticFilter filter, bool isStatic, ulong moved )
	{
		if ( filter == StaticFilter.Settled && isStatic ) return true;
		if ( isStatic ) return false;
		var cached = Borealis.Shadows.SettledCasters.SettledBy( moved, pass.SettledStamp, pass.RestlessStamp );
		if ( filter != StaticFilter.Unsettled ) return cached;
		if ( cached ) return false;

		if ( pass.NewlySettled is { } count && Borealis.Shadows.SettledCasters.Settled( moved ) ) count.Value++;
		return true;
	}

	/// <summary>
	/// A static shadow cache is kept while the camera moves, so it isn't size culled by where the camera was when it was drawn.
	/// A texel-sized cull doesn't depend on the camera, so it keeps that.
	/// </summary>
	static bool KeepsSmallCasters( ViewPass pass ) => pass.StaticFilter == StaticFilter.OnlyStatic && !pass.TexelSized;

	/// <summary>
	/// Warm type lookups before parallel culling; cache misses write shared state.
	/// </summary>
	internal void WarmFeatureTypes( RenderWorld world )
	{
		foreach ( var type in world.ObjectTypes )
			FeatureFor( type );
	}

	int FeatureFor( Type type )
	{
		return featureByType.TryGetValue( type, out var index ) ? index : FindFeature( type );
	}

	/// <summary>
	/// Keep the capturing lambda off the cache-hit path; closures allocate on method entry.
	/// </summary>
	int FindFeature( Type type )
	{
		var index = features.FindIndex( f => f.Accepts( type ) );
		featureByType[type] = index;
		return index;
	}
}
