using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nav;

[InlineArray( 6 )]
internal struct Index6
{
	ushort first;
}

/// <summary>
/// Vertices are clockwise seen from above. Neis[j] belongs to the edge from vertex j to j + 1: a polygon
/// index in the same tile, Wall, or Border + side for an edge on a tile border.
/// Sides: 0 = +X, 1 = +Y, 2 = -X, 3 = -Y.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 2 )]
internal struct Poly
{
	public const ushort Wall = 0xFFFF, Border = 0xFFF0;
	public const int MaxVerts = 6, MaxPerTile = PolyRef.MaxPolys, MaxVertsPerTile = 65535;

	public Index6 Verts, Neis;
	public byte VertCount, Area;
	ushort spare;
}

/// <summary>
/// Flat depth-first tree over polygon bounds, in cells relative to the tile. A leaf covers Count polygons
/// starting at First. An inner node has First == Inner and Count nodes in its subtree, itself included.
/// </summary>
internal struct BvNode
{
	public const ushort Inner = 0xFFFF;
	public const int LeafSize = 4;

	public ushort MinX, MinY, MinZ, MaxX, MaxY, MaxZ, First, Count;
}

/// <summary>
/// A polygon edge on a tile border. Lo and Hi are where it starts and ends along the border, in cells.
/// </summary>
internal struct PortalEdge
{
	public ushort Poly;
	public byte Edge, Side;
	public ushort Lo, Hi;
}

/// <summary>
/// A connection from Edge of Poly into a polygon of a neighbour tile. TMin and TMax are the open part
/// of the edge in 255ths, Area the area of the target polygon.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 2 )]
internal struct ExtLink
{
	public uint Target;
	public ushort Poly;
	public byte Edge, Side, TMin, TMax, Area;
}

internal struct LinkAttach
{
	public ushort Poly, Link;
}

/// <summary>
/// One built tile. Never changes once published: a rebuild or a relink makes a new object. Polys, Verts,
/// Tree and Portals belong to the build and are shared by the objects made from it, as is Serial.
/// </summary>
internal sealed class Tile
{
	public readonly Poly[] Polys;
	public readonly Vector3[] Verts;
	public readonly BvNode[] Tree;
	public readonly PortalEdge[] Portals;
	public ExtLink[] Ext = [];
	public LinkAttach[] Attach = [];

	// Island components per area mask: one list for the build, shared by its tile objects through
	// this one-element array, and one list of connections per tile object. Both fill on first need.
	readonly TileIslands?[] islands;
	TileEdges? edges;

	public readonly int X, Y;
	public int Slot = -1;
	public uint Serial;
	public readonly Vector3 Min, Max;
	public readonly Vector3 TreeOrigin, TreeCell;
	public readonly float Area;

	public Tile( int x, int y, Poly[] polys, Vector3[] verts, BvNode[] tree, PortalEdge[] portals, in NavSettings settings )
	{
		X = x;
		Y = y;
		Polys = polys;
		Verts = verts;
		Tree = tree;
		Portals = portals;
		islands = new TileIslands?[1];

		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );
		double area = 0;
		foreach ( ref readonly var poly in polys.AsSpan() )
		{
			var first = verts[poly.Verts[0]];
			for ( int j = 0; j < poly.VertCount; j++ )
			{
				var v = verts[poly.Verts[j]];
				min = Vector3.Min( min, v );
				max = Vector3.Max( max, v );
				if ( j >= 2 ) area += MathF.Abs( Geo.TriArea2D( first, verts[poly.Verts[j - 1]], v ) ) * 0.5f;
			}
		}
		Min = min;
		Max = max;
		Area = (float)area;
		TreeFrame( x, y, min.Z, max.Z, settings, out TreeOrigin, out TreeCell );
	}

	public Tile( Tile other )
	{
		Polys = other.Polys;
		Verts = other.Verts;
		Tree = other.Tree;
		Portals = other.Portals;
		Ext = other.Ext;
		Attach = other.Attach;
		X = other.X;
		Y = other.Y;
		Slot = other.Slot;
		islands = other.islands;
		Serial = other.Serial;
		Min = other.Min;
		Max = other.Max;
		TreeOrigin = other.TreeOrigin;
		TreeCell = other.TreeCell;
		Area = other.Area;
	}

	// The vertical quantum widens when a tile is taller than 65535 cell heights.
	public static void TreeFrame( int x, int y, float minZ, float maxZ, in NavSettings settings, out Vector3 origin, out Vector3 cell )
	{
		float tile = settings.TileSize * settings.CellSize;
		origin = new Vector3( x * tile, y * tile, minZ );
		cell = new Vector3( settings.CellSize, settings.CellSize, MathF.Max( settings.CellHeight, (maxZ - minZ) / 65535f ) );
	}

	public TileIslands Islands( ulong forbidden )
	{
		for ( var found = Volatile.Read( ref islands[0] ); found != null; found = found.Next )
		{
			if ( found.Forbidden == forbidden ) return found;
		}
		// Taken only on the miss path, never by a reader that finds the value
		lock ( islands )
		{
			for ( var found = islands[0]; found != null; found = found.Next )
			{
				if ( found.Forbidden == forbidden ) return found;
			}
			var built = new TileIslands( this, forbidden ) { Next = islands[0] };
			Volatile.Write( ref islands[0], built );
			return built;
		}
	}

	public TileEdges Edges( MeshState s, ulong forbidden )
	{
		for ( var found = Volatile.Read( ref edges ); found != null; found = found.Next )
		{
			if ( found.Forbidden == forbidden ) return found;
		}
		// Taken only on the miss path, never by a reader that finds the value
		lock ( this )
		{
			for ( var found = edges; found != null; found = found.Next )
			{
				if ( found.Forbidden == forbidden ) return found;
			}
			var built = new TileEdges( this, s, forbidden ) { Next = edges };
			Volatile.Write( ref edges, built );
			return built;
		}
	}

	// What the island caches hold, or would hold for one mask when none has been asked for yet.
	public long IslandBytes
	{
		get
		{
			long bytes = 0;
			for ( var found = Volatile.Read( ref islands[0] ); found != null; found = found.Next ) bytes += 40 + ArrayBytes( Polys.Length, 2 );
			for ( var found = Volatile.Read( ref edges ); found != null; found = found.Next ) bytes += 32 + ArrayBytes( found.Edges.Length, 8 );
			return bytes > 0 ? bytes : 40 + ArrayBytes( Polys.Length, 2 ) + 32 + ArrayBytes( 4, 8 );
		}
	}

	const int ObjectBytes = 16 + 8 * 8 + 4 * 4 + 4 * 12 + 4 + 8 + 24 + 8;

	public long Bytes => ObjectBytes + ArrayBytes( Polys.Length, Unsafe.SizeOf<Poly>() ) + ArrayBytes( Verts.Length, 12 ) + ArrayBytes( Tree.Length, Unsafe.SizeOf<BvNode>() )
		+ ArrayBytes( Portals.Length, Unsafe.SizeOf<PortalEdge>() ) + ArrayBytes( Ext.Length, Unsafe.SizeOf<ExtLink>() ) + ArrayBytes( Attach.Length, Unsafe.SizeOf<LinkAttach>() );

	// Empty arrays are the shared instance.
	public static long ArrayBytes( int length, int elementSize ) => length == 0 ? 0 : 24 + (long)length * elementSize;
}
