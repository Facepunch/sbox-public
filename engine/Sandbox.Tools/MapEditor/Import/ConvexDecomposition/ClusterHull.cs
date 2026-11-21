using System;

namespace Editor.MapEditor;

internal static partial class ConvexDecomposition
{
	static class ClusterHull
	{
		public static Hull Build( Cell[] surface, Point origin, double step, Boundary boundary )
		{
			// Hammer fits batches of 65,536 corners before fitting their combined hull.
			var clusters = new List<Point>();
			var corners = new HashSet<Point>();
			for ( int i = 0; i < surface.Length; i += 8192 )
			{
				corners.Clear();
				for ( int j = i; j < Math.Min( i + 8192, surface.Length ); j++ )
					foreach ( var p in surface[j].Corners() )
						corners.Add( new( origin.X + p.X * step, origin.Y + p.Y * step, origin.Z + p.Z * step ) );
				clusters.AddRange( boundary.Compute( corners ) );
			}

			var world = boundary.Compute( clusters );
			var grid = world.Select( p => (p - origin) * (1 / step) ).ToList();
			var result = new Hull( grid );
			// Preserve boundary traversal order for the final maximum-volume reduction.
			result.Vertices.Clear();
			result.Vertices.AddRange( grid );
			return result;
		}

		public static Hull Sample( List<Point> points, Point origin, double step, Point[] work )
		{
			if ( points.Count < 4 )
				return new Hull( points );
			for ( int i = 0; i < points.Count; i++ )
			{
				var p = points[i];
				work[i] = new Point( (float)(origin.X + p.X * step), (float)(origin.Y + p.Y * step), (float)(origin.Z + p.Z * step) );
			}

			return QuantizedHull( work, points.Count, origin, step );
		}

		static Hull QuantizedHull( Point[] source, int count, Point origin, double step )
		{
			var low = source[0];
			var high = low;
			for ( int i = 1; i < count; i++ )
			{
				low = Point.Min( low, source[i] );
				high = Point.Max( high, source[i] );
			}

			var center = new Point( (float)(low.X + high.X) * 0.5f, (float)(low.Y + high.Y) * 0.5f, (float)(low.Z + high.Z) * 0.5f );
			var extent = high - low;
			float Unit( double v ) => (float)v * (1f / 10216f);
			var unit = new Point( Unit( extent.X ), Unit( extent.Y ), Unit( extent.Z ) );
			int Encode( double v, double c, double u ) => u == 0 ? 0 : (int)((float)(v - c) * (1f / (float)u));
			float Decode( double v, double c, double u ) => (float)v * (float)u + (float)c;
			Point DecodePoint( Point p ) => new( Decode( p.X, center.X, unit.X ), Decode( p.Y, center.Y, unit.Y ), Decode( p.Z, center.Z, unit.Z ) );
			for ( int i = 0; i < count; i++ )
			{
				var p = source[i];
				source[i] = new Point( Encode( p.X, center.X, unit.X ), Encode( p.Y, center.Y, unit.Y ), Encode( p.Z, center.Z, unit.Z ) );
			}

			return new Hull( source, scratch: source, length: count, volumeCoordinates: p => (DecodePoint( p ) - origin) * (1 / step) );
		}
	}
}
