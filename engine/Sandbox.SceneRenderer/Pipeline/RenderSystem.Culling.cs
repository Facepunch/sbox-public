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
		Cull( world, mainPass, candidates, inTree, inFrustum, ref stats );
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
	/// Shadow views also apply caster, static and exclusion-frustum filters.
	/// </summary>
	internal void Cull( RenderWorld world, ViewPass pass, List<int> candidates, int inTree, bool inFrustum, ref RenderStats stats )
	{
		var frustum = pass.View.Frustum;
		var root = pass.Root;
		var sizeCull = new SizeCull( root );
		var shadow = pass.IsShadow;
		var filter = pass.StaticFilter;
		var exclusion = pass.Exclusion;
		var objects = world.Objects;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;

		var entries = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( candidates );
		for ( int k = 0; k < entries.Length; k++ )
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
				if ( exclusion.HasValue && exclusion.Value.Contains( centers[i], extents[i] ) ) continue;
			}

			pass.Visible( f ).Add( i );
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
