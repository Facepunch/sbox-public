
namespace Editor.MeshEditor;

static class SceneTraceMeshExtensions
{
	static Vector2 RayScreenPosition => SceneViewportWidget.MousePosition;

	public static MeshVertex GetClosestVertex( this SceneTrace trace, int radius ) => GetClosestVertex( trace, radius, out _ );

	public static MeshVertex GetClosestVertex( this SceneTrace trace, int radius, out MeshFace bestFace )
	{
		var point = RayScreenPosition;
		bestFace = TraceFace( trace, out var bestHitDistance, out _ );
		var bestVertex = bestFace.GetClosestVertex( point, radius );

		if ( bestFace.IsValid() && bestVertex.IsValid() )
			return bestVertex;

		var results = TraceFaces( trace, radius, point );
		foreach ( var result in results )
		{
			var face = result.MeshFace;
			var hitDistance = result.Distance;
			var vertex = face.GetClosestVertex( point, radius );
			if ( !vertex.IsValid() )
				continue;

			if ( hitDistance < bestHitDistance || !bestFace.IsValid() )
			{
				bestHitDistance = hitDistance;
				bestVertex = vertex;
				bestFace = face;
			}
		}

		return bestVertex;
	}

	public static MeshEdge GetClosestEdge( this SceneTrace trace, int radius ) => GetClosestEdge( trace, radius, out _, out _ );

	public static MeshEdge GetClosestEdge( this SceneTrace trace, int radius, out MeshFace bestFace, out Vector3 hitPosition )
	{
		var point = RayScreenPosition;
		bestFace = TraceFace( trace, out var bestHitDistance, out _ );
		hitPosition = Gizmo.CurrentRay.Project( bestHitDistance );
		var bestEdge = bestFace.GetClosestEdge( hitPosition, point, radius );

		if ( bestFace.IsValid() && bestEdge.IsValid() )
			return bestEdge;

		var results = TraceFaces( trace, radius, point );
		foreach ( var result in results )
		{
			var face = result.MeshFace;
			var hitDistance = result.Distance;
			hitPosition = Gizmo.CurrentRay.Project( hitDistance );

			var edge = face.GetClosestEdge( hitPosition, point, radius );
			if ( !edge.IsValid() )
				continue;

			if ( hitDistance < bestHitDistance || !bestFace.IsValid() )
			{
				bestHitDistance = hitDistance;
				bestEdge = edge;
				bestFace = face;
			}
		}

		return bestEdge;
	}

	public static MeshFace TraceFace( this SceneTrace trace )
	{
		return TraceFace( trace, out _, out _ );
	}

	public static MeshFace TraceFace( this SceneTrace trace, out Vector3 hitPosition )
	{
		return TraceFace( trace, out _, out hitPosition );
	}

	static MeshFace TraceFace( this SceneTrace trace, out float distance, out Vector3 hitPosition )
	{
		distance = default;
		hitPosition = default;

		var result = trace.Run();
		if ( !result.Hit || result.Component is not MeshComponent component )
			return default;

		hitPosition = result.HitPosition;
		distance = result.Distance;
		var face = component.Mesh.TriangleToFace( result.Triangle );
		return new MeshFace( component, face );
	}

	struct MeshFaceTraceResult
	{
		public MeshFace MeshFace;
		public float Distance;
	}

	static List<MeshFaceTraceResult> TraceFaces( this SceneTrace trace, int radius, Vector2 point )
	{
		var rays = new List<Ray> { Gizmo.CurrentRay };
		for ( var ring = 1; ring < radius; ring++ )
		{
			rays.Add( Gizmo.Camera.GetRay( point + new Vector2( 0, ring ) ) );
			rays.Add( Gizmo.Camera.GetRay( point + new Vector2( ring, 0 ) ) );
			rays.Add( Gizmo.Camera.GetRay( point + new Vector2( 0, -ring ) ) );
			rays.Add( Gizmo.Camera.GetRay( point + new Vector2( -ring, 0 ) ) );
		}

		var faces = new List<MeshFaceTraceResult>();
		var faceHash = new HashSet<MeshFace>();
		foreach ( var ray in rays )
		{
			var result = trace.Ray( ray, Gizmo.RayDepth ).Run();
			if ( !result.Hit )
				continue;

			if ( result.Component is not MeshComponent component )
				continue;

			var face = component.Mesh.TriangleToFace( result.Triangle );
			var faceElement = new MeshFace( component, face );
			if ( faceHash.Add( faceElement ) )
				faces.Add( new MeshFaceTraceResult { MeshFace = faceElement, Distance = result.Distance } );
		}

		return faces;
	}

	public static MeshFace TraceFace( this SceneTrace trace, int radius, out Vector3 hitPosition )
	{
		MeshFace closest = default;
		var closestDist = float.MaxValue;
		var closestPoint = Vector3.Zero;
		var point = RayScreenPosition;

		void TestRay( Ray ray )
		{
			var result = trace.Ray( ray, Gizmo.RayDepth ).Run();
			if ( !result.Hit || result.Distance >= closestDist )
				return;

			if ( result.Component is not MeshComponent component )
				return;

			closest = new MeshFace( component, component.Mesh.TriangleToFace( result.Triangle ) );
			closestDist = result.Distance;
			closestPoint = result.HitPosition;
		}

		TestRay( Gizmo.CurrentRay );

		for ( var ring = 1; ring < radius; ring++ )
		{
			TestRay( Gizmo.Camera.GetRay( point + new Vector2( 0, ring ) ) );
			TestRay( Gizmo.Camera.GetRay( point + new Vector2( ring, 0 ) ) );
			TestRay( Gizmo.Camera.GetRay( point + new Vector2( 0, -ring ) ) );
			TestRay( Gizmo.Camera.GetRay( point + new Vector2( -ring, 0 ) ) );
		}

		hitPosition = closestPoint;

		return closest;
	}
}
