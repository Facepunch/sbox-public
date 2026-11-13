using System.Numerics;

namespace Nav.Lib.Tests;

/// <summary>
/// Hand-made tiles for tests: one square polygon per walkable cell, 10 units wide, 64 cells per tile.
/// </summary>
static class GridMesh
{
	public const int Cells = 64;
	public const float Cell = 10, TileWorld = Cells * Cell;

	public static readonly NavSettings Settings = NavSettings.Create( agentHeight: 32, agentRadius: 8, agentClimb: 8, agentMaxSlope: 40, cellSize: Cell, cellHeight: 1, tileSize: Cells );

	public readonly record struct Layer( float Z, Func<int, int, bool>? Walkable = null, Func<int, int, byte>? Area = null, Func<float, float, float>? Height = null );

	public static NavMesh Create() => new( Settings );

	/// <summary>
	/// A tile from layers of cells. Cell coordinates are local to the tile. Height, when given, replaces
	/// the layer's Z per vertex from world x and y.
	/// </summary>
	public static Tile Tile( int tileX, int tileY, params Layer[] layers )
	{
		var verts = new List<Vector3>();
		var polys = new List<Poly>();
		foreach ( var layer in layers )
		{
			var vertexAt = new Dictionary<(int, int), ushort>();
			var polyAt = new Dictionary<(int, int), ushort>();
			bool Walkable( int x, int y ) => x >= 0 && y >= 0 && x < Cells && y < Cells && (layer.Walkable?.Invoke( x, y ) ?? true);

			for ( int y = 0; y < Cells; y++ )
				for ( int x = 0; x < Cells; x++ )
					if ( Walkable( x, y ) ) polyAt[(x, y)] = (ushort)(polys.Count + polyAt.Count);

			ushort Vertex( int x, int y )
			{
				if ( vertexAt.TryGetValue( (x, y), out ushort index ) ) return index;
				float wx = tileX * TileWorld + x * Cell, wy = tileY * TileWorld + y * Cell;
				verts.Add( new Vector3( wx, wy, layer.Height?.Invoke( wx, wy ) ?? layer.Z ) );
				return vertexAt[(x, y)] = (ushort)(verts.Count - 1);
			}

			ushort Neighbour( int x, int y, int side )
			{
				if ( x < 0 || y < 0 || x >= Cells || y >= Cells ) return (ushort)(Poly.Border + side);
				return polyAt.TryGetValue( (x, y), out ushort index ) ? index : Poly.Wall;
			}

			for ( int y = 0; y < Cells; y++ )
			{
				for ( int x = 0; x < Cells; x++ )
				{
					if ( !Walkable( x, y ) ) continue;
					// Clockwise seen from above: west, north, east and south edge.
					var poly = new Poly { VertCount = 4, Area = layer.Area?.Invoke( x, y ) ?? 1 };
					poly.Verts[0] = Vertex( x, y );
					poly.Verts[1] = Vertex( x, y + 1 );
					poly.Verts[2] = Vertex( x + 1, y + 1 );
					poly.Verts[3] = Vertex( x + 1, y );
					poly.Neis[0] = Neighbour( x - 1, y, 2 );
					poly.Neis[1] = Neighbour( x, y + 1, 1 );
					poly.Neis[2] = Neighbour( x + 1, y, 0 );
					poly.Neis[3] = Neighbour( x, y - 1, 3 );
					polys.Add( poly );
				}
			}
		}

		var tile = new TileAssembler().Create( tileX, tileY, verts.ToArray(), polys.ToArray(), Settings, out var error );
		return tile ?? throw new InvalidOperationException( error );
	}

	public static Tile Flat( int tileX, int tileY, float z = 0 ) => Tile( tileX, tileY, new Layer( z ) );

	/// <summary>
	/// A tile of one rectangular polygon, in cells. Sides on the tile border are portals.
	/// </summary>
	public static Tile Quad( int tileX, int tileY, int x0 = 0, int y0 = 0, int x1 = Cells, int y1 = Cells, float z = 0, byte area = 1 )
	{
		Vector3 At( int x, int y ) => new( tileX * TileWorld + x * Cell, tileY * TileWorld + y * Cell, z );
		var poly = new Poly { VertCount = 4, Area = area };
		for ( int i = 0; i < 4; i++ ) poly.Verts[i] = (ushort)i;
		poly.Neis[0] = x0 == 0 ? (ushort)(Poly.Border + 2) : Poly.Wall;
		poly.Neis[1] = y1 == Cells ? (ushort)(Poly.Border + 1) : Poly.Wall;
		poly.Neis[2] = x1 == Cells ? Poly.Border : Poly.Wall;
		poly.Neis[3] = y0 == 0 ? (ushort)(Poly.Border + 3) : Poly.Wall;
		var tile = new TileAssembler().Create( tileX, tileY, [At( x0, y0 ), At( x0, y1 ), At( x1, y1 ), At( x1, y0 )], [poly], Settings, out var error );
		return tile ?? throw new InvalidOperationException( error );
	}

	/// <summary>
	/// A tile of rectangles between cut lines, given in cells from 0 to 64. Unlike the one-polygon-per-cell
	/// tiles this gives the few large polygons a generated mesh has.
	/// </summary>
	public static Tile Rects( int tileX, int tileY, int[] xs, int[] ys, Func<int, int, bool>? walkable = null, float scale = 1, float z = 0 )
	{
		int columns = xs.Length - 1, rows = ys.Length - 1;
		bool Walkable( int i, int j ) => i >= 0 && j >= 0 && i < columns && j < rows && (walkable?.Invoke( i, j ) ?? true);
		var index = new Dictionary<(int, int), ushort>();
		for ( int j = 0; j < rows; j++ )
			for ( int i = 0; i < columns; i++ )
				if ( Walkable( i, j ) ) index[(i, j)] = (ushort)index.Count;

		var verts = new Vector3[xs.Length * ys.Length];
		for ( int j = 0; j < ys.Length; j++ )
			for ( int i = 0; i < xs.Length; i++ )
				verts[j * xs.Length + i] = new Vector3( tileX * TileWorld + xs[i] * Cell, tileY * TileWorld + ys[j] * Cell, z ) * scale;

		ushort Neighbour( int i, int j, int side, bool onBorder ) => index.TryGetValue( (i, j), out ushort at ) ? at : onBorder ? (ushort)(Poly.Border + side) : Poly.Wall;

		var polys = new Poly[index.Count];
		foreach ( var ((i, j), at) in index )
		{
			ref var poly = ref polys[at];
			poly.VertCount = 4;
			poly.Area = 1;
			poly.Verts[0] = (ushort)(j * xs.Length + i);
			poly.Verts[1] = (ushort)((j + 1) * xs.Length + i);
			poly.Verts[2] = (ushort)((j + 1) * xs.Length + i + 1);
			poly.Verts[3] = (ushort)(j * xs.Length + i + 1);
			poly.Neis[0] = Neighbour( i - 1, j, 2, xs[i] == 0 );
			poly.Neis[1] = Neighbour( i, j + 1, 1, ys[j + 1] == Cells );
			poly.Neis[2] = Neighbour( i + 1, j, 0, xs[i + 1] == Cells );
			poly.Neis[3] = Neighbour( i, j - 1, 3, ys[j] == 0 );
		}

		var tile = new TileAssembler().Create( tileX, tileY, verts, polys, scale == 1 ? Settings : Scaled( scale ), out var error );
		return tile ?? throw new InvalidOperationException( error );
	}

	/// <summary>
	/// The same mesh dimensions in another unit: every length of the settings times scale.
	/// </summary>
	public static NavSettings Scaled( float scale ) => NavSettings.Create( 32 * scale, 8 * scale, 8 * scale, 40, Cell * scale, 1 * scale, Cells );

	/// <summary>
	/// A mesh of flat tiles, committed in one go.
	/// </summary>
	public static NavMesh Mesh( params (int X, int Y)[] tiles )
	{
		var mesh = Create();
		mesh.CommitTiles( tiles.Select( t => Flat( t.X, t.Y ) ).ToArray() );
		return mesh;
	}

	public static NavMesh Mesh( params Tile[] tiles )
	{
		var mesh = Create();
		mesh.CommitTiles( tiles );
		return mesh;
	}

	public static readonly Vector3 Snap = new( 20, 20, 20 );

	public static PolyRef Nearest( NavQuery query, float x, float y, float z = 1 ) => Nearest( query, x, y, z, default );

	public static PolyRef Nearest( NavQuery query, float x, float y, float z, NavFilter filter )
	{
		query.FindNearest( new Vector3( x, y, z ), Snap, filter, out var poly, out _ );
		return poly;
	}
}
