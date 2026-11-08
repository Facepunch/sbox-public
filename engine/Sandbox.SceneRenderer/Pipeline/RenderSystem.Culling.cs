namespace Sandbox.SceneRenderer;

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

		var inFrustum = FindCandidates( world, mainPass, candidates, out var inTree, out var rejected );
		stats.ObjectsSizeCulled += rejected;

		if ( ParallelRecording && candidates.Count >= MinParallelCullCandidates && JobDispatcher.MaxDegreeOfParallelism > 1 )
			CullInChunks( world, mainPass, candidates, inTree, inFrustum, ref stats );
		else
			Cull( world, mainPass, candidates, inTree, inFrustum, ref stats );
	}

	/// <summary>
	/// Candidates a main view has before culling splits them across threads. 2,500 broke even; 5,000 took 20-30% off
	/// collecting, the serial tree query being the rest.
	/// </summary>
	internal static int MinParallelCullCandidates { get; set; } = 4096;

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
		}
	}

	/// <summary>
	/// Query the frustum or light sphere, then append non-tree objects. Entries encode (world index &lt;&lt; 1) | contained.
	/// Returns true when containment means frustum containment. Only the first <paramref name="inTree"/> entries are size-culled;
	/// <paramref name="rejected"/> counts objects in size-rejected subtrees, including possible off-screen objects.
	/// </summary>
	internal static bool FindCandidates( RenderWorld world, ViewPass pass, List<int> candidates, out int inTree, out int rejected )
	{
		candidates.Clear();
		var size = new SizeCull( pass.Root );

		// Avoid traversing static objects for dynamic-only shadows.
		var tree = pass.StaticFilter == StaticFilter.NoStatic ? world.DynamicTree : world.Tree;

		bool inFrustum;
		if ( pass.LightSphere is { } sphere )
		{
			var query = new SpatialTree.SphereQuery { Center = sphere.Center, Radius = sphere.Radius, Size = size };
			tree.QueryClassified( ref query, candidates, out rejected );
			inFrustum = false;
		}
		else
		{
			var query = new SpatialTree.FrustumQuery { Frustum = pass.View.Frustum, Size = size };
			tree.QueryClassified( ref query, candidates, out rejected );
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
	/// Shadow views also apply caster and static filters.
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
		var sizeCull = new SizeCull( root );
		var shadow = pass.IsShadow;
		var filter = pass.StaticFilter;
		var objects = world.Objects;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;

		var entries = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( candidates );
		for ( int k = start; k < end; k++ )
		{
			var entry = entries[k];
			var i = entry >> 1;
			if ( !(inFrustum && (entry & 1) != 0) && !frustum.Intersects( centers[i], extents[i] ) ) continue;

			// Size-cull tree entries before accessing objects.
			if ( k < inTree && sizeCull.Culls( centers[i], extents[i] ) )
			{
				stats.ObjectsSizeCulled++;
				continue;
			}

			var obj = objects[i];
			if ( !root.Shows( obj ) ) continue;

			var feature = FeatureFor( obj.GetType() );
			if ( feature < 0 ) continue;
			var f = features[feature];

			if ( shadow )
			{
				if ( !f.DrawsShadows || !f.CastsShadow( obj ) ) continue;
				if ( filter == StaticFilter.OnlyStatic && !obj.IsStatic ) continue;
				if ( filter == StaticFilter.NoStatic && obj.IsStatic ) continue;
			}

			visible[feature].Add( i );
			stats.ObjectsVisible++;
		}
	}

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
