using System.Numerics;

namespace Nav.Lib.Tests;

/// <summary>
/// Hand-made meshes for crowd tests: one tile of 64 x 64 cells of 10 units, floor surface at z = 1,
/// optional upper floor at z = 201. Polygons are the rectangles between the cut lines the layout needs,
/// so a room is a handful of large polygons as in a built mesh.
/// </summary>
static class CrowdMesh
{
	public const int Cells = 64;
	public const float Cell = 10, Size = Cells * Cell;

	public readonly record struct Options
	{
		/// <summary>A wall across the middle (x 300 to 340) with a door from y 280 to 360.</summary>
		public bool Doorway { get; init; }

		/// <summary>Nothing walkable where x is at least 280 and y below 360.</summary>
		public bool Corner { get; init; }

		/// <summary>Pillars 10 wide and 470 long at x 60, 180, 300, 420 and 540, y from 90 to 560.</summary>
		public bool Obstacles { get; init; }

		/// <summary>The strip x 300 to 340 has area 2.</summary>
		public bool AreaStrip { get; init; }

		/// <summary>
		/// A block in the middle (x 200 to 440, y 280 to 360) with a way round on either side: area 2
		/// where y is at least 360, area 3 where y is below 280, area 1 between.
		/// </summary>
		public bool Ring { get; init; }

		/// <summary>A second floor at z 201 with the same outline.</summary>
		public bool UpperFloor { get; init; }

		/// <summary>A two-way link from (320, 320, 1) straight up to the upper floor.</summary>
		public bool Link { get; init; }

		/// <summary>Edges on the tile border are portals instead of walls.</summary>
		public bool TileBorders { get; init; }

		/// <summary>Every length times this, to build the same mesh in another unit.</summary>
		public float Scale { get; init; }
	}

	public static NavSettings Settings( float scale = 1 ) => NavSettings.Create( 32 * scale, 8 * scale, 8 * scale, 40, Cell * scale, 1 * scale, Cells );

	public static NavMesh Create( Options options = default )
	{
		float scale = options.Scale > 0 ? options.Scale : 1;
		var mesh = new NavMesh( Settings( scale ) );
		mesh.CommitTiles( [Tile( options )] );
		if ( options.Link ) AddLink( mesh, scale );
		return mesh;
	}

	public static int AddLink( NavMesh mesh, float scale = 1, float x = 320, float y = 320 )
	{
		int id = mesh.AddLink( new NavLink { Start = new Vector3( x, y, 1 ) * scale, End = new Vector3( x, y, 201 ) * scale, Radius = 24 * scale, Bidirectional = true, Area = 1 } );
		mesh.Commit();
		return id;
	}

	/// <summary>
	/// An L-shaped floor round a wall corner at (280, 360): everything left of x 280 and everything
	/// above y 360. The corner is cut off from (280, 348) to (292, 360), the way erosion rounds the
	/// corner of a building, so a route round it follows 17 units of wall.
	/// </summary>
	public static NavMesh ChamferedCorner()
	{
		Vector3[] verts =
		[
			new( 0, 0, 1 ), new( 280, 0, 1 ), new( 0, 348, 1 ), new( 280, 348, 1 ), new( 0, 360, 1 ), new( 280, 360, 1 ),
			new( 292, 360, 1 ), new( 640, 360, 1 ), new( 0, 640, 1 ), new( 280, 640, 1 ), new( 292, 640, 1 ), new( 640, 640, 1 )
		];
		const ushort wall = Poly.Wall;
		// Clockwise seen from above. Per polygon: vertices, then what lies across each edge.
		(int[] Verts, ushort[] Neis)[] shapes =
		[
			([0, 2, 3, 1], [wall, 1, wall, wall]),
			([2, 4, 5, 3], [wall, 2, 5, 0]),
			([4, 8, 9, 5], [wall, wall, 3, 1]),
			([5, 9, 10, 6], [2, wall, 4, 5]),
			([6, 10, 11, 7], [3, wall, wall, wall]),
			([3, 5, 6], [1, 3, wall])
		];
		var polys = new Poly[shapes.Length];
		for ( int i = 0; i < shapes.Length; i++ )
		{
			polys[i].VertCount = (byte)shapes[i].Verts.Length;
			polys[i].Area = 1;
			for ( int j = 0; j < shapes[i].Verts.Length; j++ )
			{
				polys[i].Verts[j] = (ushort)shapes[i].Verts[j];
				polys[i].Neis[j] = shapes[i].Neis[j];
			}
		}
		var tile = new TileAssembler().Create( 0, 0, verts, polys, Settings(), out var error ) ?? throw new InvalidOperationException( error );
		var mesh = new NavMesh( Settings() );
		mesh.CommitTiles( [tile] );
		return mesh;
	}

	public static Tile Tile( Options options, int tileX = 0, int tileY = 0 )
	{
		float scale = options.Scale > 0 ? options.Scale : 1;
		var xs = new SortedSet<int> { 0, Cells };
		var ys = new SortedSet<int> { 0, Cells };
		if ( options.Doorway )
		{
			xs.UnionWith( [30, 34] );
			ys.UnionWith( [28, 36] );
		}
		if ( options.Corner )
		{
			xs.Add( 28 );
			ys.Add( 36 );
		}
		if ( options.Obstacles )
		{
			for ( int x = 6; x < Cells; x += 12 ) xs.UnionWith( [x, x + 1] );
			ys.UnionWith( [9, 56] );
		}
		if ( options.AreaStrip ) xs.UnionWith( [30, 34] );
		if ( options.Ring )
		{
			xs.UnionWith( [20, 44] );
			ys.UnionWith( [28, 36] );
		}

		bool Walkable( int x, int y )
		{
			if ( options.Obstacles && x % 12 == 6 && y > 8 && y < 56 ) return false;
			if ( options.Corner && x >= 28 && y < 36 ) return false;
			if ( options.Doorway && x >= 30 && x < 34 && (y < 28 || y >= 36) ) return false;
			if ( options.Ring && x >= 20 && x < 44 && y >= 28 && y < 36 ) return false;
			return true;
		}

		int[] cutX = [.. xs], cutY = [.. ys];
		int columns = cutX.Length - 1, rows = cutY.Length - 1;
		var verts = new List<Vector3>();
		var polys = new List<Poly>();
		foreach ( float z in options.UpperFloor ? new[] { 1f, 201f } : [1f] )
		{
			int firstPoly = polys.Count, firstVert = verts.Count;
			var index = new int[columns, rows];
			int count = 0;
			for ( int j = 0; j < rows; j++ )
				for ( int i = 0; i < columns; i++ )
					index[i, j] = Walkable( cutX[i], cutY[j] ) ? firstPoly + count++ : -1;

			for ( int j = 0; j <= rows; j++ )
				for ( int i = 0; i <= columns; i++ )
					verts.Add( new Vector3( (tileX * Cells + cutX[i]) * Cell, (tileY * Cells + cutY[j]) * Cell, z ) * scale );

			ushort Neighbour( int i, int j, int side )
			{
				if ( i < 0 || j < 0 || i >= columns || j >= rows ) return options.TileBorders ? (ushort)(Poly.Border + side) : Poly.Wall;
				return index[i, j] < 0 ? Poly.Wall : (ushort)index[i, j];
			}

			for ( int j = 0; j < rows; j++ )
			{
				for ( int i = 0; i < columns; i++ )
				{
					if ( index[i, j] < 0 ) continue;
					// Clockwise seen from above: west, north, east and south edge.
					byte area = (byte)(options.AreaStrip && cutX[i] >= 30 && cutX[i] < 34 ? 2 : options.Ring && cutY[j] >= 36 ? 2 : options.Ring && cutY[j] < 28 ? 3 : 1);
					var poly = new Poly { VertCount = 4, Area = area };
					poly.Verts[0] = (ushort)(firstVert + j * (columns + 1) + i);
					poly.Verts[1] = (ushort)(firstVert + (j + 1) * (columns + 1) + i);
					poly.Verts[2] = (ushort)(firstVert + (j + 1) * (columns + 1) + i + 1);
					poly.Verts[3] = (ushort)(firstVert + j * (columns + 1) + i + 1);
					poly.Neis[0] = Neighbour( i - 1, j, 2 );
					poly.Neis[1] = Neighbour( i, j + 1, 1 );
					poly.Neis[2] = Neighbour( i + 1, j, 0 );
					poly.Neis[3] = Neighbour( i, j - 1, 3 );
					polys.Add( poly );
				}
			}
		}

		var tile = new TileAssembler().Create( tileX, tileY, verts.ToArray(), polys.ToArray(), Settings( scale ), out var error );
		return tile ?? throw new InvalidOperationException( error );
	}
}

/// <summary>
/// A crowd on a mesh with the few shorthands the tests keep repeating.
/// </summary>
sealed class Sim
{
	public readonly NavMesh Mesh;
	public readonly NavCrowd Crowd;
	public readonly NavQuery Query;

	public Sim( NavMesh mesh, int workers = 1 )
	{
		Mesh = mesh;
		Crowd = new NavCrowd( mesh, workers ) { Share = 1 };
		Query = new NavQuery( mesh );
	}

	public Sim( CrowdMesh.Options options = default ) : this( CrowdMesh.Create( options ) )
	{
	}

	/// <summary>Radius 8, height 32, speed 120, acceleration 240.</summary>
	public static AgentParams Walker( bool autoLinks = true )
	{
		var value = AgentParams.Create( 8, 32, 120, 240 );
		value.AutoLinks = autoLinks;
		return value;
	}

	public int Add( Vector3 position ) => Crowd.Add( position, Walker() );
	public int Add( Vector3 position, AgentParams value ) => Crowd.Add( position, value );

	public int Add( Vector3 position, Vector3 target, AgentParams? value = null )
	{
		int id = Crowd.Add( position, value ?? Walker() );
		Crowd.MoveTo( id, target );
		return id;
	}

	public void Step( float delta = 0.02f ) => Crowd.Step( delta );

	public void Run( int steps, float delta = 0.02f )
	{
		for ( int i = 0; i < steps; i++ ) Crowd.Step( delta );
	}

	public AgentState this[int agent] => Crowd.States[agent];
	public Vector3 Position( int agent ) => Crowd.States[agent].Position;
	public AgentStatus Status( int agent ) => Crowd.States[agent].Status;

	/// <summary>Has a target it has not reached.</summary>
	public bool HasTarget( int agent ) => Status( agent ) is AgentStatus.Pending or AgentStatus.Unreachable or AgentStatus.Moving or AgentStatus.OnLink or AgentStatus.Blocked;

	/// <summary>Has a target and a route to it.</summary>
	public bool Navigating( int agent ) => Status( agent ) is AgentStatus.Moving or AgentStatus.OnLink or AgentStatus.Blocked;

	public bool OnLink( int agent ) => Status( agent ) == AgentStatus.OnLink;

	public PolyRef PolyAt( Vector3 point )
	{
		Query.FindNearest( point, new Vector3( 8 ), default, out var poly, out _ );
		return poly;
	}

	public static Vector3 At( float x, float y, float z = 1 ) => new( x, y, z );

	public static float Flat( Vector3 a, Vector3 b ) => Vector2.Distance( new Vector2( a.X, a.Y ), new Vector2( b.X, b.Y ) );
}
