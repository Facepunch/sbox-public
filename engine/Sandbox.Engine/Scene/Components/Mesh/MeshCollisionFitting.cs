namespace Sandbox;

// Incremental quadric-error edge collapse with Hammer's boundary, valence and face inversion rules.
internal sealed partial class MeshCollisionFitting
{
	sealed class Vertex( int id, Point point )
	{
		public readonly int Id = id;
		public bool Marked;
		public Point Point = point;
		public Quadric Quadric;
		public readonly List<Edge> Edges = [];
		public readonly List<Face> Faces = [];
		public bool Removed;
	}

	sealed class Face( Vertex a, Vertex b, Vertex c )
	{
		public Vertex A = a, B = b, C = c;
		public Point Normal;
		public bool Removed;
		public Point ComputeNormal()
		{
			var n = Point.Cross( B.Point - A.Point, C.Point - A.Point );
			return n.Length > 0 ? n / n.Length : default;
		}
	}

	sealed class Edge( Vertex a, Vertex b )
	{
		public Vertex A = a, B = b;
		public readonly List<Face> Faces = new( 2 );
		public double Error;
		public Point Target;
		public int HeapIndex = -1;
		public bool Dirty;
		public Vertex Other( Vertex v ) => A == v ? B : A;
		public void SortEnds()
		{
			if ( A.Id > B.Id )
				(A, B) = (B, A);
		}
	}

	readonly Vertex[] vertices;
	readonly Face[] faces;
	readonly List<Edge> heap = [];
	readonly List<Edge> dirty = [];
	readonly double tolerance;
	MeshCollisionFitting( Point[] positions, int[] indices, double tolerance )
	{
		this.tolerance = tolerance;
		vertices = new Vertex[positions.Length];
		for ( int i = 0; i < positions.Length; i++ )
			vertices[i] = new( i, positions[i] );
		faces = new Face[indices.Length / 3];
		var edges = new Dictionary<(int, int), Edge>();
		for ( int i = 0; i < faces.Length; i++ )
		{
			var a = vertices[indices[3 * i]];
			var b = vertices[indices[3 * i + 1]];
			var c = vertices[indices[3 * i + 2]];
			var f = faces[i] = new( a, b, c );
			var cross = Point.Cross( b.Point - a.Point, c.Point - a.Point );
			double length = cross.Length;
			if ( length == 0 )
			{
				f.Removed = true;
				continue;
			}

			f.Normal = cross / length;
			double offset = Point.Dot( cross, a.Point ) / length;
			var metric = new Quadric();
			metric.Accumulate( f.Normal, offset, length / 2 );
			a.Quadric += metric;
			b.Quadric += metric;
			c.Quadric += metric;
			a.Faces.Add( f );
			b.Faces.Add( f );
			c.Faces.Add( f );
			Connect( a, b, f );
			Connect( b, c, f );
			Connect( c, a, f );
		}

		foreach ( var edge in edges.Values )
		{
			if ( edge.Faces.Count != 1 )
				continue;
			var direction = edge.B.Point - edge.A.Point;
			var cross = Point.Cross( direction, edge.Faces[0].Normal );
			var length = cross.Length;
			if ( length == 0 )
				continue;
			var metric = new Quadric();
			metric.Accumulate( cross / length, Point.Dot( cross, edge.A.Point ) / length, Point.Dot( direction, direction ), boundary: true );
			edge.A.Quadric += metric;
			edge.B.Quadric += metric;
		}

		foreach ( var e in edges.Values )
		{
			Score( e );
			e.HeapIndex = heap.Count;
			heap.Add( e );
			Up( e );
		}

		void Connect( Vertex a, Vertex b, Face f )
		{
			if ( a.Id > b.Id )
				(a, b) = (b, a);
			if ( !edges.TryGetValue( (a.Id, b.Id), out var e ) )
			{
				e = new( a, b );
				edges.Add( (a.Id, b.Id), e );
				a.Edges.Add( e );
				b.Edges.Add( e );
			}

			e.Faces.Add( f );
		}
	}

	static double Aspect( Point a, Point b, Point c )
	{
		var ab = a - b;
		var bc = b - c;
		var ca = c - a;
		double ab2 = Point.Dot( ab, ab ), bc2 = Point.Dot( bc, bc ), ca2 = Point.Dot( ca, ca );
		double x = Math.Sqrt( ab2 ), y = Math.Sqrt( bc2 ), z = Math.Sqrt( ca2 );
		double s = (x + y + z) / 2;
		return Math.Sqrt( (ab2 * bc2) * ca2 ) / ((8 * (s - x)) * ((s - y) * (s - z)));
	}

	void Score( Edge e )
	{
		var a = e.A;
		var b = e.B;
		e.Error = double.MaxValue;
		e.Target = default;
		if ( e.Faces.Count is not (1 or 2) )
			return;
		if ( e.Faces.Count != 1 && a.Edges.Exists( Boundary ) && b.Edges.Exists( Boundary ) )
			return;
		// Collapsing an edge with more than two shared neighbors would join unrelated faces.
		foreach ( var edge in b.Edges ) edge.Other( b ).Marked = true;
		int common = 0;
		foreach ( var edge in a.Edges )
			if ( edge.Other( a ).Marked ) common++;
		foreach ( var edge in b.Edges ) edge.Other( b ).Marked = false;
		if ( common > 2 ) return;

		var q = a.Quadric + b.Quadric;
		var target = q.Minimum( a.Point, b.Point );
		double error = q.Evaluate( target ) / q.Area;
		if ( Inverts( a ) || Inverts( b ) )
			return;
		int valence = a.Edges.Count + b.Edges.Count - e.Faces.Count - 2;
		if ( valence > 6 )
			error += 0.05 * (valence - 6) * tolerance;
		e.Error = error;
		e.Target = target;
		static bool Boundary( Edge edge ) => edge.Faces.Count == 1;
		bool Inverts( Vertex v )
		{
			foreach ( var f in v.Faces )
			{
				if ( e.Faces.Contains( f ) )
					continue;
				var x = f.A == v ? target : f.A.Point;
				var y = f.B == v ? target : f.B.Point;
				var z = f.C == v ? target : f.C.Point;
				var cross = Point.Cross( y - x, z - x );
				double length = cross.Length;
				if ( length < 2 * 0.005 * 0.005 || Point.Dot( f.Normal, cross / length ) < 0.5 )
					return true;
				double difference = Aspect( x, y, z ) - Aspect( f.A.Point, f.B.Point, f.C.Point );
				if ( difference > 0 )
					error += 0.05 * difference * tolerance;
			}

			return false;
		}
	}

	(Point[] Points, int[] Indices) Run()
	{
		for ( int i = 0; i < vertices.Length - 4 && heap.Count > 0 && heap[0].Error <= tolerance; i++ )
			Collapse( heap[0] );
		var points = new List<Point>();
		var indices = new List<int>();
		var map = new int[vertices.Length];
		foreach ( var v in vertices )
			if ( !v.Removed )
			{
				map[v.Id] = points.Count;
				points.Add( v.Point );
			}

		foreach ( var f in faces )
			if ( !f.Removed )
			{
				indices.Add( map[f.A.Id] );
				indices.Add( map[f.B.Id] );
				indices.Add( map[f.C.Id] );
			}

		return (points.ToArray(), indices.ToArray());
	}

	void Collapse( Edge e )
	{
		var a = e.A;
		var b = e.B;
		Collect( a );
		Collect( b );
		b.Point = e.Target;
		b.Quadric += a.Quadric;
		foreach ( var f in e.Faces )
		{
			var opposite = f.A != a && f.A != b ? f.A : f.B != a && f.B != b ? f.B : f.C;
			SwapRemove( a.Faces, f );
			SwapRemove( b.Faces, f );
			SwapRemove( opposite.Faces, f );
			foreach ( var n in opposite.Edges )
				if ( n.Other( opposite ) == a || n.Other( opposite ) == b )
					SwapRemove( n.Faces, f );
			f.Removed = true;
		}

		SwapRemove( a.Edges, e );
		SwapRemove( b.Edges, e );
		RemoveFromHeap( e );
		foreach ( var n in a.Edges )
		{
			if ( n.A == a )
				n.A = b;
			else
				n.B = b;
			n.SortEnds();
			b.Edges.Add( n );
		}

		foreach ( var f in a.Faces )
		{
			if ( f.A == a )
				f.A = b;
			else if ( f.B == a )
				f.B = b;
			else
				f.C = b;
			f.Normal = f.ComputeNormal();
			b.Faces.Add( f );
		}

		a.Edges.Clear();
		a.Faces.Clear();
		a.Removed = true;
		b.Edges.Sort( ( x, y ) => x.A.Id != y.A.Id ? x.A.Id.CompareTo( y.A.Id ) : x.B.Id.CompareTo( y.B.Id ) );
		for ( int i = b.Edges.Count - 1; i > 0; i-- )
		{
			var x = b.Edges[i - 1];
			var y = b.Edges[i];
			if ( x.A != y.A || x.B != y.B )
				continue;
			x.Faces.AddRange( y.Faces );
			SwapRemove( b.Edges, y );
			SwapRemove( y.Other( b ).Edges, y );
			RemoveFromHeap( y );
		}

		foreach ( var n in dirty )
		{
			n.Dirty = false;
			if ( n.HeapIndex < 0 )
				continue;
			Score( n );
			if ( n.HeapIndex > 0 && heap[(n.HeapIndex - 1) / 2].Error > n.Error )
				Up( n );
			else
				Down( n );
		}

		dirty.Clear();
		void Collect( Vertex v )
		{
			foreach ( var n in v.Edges )
				foreach ( var other in n.Other( v ).Edges )
					if ( !other.Dirty )
					{
						other.Dirty = true;
						dirty.Add( other );
					}
		}
	}

	static void SwapRemove<T>( List<T> list, T value )
	{
		int i = list.LastIndexOf( value );
		if ( i < 0 )
			return;
		list[i] = list[^1];
		list.RemoveAt( list.Count - 1 );
	}

	void Put( int i, Edge n )
	{
		heap[i] = n;
		n.HeapIndex = i;
	}

	void RemoveFromHeap( Edge e )
	{
		int i = e.HeapIndex;
		var last = heap[^1];
		Put( i, last );
		heap.RemoveAt( heap.Count - 1 );
		e.HeapIndex = -1;
		if ( i < heap.Count )
		{
			if ( last.Error > e.Error )
				Down( last );
			else
				Up( last );
		}
	}

	void Up( Edge n )
	{
		int i = n.HeapIndex;
		while ( i > 0 )
		{
			int parent = (i - 1) / 2;
			var p = heap[parent];
			if ( p.Error <= n.Error )
				break;
			Put( i, p );
			i = parent;
		}

		Put( i, n );
	}

	void Down( Edge n )
	{
		int i = n.HeapIndex;
		while ( i < heap.Count / 2 )
		{
			int child = 2 * i + 1;
			if ( child + 1 < heap.Count && heap[child + 1].Error < heap[child].Error )
				child++;
			if ( n.Error <= heap[child].Error )
				break;
			Put( i, heap[child] );
			i = child;
		}

		Put( i, n );
	}
}
