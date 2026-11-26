public sealed partial class SnapGrid
{
	// Rebuilt every frame the grid shows, so the storage is kept between frames
	private readonly Dictionary<(int, int, int), int> _vertexOwners = new();
	private readonly List<int> _triangleSets = new();
	private readonly List<(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal)> _connectedTriangles = new();

	// Curved, hollow props can have several disjoint surfaces facing the same axis.
	// Triangles sharing a vertex join one set, the one under the aim is kept.
	private void SelectConnectedSurface( Vector3 aim )
	{
		if ( _surfaceTriangles.Count < 2 ) return;

		_vertexOwners.Clear();
		_triangleSets.Clear();
		for ( var i = 0; i < _surfaceTriangles.Count; i++ ) _triangleSets.Add( i );

		var seed = 0;
		var best = float.MaxValue;
		for ( var i = 0; i < _surfaceTriangles.Count; i++ )
		{
			var t = _surfaceTriangles[i];
			Join( t.A, i ); Join( t.B, i ); Join( t.C, i );
			var projected = aim - t.Normal * Vector3.Dot( aim - t.A, t.Normal );
			var distance = InsideTriangle( projected, t.A, t.B, t.C, t.Normal )
				? (projected - aim).LengthSquared : ((t.A + t.B + t.C) / 3f - aim).LengthSquared;
			if ( distance < best ) { best = distance; seed = i; }
		}

		var set = Find( seed );
		_connectedTriangles.Clear();
		for ( var i = 0; i < _surfaceTriangles.Count; i++ )
		{
			if ( Find( i ) == set ) _connectedTriangles.Add( _surfaceTriangles[i] );
		}

		_surfaceTriangles.Clear();
		_surfaceTriangles.AddRange( _connectedTriangles );
		_surfaceVertices.Clear();
		foreach ( var t in _connectedTriangles )
		{
			_surfaceVertices.Add( new Vertex( t.A ) );
			_surfaceVertices.Add( new Vertex( t.B ) );
			_surfaceVertices.Add( new Vertex( t.C ) );
		}

		static (int, int, int) Key( Vector3 p ) => ((int)MathF.Round( p.x * 100 ), (int)MathF.Round( p.y * 100 ), (int)MathF.Round( p.z * 100 ));

		int Find( int i )
		{
			while ( _triangleSets[i] != i )
			{
				_triangleSets[i] = _triangleSets[_triangleSets[i]];
				i = _triangleSets[i];
			}
			return i;
		}

		void Join( Vector3 p, int i )
		{
			var key = Key( p );
			if ( !_vertexOwners.TryGetValue( key, out var other ) )
			{
				_vertexOwners[key] = i;
				return;
			}

			var a = Find( i );
			var b = Find( other );
			if ( a != b ) _triangleSets[a] = b;
		}
	}
}