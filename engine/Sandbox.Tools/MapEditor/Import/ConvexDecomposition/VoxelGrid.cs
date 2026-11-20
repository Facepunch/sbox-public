using System;

namespace Editor.MapEditor;

internal static partial class ConvexDecomposition
{
	record struct Point( double X, double Y, double Z )
	{
		public static Point operator -( Point a, Point b ) => new( a.X - b.X, a.Y - b.Y, a.Z - b.Z );
		public static Point operator *( Point a, double s ) => new( a.X * s, a.Y * s, a.Z * s );
		public static Point Min( Point a, Point b ) => new( Math.Min( a.X, b.X ), Math.Min( a.Y, b.Y ), Math.Min( a.Z, b.Z ) );
		public static Point Max( Point a, Point b ) => new( Math.Max( a.X, b.X ), Math.Max( a.Y, b.Y ), Math.Max( a.Z, b.Z ) );
		public static Point Cross( Point a, Point b ) => new( a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X );
		public double Dot( Point p ) => X * p.X + Y * p.Y + Z * p.Z;
	}

	record struct Slab( Point Axis, double Min, double Max )
	{
		public Slab( Point axis, Point a, Point b, Point c ) : this( axis, 0, 0 )
		{
			Axis = axis;
			double r = (Math.Abs( axis.X ) + Math.Abs( axis.Y ) + Math.Abs( axis.Z )) * 0.5;
			double x = axis.Dot( a ), y = axis.Dot( b ), z = axis.Dot( c );
			Min = Math.Min( x, Math.Min( y, z ) ) - r;
			Max = Math.Max( x, Math.Max( y, z ) ) + r;
		}

		public bool Contains( Point center )
		{
			double d = Axis.Dot( center );
			return d >= Min && d <= Max;
		}
	}

	static class Grid
	{
		static (int Solid, int Surface, double Step, byte[] Cells, int Width, int Height, int Resolution) Raster( Point[] points, ReadOnlySpan<int> indices, int dim )
		{
			var min = points.Aggregate( Point.Min );
			var size = points.Aggregate( Point.Max ) - min;
			int gridAxis = size.X > size.Y && size.X > size.Z ? 0 : size.Y > size.X && size.Y > size.Z ? 1 : 2;
			// V-HACD breaks tied extents toward Z. For a thin XY sheet this can
			// select an arbitrarily small denominator, even for the initial grid.
			const int maxCells = 16 * 1024 * 1024;
			double EstimatedCells( int resolution, double axisLength ) => (2 + resolution * size.X / axisLength) * (2 + resolution * size.Y / axisLength) * (2 + resolution * size.Z / axisLength);
			double maxExtent = Math.Max( size.X, Math.Max( size.Y, size.Z ) );
			if ( maxExtent == 0 )
				return (0, 0, 0, [], 0, 0, 0);
			if ( gridAxis == 2 && (size.Z == 0 || EstimatedCells( 64, size.Z ) > maxCells) )
				gridAxis = size.X == maxExtent ? 0 : size.Y == maxExtent ? 1 : 2;
			var longest = gridAxis == 0 ? size.X : gridAxis == 1 ? size.Y : size.Z;
			if ( EstimatedCells( dim, longest ) > maxCells )
			{
				int low = 2, high = dim;
				while ( low < high )
				{
					int mid = low + (high - low + 1) / 2;
					if ( EstimatedCells( mid, longest ) <= maxCells )
						low = mid;
					else
						high = mid - 1;
				}

				dim = low;
			}

			var step = longest / (dim - 1);
			int Size( double length, int index ) => index == gridAxis ? dim : 2 + (int)(dim * length / longest);
			int nx = Size( size.X, 0 ), ny = Size( size.Y, 1 ), nz = Size( size.Z, 2 ), stride = nx * ny;
			var cells = new byte[checked(stride * nz)];
			var vertices = points.Select( p => (p - min) * ((dim - 1) / longest) ).ToArray();
			// The three box axes, nine edge cross products and triangle normal separate
			// a triangle from a voxel. Project once per triangle and reuse for all candidate cells.
			Span<Slab> slabs = stackalloc Slab[13];
			Point[] axes = [new( 1, 0, 0 ), new( 0, 1, 0 ), new( 0, 0, 1 )];
			int surface = 0;
			for ( int t = 0; t < indices.Length; t += 3 )
			{
				var a = vertices[indices[t]];
				var b = vertices[indices[t + 1]];
				var c = vertices[indices[t + 2]];
				int n = 0;
				foreach ( var axis in axes )
				{
					slabs[n++] = new( axis, a, b, c );
					slabs[n++] = new( Point.Cross( b - a, axis ), a, b, c );
					slabs[n++] = new( Point.Cross( c - b, axis ), a, b, c );
					slabs[n++] = new( Point.Cross( a - c, axis ), a, b, c );
				}

				slabs[n] = new( Point.Cross( b - a, c - a ), a, b, c );
				var lo = Point.Min( a, Point.Min( b, c ) );
				var hi = Point.Max( a, Point.Max( b, c ) );
				for ( int z = Math.Max( 0, (int)(lo.Z + 0.5) - 1 ); z <= Math.Min( nz - 1, (int)(hi.Z + 0.5) ); z++ )
					for ( int y = Math.Max( 0, (int)(lo.Y + 0.5) - 1 ); y <= Math.Min( ny - 1, (int)(hi.Y + 0.5) ); y++ )
						for ( int x = Math.Max( 0, (int)(lo.X + 0.5) - 1 ); x <= Math.Min( nx - 1, (int)(hi.X + 0.5) ); x++ )
						{
							int i = x + y * nx + z * stride;
							if ( cells[i] != 0 )
								continue;
							bool hit = true;
							foreach ( var slab in slabs )
								if ( !slab.Contains( new( x, y, z ) ) )
								{
									hit = false;
									break;
								}

							if ( hit )
							{
								cells[i] = 1;
								surface++;
							}
						}
			}

			// Flood-fill from the exterior. Unvisited empty cells are inside the mesh.
			var outside = new Queue<int>();
			int solid = cells.Length;
			void Visit( int i )
			{
				if ( cells[i] != 0 )
					return;
				cells[i] = 2;
				outside.Enqueue( i );
				solid--;
			}

			for ( int z = 0; z < nz; z++ )
				for ( int y = 0; y < ny; y++ )
					for ( int x = 0; x < nx; x++ )
						if ( x == 0 || x == nx - 1 || y == 0 || y == ny - 1 || z == 0 || z == nz - 1 )
							Visit( x + y * nx + z * stride );
			while ( outside.TryDequeue( out int i ) )
			{
				int x = i % nx, y = i / nx % ny, z = i / stride;
				if ( x > 0 )
					Visit( i - 1 );
				if ( x + 1 < nx )
					Visit( i + 1 );
				if ( y > 0 )
					Visit( i - nx );
				if ( y + 1 < ny )
					Visit( i + nx );
				if ( z > 0 )
					Visit( i - stride );
				if ( z + 1 < nz )
					Visit( i + stride );
			}

			return (solid, surface, step, cells, nx, ny, dim);
		}

		public static (int Dimension, double Step, List<Cell> Cells) Voxelize( Point[] points, ReadOnlySpan<int> indices )
		{
			int dim = 64;
			for ( int iteration = 1; ; iteration++ )
			{
				var grid = Raster( points, indices, dim );
				if ( grid.Solid == 0 )
					return (dim, grid.Step, []);
				int next = (int)(dim * Math.Pow( 100000.0 / grid.Solid, 0.33 ) + 0.5);
				if ( grid.Solid < 100000 && grid.Surface < 12500 && iteration < 5 && next != dim && grid.Resolution == dim )
				{
					dim = next;
					continue;
				}

				var occupied = new List<Cell>( grid.Solid );
				int stride = grid.Width * grid.Height;
				int depth = grid.Cells.Length / stride;
				for ( int x = 0; x < grid.Width; x++ )
					for ( int y = 0; y < grid.Height; y++ )
						for ( int z = 0; z < depth; z++ )
						{
							byte value = grid.Cells[x + y * grid.Width + z * stride];
							if ( value != 2 )
								occupied.Add( new( x, y, z, value == 1 ) );
						}

				return (grid.Resolution, grid.Step, occupied);
			}
		}
	}
}
