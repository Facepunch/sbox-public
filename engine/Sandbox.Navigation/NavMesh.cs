using System.Numerics;

namespace Nav;

/// <summary>
/// The tiles and links of one navigation world, the volumes that mark areas on it, and the rebuild
/// cache: the eroded field of every tile a volume overlaps, kept so that a volume change rebuilds the
/// tile without its triangles. One owner thread changes it, any number of threads query it without
/// locks: every Commit swaps in a new immutable state. Everything that marks, takes, commits, adds,
/// sets or removes belongs on the owner thread.
/// Setting a link or volume id that is not in use throws; removing one does nothing.
/// </summary>
public sealed partial class NavMesh
{
	readonly NavSettings settings;
	internal readonly NavCodec Codec;
	internal readonly bool KeepFields;
	internal readonly float TileWorld;

	// Portal edges connect when they are at most this many AgentClimb apart.
	internal const float PortalClimbFactor = 1;

	// What queries and the crowd derive their tolerances from: a length that is a tenth of a unit at a
	// cell of 4, and the factor for area-like terms.
	internal float Epsilon => settings.CellSize / 40f;
	internal float AreaScale => settings.CellSize * settings.CellSize / 16f;

	// Kept up to date by generation.
	internal long CacheBytes, TrianglesUsed, TrianglesRejected;

	MeshState state;

	/// <summary>
	/// Throws when the settings are not valid.
	/// </summary>
	public NavMesh( in NavSettings settings, NavCodec codec = default, bool keepFields = true )
	{
		if ( !settings.IsValid( out var reason ) ) throw new ArgumentException( reason, nameof( settings ) );
		this.settings = settings;
		Codec = codec;
		KeepFields = keepFields;
		TileWorld = settings.TileSize * settings.CellSize;
		state = new MeshState( [], new TileIndex(), [], 0, 0 );
	}

	public NavSettings Settings => settings;

	/// <summary>
	/// Bumped by every Commit that changed the mesh.
	/// </summary>
	public uint Version => State.Version;

	internal MeshState State => Volatile.Read( ref state );

	/// <summary>
	/// Tile coordinates are unbounded and may be negative.
	/// </summary>
	public (int X, int Y) GetTileAt( Vector3 point ) => (TileOf( point.X ), TileOf( point.Y ));

	// Saturates, so a box without end reaches every tile.
	int TileOf( float world ) => (int)Math.Clamp( MathF.Floor( world / TileWorld ), int.MinValue, int.MaxValue );

	/// <summary>
	/// RuntimeBytes is what queries need, including the island data of one area mask. The triangle
	/// counters are running totals over every build since the mesh was made, rebuilds included, not the
	/// triangles of the tiles there are now. Safe on any thread.
	/// </summary>
	public (int Tiles, int Polygons, int Vertices, long RuntimeBytes, long CacheBytes, long TrianglesUsed, long TrianglesRejected) GetStats()
	{
		var s = State;
		int tiles = 0, polygons = 0, vertices = 0, slots = 0;
		long bytes = 16 + 48 + Tile.ArrayBytes( s.Pages.Length, 8 ) + s.Index.Bytes + Tile.ArrayBytes( s.Links.Length, System.Runtime.CompilerServices.Unsafe.SizeOf<LinkEntry>() );
		foreach ( var page in s.Pages )
		{
			if ( page == null ) continue;
			bytes += Tile.ArrayBytes( page.Length, 8 );
			slots += page.Length;
			foreach ( var tile in page )
			{
				if ( tile == null ) continue;
				tiles++;
				polygons += tile.Polys.Length;
				vertices += tile.Verts.Length;
				bytes += tile.Bytes + tile.IslandBytes;
			}
		}

		// The top level of the island maps: a label per tile component, about two per tile.
		int maps = 0;
		for ( var map = Volatile.Read( ref s.Islands ); map != null; map = map.Next ) maps++;
		bytes += Math.Max( maps, 1 ) * (16 + 40 + Tile.ArrayBytes( slots, 4 ) + Tile.ArrayBytes( tiles * 2, 4 ));

		return (tiles, polygons, vertices, bytes, Interlocked.Read( ref CacheBytes ), Interlocked.Read( ref TrianglesUsed ), Interlocked.Read( ref TrianglesRejected ));
	}

	/// <summary>
	/// Writes the coordinates of loaded tiles and returns how many were written.
	/// </summary>
	public int GetTiles( Span<(int X, int Y)> coordinates )
	{
		int count = 0;
		foreach ( var page in State.Pages )
		{
			if ( page == null ) continue;
			foreach ( var tile in page )
			{
				if ( tile == null ) continue;
				if ( count == coordinates.Length ) return count;
				coordinates[count++] = (tile.X, tile.Y);
			}
		}
		return count;
	}

	/// <summary>
	/// Appends the polygons of one tile, counter-clockwise seen from above: for consecutive vertices
	/// a, b, c the cross product of b - a and c - b points up.
	/// </summary>
	public void GetPolygons( int tileX, int tileY, List<Vector3> vertices, List<int> vertexCounts, List<byte> areas )
	{
		var tile = State.Find( tileX, tileY );
		if ( tile == null ) return;
		foreach ( ref readonly var poly in tile.Polys.AsSpan() )
		{
			for ( int j = poly.VertCount - 1; j >= 0; j-- ) vertices.Add( tile.Verts[poly.Verts[j]] );
			vertexCounts.Add( poly.VertCount );
			areas.Add( poly.Area );
		}
	}
}
