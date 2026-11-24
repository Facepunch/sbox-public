using System.Numerics;

namespace Nav.Lib.Tests;

/// <summary>
/// Closed, outward facing boxes and slabs as triangles: the part of the scenario harness's mesh builder the
/// agent type tests use.
/// </summary>
sealed class MeshBuilder
{
	public readonly List<Vector3> Vertices = [];
	public readonly List<int> Indices = [];

	void Quad( int a, int b, int c, int d )
	{
		Indices.AddRange( [a, b, c, a, c, d] );
	}

	// Corners 0-3 are the bottom, counter-clockwise seen from above, 4-7 the top in the same order
	void Sides( int b )
	{
		Quad( b + 0, b + 3, b + 2, b + 1 );
		Quad( b + 0, b + 1, b + 5, b + 4 );
		Quad( b + 1, b + 2, b + 6, b + 5 );
		Quad( b + 2, b + 3, b + 7, b + 6 );
		Quad( b + 3, b + 0, b + 4, b + 7 );
	}

	void Corners( Vector3 min, Vector3 max )
	{
		Vertices.AddRange( [
			new( min.X, min.Y, min.Z ), new( max.X, min.Y, min.Z ), new( max.X, max.Y, min.Z ), new( min.X, max.Y, min.Z ),
			new( min.X, min.Y, max.Z ), new( max.X, min.Y, max.Z ), new( max.X, max.Y, max.Z ), new( min.X, max.Y, max.Z )] );
	}

	public void Box( Vector3 min, Vector3 max )
	{
		int b = Vertices.Count;
		Corners( min, max );
		Quad( b + 4, b + 5, b + 6, b + 7 );
		Sides( b );
	}

	/// <summary>
	/// A box whose top face is a grid of cells no larger than cell.
	/// </summary>
	public void Slab( Vector3 min, Vector3 max, float cell )
	{
		var nx = Math.Max( 1, (int)MathF.Ceiling( (max.X - min.X) / cell ) );
		var ny = Math.Max( 1, (int)MathF.Ceiling( (max.Y - min.Y) / cell ) );

		int top = Vertices.Count;
		for ( int j = 0; j <= ny; j++ )
		{
			var y = j == ny ? max.Y : min.Y + (max.Y - min.Y) * j / ny;
			for ( int i = 0; i <= nx; i++ )
			{
				var x = i == nx ? max.X : min.X + (max.X - min.X) * i / nx;
				Vertices.Add( new Vector3( x, y, max.Z ) );
			}
		}

		for ( int j = 0; j < ny; j++ )
		{
			for ( int i = 0; i < nx; i++ )
			{
				var v = top + j * (nx + 1) + i;
				Quad( v, v + 1, v + nx + 2, v + nx + 1 );
			}
		}

		int b = Vertices.Count;
		Corners( min, max );
		Sides( b );
	}
}
