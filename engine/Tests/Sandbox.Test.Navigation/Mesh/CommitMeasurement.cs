using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// The meshes of the library's commit measurements, which the link page and island tests use. The
/// measurements themselves are not here.
/// </summary>
static class CommitMeasurement
{
	// 30 polygons per tile, portals on all four sides: about what a city tile has.
	static readonly int[] Cuts = [0, 10, 20, 32, 44, 54, 64];

	internal static Tile Build( int x, int y ) => GridMesh.Rects( x, y, Cuts, [0, 12, 24, 40, 52, 64] );

	// About 30,000 links: four in each of 87 x 87 tiles.
	public const int LinkSide = 87, LinksPerTile = 4;

	// A two-way link inside tile x, y, k from 0 to 3, its ends inside polygons.
	public static NavLink LinkIn( int x, int y, int k ) => new()
	{
		Start = new Vector3( x * GridMesh.TileWorld + 70 + 150 * k, y * GridMesh.TileWorld + 60, 1 ),
		End = new Vector3( x * GridMesh.TileWorld + 70 + 150 * k, y * GridMesh.TileWorld + 580, 1 ),
		Radius = 8,
		Bidirectional = true,
		Area = 1
	};

	// As LinkIn, but its end in the tile east of x, y, or west in the last column, so that it joins two tiles.
	public static NavLink LinkAcross( int x, int y, int k ) => LinkIn( x, y, k ) with
	{
		End = new Vector3( (x + 1 < LinkSide ? x + 1 : x - 1) * GridMesh.TileWorld + 70 + 150 * k, y * GridMesh.TileWorld + 580, 1 )
	};

	/// <summary>
	/// side x side tiles with perTile links in each, added in a shuffled order so that the ids of one
	/// tile are spread over the table, as the links a game places by hand are. Links inside their tile
	/// unless made by make.
	/// </summary>
	public static NavMesh ManyLinks( int side, int perTile, Func<int, int, int, NavLink>? make = null )
	{
		make ??= LinkIn;
		var mesh = GridMesh.Create();
		var batch = new List<Tile>();
		for ( int i = 0; i < side * side; i++ )
		{
			batch.Add( Build( i % side, i / side ) );
			if ( batch.Count < 256 && i + 1 < side * side ) continue;
			mesh.CommitTiles( batch.ToArray() );
			batch.Clear();
		}
		var order = Enumerable.Range( 0, side * side * perTile ).ToArray();
		new Random( 7 ).Shuffle( order );
		foreach ( int i in order ) Assert.IsTrue( mesh.AddLink( make( i / perTile % side, i / perTile / side, i % perTile ) ) >= 0 );
		mesh.Commit();
		return mesh;
	}
}
