using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	static void SetPropCollision( GameObject target, Model model, Dictionary<string, Transform> pose, List<string> warnings )
	{
		var physics = model?.Physics;
		if ( pose is null )
		{
			if ( physics?.Parts.Count > 0 )
			{
				var collider = target.Components.GetOrCreate<ModelCollider>();
				collider.Model = model;
				collider.Static = true;
			}
			else target.Components.Get<ModelCollider>( FindMode.EverythingInSelf )?.Destroy();
			return;
		}

		// Match colliders in source order so a prefab override can reuse the same component IDs.
		var existing = new Queue<Collider>( target.Components.GetAll<Collider>( FindMode.EverythingInSelf )
			.Where( c => c is SphereCollider or CapsuleCollider or HullCollider or ModelCollider ) );
		T Shape<T>() where T : Collider, new()
		{
			var shape = existing.TryPeek( out var next ) && next is T ? (T)existing.Dequeue() : target.Components.Create<T>();
			shape.Static = true;
			return shape;
		}

		if ( physics?.Parts.Count > 0 )
		{
			if ( physics.BoneCount != physics.Parts.Count || !physics.Parts.Any( p => pose.ContainsKey( p.BoneName ) ) )
				Shape<ModelCollider>().Model = model;
			else if ( physics.Parts.Any( p => p.Parts.Any( s => s is PhysicsGroupDescription.BodyPart.MeshPart ) ) )
			{
				Shape<ModelCollider>().Model = model;
				warnings.Add( $"Posed prop '{target.Name}' uses triangle-mesh collision in its model's bind pose." );
			}
			else
				foreach ( var part in physics.Parts )
				{
					var transform = pose.GetValueOrDefault( part.BoneName, part.Transform );
					foreach ( var primitive in part.Parts )
					{
						switch ( primitive )
						{
							case PhysicsGroupDescription.BodyPart.SpherePart sphere:
								var ball = Shape<SphereCollider>();
								ball.Center = transform.PointToWorld( sphere.Sphere.Center );
								ball.Radius = sphere.Sphere.Radius * transform.UniformScale;
								ball.Surface = sphere.Surface;
								break;
							case PhysicsGroupDescription.BodyPart.CapsulePart capsule:
								var tube = Shape<CapsuleCollider>();
								tube.Start = transform.PointToWorld( capsule.Capsule.CenterA );
								tube.End = transform.PointToWorld( capsule.Capsule.CenterB );
								tube.Radius = capsule.Capsule.Radius * transform.UniformScale;
								tube.Surface = capsule.Surface;
								break;
							case PhysicsGroupDescription.BodyPart.HullPart hull:
								var convex = Shape<HullCollider>();
								convex.Type = HullCollider.PrimitiveType.Points;
								convex.Points = hull.GetPoints().Select( point => transform.PointToWorld( point ) ).ToList();
								convex.Surface = hull.Surface;
								break;
						}
					}
				}
		}
		foreach ( var unused in existing ) unused.Destroy();
	}

	static void AddCollisionHulls( GameObject owner, SceneMeshData data, Transform placement )
	{
		foreach ( var submesh in data.Mesh.Triangulate() )
		{
			var vertices = submesh.Vertices.Select( vertex => vertex.Position ).ToList();
			var (points, indices) = MeshCollisionFitting.SimplifySurface( vertices, submesh.Indices.ToList(), placement, data.CollisionError );
			IEnumerable<IReadOnlyList<Vector3>> hulls = data.Physics == SceneMeshPhysics.MultipleHulls
				? GetCollisionIslands( points, indices )
				: ConvexDecomposition.Build( points, indices );
			foreach ( var hull in hulls )
			{
				var collider = owner.Components.Create<HullCollider>();
				collider.Type = HullCollider.PrimitiveType.Points;
				collider.Points = hull.Select( point => placement.PointToLocal( point ) ).ToList();
				collider.Surface = data.CollisionSurface ?? submesh.Material?.Surface;
			}
		}
	}

	// Rendering duplicates vertices at face seams. Partition collision triangles using
	// their shared positions so a disconnected piece gets its own convex hull.
	internal static IEnumerable<List<Vector3>> GetCollisionIslands( ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices )
	{
		var welded = new Dictionary<Vector3, int>( positions.Length );
		var parents = new int[positions.Length];
		for ( int i = 0; i < positions.Length; i++ )
		{
			if ( welded.TryAdd( positions[i], i ) ) parents[i] = i;
			else parents[i] = welded[positions[i]];
		}

		int Root( int index )
		{
			while ( index != parents[index] )
			{
				parents[index] = parents[parents[index]];
				index = parents[index];
			}
			return index;
		}

		for ( int i = 0; i < indices.Length; i += 3 )
		{
			int root = Root( indices[i] );
			parents[Root( indices[i + 1] )] = root;
			parents[Root( indices[i + 2] )] = root;
		}
		var islands = new Dictionary<int, List<Vector3>>();
		foreach ( var (position, index) in welded )
		{
			int root = Root( index );
			if ( !islands.TryGetValue( root, out var island ) ) islands[root] = island = [];
			island.Add( position );
		}
		return islands.Values.Where( island => island.Count >= 3 );
	}
}
