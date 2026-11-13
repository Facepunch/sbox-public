namespace Nav.Build;

/// <summary>
/// Walks the outline of every region, simplifies it and joins the holes of a region to its outline.
/// A vertex is four ints: x, y, height, flags. After Build the flags hold the region on the other side
/// of the edge that starts at the vertex, plus BorderVertex and AreaBorder.
/// </summary>
internal sealed class ContourBuilder
{
	public const int RegionMask = 0xFFFF;
	public const int BorderVertex = 0x10000;
	public const int AreaBorder = 0x20000;

	public int Count;
	public int[] Start = new int[64];
	public int[] VertCount = new int[64];
	public int[] Region = new int[64];
	public int[] Area = new int[64];
	public int[] Verts = new int[1024];
	int vertsUsed;

	byte[] flags = [];
	int[] starts = new int[512];
	int startCount;
	int[] raw = new int[1024];
	int rawCount;
	int[] simple = new int[512];
	int simpleCount;

	// Hole merging
	int[] winding = [];
	int[] outlineOf = [];
	int[] holeStart = [];
	int[] holeCount = [];
	Hole[] holes = [];
	Diagonal[] diagonals = [];

	struct Hole
	{
		public int Contour, MinX, MinY, LeftMost;
	}

	struct Diagonal
	{
		public int Vertex, Distance;
	}

	static readonly Comparison<Hole> holeOrder = static ( a, b ) => a.MinX == b.MinX ? a.MinY.CompareTo( b.MinY ) : a.MinX.CompareTo( b.MinX );
	static readonly Comparison<Diagonal> diagonalOrder = static ( a, b ) => a.Distance.CompareTo( b.Distance );

	/// <summary>
	/// Runs on the regions RegionBuilder.Build left in the field, with the same open blocks and border.
	/// Vertices come out in cells from the corner of the field without its border.
	/// </summary>
	public bool Build( CompactField field, OpenBlocks open, int border, float maxError, int maxEdgeLength, out string? error )
	{
		int w = field.Width;
		int x1 = w - border, y1 = field.Height - border;
		var index = field.Index;
		var con = field.Con;
		var region = field.Region;

		Count = 0;
		vertsUsed = 0;
		error = null;
		if ( flags.Length < field.SpanCount ) flags = new byte[field.SpanCount + field.SpanCount / 2];

		// Per span, a bit for every side that faces another region. Spans with some but not all bits set
		// are where contours start, remembered in scan order.
		startCount = 0;
		const int blockSize = CompactField.BlockSize;
		for ( int y = border; y < y1; y++ )
		{
			int row = y * w;
			int block = (y - border) * open.PerRow;
			for ( int x = border; x < x1; x += blockSize, block++ )
			{
				int end = Math.Min( x + blockSize, x1 );

				// Level open ground all of one region, with that region on every side, has no edges
				if ( end - x == blockSize && open.Open[block] )
				{
					int i = index[x + row];
					int r = region[i];
					if ( CompactField.All( region, i, r ) && region[i - 1] == r && region[i + blockSize] == r
						&& CompactField.All( region, index[x + row - w], r ) && CompactField.All( region, index[x + row + w], r ) )
					{
						Array.Clear( flags, i, blockSize );
						continue;
					}
				}

				FlagColumns( field, x + row, end + row );
			}
		}

		for ( int next = 0; next < startCount; next++ )
		{
			int i = starts[next * 2];
			int column = starts[next * 2 + 1];
			if ( flags[i] == 0 ) continue;

			rawCount = 0;
			simpleCount = 0;
			if ( !Walk( field, column % w, column / w, i ) )
			{
				error = "The connections of the field do not agree with each other";
				return false;
			}
			Simplify( maxError, maxEdgeLength );
			RemoveDegenerateSegments();
			if ( simpleCount < 3 * 4 ) continue;

			if ( Count == Start.Length )
			{
				Array.Resize( ref Start, Count * 2 );
				Array.Resize( ref VertCount, Count * 2 );
				Array.Resize( ref Region, Count * 2 );
				Array.Resize( ref Area, Count * 2 );
			}

			int start = Reserve( simpleCount );
			for ( int v = 0; v < simpleCount; v += 4 )
			{
				Verts[start + v] = simple[v] - border;
				Verts[start + v + 1] = simple[v + 1] - border;
				Verts[start + v + 2] = simple[v + 2];
				Verts[start + v + 3] = simple[v + 3];
			}

			Start[Count] = start;
			VertCount[Count] = simpleCount / 4;
			Region[Count] = region[i];
			Area[Count] = field.Area[i];
			Count++;
		}

		return Count == 0 || MergeHoles( field.RegionCount, out error );
	}

	void FlagColumns( CompactField field, int from, int to )
	{
		var index = field.Index;
		var region = field.Region;
		var con = field.Con;
		int w = field.Width;
		for ( int column = from; column < to; column++ )
		{
			for ( int i = index[column], end = index[column + 1]; i < end; i++ )
			{
				int r = region[i];
				if ( r == 0 || (r & RegionBuilder.Border) != 0 )
				{
					flags[i] = 0;
					continue;
				}

				uint c = con[i];
				int same = 0;
				int west = (int)(c & 63), north = (int)((c >> 6) & 63), east = (int)((c >> 12) & 63), south = (int)((c >> 18) & 63);
				if ( west != 63 && region[index[column - 1] + west] == r ) same |= 1;
				if ( north != 63 && region[index[column + w] + north] == r ) same |= 2;
				if ( east != 63 && region[index[column + 1] + east] == r ) same |= 4;
				if ( south != 63 && region[index[column - w] + south] == r ) same |= 8;

				// A span with no neighbour of its own region has no outline worth walking
				flags[i] = same == 0 ? (byte)0 : (byte)(same ^ 0xF);
				if ( same == 0 || same == 0xF ) continue;

				if ( startCount * 2 == starts.Length ) Array.Resize( ref starts, starts.Length * 2 );
				starts[startCount * 2] = i;
				starts[startCount * 2 + 1] = column;
				startCount++;
			}
		}
	}

	int Reserve( int ints )
	{
		if ( Verts.Length < vertsUsed + ints ) Array.Resize( ref Verts, (vertsUsed + ints) * 2 );
		int start = vertsUsed;
		vertsUsed += ints;
		return start;
	}

	void AddRaw( int x, int y, int height, int flag )
	{
		if ( rawCount + 4 > raw.Length ) Array.Resize( ref raw, raw.Length * 2 );
		raw[rawCount] = x;
		raw[rawCount + 1] = y;
		raw[rawCount + 2] = height;
		raw[rawCount + 3] = flag;
		rawCount += 4;
	}

	void AddSimple( int x, int y, int height, int source )
	{
		if ( simpleCount + 4 > simple.Length ) Array.Resize( ref simple, simple.Length * 2 );
		simple[simpleCount] = x;
		simple[simpleCount + 1] = y;
		simple[simpleCount + 2] = height;
		simple[simpleCount + 3] = source;
		simpleCount += 4;
	}

	// Follows the region's edge clockwise from span i, one raw point per cell corner. The walk passes each
	// side of each span at most once, however long the outline is. False when it is not back where it
	// started after that many steps, which only happens in a field whose connections are not mutual.
	bool Walk( CompactField field, int x, int y, int i )
	{
		int w = field.Width;
		var index = field.Index;
		var con = field.Con;

		int direction = 0;
		while ( (flags[i] & (1 << direction)) == 0 ) direction++;

		int startDirection = direction;
		int startSpan = i;
		int area = field.Area[i];

		for ( long steps = 4L * field.SpanCount; steps > 0; steps-- )
		{
			int column = x + y * w;
			int connection = CompactField.Connection( con[i], direction );
			if ( (flags[i] & (1 << direction)) != 0 )
			{
				int height = CornerHeight( field, column, i, direction, out bool borderVertex );
				int px = x, py = y;
				switch ( direction )
				{
					case 0: py++; break;
					case 1: px++; py++; break;
					case 2: px++; break;
				}

				int r = 0;
				if ( connection != 63 )
				{
					int neighbour = index[column + field.Offset( direction )] + connection;
					r = field.Region[neighbour];
					if ( area != field.Area[neighbour] ) r |= AreaBorder;
				}
				if ( borderVertex ) r |= BorderVertex;
				AddRaw( px, py, height, r );

				flags[i] = (byte)(flags[i] & ~(1 << direction));
				direction = (direction + 1) & 3;
			}
			else
			{
				if ( connection == 63 ) return false;
				i = index[column + field.Offset( direction )] + connection;
				switch ( direction )
				{
					case 0: x--; break;
					case 1: y++; break;
					case 2: x++; break;
					default: y--; break;
				}
				direction = (direction + 3) & 3;
			}

			if ( startSpan == i && startDirection == direction ) return true;
		}
		return false;
	}

	// The highest floor of the up to four spans that meet at the corner ahead and to the right of the
	// direction, and whether the corner sits on the border between two tiles and can be dropped later.
	static int CornerHeight( CompactField field, int column, int i, int direction, out bool borderVertex )
	{
		var index = field.Index;
		var con = field.Con;
		var floor = field.Floor;
		var region = field.Region;
		var area = field.Area;

		int height = floor[i];
		int next = (direction + 1) & 3;
		int r0 = region[i] | (area[i] << 16), r1 = 0, r2 = 0, r3 = 0;

		int connection = CompactField.Connection( con[i], direction );
		if ( connection != 63 )
		{
			int aColumn = column + field.Offset( direction );
			int a = index[aColumn] + connection;
			height = Math.Max( height, floor[a] );
			r1 = region[a] | (area[a] << 16);
			int second = CompactField.Connection( con[a], next );
			if ( second != 63 )
			{
				int b = index[aColumn + field.Offset( next )] + second;
				height = Math.Max( height, floor[b] );
				r2 = region[b] | (area[b] << 16);
			}
		}

		connection = CompactField.Connection( con[i], next );
		if ( connection != 63 )
		{
			int aColumn = column + field.Offset( next );
			int a = index[aColumn] + connection;
			height = Math.Max( height, floor[a] );
			r3 = region[a] | (area[a] << 16);
			int second = CompactField.Connection( con[a], direction );
			if ( second != 63 )
			{
				int b = index[aColumn + field.Offset( direction )] + second;
				height = Math.Max( height, floor[b] );
				r2 = region[b] | (area[b] << 16);
			}
		}

		borderVertex = BorderCorner( r0, r1, r2, r3 ) || BorderCorner( r1, r2, r3, r0 ) || BorderCorner( r2, r3, r0, r1 ) || BorderCorner( r3, r0, r1, r2 );
		return height;
	}

	// Two cells of the same border region followed by two cells of one area inside the tile.
	static bool BorderCorner( int a, int b, int c, int d )
	{
		bool twoSameOutside = (a & b & RegionBuilder.Border) != 0 && a == b;
		bool twoInside = ((c | d) & RegionBuilder.Border) == 0;
		bool sameArea = (c >> 16) == (d >> 16);
		bool noneEmpty = a != 0 && b != 0 && c != 0 && d != 0;
		return twoSameOutside && twoInside && sameArea && noneEmpty;
	}

	static float DistanceToSegment( int x, int y, int px, int py, int qx, int qy )
	{
		float pqx = qx - px;
		float pqy = qy - py;
		float d = pqx * pqx + pqy * pqy;
		float t = pqx * (x - px) + pqy * (y - py);
		if ( d > 0 ) t /= d;
		t = Math.Clamp( t, 0f, 1f );

		float dx = px + t * pqx - x;
		float dy = py + t * pqy - y;
		return dx * dx + dy * dy;
	}

	void InsertSimple( int at, int source )
	{
		if ( simpleCount + 4 > simple.Length ) Array.Resize( ref simple, simple.Length * 2 );
		Array.Copy( simple, at * 4, simple, at * 4 + 4, simpleCount - at * 4 );
		simple[at * 4] = raw[source * 4];
		simple[at * 4 + 1] = raw[source * 4 + 1];
		simple[at * 4 + 2] = raw[source * 4 + 2];
		simple[at * 4 + 3] = source;
		simpleCount += 4;
	}

	// Keeps the raw points where the neighbouring region changes, then adds points until every raw point
	// is within maxError of the outline, then splits wall edges longer than maxEdgeLength.
	void Simplify( float maxError, int maxEdgeLength )
	{
		int pointCount = rawCount / 4;
		bool hasConnections = false;
		for ( int i = 0; i < rawCount; i += 4 )
		{
			if ( (raw[i + 3] & RegionMask) != 0 )
			{
				hasConnections = true;
				break;
			}
		}

		if ( hasConnections )
		{
			for ( int i = 0; i < pointCount; i++ )
			{
				int next = (i + 1) % pointCount;
				bool differentRegions = (raw[i * 4 + 3] & RegionMask) != (raw[next * 4 + 3] & RegionMask);
				bool areaBorders = (raw[i * 4 + 3] & AreaBorder) != (raw[next * 4 + 3] & AreaBorder);
				if ( differentRegions || areaBorders ) AddSimple( raw[i * 4], raw[i * 4 + 1], raw[i * 4 + 2], i );
			}
		}

		if ( simpleCount == 0 )
		{
			// No portals at all: start from the lower left and upper right points
			int lowX = raw[0], lowY = raw[1], lowHeight = raw[2], low = 0;
			int highX = raw[0], highY = raw[1], highHeight = raw[2], high = 0;
			for ( int i = 0; i < rawCount; i += 4 )
			{
				int x = raw[i], y = raw[i + 1], height = raw[i + 2];
				if ( x < lowX || (x == lowX && y < lowY) )
				{
					lowX = x;
					lowHeight = height;
					lowY = y;
					low = i / 4;
				}
				if ( x > highX || (x == highX && y > highY) )
				{
					highX = x;
					highHeight = height;
					highY = y;
					high = i / 4;
				}
			}
			AddSimple( lowX, lowY, lowHeight, low );
			AddSimple( highX, highY, highHeight, high );
		}

		float maxErrorSquared = maxError * maxError;
		for ( int i = 0; i < simpleCount / 4; )
		{
			int next = (i + 1) % (simpleCount / 4);
			int ax = simple[i * 4], ay = simple[i * 4 + 1], a = simple[i * 4 + 3];
			int bx = simple[next * 4], by = simple[next * 4 + 1], b = simple[next * 4 + 3];

			// Walk the segment in lexical order so both sides of a shared edge find the same point
			float furthest = 0;
			int furthestPoint = -1;
			int current, step, last;
			if ( bx > ax || (bx == ax && by > ay) )
			{
				step = 1;
				current = (a + step) % pointCount;
				last = b;
			}
			else
			{
				step = pointCount - 1;
				current = (b + step) % pointCount;
				last = a;
				(ax, bx) = (bx, ax);
				(ay, by) = (by, ay);
			}

			// Only walls and area borders are tessellated
			if ( (raw[current * 4 + 3] & RegionMask) == 0 || (raw[current * 4 + 3] & AreaBorder) != 0 )
			{
				while ( current != last )
				{
					float d = DistanceToSegment( raw[current * 4], raw[current * 4 + 1], ax, ay, bx, by );
					if ( d > furthest )
					{
						furthest = d;
						furthestPoint = current;
					}
					current = (current + step) % pointCount;
				}
			}

			if ( furthestPoint != -1 && furthest > maxErrorSquared ) InsertSimple( i + 1, furthestPoint );
			else i++;
		}

		if ( maxEdgeLength > 0 )
		{
			for ( int i = 0; i < simpleCount / 4; )
			{
				int next = (i + 1) % (simpleCount / 4);
				int ax = simple[i * 4], ay = simple[i * 4 + 1], a = simple[i * 4 + 3];
				int bx = simple[next * 4], by = simple[next * 4 + 1], b = simple[next * 4 + 3];

				int split = -1;
				int current = (a + 1) % pointCount;
				if ( (raw[current * 4 + 3] & RegionMask) == 0 )
				{
					int dx = bx - ax;
					int dy = by - ay;
					if ( dx * dx + dy * dy > maxEdgeLength * maxEdgeLength )
					{
						int n = b < a ? b + pointCount - a : b - a;
						if ( n > 1 )
						{
							if ( bx > ax || (bx == ax && by > ay) ) split = (a + n / 2) % pointCount;
							else split = (a + (n + 1) / 2) % pointCount;
						}
					}
				}

				if ( split != -1 ) InsertSimple( i + 1, split );
				else i++;
			}
		}

		// The border flag comes from the point itself, the neighbour region from the point after it
		for ( int i = 0; i < simpleCount; i += 4 )
		{
			int after = (simple[i + 3] + 1) % pointCount;
			int at = simple[i + 3];
			simple[i + 3] = (raw[after * 4 + 3] & (RegionMask | AreaBorder)) | (raw[at * 4 + 3] & BorderVertex);
		}
	}

	// Drops a vertex that sits on the next one, which would confuse the triangulation.
	void RemoveDegenerateSegments()
	{
		int count = simpleCount / 4;
		int write = 0;
		for ( int i = 0; i < count; i++ )
		{
			int next = (i + 1) % count;
			if ( simple[i * 4] == simple[next * 4] && simple[i * 4 + 1] == simple[next * 4 + 1] ) continue;
			if ( write != i ) Array.Copy( simple, i * 4, simple, write * 4, 4 );
			write++;
		}
		simpleCount = write * 4;
	}

	int SignedArea( int contour )
	{
		int start = Start[contour];
		int count = VertCount[contour];
		int area = 0;
		for ( int i = 0, j = count - 1; i < count; j = i++ )
		{
			area += Verts[start + i * 4] * Verts[start + j * 4 + 1] - Verts[start + j * 4] * Verts[start + i * 4 + 1];
		}
		return (area + 1) / 2;
	}

	bool MergeHoles( int regionCount, out string? error )
	{
		error = null;
		if ( winding.Length < Count ) winding = new int[Count * 2];

		int holeTotal = 0;
		for ( int i = 0; i < Count; i++ )
		{
			winding[i] = SignedArea( i ) < 0 ? -1 : 1;
			if ( winding[i] < 0 ) holeTotal++;
		}
		if ( holeTotal == 0 ) return true;

		int regions = regionCount + 1;
		if ( outlineOf.Length < regions )
		{
			outlineOf = new int[regions * 2];
			holeStart = new int[regions * 2];
			holeCount = new int[regions * 2];
		}
		if ( holes.Length < holeTotal ) holes = new Hole[holeTotal * 2];
		Array.Fill( outlineOf, -1, 0, regions );
		Array.Clear( holeCount, 0, regions );

		for ( int i = 0; i < Count; i++ )
		{
			int r = Region[i];
			if ( winding[i] < 0 )
			{
				holeCount[r]++;
				continue;
			}

			if ( outlineOf[r] != -1 )
			{
				error = "A region has more than one outline";
				return false;
			}
			outlineOf[r] = i;
		}

		int cursor = 0;
		for ( int r = 0; r < regions; r++ )
		{
			holeStart[r] = cursor;
			cursor += holeCount[r];
			holeCount[r] = 0;
		}

		for ( int i = 0; i < Count; i++ )
		{
			if ( winding[i] >= 0 ) continue;
			int r = Region[i];
			holes[holeStart[r] + holeCount[r]++].Contour = i;
		}

		for ( int r = 0; r < regions; r++ )
		{
			if ( holeCount[r] == 0 ) continue;
			if ( outlineOf[r] == -1 )
			{
				error = "A region has holes but no outline";
				return false;
			}
			MergeRegionHoles( outlineOf[r], holes.AsSpan( holeStart[r], holeCount[r] ) );
		}
		return true;
	}

	// Joins each hole to the outline by the shortest diagonal that crosses nothing, leftmost hole first.
	void MergeRegionHoles( int outline, Span<Hole> regionHoles )
	{
		int maxVerts = VertCount[outline];
		for ( int i = 0; i < regionHoles.Length; i++ )
		{
			ref var hole = ref regionHoles[i];
			int start = Start[hole.Contour];
			int count = VertCount[hole.Contour];
			hole.MinX = Verts[start];
			hole.MinY = Verts[start + 1];
			hole.LeftMost = 0;
			for ( int v = 1; v < count; v++ )
			{
				int x = Verts[start + v * 4], y = Verts[start + v * 4 + 1];
				if ( x < hole.MinX || (x == hole.MinX && y < hole.MinY) )
				{
					hole.MinX = x;
					hole.MinY = y;
					hole.LeftMost = v;
				}
			}
			maxVerts += count + 2;
		}

		regionHoles.Sort( holeOrder );
		if ( diagonals.Length < maxVerts ) diagonals = new Diagonal[maxVerts * 2];

		for ( int i = 0; i < regionHoles.Length; i++ )
		{
			int hole = regionHoles[i].Contour;
			int holeVerts = Start[hole];
			int holeVertCount = VertCount[hole];
			int outlineVerts = Start[outline];
			int outlineVertCount = VertCount[outline];

			int found = -1;
			int best = regionHoles[i].LeftMost;
			for ( int attempt = 0; attempt < holeVertCount; attempt++ )
			{
				// Outline vertices that see the hole vertex inside their corner
				int cx = Verts[holeVerts + best * 4], cy = Verts[holeVerts + best * 4 + 1];
				int candidates = 0;
				for ( int j = 0; j < outlineVertCount; j++ )
				{
					if ( !InCone( outlineVerts, j, outlineVertCount, cx, cy ) ) continue;
					int dx = Verts[outlineVerts + j * 4] - cx;
					int dy = Verts[outlineVerts + j * 4 + 1] - cy;
					diagonals[candidates].Vertex = j;
					diagonals[candidates].Distance = dx * dx + dy * dy;
					candidates++;
				}

				diagonals.AsSpan( 0, candidates ).Sort( diagonalOrder );

				found = -1;
				for ( int j = 0; j < candidates; j++ )
				{
					int vertex = diagonals[j].Vertex;
					int px = Verts[outlineVerts + vertex * 4], py = Verts[outlineVerts + vertex * 4 + 1];
					bool intersect = IntersectsContour( px, py, cx, cy, vertex, outlineVerts, outlineVertCount );
					for ( int k = i; k < regionHoles.Length && !intersect; k++ )
						intersect |= IntersectsContour( px, py, cx, cy, -1, Start[regionHoles[k].Contour], VertCount[regionHoles[k].Contour] );
					if ( !intersect )
					{
						found = vertex;
						break;
					}
				}

				if ( found != -1 ) break;
				best = (best + 1) % holeVertCount;
			}

			// No diagonal: the hole stays a contour of its own
			if ( found == -1 ) continue;

			// The outline from the diagonal's end back to it, then the hole likewise
			int merged = Reserve( (outlineVertCount + holeVertCount + 2) * 4 );
			int write = merged;
			for ( int v = 0; v <= outlineVertCount; v++, write += 4 ) Array.Copy( Verts, outlineVerts + (found + v) % outlineVertCount * 4, Verts, write, 4 );
			for ( int v = 0; v <= holeVertCount; v++, write += 4 ) Array.Copy( Verts, holeVerts + (best + v) % holeVertCount * 4, Verts, write, 4 );

			Start[outline] = merged;
			VertCount[outline] = outlineVertCount + holeVertCount + 2;
			VertCount[hole] = 0;
		}
	}

	// True when the segment from (px, py) to the corner crosses an edge of the contour. Edges at vertex skip are left out.
	bool IntersectsContour( int px, int py, int cx, int cy, int skip, int verts, int count )
	{
		for ( int k = 0; k < count; k++ )
		{
			int k1 = k + 1 < count ? k + 1 : 0;
			if ( skip == k || skip == k1 ) continue;

			if ( GridGeometry.Crosses( px, py, cx, cy, Verts[verts + k * 4], Verts[verts + k * 4 + 1], Verts[verts + k1 * 4], Verts[verts + k1 * 4 + 1] ) ) return true;
		}
		return false;
	}

	// True when the point lies inside the corner at vertex i of the contour.
	bool InCone( int verts, int i, int count, int px, int py )
	{
		int next = verts + (i + 1 < count ? i + 1 : 0) * 4;
		int previous = verts + (i - 1 >= 0 ? i - 1 : count - 1) * 4;
		int at = verts + i * 4;
		return GridGeometry.InCone( Verts[previous], Verts[previous + 1], Verts[at], Verts[at + 1], Verts[next], Verts[next + 1], px, py );
	}
}
