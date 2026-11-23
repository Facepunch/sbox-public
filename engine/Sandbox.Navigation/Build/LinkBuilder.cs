using System.Numerics;
using System.Runtime.InteropServices;

namespace Nav.Build;

internal enum LinkKind : byte { Drop, Jump }

/// <summary>
/// A drop or jump a build found clear. Start is an agent radius inside the edge it leaves over.
/// </summary>
internal struct FoundLink
{
	public Vector3 Start, End;
	public LinkKind Kind;

	// A jump whose way back climbs no more than MaxJumpUp. A drop is one way.
	public bool Bidirectional;

	// A drop whose landing is no more than MaxJumpUp below the edge, with the jump from the landing up to
	// the start clear too: the mesh makes that jump beside the drop.
	public bool Back;


	// The jump from the landing of a drop with Back up to its start.
	public readonly FoundLink Up => new() { Start = End, End = Start, Kind = LinkKind.Jump };
}

/// <summary>
/// A point on a polygon edge whose search for a landing left the field of its tile's build: where
/// links into other tiles start. X and Y in quarter cells from the corner of the field, Z in cell
/// heights from its floor 0, the edge's normal in 65536ths of a turn.
/// </summary>
internal struct EdgePoint
{
	public ushort X, Y, Z, Angle;

	public static EdgePoint Pack( Vector3 edge, Vector3 normal, in FieldFrame frame )
	{
		float turn = MathF.Atan2( normal.Y, normal.X ) / MathF.Tau;
		return new EdgePoint
		{
			X = (ushort)MathF.Round( (edge.X - frame.MinX) * frame.InverseCellSize * 4 ),
			Y = (ushort)MathF.Round( (edge.Y - frame.MinY) * frame.InverseCellSize * 4 ),
			Z = (ushort)MathF.Round( (edge.Z - frame.MinZ) * frame.InverseCellHeight ),
			Angle = (ushort)(int)MathF.Round( (turn < 0 ? turn + 1 : turn) * 65536 )
		};
	}

	public readonly void Unpack( in FieldFrame frame, out Vector3 edge, out Vector3 normal )
	{
		edge = new Vector3( frame.MinX + X * 0.25f * frame.CellSize, frame.MinY + Y * 0.25f * frame.CellSize, frame.MinZ + Z / frame.InverseCellHeight );
		var (sin, cos) = MathF.SinCos( Angle * (MathF.Tau / 65536) );
		normal = new Vector3( cos, sin, 0 );
	}
}

/// <summary>
/// What a build from triangles found about links. Own: the whole way lies in the tile's field. Open: the
/// edge points whose search left it. With an apron, Seam are the links from those that land beyond the
/// field, and Incoming those from the edge points of the tiles around that land in this tile, each with
/// the tile its edge point is in: a start lies an agent radius behind its edge point, which may be
/// across a border.
/// </summary>
internal sealed class LinkSet
{
	public FoundLink[] Own = [], Seam = [], Incoming = [];
	public (int X, int Y)[] IncomingFrom = [];
	public EdgePoint[] Open = [];
	public float MinZ;
}

/// <summary>
/// The solid spans of one tile build as the mesh holds them until the tiles around are built: the
/// first span of each row, per column the spans of its row up to and with its own, min | max &lt;&lt; 16
/// per span.
/// </summary>
internal sealed class HeldField
{
	public FieldFrame Frame;
	public int Size;
	public int[] Rows = [];
	public ushort[] Ends = [];
	public uint[] Spans = [];

	// Owner thread: the jobs in flight that read it, and whether its arrays may serve another build
	// once it is let go. Not after a job that read it was abandoned and may still be running.
	public int Readers;
	public bool Reusable = true;

	public long Bytes => 96 + 3 * 24 + Ends.Length * 2L + Rows.Length * 4L + Spans.Length * 4L;

	// Into the arrays of one the mesh let go, when it hands one over.
	public static HeldField From( SolidField solid, in FieldFrame frame, HeldField? spare )
	{
		int size = solid.Width, capacity = solid.Capacity;
		var held = spare ?? new HeldField();
		held.Frame = frame;
		held.Size = size;
		if ( held.Ends.Length != size * size ) held.Ends = new ushort[size * size];
		if ( held.Rows.Length != size ) held.Rows = new int[size];
		if ( held.Spans.Length < solid.SpanCount ) held.Spans = new uint[spare == null ? solid.SpanCount : solid.SpanCount + solid.SpanCount / 4];
		var spans = held.Spans.AsSpan( 0, solid.SpanCount );
		int write = 0;
		for ( int y = 0; y < size; y++ )
		{
			int row = write;
			held.Rows[y] = row;
			var counts = solid.Counts.AsSpan( y * size, size );
			var ends = held.Ends.AsSpan( y * size, size );
			var source = solid.Spans.AsSpan( y * size * capacity, size * capacity );
			for ( int x = 0; x < counts.Length; x++ )
			{
				for ( int i = x * capacity, end = i + counts[x]; i < end; i++ ) spans[write++] = (uint)source[i];
				ends[x] = (ushort)(write - row);
			}
			// A row of more spans than an end can count: nothing is known of this field.
			if ( write - row > ushort.MaxValue ) held.Size = 0;
		}
		return held;
	}

	// No solid in the column under the point between low and high. Outside the field nothing is known.
	public bool Free( Vector3 point, float low, float high )
	{
		int x = (int)MathF.Floor( (point.X - Frame.MinX) * Frame.InverseCellSize ), y = (int)MathF.Floor( (point.Y - Frame.MinY) * Frame.InverseCellSize );
		if ( (uint)x >= (uint)Size || (uint)y >= (uint)Size ) return false;
		int column = y * Size + x;
		float cellHeight = 1 / Frame.InverseCellHeight;
		for ( int i = Rows[y] + (x > 0 ? Ends[column - 1] : 0), end = Rows[y] + Ends[column]; i < end; i++ )
		{
			uint span = Spans[i];
			if ( Frame.MinZ + (span & 0xFFFF) * cellHeight < high && Frame.MinZ + (span >> 16) * cellHeight > low ) return false;
		}
		return true;
	}
}

/// <summary>
/// One tile's part of a job that judges links across tile borders: the edge points to search the mesh
/// from, and the held spans of the tile and the eight around it, row by row, to check the ways by.
/// Found comes back.
/// </summary>
internal sealed class SeamTask
{
	public int X, Y;
	public float MinZ;
	public EdgePoint[] Open = [];
	public int[] Points = [];
	public HeldField?[] Held = new HeldField?[9];
	public FoundLink[] Found = [];
}

/// <summary>
/// Links from the fields of a tile build. The solid spans say where an agent's body fits, the open
/// spans where it can stand. A way is clear when every column under it, over the agent's width less a
/// cell on each side, has no solid between the agent's climb and its height above the way. A drop goes
/// out level and then straight down; a jump follows NavLinkSettings.JumpHeight. Links are found as if
/// there were no volumes, on ground that erosion left walkable.
/// </summary>
internal sealed class LinkBuilder
{
	readonly List<FoundLink> own = new(), seam = new(), incoming = new(), probes = new();
	readonly List<EdgePoint> open = new();
	readonly List<(int X, int Y)> incomingFrom = new();
	(int X, int Y) aroundTile;
	readonly Dictionary<(int, int), int> thinHeads = new();
	int[] thinNext = [];

	SolidField? solid, apron;
	CompactField? field;
	SeamTask? around;
	FieldFrame frame, apronFrame;
	NavLinkSettings search;
	float cellSize, cellHeight, radius, climb, height, tileWorld, reach, dropReach;
	int radiusCells, heightCells, apronCells;
	bool wide, fromAround;
	Vector2 tileMin;

	/// <summary>
	/// The links of a tile just built from triangles, from the fields the builder still holds. With an
	/// apron the triangles are rasterised once more, as solid spans only, into a ring around the field as
	/// wide as a link is long, when an edge point of this tile or of one around it needs to see that far.
	/// </summary>
	public LinkSet Run( Tile tile, NavBuilder builder, in FieldFrame frame, in BuildConfig config, in NavSettings settings, in NavLinkSettings links, TileJob job, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices )
	{
		own.Clear();
		seam.Clear();
		incoming.Clear();
		incomingFrom.Clear();
		open.Clear();
		solid = builder.Solid;
		field = builder.Field;
		apron = null;
		around = null;
		wide = fromAround = false;
		this.frame = frame;
		Set( settings, links );
		tileMin = new Vector2( tile.X, tile.Y ) * tileWorld;

		float spacing = links.Spacing;
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		for ( int p = 0; p < tile.Polys.Length; p++ )
		{
			ref readonly var poly = ref tile.Polys[p];
			int n = poly.VertCount;
			for ( int j = 0; j < n; j++ ) v[j] = tile.Verts[poly.Verts[j]];
			for ( int edge = 0; edge < n; edge++ )
			{
				ushort nei = poly.Neis[edge];
				if ( nei < Poly.Border ) continue;

				Vector3 a = v[edge], b = v[edge + 1 < n ? edge + 1 : 0];
				float dx = b.X - a.X, dy = b.Y - a.Y, length = MathF.Sqrt( dx * dx + dy * dy );
				if ( length < 2 * radius || length <= 0 ) continue;

				// Polygons are clockwise seen from above: the outside is to the left of an edge.
				var outward = new Vector3( -dy / length, dx / length, 0 );
				int samples = Math.Max( 1, (int)(length / spacing) );
				for ( int i = 0; i < samples; i++ )
				{
					var point = Vector3.Lerp( a, b, (i + 0.5f) / samples );

					// Ground at this height right outside the edge is no ledge: across a tile border a seam,
					// where only the two cells next to it are eroded as the neighbour tile will have them, and
					// elsewhere ground of a region, not a patch too small to have become mesh.
					bool seam = nei != Poly.Wall;
					if ( Ground( point + outward * (seam ? 1.5f * cellSize : radius), point.Z - climb, point.Z + climb, seam, out _ ) ) continue;
					if ( !Walled( point, outward ) ) Probe( point, outward );
				}
			}
		}

		if ( job.Apron && (open.Count > 0 || job.Around.Length > 0) )
		{
			apronCells = BuildConfig.ApronCells( settings, links );
			builder.RasterizeApron( vertices, indices, job.MinZ, job.MaxZ, config, tile.X, tile.Y, frame.MinZ - (MathF.Ceiling( links.MaxDrop / cellHeight ) + 8) * cellHeight, apronCells );
			apron = builder.Apron;
			apronFrame = builder.ApronFrame;

			// Again from the edge points that got no further, now over the solid spans beyond the field.
			wide = true;
			foreach ( ref readonly var point in CollectionsMarshal.AsSpan( open ) )
			{
				point.Unpack( frame, out var edge, out var normal );
				Probe( edge, normal );
			}

			// And from the edge points of the tiles around whose way out comes over this tile.
			fromAround = true;
			foreach ( var (points, origin, x, y) in job.Around )
			{
				aroundTile = (x, y);
				foreach ( ref readonly var point in points.AsSpan() )
				{
					point.Unpack( origin, out var edge, out var normal );
					if ( Toward( edge, normal, reach + 2 * radius, radius, tileMin, tileWorld ) ) Probe( edge, normal );
				}
			}
		}

		// Not thinned here: of two links that are the same to thinning, the mesh may refuse one.
		return new LinkSet { Own = Array( own ), Seam = Array( seam ), Incoming = Array( incoming ), IncomingFrom = incomingFrom.Count > 0 ? incomingFrom.ToArray() : [], Open = open.Count > 0 ? open.ToArray() : [], MinZ = frame.MinZ };
	}

	static FoundLink[] Array( List<FoundLink> list ) => list.Count > 0 ? list.ToArray() : [];

	/// <summary>
	/// Whether the straight way from an edge point, this long and this much to either side, comes over a tile.
	/// </summary>
	public static bool Toward( Vector3 edge, Vector3 outward, float length, float margin, Vector2 tileMin, float tileWorld )
	{
		Vector2 from = new Vector2( edge.X, edge.Y ) - tileMin, to = from + new Vector2( outward.X, outward.Y ) * length;
		float enter = 0, leave = 1;
		for ( int axis = 0; axis < 2; axis++ )
		{
			float a = axis == 0 ? from.X : from.Y, d = (axis == 0 ? to.X : to.Y) - a;
			if ( MathF.Abs( d ) < 1e-6f )
			{
				if ( a < -margin || a > tileWorld + margin ) return false;
				continue;
			}
			float t0 = (-margin - a) / d, t1 = (tileWorld + margin - a) / d;
			enter = MathF.Max( enter, MathF.Min( t0, t1 ) );
			leave = MathF.Min( leave, MathF.Max( t0, t1 ) );
		}
		return enter <= leave;
	}

	void Set( in NavSettings settings, in NavLinkSettings links )
	{
		search = links;
		cellSize = settings.CellSize;
		cellHeight = settings.CellHeight;
		radius = settings.AgentRadius;
		climb = settings.AgentClimb;
		height = settings.AgentHeight;
		tileWorld = settings.TileSize * settings.CellSize;
		radiusCells = BuildConfig.RadiusCells( settings );
		heightCells = BuildConfig.HeightCells( settings );

		// Erosion keeps the two sides of a ledge or a wall an agent diameter apart. The outlines of both
		// may be off by the simplification error and a cell.
		dropReach = 2 * radius + (2 * settings.MaxEdgeError + 2) * cellSize;
		reach = links.Reach( settings );
	}

	/// <summary>
	/// The links from a tile's edge points into other tiles: found on the mesh, clear by the held spans of
	/// every tile they pass over. Over a tile whose spans are not held nothing is known and no link is
	/// taken. What lands in the tile itself its build has seen.
	/// </summary>
	public void Judge( SeamTask task, MeshState state, NavMesh mesh, NavQuery query, in BuildConfig config )
	{
		solid = apron = null;
		field = null;
		around = task;
		Set( mesh.Settings, mesh.LinkSettings );
		seam.Clear();
		var origin = config.Frame( task.X, task.Y ) with { MinZ = task.MinZ };
		foreach ( int point in task.Points )
		{
			task.Open[point].Unpack( origin, out var edge, out var normal );
			probes.Clear();
			query.Probe( state, PolyRef.NoId, edge, normal, search, probes, false );
			foreach ( var link in probes )
			{
				if ( mesh.GetTileAt( link.End ) == (task.X, task.Y) || !Clear( link.Start, link.End, link.Kind ) ) continue;
				seam.Add( link with { Back = link.Back && Clear( link.End, link.Start, LinkKind.Jump ) } );
			}
		}
		task.Found = Array( seam );
	}

	// Marches outwards from a point on a boundary edge. The first ground it comes over, when that is right
	// below the edge, is a drop and from then on the ground. A jump is the first ground after a stretch of
	// nothing, or the first that stands higher than the ground by more than the climb.
	void Probe( Vector3 edge, Vector3 outward )
	{
		float step = MathF.Max( cellSize, radius * 0.5f );
		var start = edge - outward * radius;
		if ( Ground( start, edge.Z - climb, edge.Z + climb, false, out float startZ ) ) start.Z = startZ;
		else start = edge;

		float ground = float.NaN;
		for ( float d = 2 * radius; d <= reach; d += step )
		{
			var end = edge + outward * d;
			bool near = Lands( end );
			if ( !near && !(wide && Known( end )) )
			{
				if ( !wide ) open.Add( EdgePoint.Pack( edge, outward, frame ) );
				return;
			}
			if ( !Ground( end, edge.Z - search.MaxDrop, edge.Z + search.MaxJumpUp, false, out end.Z ) ) continue;

			var kind = LinkKind.Jump;
			if ( float.IsNaN( ground ) && d <= dropReach && MathF.Abs( end.Z - edge.Z ) > climb )
			{
				// The foot of a low ledge: the drop from its top says that it can be climbed.
				if ( end.Z > edge.Z ) return;
				kind = LinkKind.Drop;
			}
			else if ( end.Z <= ground + climb || d > search.MaxJumpDistance )
			{
				ground = end.Z;
				continue;
			}
			else
			{
				// A jump lands an agent radius inside the far edge, as it starts.
				var inside = end + outward * radius;
				if ( (wide || Lands( inside )) && Ground( inside, end.Z - climb, end.Z + climb, false, out inside.Z ) ) end = inside;
			}

			// The second time round only what the first could not see is new. From another tile's edge
			// point, only what lands in this tile.
			bool mine = fromAround ? end.X >= tileMin.X && end.X < tileMin.X + tileWorld && end.Y >= tileMin.Y && end.Y < tileMin.Y + tileWorld : !(wide && near);
			if ( mine && Clear( start, end, kind ) )
			{
				bool low = edge.Z - end.Z <= search.MaxJumpUp;
				if ( fromAround ) incomingFrom.Add( aroundTile );
				(fromAround ? incoming : wide ? seam : own).Add( new FoundLink
				{
					Start = start,
					End = end,
					Kind = kind,
					Bidirectional = kind == LinkKind.Jump && low,
					Back = kind == LinkKind.Drop && low && Clear( end, start, LinkKind.Jump )
				} );
			}
			if ( kind == LinkKind.Jump ) return;
			ground = end.Z;
		}
	}

	// A wall right outside an edge: going out along the way the floor goes on, then comes a column that
	// leaves an agent no room from the floor up to the top of the highest jump, nearer than any landing.
	// No link gets past it, so nothing is searched for there.
	bool Walled( Vector3 edge, Vector3 way )
	{
		// The highest the feet get on NavLinkSettings.JumpHeight: from a climb above the edge to MaxJumpUp above it.
		float rise = MathF.Max( search.MaxJumpUp - climb, 0 ), hop = MathF.Max( climb, search.MaxJumpUp );
		float level = edge.Z, top = edge.Z + climb + (rise + 2 * hop) * (rise + 2 * hop) / (rise + 4 * hop);
		for ( float d = cellSize; d < 2 * radius; d += cellSize )
		{
			if ( !Column( edge + way * d, out int column ) ) return false;
			var s = solid!;
			int first = column * s.Capacity, end = first + s.Counts[column];
			if ( first == end || frame.MinZ + SolidField.Min( s.Spans[first] ) * cellHeight - height >= level - climb ) return false;

			// The lowest span with room above it for feet between the floor and the top: the floor, or no wall.
			int i = first;
			for ( ; i < end; i++ )
			{
				float max = frame.MinZ + SolidField.Max( s.Spans[i] ) * cellHeight;
				float low = MathF.Max( max, level ) - climb, high = i + 1 < end ? frame.MinZ + SolidField.Min( s.Spans[i + 1] ) * cellHeight - height : top;
				if ( low > high ) continue;
				if ( MathF.Abs( max - level ) > climb ) return false;
				level = max;
				break;
			}
			if ( i == end ) return true;
		}
		return false;
	}

	// Whether a point is over the part of the field where the open spans are those the neighbour tile
	// will have: not within the agent radius and a cell of the field's edge.
	bool Lands( Vector3 point )
	{
		int size = field!.Width;
		int x = (int)MathF.Floor( (point.X - frame.MinX) * frame.InverseCellSize ), y = (int)MathF.Floor( (point.Y - frame.MinY) * frame.InverseCellSize );
		return x > radiusCells && y > radiusCells && x < size - radiusCells - 1 && y < size - radiusCells - 1;
	}

	bool Column( Vector3 point, out int column )
	{
		int w = solid!.Width;
		int x = (int)MathF.Floor( (point.X - frame.MinX) * frame.InverseCellSize ), y = (int)MathF.Floor( (point.Y - frame.MinY) * frame.InverseCellSize );
		column = x + y * w;
		return (uint)x < (uint)w && (uint)y < (uint)solid.Height;
	}

	// The solid spans under a point: those of the field, or of the apron around it.
	bool Spans( Vector3 point, out SolidField spans, out int column, out float minZ )
	{
		spans = solid!;
		minZ = frame.MinZ;
		if ( Column( point, out column ) ) return true;
		if ( apron == null ) return false;

		int size = apron.Width;
		int x = (int)MathF.Floor( (point.X - apronFrame.MinX) * frame.InverseCellSize ), y = (int)MathF.Floor( (point.Y - apronFrame.MinY) * frame.InverseCellSize );
		if ( (uint)x >= (uint)size || (uint)y >= (uint)size ) return false;
		spans = apron;
		minZ = apronFrame.MinZ;
		column = x + y * size;
		return true;
	}

	bool Known( Vector3 point ) => Spans( point, out _, out _, out _ );

	// The highest ground under the point with its floor between low and high. On the eroded part of the
	// field that is an open span erosion left walkable: any such when all counts, else one that is in a
	// region. Further out, the second time round, it comes from the solid spans.
	bool Ground( Vector3 point, float low, float high, bool all, out float z )
	{
		z = point.Z;
		if ( wide && !Lands( point ) ) return SolidGround( point, low, high, out z );
		if ( !Column( point, out int column ) ) return false;
		bool found = false;
		var f = field!;
		for ( int i = f.Index[column], end = f.Index[column + 1]; i < end; i++ )
		{
			if ( f.Con[i] >> 24 == 0 || (!all && f.Region[i] == 0) ) continue;
			float floor = frame.MinZ + f.Floor[i] * cellHeight;
			if ( floor < low || floor > high ) continue;
			z = floor;
			found = true;
		}
		return found;
	}

	// The top of a solid span the triangles made ground, between low and high, with the agent's height
	// free above it.
	bool Stands( Vector3 point, float low, float high, out float z )
	{
		z = point.Z;
		if ( !Spans( point, out var s, out int column, out float minZ ) ) return false;
		bool found = false;
		for ( int i = column * s.Capacity, end = i + s.Counts[column]; i < end; i++ )
		{
			ulong span = s.Spans[i];
			if ( SolidField.Area( span ) == 0 ) continue;
			float top = minZ + SolidField.Max( span ) * cellHeight;
			if ( top < low || top > high || (i + 1 < end && SolidField.Min( s.Spans[i + 1] ) - SolidField.Max( span ) < heightCells) ) continue;
			z = top;
			found = true;
		}
		return found;
	}

	// Ground by the solid spans that has ground all around it within the agent's radius, as erosion asks.
	// Columns nothing is known of count as ground.
	bool SolidGround( Vector3 point, float low, float high, out float z )
	{
		if ( !Stands( point, low, high, out z ) ) return false;
		for ( int y = -radiusCells; y <= radiusCells; y++ )
		{
			for ( int x = -radiusCells; x <= radiusCells; x++ )
			{
				if ( x * x + y * y > radiusCells * radiusCells || (x == 0 && y == 0) ) continue;
				var near = point + new Vector3( x, y, 0 ) * cellSize;
				if ( Known( near ) && !Stands( near, z - 2 * climb, z + 2 * climb, out _ ) ) return false;
			}
		}
		return true;
	}

	// No solid in the column under the point between low and high. Where nothing is known, not free.
	bool Free( Vector3 point, float low, float high )
	{
		if ( around != null )
		{
			int dx = (int)MathF.Floor( point.X / tileWorld ) - around.X + 1, dy = (int)MathF.Floor( point.Y / tileWorld ) - around.Y + 1;
			return (uint)dx < 3 && (uint)dy < 3 && around.Held[dy * 3 + dx] is { } held && held.Free( point, low, high );
		}
		if ( !Spans( point, out var s, out int column, out float minZ ) ) return false;
		for ( int i = column * s.Capacity, end = i + s.Counts[column]; i < end; i++ )
		{
			if ( minZ + SolidField.Min( s.Spans[i] ) * cellHeight < high && minZ + SolidField.Max( s.Spans[i] ) * cellHeight > low ) return false;
		}
		return true;
	}

	// Every column a cell apart along the way, over the agent's width less a cell on each side. Lane by
	// lane from the middle: a way that is not clear mostly ends at a wall across it, a few steps out.
	bool Clear( Vector3 start, Vector3 end, LinkKind kind )
	{
		float dx = end.X - start.X, dy = end.Y - start.Y, flat = MathF.Sqrt( dx * dx + dy * dy );
		if ( flat <= 0 ) return false;
		var side = new Vector3( -dy / flat, dx / flat, 0 ) * cellSize;
		int steps = (int)MathF.Ceiling( flat / cellSize ), lanes = Math.Max( radiusCells - 1, 0 );
		Span<float> feet = stackalloc float[steps + 1];
		for ( int k = 0; k <= 2 * lanes; k++ )
		{
			var offset = side * ((k & 1) == 0 ? k / 2 : -(k + 1) / 2);
			for ( int i = 0; i <= steps; i++ )
			{
				// The middle lane works out the heights as it goes: most ways end on it.
				float t = i / (float)steps;
				if ( k == 0 ) feet[i] = kind == LinkKind.Drop ? start.Z : search.JumpHeight( start, end, t, climb );
				float z = feet[i];
				var point = new Vector3( start.X + dx * t, start.Y + dy * t, z ) + offset;
				if ( !Free( point, (kind == LinkKind.Drop && i == steps ? end.Z : z) + climb, z + height ) ) return false;
			}
		}
		return true;
	}

	/// <summary>
	/// Removes every link that one before it stands for by Same. Those that stay are sorted: the ones that
	/// can be taken both ways first, then by length.
	/// </summary>
	public void Thin( List<FoundLink> links, float spacing )
	{
		if ( links.Count < 2 ) return;
		var span = CollectionsMarshal.AsSpan( links );
		span.Sort( static ( a, b ) => a.Bidirectional != b.Bidirectional ? (a.Bidirectional ? -1 : 1) : Vector3.DistanceSquared( a.Start, a.End ).CompareTo( Vector3.DistanceSquared( b.Start, b.End ) ) );

		// Those that stay, chained by the cell of spacing their start is in.
		float inverse = 1 / spacing;
		thinHeads.Clear();
		if ( thinNext.Length < span.Length ) thinNext = new int[span.Length];
		int kept = 0;
		foreach ( ref readonly var link in span )
		{
			bool crowded = false;
			for ( int pass = 0; pass < 2 && !crowded; pass++ )
			{
				// Starts around the start, then around the end: one that can be taken both ways, which comes
				// after none that cannot, stands for a jump the other way round too.
				if ( pass == 1 && link.Kind == LinkKind.Drop ) break;
				var at = pass == 0 ? link.Start : link.End;
				int cx = (int)MathF.Floor( at.X * inverse ), cy = (int)MathF.Floor( at.Y * inverse );
				for ( int y = cy - 1; y <= cy + 1 && !crowded; y++ )
				{
					for ( int x = cx - 1; x <= cx + 1 && !crowded; x++ )
					{
						if ( !thinHeads.TryGetValue( (x, y), out int k ) ) continue;
						for ( ; k >= 0 && !crowded; k = thinNext[k] ) crowded = Same( span[k], link, spacing );
					}
				}
			}
			if ( crowded ) continue;

			var cell = ((int)MathF.Floor( link.Start.X * inverse ), (int)MathF.Floor( link.Start.Y * inverse ));
			thinNext[kept] = thinHeads.TryGetValue( cell, out int head ) ? head : -1;
			thinHeads[cell] = kept;
			span[kept++] = link;
		}
		links.RemoveRange( kept, links.Count - kept );
	}

	/// <summary>
	/// Whether a stands for b: of one kind, a going every way b goes, with both ends less than spacing
	/// apart, the same way round or, when a can be taken both ways, the other. A drop and the jump back up
	/// it are of two kinds and never stand for each other.
	/// </summary>
	public static bool Same( in FoundLink a, in FoundLink b, float spacing )
	{
		if ( a.Kind != b.Kind || (b.Bidirectional && !a.Bidirectional) ) return false;
		float near = spacing * spacing;
		if ( Vector3.DistanceSquared( a.Start, b.Start ) < near && Vector3.DistanceSquared( a.End, b.End ) < near ) return true;
		return a.Bidirectional && Vector3.DistanceSquared( a.Start, b.End ) < near && Vector3.DistanceSquared( a.End, b.Start ) < near;
	}
}
