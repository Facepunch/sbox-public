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
	internal readonly int MaxTiles;
	readonly int polyBits;
	readonly uint polyMask;

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
	/// Throws when the settings or the link settings of the options are not valid. With links enabled,
	/// tile builds from triangles also find drops and jumps.
	/// </summary>
	public NavMesh( in NavSettings settings, in NavMeshOptions options )
	{
		if ( !settings.IsValid( out var reason ) ) throw new ArgumentException( reason, nameof( settings ) );
		if ( !options.Links.IsValid( settings, out reason ) ) throw new ArgumentException( reason, nameof( options ) );
		this.settings = settings;
		links = options.Links;
		Codec = options.Codec;
		KeepFields = options.KeepFields;
		TileWorld = settings.TileSize * settings.CellSize;
		polyBits = settings.PolyBits;
		polyMask = (1u << polyBits) - 1;
		MaxTiles = PolyRef.MaxTiles( polyBits );
		state = new MeshState( [], new TileIndex(), [], 0, 0, polyBits, [] );
	}

	public NavSettings Settings => settings;
	public NavLinkSettings LinkSettings => links;

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
		int tiles = 0, polygons = 0, vertices = 0;
		long bytes = 16 + 48 + Tile.ArrayBytes( s.Pages.Length, 8 ) + s.Index.Bytes + Tile.ArrayBytes( s.LinkPages.Length, 8 );
		foreach ( var page in s.LinkPages )
		{
			if ( page != null ) bytes += Tile.ArrayBytes( page.Length, System.Runtime.CompilerServices.Unsafe.SizeOf<LinkEntry>() );
		}
		foreach ( var page in s.Pages )
		{
			if ( page == null ) continue;
			bytes += Tile.ArrayBytes( page.Length, 8 );
			foreach ( var tile in page )
			{
				if ( tile == null ) continue;
				tiles++;
				polygons += tile.Polys.Length;
				vertices += tile.Verts.Length;
				bytes += tile.Bytes + tile.IslandBytes;
			}
		}

		// The island maps, their blocks once per area mask. Before any is built, what one would hold.
		long islands = 0;
		var maps = Volatile.Read( ref s.Islands );
		for ( var map = maps; map != null; map = map.Next )
		{
			var earlier = maps;
			while ( earlier != map && earlier!.Forbidden != map.Forbidden ) earlier = earlier.Next;
			islands += map.Bytes( earlier == map );
		}
		bytes += islands > 0 ? islands : tiles * 16L;

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
