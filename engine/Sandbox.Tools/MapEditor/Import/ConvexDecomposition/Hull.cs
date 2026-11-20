using System;

namespace Editor.MapEditor;

internal static partial class ConvexDecomposition
{
	// Incremental convex hull. The final reduction chooses the vertex that adds the most volume.
	sealed class Hull
	{
		public List<Point> Vertices { get; } = new();
		public double Volume { get; }

		sealed class Face( int a, int b, int c, Point normal )
		{
			public readonly int A = a, B = b, C = c;
			public readonly Point Normal = normal;
			public readonly List<int> Outside = new();
		}

		public Hull( IEnumerable<Point> input, int vertexLimit = int.MaxValue, double minimumVolume = 0, Func<Point, Point> volumeCoordinates = null, Point[] scratch = null, int length = 0 )
		{
			var p = scratch ?? input.Distinct().ToArray();
			int inputLength = scratch == null ? p.Length : length;
			if ( inputLength < 4 )
				return;
			int a = 0;
			int b = Farthest( i => LengthSquared( p[i] - p[a] ) );
			int c = Farthest( i => LengthSquared( Point.Cross( p[b] - p[a], p[i] - p[a] ) ) );
			if ( vertexLimit != int.MaxValue )
			{
				a = inputLength - 1;
				b = 0;
				c = 1;
				while ( LengthSquared( Point.Cross( p[b] - p[a], p[c] - p[a] ) ) < 1e-18 )
				{
					a = b;
					b = c;
					c = (c + 1) % inputLength;
					if ( a == inputLength - 1 )
						return;
				}
			}

			var n = Point.Cross( p[b] - p[a], p[c] - p[a] );
			int d = Farthest( i => Math.Abs( n.Dot( p[i] - p[a] ) ) );
			if ( Math.Abs( n.Dot( p[d] - p[a] ) ) < 1e-10 )
				return;
			var remaining = vertexLimit == int.MaxValue ? null : Enumerable.Range( 1, inputLength ).Select( offset => (c + offset) % inputLength ).Where( i => i != a && i != b && i != c ).ToList();
			if ( remaining != null )
			{
				int nonplanar = remaining.FindIndex( i => Math.Abs( n.Dot( p[i] - p[a] ) ) >= 1e-15 );
				if ( nonplanar > 0 )
					(remaining[0], remaining[nonplanar]) = (remaining[nonplanar], remaining[0]);
				// Keep the winding of each side of the initial triangle. Equivalent determinants
				// round differently and can select a different fourth vertex on symmetric meshes.
				double InitialVolume( int i )
				{
					var x = p[a] - p[i];
					var y = p[b] - p[i];
					var z = p[c] - p[i];
					var v = x.Dot( Point.Cross( y, z ) );
					return v < 0 ? -v : -z.Dot( Point.Cross( y, x ) );
				}

				d = remaining.MaxBy( i => Math.Abs( InitialVolume( i ) ) );
				int pos = remaining.IndexOf( d );
				remaining[pos] = remaining[0];
				remaining.RemoveAt( 0 );
			}

			var inside = new Point( (p[a].X + p[b].X + p[c].X + p[d].X) / 4, (p[a].Y + p[b].Y + p[c].Y + p[d].Y) / 4, (p[a].Z + p[b].Z + p[c].Z + p[d].Z) / 4 );
			var faces = new List<Face>
			{
				Make( a, b, c ),
				Make( a, d, b ),
				Make( b, d, c ),
				Make( c, d, a )
			};
			for ( int i = 0; i < inputLength; i++ )
				Assign( i, faces );
			var horizon = new HashSet<(int A, int B)>();
			var displaced = new List<int>();
			var added = new List<Face>();
			var visible = new List<Face>();
			while ( faces.Find( f => f.Outside.Count > 0 ) is { } seed )
			{
				int tip = seed.Outside[0];
				double farthest = Distance( seed, tip );
				foreach ( int candidate in seed.Outside )
				{
					double distance = Distance( seed, candidate );
					if ( distance <= farthest )
						continue;
					farthest = distance;
					tip = candidate;
				}

				if ( vertexLimit != int.MaxValue )
				{
					int count = faces.Count / 2 + 2;
					if ( count >= vertexLimit )
						break;
					double best = count > 4 ? minimumVolume : 0;
					tip = -1;
					for ( int ci = 0; ci < remaining!.Count; ci++ )
					{
						int candidate = remaining[ci];
						double gain = 0;
						foreach ( var plane in faces )
						{
							var origin = p[candidate];
							double volume = (p[plane.A] - origin).Dot( Point.Cross( p[plane.C] - origin, p[plane.B] - origin ) );
							if ( volume > 1e-15 )
								gain += volume;
						}

						if ( gain == 0 )
						{
							remaining.RemoveAt( ci-- );
							continue;
						}

						if ( gain <= best )
							continue;
						best = gain;
						tip = candidate;
					}

					if ( tip < 0 )
						break;
					int pos = remaining!.IndexOf( tip );
					remaining[pos] = remaining[0];
					remaining.RemoveAt( 0 );
				}

				visible.Clear();
				horizon.Clear();
				displaced.Clear();
				added.Clear();
				foreach ( var face in faces )
					if ( Distance( face, tip ) > 1e-9 )
						visible.Add( face );
				foreach ( var f in visible )
				{
					Edge( f.A, f.B );
					Edge( f.B, f.C );
					Edge( f.C, f.A );
					displaced.AddRange( f.Outside );
					faces.Remove( f );
				}

				foreach ( var edge in horizon )
					added.Add( Make( edge.A, edge.B, tip ) );
				faces.AddRange( added );
				foreach ( int i in displaced )
					if ( i != tip )
						Assign( i, added );
				void Edge( int x, int y )
				{
					if ( !horizon.Remove( (y, x) ) )
						horizon.Add( (x, y) );
				}
			}

			var included = new bool[inputLength];
			foreach ( var face in faces )
			{
				Include( face.A );
				Include( face.B );
				Include( face.C );
			}

			void Include( int index )
			{
				if ( included[index] )
					return;
				included[index] = true;
				Vertices.Add( p[index] );
			}

			if ( volumeCoordinates != null )
			{
				for ( int i = 0; i < inputLength; i++ )
					if ( included[i] )
						p[i] = volumeCoordinates( p[i] );
				inside = volumeCoordinates( inside );
			}

			Volume = faces.Sum( f => Point.Cross( p[f.B] - p[f.A], p[f.C] - p[f.A] ).Dot( p[f.A] - inside ) ) / 6;
			int Farthest( Func<int, double> f ) => Enumerable.Range( 0, inputLength ).MaxBy( f );
			double LengthSquared( Point v ) => v.Dot( v );
			Face Make( int x, int y, int z )
			{
				var norm = Point.Cross( p[y] - p[x], p[z] - p[x] );
				if ( norm.Dot( inside - p[x] ) > 0 )
				{
					(y, z) = (z, y);
					norm = norm * (-1);
				}

				double area = Math.Sqrt( norm.Dot( norm ) );
				return new( x, y, z, norm * (1 / area) );
			}

			double Distance( Face f, int i ) => f.Normal.Dot( p[i] - p[f.A] );
			void Assign( int i, List<Face> options )
			{
				foreach ( var f in options )
					if ( Distance( f, i ) > 1e-9 )
					{
						f.Outside.Add( i );
						break;
					}
			}
		}
	}
}
