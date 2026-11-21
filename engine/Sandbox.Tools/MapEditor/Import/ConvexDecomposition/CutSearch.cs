using System;

namespace Editor.MapEditor;

internal static partial class ConvexDecomposition
{
	record struct Cell( int X, int Y, int Z, bool Surface )
	{
		public int Axis( int a ) => a == 0 ? X : a == 1 ? Y : Z;
		public IEnumerable<Point> Corners()
		{
			for ( int i = 0; i < 8; i++ )
				yield return new( X + ((i & 1) - 0.5), Y + (((i >> 1) & 1) - 0.5), Z + (((i >> 2) & 1) - 0.5) );
		}
	}

	// V-HACD first scores coarse axis-aligned cuts, then refines the best one.
	static List<Hull> Decompose( List<Cell> cells, Point origin, double step )
	{
		var boundary = new Boundary();
		Point[] samples = [];
		Hull Sample( List<Point> points )
		{
			if ( samples.Length < points.Count )
				Array.Resize( ref samples, Math.Max( points.Count, samples.Length * 2 ) );
			return ClusterHull.Sample( points, origin, step, samples );
		}

		double initialVolume = 0;
		var leaves = new List<Hull>();
		int width = cells.Max( v => v.X ) + 2, height = cells.Max( v => v.Y ) + 2;
		var lowColumns = new CornerColumns( width, height );
		var highColumns = new CornerColumns( width, height );
		Split( cells, 0, 0 );
		Merge( leaves, initialVolume * 0.0005 );
		var minimumVolume = initialVolume * 0.0001 * step * step * step;
		Point ToWorld( Point p ) => new( (float)(origin.X + p.X * step), (float)(origin.Y + p.Y * step), (float)(origin.Z + p.Z * step) );
		for ( int i = 0; i < leaves.Count; i++ )
			leaves[i] = new Hull( leaves[i].Vertices.Select( ToWorld ), 64, minimumVolume );
		return leaves;
		void Split( List<Cell> part, double rootVolume, int depth )
		{
			var surface = part.Where( v => v.Surface ).ToArray();
			if ( surface.Length == 0 )
				return;
			var hull = ClusterHull.Build( surface, origin, step, boundary );
			if ( hull.Volume <= 0 )
				return;
			if ( rootVolume == 0 )
				initialVolume = rootVolume = hull.Volume;
			double concavity = Math.Abs( hull.Volume - part.Count ) / rootVolume;
			if ( depth >= 20 || concavity <= Math.Max( 0.001, 1.01 * surface.Length / rootVolume ) )
			{
				leaves.Add( hull );
				return;
			}

			var (direction, weight) = Symmetry( part );
			double best = double.PositiveInfinity;
			int axis = -1, index = -1;
			var population = new int[3][];
			var mins = new int[3];
			var maxs = new int[3];
			for ( int a = 0; a < 3; a++ )
			{
				mins[a] = part.Min( v => v.Axis( a ) );
				maxs[a] = part.Max( v => v.Axis( a ) );
				var counts = population[a] = new int[maxs[a] + 1];
				foreach ( var v in part )
					counts[v.Axis( a )]++;
				for ( int i = 1; i < counts.Length; i++ )
					counts[i] += counts[i - 1];
			}

			for ( int a = 0; a < 3; a++ )
				for ( int i = mins[a]; i <= maxs[a]; i += 4 )
					Score( a, i, 128 );
			if ( axis < 0 )
			{
				leaves.Add( hull );
				return;
			}

			int coarse = index, selected = axis;
			best = double.PositiveInfinity;
			for ( int i = Math.Max( mins[selected], coarse - 4 ); i <= Math.Min( maxs[selected], coarse + 4 ); i++ )
				Score( selected, i, 32 );
			int leftCount = population[axis][index];
			var leftPart = new List<Cell>( leftCount );
			var rightPart = new List<Cell>( part.Count - leftCount );
			foreach ( var cell in part )
			{
				int coordinate = cell.Axis( axis );
				var side = coordinate <= index ? leftPart : rightPart;
				side.Add( coordinate == index || coordinate == index + 1 ? cell with { Surface = true } : cell );
			}

			Split( leftPart, rootVolume, depth + 1 );
			Split( rightPart, rootVolume, depth + 1 );
			void Score( int a, int i, int stride )
			{
				int nl = 0, nr = 0;
				foreach ( var v in surface )
				{
					int k = v.Axis( a );
					bool low = k <= i;
					ref int n = ref (low ? ref nl : ref nr);
					if ( k != i && k != i + 1 && ++n != stride )
						continue;
					if ( k != i && k != i + 1 )
						n = 0;
					(low ? lowColumns : highColumns).Add( v );
				}

				var left = lowColumns.Take();
				var right = highColumns.Take();
				foreach ( var p in hull.Vertices )
				{
					double k = a == 0 ? p.X : a == 1 ? p.Y : p.Z;
					if ( k <= i + 0.5 )
						left.Add( p );
					if ( k >= i + 0.5 )
						right.Add( p );
				}

				int l = population[a][i], r = part.Count - l;
				if ( l == 0 || r == 0 )
					return;
				double cost = (Math.Abs( Sample( left ).Volume - l ) + Math.Abs( Sample( right ).Volume - r ) + concavity * 0.05 * Math.Abs( l - r )) / rootVolume;
				if ( a == direction )
					cost += concavity * 0.05 * weight;
				if ( cost < best )
				{
					best = cost;
					axis = a;
					index = i;
				}
			}
		}
	}

	// Only merge if the extra enclosed volume falls below the map compiler's tolerance.
	static void Merge( List<Hull> hulls, double tolerance )
	{
		while ( hulls.Count > 1 )
		{
			double best = tolerance;
			int first = -1, second = -1;
			Hull replacement = null;
			for ( int a = 0; a < hulls.Count; a++ )
				for ( int b = a + 1; b < hulls.Count; b++ )
				{
					var combined = new Hull( hulls[a].Vertices.Concat( hulls[b].Vertices ) );
					double difference = Math.Abs( combined.Volume - hulls[a].Volume - hulls[b].Volume );
					if ( difference >= best )
						continue;
					best = difference;
					first = a;
					second = b;
					replacement = combined;
				}

			if ( replacement == null )
				return;
			hulls[first] = replacement;
			hulls.RemoveAt( second );
		}
	}

	static (int, double) Symmetry( List<Cell> part )
	{
		int[] center = Enumerable.Range( 0, 3 ).Select( a => (int)(part.Average( v => v.Axis( a ) ) + 0.5) ).ToArray();
		var m = new double[3, 3];
		foreach ( var v in part )
			for ( int a = 0; a < 3; a++ )
				for ( int b = 0; b < 3; b++ )
					m[a, b] += (v.Axis( a ) - center[a]) * (v.Axis( b ) - center[b]);
		for ( int iter = 0; iter < 32; iter++ )
		{
			int a = 0, b = 1;
			if ( Math.Abs( m[0, 2] ) > Math.Abs( m[a, b] ) )
			{
				a = 0;
				b = 2;
			}

			if ( Math.Abs( m[1, 2] ) > Math.Abs( m[a, b] ) )
			{
				a = 1;
				b = 2;
			}

			if ( Math.Abs( m[a, b] ) < 1e-10 )
				break;
			double u = (m[b, b] - m[a, a]) / (2 * m[a, b]);
			double t = Math.CopySign( 1, u ) / (Math.Abs( u ) + Math.Sqrt( 1 + u * u ));
			double c = 1 / Math.Sqrt( 1 + t * t ), s = t * c;
			m[a, a] -= t * m[a, b];
			m[b, b] += t * m[a, b];
			m[a, b] = m[b, a] = 0;
			int k = 3 - a - b;
			double x = m[k, a], y = m[k, b];
			m[a, k] = m[k, a] = c * x - s * y;
			m[b, k] = m[k, b] = s * x + c * y;
		}

		double[] diff = new double[3], sum = new double[3];
		for ( int i = 0; i < 3; i++ )
		{
			int a = (i + 1) % 3, b = (i + 2) % 3;
			double x = m[a, a], y = m[b, b];
			diff[i] = (x - y) * (x - y);
			sum[i] = x * x + y * y;
		}

		int axis = diff[0] < diff[1] && diff[0] < diff[2] ? 0 : diff[1] < diff[0] && diff[1] < diff[2] ? 1 : 2;
		return (axis, sum[axis] == 0 ? 0 : 1 - diff[axis] / sum[axis]);
	}

	// For fixed X and Y only the lowest and highest Z can be extreme points.
	sealed class CornerColumns( int width, int height )
	{
		readonly int[] low = new int[width * height], high = new int[width * height];
		readonly int[] touched = new int[width * height];
		readonly bool[] used = new bool[width * height];
		int count;
		public void Add( Cell cell )
		{
			int index = cell.X + cell.Y * width;
			Include( index, cell.Z );
			Include( index + 1, cell.Z );
			Include( index + width, cell.Z );
			Include( index + width + 1, cell.Z );
		}

		void Include( int index, int z )
		{
			if ( !used[index] )
			{
				used[index] = true;
				touched[count++] = index;
				low[index] = z;
				high[index] = z + 1;
			}
			else
			{
				if ( z < low[index] )
					low[index] = z;
				if ( z >= high[index] )
					high[index] = z + 1;
			}
		}

		readonly int[] chain = new int[height];
		readonly List<Point> result = [];
		public List<Point> Take()
		{
			result.Clear();
			for ( int x = 0; x < width; x++ )
			{
				Append( x, low, 1 );
				Append( x, high, -1 );
			}

			for ( int i = 0; i < count; i++ )
				used[touched[i]] = false;
			count = 0;
			return result;
			void Append( int x, int[] z, int sign )
			{
				int length = 0;
				for ( int y = 0; y < height; y++ )
				{
					int i = x + y * width;
					if ( !used[i] )
						continue;
					while ( length >= 2 )
					{
						int a = chain[length - 2], b = chain[length - 1];
						long turn = (long)(b - a) * (z[i] - z[a]) - (long)(i - a) * (z[b] - z[a]);
						if ( turn * sign > 0 )
							break;
						length--;
					}

					chain[length++] = i;
				}

				for ( int k = 0; k < length; k++ )
				{
					int i = chain[k];
					result.Add( new( x - 0.5, i / width - 0.5, z[i] - 0.5 ) );
				}
			}
		}
	}
}
