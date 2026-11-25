namespace Sandbox;

internal sealed partial class MeshCollisionFitting
{
	readonly record struct Point( double X, double Y, double Z )
	{
		public static Point operator -( Point a, Point b ) => new( a.X - b.X, a.Y - b.Y, a.Z - b.Z );
		public static Point operator /( Point p, double k ) => new( p.X / k, p.Y / k, p.Z / k );
		public static Point operator *( double k, Point p ) => new( k * p.X, k * p.Y, k * p.Z );
		public static double Dot( Point a, Point b ) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
		public static Point Cross( Point a, Point b ) => new( a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X );
		public double Length => Math.Sqrt( Dot( this, this ) );
	}

	struct Quadric
	{
		public double Area, XX, XY, XZ, XW, YY, YZ, YW, ZZ, ZW, WW;
		public void Accumulate( Point plane, double offset, double area, bool boundary = false )
		{
			double x = plane.X, y = plane.Y, z = plane.Z, w = -offset;
			double Contribution( double a, double b ) => boundary ? ((a * b) * area) * 1000 : ((area * 2) * a) * (0.5 * b);
			Area += area;
			XX += Contribution( x, x );
			XY += Contribution( y, x );
			XZ += Contribution( z, x );
			XW += Contribution( x, w );
			YY += Contribution( y, y );
			YZ += Contribution( z, y );
			YW += Contribution( y, w );
			ZZ += Contribution( z, z );
			ZW += Contribution( z, w );
			WW += Contribution( w, w );
		}

		public static Quadric operator +( Quadric a, Quadric b ) => new()
		{
			Area = a.Area + b.Area,
			XX = a.XX + b.XX,
			XY = a.XY + b.XY,
			XZ = a.XZ + b.XZ,
			XW = a.XW + b.XW,
			YY = a.YY + b.YY,
			YZ = a.YZ + b.YZ,
			YW = a.YW + b.YW,
			ZZ = a.ZZ + b.ZZ,
			ZW = a.ZW + b.ZW,
			WW = a.WW + b.WW
		};
		public double Evaluate( Point p )
		{
			double x = p.X, y = p.Y, z = p.Z;
			double alongX = x * (x * XX + 2 * XW);
			double alongY = y * (y * YY + x * (2 * XY) + 2 * YW);
			double alongZ = z * (y * (2 * YZ) + x * (2 * XZ) + z * ZZ + 2 * ZW);
			return (alongY + alongX) + (alongZ + WW);
		}

		public Point Minimum( Point a, Point b )
		{
			Point x = new( XX, XY, XZ ), y = new( XY, YY, YZ ), z = new( XZ, YZ, ZZ );
			var yz = Point.Cross( y, z );
			double determinant = Point.Dot( x, yz );
			if ( determinant <= 2.2250738585072014e-305 )
				return Evaluate( a ) < Evaluate( b ) ? a : b;
			return -XW * (yz / determinant) - YW * (Point.Cross( z, x ) / determinant) - ZW * (Point.Cross( x, y ) / determinant);
		}
	}
}
