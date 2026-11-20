public sealed partial class SnapGrid
{
	// Curved, hollow props can have several disjoint surfaces facing the same axis.
	private void SelectConnectedSurface( Vector3 aim )
	{
		if ( _surfaceTriangles.Count < 2 ) return;
		var vertices = new Dictionary<(int, int, int), List<int>>();
		(int, int, int) Key( Vector3 p ) => ((int)MathF.Round( p.x * 100 ), (int)MathF.Round( p.y * 100 ), (int)MathF.Round( p.z * 100 ));
		var seed = 0;
		var best = float.MaxValue;
		for ( var i = 0; i < _surfaceTriangles.Count; i++ )
		{
			var t = _surfaceTriangles[i];
			Add( t.A, i ); Add( t.B, i ); Add( t.C, i );
			var projected = aim - t.Normal * Vector3.Dot( aim - t.A, t.Normal );
			var distance = InsideTriangle( projected, t.A, t.B, t.C, t.Normal )
				? (projected - aim).LengthSquared : ((t.A + t.B + t.C) / 3f - aim).LengthSquared;
			if ( distance < best ) { best = distance; seed = i; }
		}
		var selected = new HashSet<int> { seed };
		var pending = new Queue<int>();
		pending.Enqueue( seed );
		while ( pending.TryDequeue( out var index ) )
		{
			var t = _surfaceTriangles[index];
			Visit( t.A ); Visit( t.B ); Visit( t.C );
		}
		var triangles = _surfaceTriangles.Where( (_, i) => selected.Contains( i ) ).ToArray();
		_surfaceTriangles.Clear();
		_surfaceTriangles.AddRange( triangles );
		_surfaceVertices.Clear();
		foreach ( var t in triangles )
		{
			_surfaceVertices.Add( new Vertex( t.A ) );
			_surfaceVertices.Add( new Vertex( t.B ) );
			_surfaceVertices.Add( new Vertex( t.C ) );
		}
		void Add( Vector3 p, int i )
		{
			var key = Key( p );
			if ( !vertices.TryGetValue( key, out var list ) ) vertices[key] = list = new();
			list.Add( i );
		}
		void Visit( Vector3 p )
		{
			foreach ( var i in vertices[Key( p )] )
				if ( selected.Add( i ) ) pending.Enqueue( i );
		}
	}
}