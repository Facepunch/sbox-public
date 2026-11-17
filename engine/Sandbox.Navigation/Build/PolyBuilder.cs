namespace Nav.Build;

/// <summary>
/// Turns contours into convex polygons of up to six vertices: triangulate each contour, merge
/// triangles across their longest shared edge, drop the helper vertices on tile borders, then
/// find neighbours.
/// </summary>
internal sealed class PolyBuilder
{
	const int Nvp = BuildConfig.VertsPerPoly;
	const ushort None = BuildConfig.NoIndex;
	const int VertexBuckets = 1 << 12;
	const int EdgeBuckets = 256;
	const int IndexMask = 0x0FFFFFFF;
	const int Removable = int.MinValue;

	public int VertCount, PolyCount;

	/// <summary>
	/// x, y, height per vertex, in cells from the tile's corner and cell heights from the field's origin.
	/// </summary>
	public ushort[] Verts = [];

	/// <summary>
	/// Six vertex indices per polygon, unused ones are NoIndex.
	/// </summary>
	public ushort[] Polys = [];

	/// <summary>
	/// Per polygon edge (from vertex j to vertex j + 1): the polygon on the other side, NoIndex for a wall,
	/// BorderEdge + side on the tile's edge. Polygons are clockwise seen from above.
	/// </summary>
	public ushort[] Neis = [];

	public byte[] Areas = [];

	int[] regions = [];
	int maxPolys;

	byte[] vertexFlags = [];
	int[] nextVertex = [];
	readonly int[] firstVertex = new int[VertexBuckets];
	int[] indices = [];
	int[] tris = [];
	ushort[] work = [];

	// Edge hash of the polygons being merged
	readonly int[] buckets = new int[EdgeBuckets];
	int[] edgePoly = [];
	int[] edgeIndex = [];
	int[] edgeV0 = [];
	int[] edgeV1 = [];
	int[] edgeNext = [];

	// What MergeAll keeps up to date between merges
	int[] vertCount = [];
	int[] neighbourPoly = [];
	int[] neighbourEdge = [];
	int[] mergeValue = [];

	// Vertex removal
	int[] holeEdges = [];
	int[] hole = [];
	int[] holeRegion = [];
	int[] holeArea = [];
	int[] holeVerts = [];
	int[] holeIndices = [];
	int[] holeTris = [];
	ushort[] holePolys = [];
	int[] holePolyRegion = [];
	int[] holePolyArea = [];

	// Adjacency
	int[] firstEdge = [];
	int[] nextEdge = [];
	MeshEdge[] meshEdges = [];

	struct MeshEdge
	{
		public ushort V0, V1, Poly0, Poly1, Edge0, Edge1;
	}

	static void Ensure<T>( ref T[] array, int length )
	{
		if ( array.Length < length ) array = new T[length + length / 2];
	}

	/// <summary>
	/// size is the tile's edge in cells: an unconnected edge along it leads into the next tile.
	/// </summary>
	public bool Build( ContourBuilder contours, int size, out string? error )
	{
		error = null;
		VertCount = 0;
		PolyCount = 0;

		int maxVertices = 0, maxTris = 0, maxVertsPerContour = 0;
		for ( int i = 0; i < contours.Count; i++ )
		{
			int count = contours.VertCount[i];
			if ( count < 3 ) continue;
			maxVertices += count;
			maxTris += count - 2;
			maxVertsPerContour = Math.Max( maxVertsPerContour, count );
		}

		if ( maxVertices >= 0xFFFE )
		{
			error = "Too many vertices";
			return false;
		}

		maxPolys = maxTris;
		Ensure( ref Verts, maxVertices * 3 );
		Ensure( ref Polys, maxTris * Nvp );
		Ensure( ref Neis, maxTris * Nvp );
		Ensure( ref Areas, maxTris );
		Ensure( ref regions, maxTris );
		Ensure( ref vertexFlags, maxVertices );
		Ensure( ref nextVertex, maxVertices );
		Ensure( ref indices, maxVertsPerContour );
		Ensure( ref tris, maxVertsPerContour * 3 );
		Ensure( ref work, (maxVertsPerContour + 1) * Nvp );
		Array.Clear( vertexFlags, 0, maxVertices );
		Array.Fill( firstVertex, -1 );
		Array.Fill( Polys, None, 0, maxTris * Nvp );
		Array.Fill( Neis, None, 0, maxTris * Nvp );

		var contourVerts = contours.Verts;
		for ( int c = 0; c < contours.Count; c++ )
		{
			int count = contours.VertCount[c];
			if ( count < 3 ) continue;
			int start = contours.Start[c];

			for ( int j = 0; j < count; j++ ) indices[j] = j;
			int triangleCount = Math.Abs( Triangulate( count, contourVerts, start, indices, tris ) );

			for ( int j = 0; j < count; j++ )
			{
				int v = start + j * 4;
				indices[j] = AddVertex( contourVerts[v], contourVerts[v + 1], contourVerts[v + 2] );
				if ( (contourVerts[v + 3] & ContourBuilder.BorderVertex) != 0 ) vertexFlags[indices[j]] = 1;
			}

			int polyCount = 0;
			Array.Fill( work, None, 0, (maxVertsPerContour + 1) * Nvp );
			for ( int j = 0; j < triangleCount; j++ )
			{
				int t0 = tris[j * 3], t1 = tris[j * 3 + 1], t2 = tris[j * 3 + 2];
				if ( t0 == t1 || t0 == t2 || t1 == t2 ) continue;
				work[polyCount * Nvp] = (ushort)indices[t0];
				work[polyCount * Nvp + 1] = (ushort)indices[t1];
				work[polyCount * Nvp + 2] = (ushort)indices[t2];
				polyCount++;
			}
			if ( polyCount == 0 ) continue;

			polyCount = MergeAll( work, polyCount, null, null );

			for ( int j = 0; j < polyCount; j++ )
			{
				Array.Copy( work, j * Nvp, Polys, PolyCount * Nvp, Nvp );
				regions[PolyCount] = contours.Region[c];
				Areas[PolyCount] = (byte)contours.Area[c];
				PolyCount++;
			}
		}

		for ( int i = 0; i < VertCount; i++ )
		{
			if ( vertexFlags[i] == 0 || !CanRemoveVertex( i ) ) continue;
			if ( !RemoveVertex( i ) )
			{
				error = "Too many polygons";
				return false;
			}
			Array.Copy( vertexFlags, i + 1, vertexFlags, i, VertCount - i );
			i--;
		}

		if ( PolyCount > BuildConfig.MaxPolys )
		{
			error = "Too many polygons";
			return false;
		}

		BuildAdjacency();
		MarkBorderEdges( size );
		return true;
	}

	int AddVertex( int x, int y, int height )
	{
		const uint h1 = 0x8da6b343, h3 = 0xcb1ab31f;
		int bucket = (int)((h1 * (uint)x + h3 * (uint)y) & (VertexBuckets - 1));
		for ( int i = firstVertex[bucket]; i != -1; i = nextVertex[i] )
		{
			if ( Verts[i * 3] == x && Verts[i * 3 + 1] == y && Math.Abs( Verts[i * 3 + 2] - height ) <= 2 ) return i;
		}

		int vertex = VertCount++;
		Verts[vertex * 3] = (ushort)x;
		Verts[vertex * 3 + 1] = (ushort)y;
		Verts[vertex * 3 + 2] = (ushort)height;
		nextVertex[vertex] = firstVertex[bucket];
		firstVertex[bucket] = vertex;
		return vertex;
	}

	static int Next( int i, int n ) => i + 1 < n ? i + 1 : 0;
	static int Previous( int i, int n ) => i - 1 >= 0 ? i - 1 : n - 1;

	// True when the line from vertex i to vertex j of the outline runs inside it and crosses none of its
	// edges. The vertices are four ints each (x, y, height, flags) starting at offset, as contours have them.
	static bool Diagonal( int i, int j, int n, int[] verts, int offset, int[] indices, bool loose = false )
	{
		int pi = offset + (indices[i] & IndexMask) * 4;
		int pj = offset + (indices[j] & IndexMask) * 4;
		int pn = offset + (indices[Next( i, n )] & IndexMask) * 4;
		int pp = offset + (indices[Previous( i, n )] & IndexMask) * 4;
		if ( !GridGeometry.InCone( verts[pp], verts[pp + 1], verts[pi], verts[pi + 1], verts[pn], verts[pn + 1], verts[pj], verts[pj + 1], loose ) ) return false;

		for ( int k = 0; k < n; k++ )
		{
			int k1 = Next( k, n );
			if ( k == i || k1 == i || k == j || k1 == j ) continue;

			int p0 = offset + (indices[k] & IndexMask) * 4;
			int p1 = offset + (indices[k1] & IndexMask) * 4;
			if ( GridGeometry.Crosses( verts[pi], verts[pi + 1], verts[pj], verts[pj + 1], verts[p0], verts[p0 + 1], verts[p1], verts[p1 + 1], loose ) ) return false;
		}
		return true;
	}

	// Ear clipping, shortest diagonal first. Returns the triangle count, negative when it had to give up.
	static int Triangulate( int n, int[] verts, int offset, int[] indices, int[] tris )
	{
		int count = 0;

		// The top bit of an index marks a vertex whose neighbours can be joined by a diagonal
		for ( int i = 0; i < n; i++ )
		{
			int i1 = Next( i, n );
			int i2 = Next( i1, n );
			if ( Diagonal( i, i2, n, verts, offset, indices ) ) indices[i1] |= Removable;
		}

		while ( n > 3 )
		{
			int shortest = -1;
			int ear = -1;
			for ( int i = 0; i < n; i++ )
			{
				int i1 = Next( i, n );
				if ( (indices[i1] & Removable) == 0 ) continue;

				int p0 = offset + (indices[i] & IndexMask) * 4;
				int p2 = offset + (indices[Next( i1, n )] & IndexMask) * 4;
				int dx = verts[p2] - verts[p0];
				int dy = verts[p2 + 1] - verts[p0 + 1];
				int length = dx * dx + dy * dy;
				if ( shortest < 0 || length < shortest )
				{
					shortest = length;
					ear = i;
				}
			}

			if ( ear == -1 )
			{
				// Overlapping segments in the contour. Try again accepting diagonals that touch it.
				for ( int i = 0; i < n; i++ )
				{
					int i1 = Next( i, n );
					int i2 = Next( i1, n );
					if ( !Diagonal( i, i2, n, verts, offset, indices, loose: true ) ) continue;

					int p0 = offset + (indices[i] & IndexMask) * 4;
					int p2 = offset + (indices[Next( i2, n )] & IndexMask) * 4;
					int dx = verts[p2] - verts[p0];
					int dy = verts[p2 + 1] - verts[p0 + 1];
					int length = dx * dx + dy * dy;
					if ( shortest < 0 || length < shortest )
					{
						shortest = length;
						ear = i;
					}
				}

				if ( ear == -1 ) return -count;
			}

			{
				int i0 = ear;
				int i1 = Next( i0, n );
				int i2 = Next( i1, n );

				tris[count * 3] = indices[i0] & IndexMask;
				tris[count * 3 + 1] = indices[i1] & IndexMask;
				tris[count * 3 + 2] = indices[i2] & IndexMask;
				count++;

				n--;
				for ( int k = i1; k < n; k++ ) indices[k] = indices[k + 1];

				if ( i1 >= n ) i1 = 0;
				i0 = Previous( i1, n );

				if ( Diagonal( Previous( i0, n ), i1, n, verts, offset, indices ) ) indices[i0] |= Removable;
				else indices[i0] &= IndexMask;

				if ( Diagonal( i0, Next( i1, n ), n, verts, offset, indices ) ) indices[i1] |= Removable;
				else indices[i1] &= IndexMask;
			}
		}

		tris[count * 3] = indices[0] & IndexMask;
		tris[count * 3 + 1] = indices[1] & IndexMask;
		tris[count * 3 + 2] = indices[2] & IndexMask;
		return count + 1;
	}

	static int CountVerts( ushort[] polys, int poly )
	{
		for ( int i = 0; i < Nvp; i++ )
		{
			if ( polys[poly * Nvp + i] == None ) return i;
		}
		return Nvp;
	}

	bool Left( int a, int b, int c )
	{
		int ax = Verts[a * 3], ay = Verts[a * 3 + 1];
		int bx = Verts[b * 3], by = Verts[b * 3 + 1];
		int cx = Verts[c * 3], cy = Verts[c * 3 + 1];
		return (bx - ax) * (cy - ay) - (cx - ax) * (by - ay) < 0;
	}

	// Merges polygons across shared edges, always the longest edge whose two polygons make a convex one
	// of at most six vertices, the first such edge in polygon order on a tie. The merged polygon takes
	// the place of the lower of the two, the last polygon moves into the place of the other.
	// regionOf and areaOf follow the polygons when given; a polygon made of two regions gets region 0.
	int MergeAll( ushort[] polys, int polyCount, int[]? regionOf, int[]? areaOf )
	{
		if ( !Link( polys, polyCount ) )
		{
			// An edge with three polygons on it, or two that walk it the same way: search from scratch every time
			while ( FindBestMerge( polys, polyCount, out int a, out int b, out int ea, out int eb ) )
			{
				MergePolys( polys, a, b, ea, eb );
				Array.Copy( polys, (polyCount - 1) * Nvp, polys, b * Nvp, Nvp );
				Follow( regionOf, areaOf, a, b, polyCount - 1 );
				polyCount--;
			}
			return polyCount;
		}

		Span<int> mergedPoly = stackalloc int[Nvp];
		Span<int> mergedEdge = stackalloc int[Nvp];
		while ( true )
		{
			int best = 0, at = -1;
			for ( int i = 0, n = polyCount * Nvp; i < n; i++ )
			{
				if ( mergeValue[i] > best )
				{
					best = mergeValue[i];
					at = i;
				}
			}
			if ( at < 0 ) break;

			int a = at / Nvp, ea = at % Nvp;
			int b = neighbourPoly[at], eb = neighbourEdge[at];
			int na = vertCount[a], nb = vertCount[b];

			// The merged polygon is a's edges after the shared one, then b's. Each keeps the polygon behind it,
			// except where a and b shared a second edge.
			int count = 0;
			for ( int i = 1; i < na; i++, count++ )
			{
				int edge = a * Nvp + (ea + i) % na;
				mergedPoly[count] = neighbourPoly[edge] == b ? -1 : neighbourPoly[edge];
				mergedEdge[count] = neighbourEdge[edge];
			}
			for ( int i = 1; i < nb; i++, count++ )
			{
				int edge = b * Nvp + (eb + i) % nb;
				mergedPoly[count] = neighbourPoly[edge] == a ? -1 : neighbourPoly[edge];
				mergedEdge[count] = neighbourEdge[edge];
			}

			MergePolys( polys, a, b, ea, eb );
			vertCount[a] = count;
			for ( int k = 0; k < Nvp; k++ )
			{
				int edge = a * Nvp + k;
				mergeValue[edge] = 0;
				neighbourPoly[edge] = k < count ? mergedPoly[k] : -1;
				if ( k >= count || mergedPoly[k] < 0 ) continue;
				neighbourEdge[edge] = mergedEdge[k];
				neighbourPoly[mergedPoly[k] * Nvp + mergedEdge[k]] = a;
				neighbourEdge[mergedPoly[k] * Nvp + mergedEdge[k]] = k;
			}

			int last = polyCount - 1;
			Follow( regionOf, areaOf, a, b, last );
			if ( b != last )
			{
				Array.Copy( polys, last * Nvp, polys, b * Nvp, Nvp );
				vertCount[b] = vertCount[last];
				for ( int k = 0; k < Nvp; k++ )
				{
					int edge = b * Nvp + k;
					mergeValue[edge] = 0;
					neighbourPoly[edge] = neighbourPoly[last * Nvp + k];
					neighbourEdge[edge] = neighbourEdge[last * Nvp + k];
					if ( neighbourPoly[edge] < 0 ) continue;
					neighbourPoly[neighbourPoly[edge] * Nvp + neighbourEdge[edge]] = b;
				}
			}
			polyCount--;

			for ( int k = 0; k < vertCount[a]; k++ ) Rate( polys, a, k );
			if ( b != last )
			{
				for ( int k = 0; k < vertCount[b]; k++ ) Rate( polys, b, k );
			}
		}
		return polyCount;
	}

	static void Follow( int[]? regionOf, int[]? areaOf, int a, int b, int last )
	{
		if ( regionOf == null || areaOf == null ) return;
		if ( regionOf[a] != regionOf[b] ) regionOf[a] = 0;
		regionOf[b] = regionOf[last];
		areaOf[b] = areaOf[last];
	}

	// Finds the polygon on the other side of every edge and rates every shared edge. False when the
	// polygons are not a clean surface: more than two on an edge, or two walking it in the same direction.
	bool Link( ushort[] polys, int polyCount )
	{
		int maxEdges = polyCount * Nvp;
		Ensure( ref vertCount, polyCount );
		Ensure( ref neighbourPoly, maxEdges );
		Ensure( ref neighbourEdge, maxEdges );
		Ensure( ref mergeValue, maxEdges );
		Ensure( ref edgeNext, maxEdges );
		Array.Fill( buckets, -1 );
		Array.Fill( neighbourPoly, -1, 0, maxEdges );
		Array.Clear( mergeValue, 0, maxEdges );

		for ( int p = 0; p < polyCount; p++ )
		{
			int nv = CountVerts( polys, p );
			vertCount[p] = nv;
			for ( int e = 0; e < nv; e++ )
			{
				int edge = p * Nvp + e;
				int v0 = polys[edge], v1 = polys[p * Nvp + (e + 1) % nv];
				int bucket = (Math.Min( v0, v1 ) * 31 + Math.Max( v0, v1 )) % EdgeBuckets;

				int other = buckets[bucket];
				for ( ; other != -1; other = edgeNext[other] )
				{
					int q = other / Nvp, eq = other % Nvp;
					int w0 = polys[other], w1 = polys[q * Nvp + (eq + 1) % vertCount[q]];
					if ( w0 == v1 && w1 == v0 ) break;
					if ( w0 == v0 && w1 == v1 ) return false;
				}

				if ( other == -1 )
				{
					edgeNext[edge] = buckets[bucket];
					buckets[bucket] = edge;
					continue;
				}

				if ( neighbourPoly[other] != -1 ) return false;
				neighbourPoly[other] = p;
				neighbourEdge[other] = e;
				neighbourPoly[edge] = other / Nvp;
				neighbourEdge[edge] = other % Nvp;
			}
		}

		for ( int p = 0; p < polyCount; p++ )
		{
			for ( int e = 0; e < vertCount[p]; e++ )
			{
				if ( neighbourPoly[p * Nvp + e] > p ) Rate( polys, p, e );
			}
		}
		return true;
	}

	// The squared length of a shared edge when its two polygons can merge, else 0, kept with the lower polygon.
	void Rate( ushort[] polys, int p, int e )
	{
		int q = neighbourPoly[p * Nvp + e];
		if ( q < 0 ) return;
		int eq = neighbourEdge[p * Nvp + e];
		int nvP = vertCount[p], nvQ = vertCount[q];

		int value = 0;
		if ( nvP + nvQ - 2 <= Nvp
			&& Left( polys[p * Nvp + (e + nvP - 1) % nvP], polys[p * Nvp + e], polys[q * Nvp + (eq + 2) % nvQ] )
			&& Left( polys[q * Nvp + (eq + nvQ - 1) % nvQ], polys[q * Nvp + eq], polys[p * Nvp + (e + 2) % nvP] ) )
		{
			int a = polys[p * Nvp + e] * 3, b = polys[p * Nvp + (e + 1) % nvP] * 3;
			int dx = Verts[a] - Verts[b], dy = Verts[a + 1] - Verts[b + 1];
			value = dx * dx + dy * dy;
		}

		mergeValue[p * Nvp + e] = p < q ? value : 0;
		mergeValue[q * Nvp + eq] = p < q ? 0 : value;
	}

	// The pair of polygons sharing the longest edge whose union is still convex and fits in six vertices.
	bool FindBestMerge( ushort[] polys, int polyCount, out int bestA, out int bestB, out int bestEdgeA, out int bestEdgeB )
	{
		int maxEdges = polyCount * Nvp;
		if ( edgePoly.Length < maxEdges )
		{
			int size = maxEdges * 2;
			edgePoly = new int[size];
			edgeIndex = new int[size];
			edgeV0 = new int[size];
			edgeV1 = new int[size];
			edgeNext = new int[size];
		}

		Array.Fill( buckets, -1 );
		int edges = 0;
		for ( int p = 0; p < polyCount; p++ )
		{
			int nv = CountVerts( polys, p );
			for ( int e = 0; e < nv; e++ )
			{
				int v0 = polys[p * Nvp + e], v1 = polys[p * Nvp + (e + 1) % nv];
				if ( v0 > v1 ) (v0, v1) = (v1, v0);
				int bucket = (v0 * 31 + v1) % EdgeBuckets;
				edgePoly[edges] = p;
				edgeIndex[edges] = e;
				edgeV0[edges] = v0;
				edgeV1[edges] = v1;
				edgeNext[edges] = buckets[bucket];
				buckets[bucket] = edges++;
			}
		}

		int bestLength = 0;
		bestA = bestB = bestEdgeA = bestEdgeB = 0;
		for ( int p = 0; p < polyCount; p++ )
		{
			int nvP = CountVerts( polys, p );
			for ( int e = 0; e < nvP; e++ )
			{
				int v0 = polys[p * Nvp + e], v1 = polys[p * Nvp + (e + 1) % nvP];
				if ( v0 > v1 ) (v0, v1) = (v1, v0);

				for ( int entry = buckets[(v0 * 31 + v1) % EdgeBuckets]; entry != -1; entry = edgeNext[entry] )
				{
					if ( edgeV0[entry] != v0 || edgeV1[entry] != v1 ) continue;
					int q = edgePoly[entry];
					if ( q <= p ) continue;

					int eq = edgeIndex[entry];
					int nvQ = CountVerts( polys, q );
					if ( nvP + nvQ - 2 > Nvp ) continue;

					// Convex at both ends of the shared edge
					if ( !Left( polys[p * Nvp + (e + nvP - 1) % nvP], polys[p * Nvp + e], polys[q * Nvp + (eq + 2) % nvQ] ) ) continue;
					if ( !Left( polys[q * Nvp + (eq + nvQ - 1) % nvQ], polys[q * Nvp + eq], polys[p * Nvp + (e + 2) % nvP] ) ) continue;

					int a = polys[p * Nvp + e] * 3, b = polys[p * Nvp + (e + 1) % nvP] * 3;
					int dx = Verts[a] - Verts[b], dy = Verts[a + 1] - Verts[b + 1];
					int length = dx * dx + dy * dy;
					if ( length > bestLength )
					{
						bestLength = length;
						bestA = p;
						bestB = q;
						bestEdgeA = e;
						bestEdgeB = eq;
					}
				}
			}
		}
		return bestLength > 0;
	}

	// Replaces polygon a with the union of a and b, which share edge ea of a and eb of b.
	static void MergePolys( ushort[] polys, int a, int b, int ea, int eb )
	{
		int na = CountVerts( polys, a );
		int nb = CountVerts( polys, b );
		Span<ushort> merged = stackalloc ushort[Nvp];
		merged.Fill( None );
		int n = 0;
		for ( int i = 0; i < na - 1; i++ ) merged[n++] = polys[a * Nvp + (ea + 1 + i) % na];
		for ( int i = 0; i < nb - 1; i++ ) merged[n++] = polys[b * Nvp + (eb + 1 + i) % nb];
		merged.CopyTo( polys.AsSpan( a * Nvp, Nvp ) );
	}

	// A vertex can go when what is left around it still closes into one polygon.
	bool CanRemoveVertex( int remove )
	{
		int touching = 0;
		int remainingEdges = 0;
		for ( int i = 0; i < PolyCount; i++ )
		{
			int nv = CountVerts( Polys, i );
			int removed = 0;
			for ( int j = 0; j < nv; j++ )
			{
				if ( Polys[i * Nvp + j] == remove ) removed++;
			}
			if ( removed == 0 ) continue;
			touching += removed;
			remainingEdges += nv - (removed + 1);
		}

		if ( remainingEdges <= 2 ) return false;

		// Edges at the vertex, with how many polygons use each
		Ensure( ref holeEdges, touching * 2 * 3 );
		var edges = holeEdges;
		int edgeCount = 0;
		for ( int i = 0; i < PolyCount; i++ )
		{
			int nv = CountVerts( Polys, i );
			for ( int j = 0, k = nv - 1; j < nv; k = j++ )
			{
				int a = Polys[i * Nvp + j], b = Polys[i * Nvp + k];
				if ( a != remove && b != remove ) continue;
				if ( b == remove ) (a, b) = (b, a);

				bool exists = false;
				for ( int m = 0; m < edgeCount; m++ )
				{
					if ( edges[m * 3 + 1] != b ) continue;
					edges[m * 3 + 2]++;
					exists = true;
				}

				if ( exists ) continue;
				edges[edgeCount * 3] = a;
				edges[edgeCount * 3 + 1] = b;
				edges[edgeCount * 3 + 2] = 1;
				edgeCount++;
			}
		}

		// More than two open edges: two fans that only meet at the vertex
		int open = 0;
		for ( int i = 0; i < edgeCount; i++ )
		{
			if ( edges[i * 3 + 2] < 2 ) open++;
		}
		return open <= 2;
	}

	// Removes the vertex and re-triangulates the hole its polygons leave. False when the new polygons do
	// not fit in the room the triangles of the contours had, which a hole never needs.
	bool RemoveVertex( int remove )
	{
		int touching = 0;
		for ( int i = 0; i < PolyCount; i++ )
		{
			int nv = CountVerts( Polys, i );
			for ( int j = 0; j < nv; j++ )
			{
				if ( Polys[i * Nvp + j] == remove ) touching++;
			}
		}

		Ensure( ref holeEdges, touching * Nvp * 4 );
		Ensure( ref hole, touching * Nvp );
		Ensure( ref holeRegion, touching * Nvp );
		Ensure( ref holeArea, touching * Nvp );
		var edges = holeEdges;
		int edgeCount = 0;

		for ( int i = 0; i < PolyCount; i++ )
		{
			int nv = CountVerts( Polys, i );
			bool has = false;
			for ( int j = 0; j < nv; j++ )
			{
				if ( Polys[i * Nvp + j] == remove ) has = true;
			}
			if ( !has ) continue;

			// The edges of the polygon that do not touch the vertex bound the hole
			for ( int j = 0, k = nv - 1; j < nv; k = j++ )
			{
				int a = Polys[i * Nvp + k], b = Polys[i * Nvp + j];
				if ( a == remove || b == remove ) continue;
				edges[edgeCount * 4] = a;
				edges[edgeCount * 4 + 1] = b;
				edges[edgeCount * 4 + 2] = regions[i];
				edges[edgeCount * 4 + 3] = Areas[i];
				edgeCount++;
			}

			int last = PolyCount - 1;
			if ( i != last ) Array.Copy( Polys, last * Nvp, Polys, i * Nvp, Nvp );
			regions[i] = regions[last];
			Areas[i] = Areas[last];
			PolyCount--;
			i--;
		}

		Array.Copy( Verts, (remove + 1) * 3, Verts, remove * 3, (VertCount - 1 - remove) * 3 );
		VertCount--;

		for ( int i = 0; i < PolyCount; i++ )
		{
			int nv = CountVerts( Polys, i );
			for ( int j = 0; j < nv; j++ )
			{
				if ( Polys[i * Nvp + j] > remove ) Polys[i * Nvp + j]--;
			}
		}

		for ( int i = 0; i < edgeCount; i++ )
		{
			if ( edges[i * 4] > remove ) edges[i * 4]--;
			if ( edges[i * 4 + 1] > remove ) edges[i * 4 + 1]--;
		}

		if ( edgeCount == 0 ) return true;

		// Chain the edges into the outline of the hole
		int holeCount = 0;
		hole[holeCount] = edges[0];
		holeRegion[holeCount] = edges[2];
		holeArea[holeCount] = edges[3];
		holeCount++;

		while ( edgeCount > 0 )
		{
			bool match = false;
			for ( int i = 0; i < edgeCount; i++ )
			{
				int ea = edges[i * 4], eb = edges[i * 4 + 1], r = edges[i * 4 + 2], a = edges[i * 4 + 3];
				bool add = false;
				if ( hole[0] == eb )
				{
					Array.Copy( hole, 0, hole, 1, holeCount );
					Array.Copy( holeRegion, 0, holeRegion, 1, holeCount );
					Array.Copy( holeArea, 0, holeArea, 1, holeCount );
					hole[0] = ea;
					holeRegion[0] = r;
					holeArea[0] = a;
					holeCount++;
					add = true;
				}
				else if ( hole[holeCount - 1] == ea )
				{
					hole[holeCount] = eb;
					holeRegion[holeCount] = r;
					holeArea[holeCount] = a;
					holeCount++;
					add = true;
				}

				if ( !add ) continue;
				Array.Copy( edges, (edgeCount - 1) * 4, edges, i * 4, 4 );
				edgeCount--;
				match = true;
				i--;
			}

			if ( !match ) break;
		}

		Ensure( ref holeTris, holeCount * 3 );
		Ensure( ref holeVerts, holeCount * 4 );
		Ensure( ref holeIndices, holeCount );
		for ( int i = 0; i < holeCount; i++ )
		{
			int v = hole[i] * 3;
			holeVerts[i * 4] = Verts[v];
			holeVerts[i * 4 + 1] = Verts[v + 1];
			holeVerts[i * 4 + 2] = Verts[v + 2];
			holeVerts[i * 4 + 3] = 0;
			holeIndices[i] = i;
		}

		int triangleCount = Math.Abs( Triangulate( holeCount, holeVerts, 0, holeIndices, holeTris ) );

		Ensure( ref holePolys, (triangleCount + 1) * Nvp );
		Ensure( ref holePolyRegion, triangleCount );
		Ensure( ref holePolyArea, triangleCount );
		var polys = holePolys;
		Array.Fill( polys, None, 0, (triangleCount + 1) * Nvp );

		int polyCount = 0;
		for ( int j = 0; j < triangleCount; j++ )
		{
			int t0 = holeTris[j * 3], t1 = holeTris[j * 3 + 1], t2 = holeTris[j * 3 + 2];
			if ( t0 == t1 || t0 == t2 || t1 == t2 ) continue;

			polys[polyCount * Nvp] = (ushort)hole[t0];
			polys[polyCount * Nvp + 1] = (ushort)hole[t1];
			polys[polyCount * Nvp + 2] = (ushort)hole[t2];

			// 0 marks a polygon that covers more than one region
			holePolyRegion[polyCount] = holeRegion[t0] != holeRegion[t1] || holeRegion[t1] != holeRegion[t2] ? 0 : holeRegion[t0];
			holePolyArea[polyCount] = holeArea[t0];
			polyCount++;
		}
		if ( polyCount == 0 ) return true;

		polyCount = MergeAll( polys, polyCount, holePolyRegion, holePolyArea );
		if ( PolyCount + polyCount > maxPolys ) return false;

		for ( int i = 0; i < polyCount; i++ )
		{
			Array.Copy( polys, i * Nvp, Polys, PolyCount * Nvp, Nvp );
			regions[PolyCount] = holePolyRegion[i];
			Areas[PolyCount] = (byte)holePolyArea[i];
			PolyCount++;
		}
		return true;
	}

	// Every edge is filed under its lower vertex by the polygon that walks it upwards, then matched by the
	// polygon that walks it downwards.
	void BuildAdjacency()
	{
		int maxEdges = PolyCount * Nvp;
		Ensure( ref firstEdge, VertCount );
		Ensure( ref nextEdge, maxEdges );
		Ensure( ref meshEdges, maxEdges );
		Array.Fill( firstEdge, -1, 0, VertCount );
		Array.Fill( Neis, None, 0, PolyCount * Nvp );

		int edgeCount = 0;
		for ( int i = 0; i < PolyCount; i++ )
		{
			for ( int j = 0; j < Nvp; j++ )
			{
				ushort v0 = Polys[i * Nvp + j];
				if ( v0 == None ) break;
				ushort v1 = j + 1 >= Nvp || Polys[i * Nvp + j + 1] == None ? Polys[i * Nvp] : Polys[i * Nvp + j + 1];
				if ( v0 >= v1 ) continue;

				meshEdges[edgeCount] = new MeshEdge { V0 = v0, V1 = v1, Poly0 = (ushort)i, Edge0 = (ushort)j, Poly1 = (ushort)i, Edge1 = 0 };
				nextEdge[edgeCount] = firstEdge[v0];
				firstEdge[v0] = edgeCount;
				edgeCount++;
			}
		}

		for ( int i = 0; i < PolyCount; i++ )
		{
			for ( int j = 0; j < Nvp; j++ )
			{
				ushort v0 = Polys[i * Nvp + j];
				if ( v0 == None ) break;
				ushort v1 = j + 1 >= Nvp || Polys[i * Nvp + j + 1] == None ? Polys[i * Nvp] : Polys[i * Nvp + j + 1];
				if ( v0 <= v1 ) continue;

				for ( int e = firstEdge[v1]; e != -1; e = nextEdge[e] )
				{
					ref var edge = ref meshEdges[e];
					if ( edge.V1 != v0 || edge.Poly0 != edge.Poly1 ) continue;
					edge.Poly1 = (ushort)i;
					edge.Edge1 = (ushort)j;
					break;
				}
			}
		}

		for ( int i = 0; i < edgeCount; i++ )
		{
			ref var edge = ref meshEdges[i];
			if ( edge.Poly0 == edge.Poly1 ) continue;
			Neis[edge.Poly0 * Nvp + edge.Edge0] = edge.Poly1;
			Neis[edge.Poly1 * Nvp + edge.Edge1] = edge.Poly0;
		}
	}

	// An unconnected edge that runs along the tile's edge leads into whatever is on the other side.
	void MarkBorderEdges( int size )
	{
		for ( int i = 0; i < PolyCount; i++ )
		{
			for ( int j = 0; j < Nvp; j++ )
			{
				ushort v = Polys[i * Nvp + j];
				if ( v == None ) break;
				if ( Neis[i * Nvp + j] != None ) continue;

				ushort next = j + 1 >= Nvp || Polys[i * Nvp + j + 1] == None ? Polys[i * Nvp] : Polys[i * Nvp + j + 1];
				int ax = Verts[v * 3], ay = Verts[v * 3 + 1];
				int bx = Verts[next * 3], by = Verts[next * 3 + 1];

				// Sides as in Poly: 0 = +x, 1 = +y, 2 = -x, 3 = -y.
				if ( ax == size && bx == size ) Neis[i * Nvp + j] = BuildConfig.BorderEdge;
				else if ( ay == size && by == size ) Neis[i * Nvp + j] = BuildConfig.BorderEdge + 1;
				else if ( ax == 0 && bx == 0 ) Neis[i * Nvp + j] = BuildConfig.BorderEdge + 2;
				else if ( ay == 0 && by == 0 ) Neis[i * Nvp + j] = BuildConfig.BorderEdge + 3;
			}
		}
	}
}
