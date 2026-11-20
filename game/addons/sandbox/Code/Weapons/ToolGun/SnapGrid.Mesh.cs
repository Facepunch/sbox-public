public sealed partial class SnapGrid
{
	private PhysicsBody _collisionBody;
	private PhysicsShape[] _collisionShapes = Array.Empty<PhysicsShape>();
	private readonly List<Vertex> _surfaceVertices = new();
	private readonly List<(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal)> _surfaceTriangles = new();
	private bool BuildCollisionSurface( GameObject go, Vector3 normal )
	{
		_surfaceTriangles.Clear();
		_surfaceVertices.Clear();
		var rigidbody = go.GetComponentInParent<Rigidbody>();
		if ( !rigidbody.IsValid() ) rigidbody = go.GetComponentInChildren<Rigidbody>( false );
		var body = rigidbody.IsValid() ? rigidbody.PhysicsBody : null;
		if ( !body.IsValid() ) return false;
		var shapes = body.Shapes.Where( s => !s.IsTrigger ).ToArray();
		if ( _collisionBody != body || !_collisionShapes.SequenceEqual( shapes ) )
		{
			_collisionBody = body;
			_collisionShapes = shapes;
			var vertices = new List<Vertex>();
			var indices = new List<uint>();
			foreach ( var shape in shapes )
			{
				shape.Triangulate( out var points, out var triangles );
				if ( points is null || triangles is null ) continue;
				var offset = (uint)vertices.Count;
				vertices.AddRange( points.Select( p => new Vertex( p ) ) );
				indices.AddRange( triangles.Select( i => i + offset ) );
			}
			_snapVertices = vertices.ToArray();
			_snapIndices = indices.ToArray();
		}
		if ( _snapVertices is null || _snapIndices is null ) return false;
		var triangleCount = _snapIndices.Length / 3;
		for ( var i = 0; i < triangleCount; i++ )
		{
			var ia = _snapIndices[i * 3]; var ib = _snapIndices[i * 3 + 1]; var ic = _snapIndices[i * 3 + 2];
			if ( ia >= _snapVertices.Length || ib >= _snapVertices.Length || ic >= _snapVertices.Length ) continue;
			var a = body.Transform.PointToWorld( _snapVertices[ia].Position );
			var b = body.Transform.PointToWorld( _snapVertices[ib].Position );
			var c = body.Transform.PointToWorld( _snapVertices[ic].Position );
			var cross = Vector3.Cross( b - a, c - a );
			if ( cross.LengthSquared < 0.000001f ) continue;
			var n = cross.Normal;
			if ( Vector3.Dot( n, normal ) < 0.25f ) continue;
			_surfaceTriangles.Add( (a, b, c, n) );
			_surfaceVertices.Add( new Vertex( a ) );
			_surfaceVertices.Add( new Vertex( b ) );
			_surfaceVertices.Add( new Vertex( c ) );
		}
		return _surfaceTriangles.Count > 0;
	}

	private Vector3 SnapToSurface( Vector3 gridPoint, Vector3 aim, Vector3 normal, Vector3 origin, float cellSize, Vector2 spacing, bool round )
	{
		var result = gridPoint;
		var best = float.MaxValue;
		HasValidSnap = false;
		// Only project actual rendered grid intersections. Never slide a point onto an edge.
		void TryPoint( Vector3 candidate )
		{
			// Projection only moves along the normal: planar distance is a lower
			// bound on the final distance. Skip candidates that cannot win.
			var delta = candidate - aim;
			var planarDelta = delta - normal * Vector3.Dot( delta, normal );
			if ( planarDelta.LengthSquared > best + 0.0001f ) return;
			foreach ( var t in _surfaceTriangles )
			{
				var depth = Vector3.Dot( t.A - candidate, t.Normal ) / Vector3.Dot( normal, t.Normal );
				var projected = candidate + normal * depth;
				if ( !InsideTriangle( projected, t.A, t.B, t.C, t.Normal ) ) continue;
				var distance = (projected - aim).LengthSquared;
				if ( distance >= best ) continue;
				best = distance; result = projected; LastSnapWorldNormal = t.Normal; HasValidSnap = true;
			}
		}
		// Establish a tight distance bound before considering the rest of the lattice.
		TryPoint( gridPoint );
		if ( round )
		{
			var spokes = (int)MathF.Round( MathF.Tau / _roundAngleStep );
			for ( var ring = 0; ring <= (int)MathF.Ceiling( _roundRadius / cellSize ); ring++ )
			{
				var radius = MathF.Min( ring * cellSize, _roundRadius );
				for ( var spoke = 0; spoke < (ring == 0 ? 1 : spokes); spoke++ )
				{
					var angle = spoke * _roundAngleStep;
					TryPoint( origin + (_cachedRight * MathF.Cos( angle ) + _cachedUp * MathF.Sin( angle )) * radius );
				}
			}
		}
		else
		{
			// Search the nearby lattice when the closest intersection falls outside the mesh or in a hole.
			for ( var x = -4; x <= 4; x++ )
				for ( var y = -4; y <= 4; y++ )
					TryPoint( gridPoint + _cachedRight * (x * spacing.x) + _cachedUp * (y * spacing.y) );
		}
		return result;
	}
}
