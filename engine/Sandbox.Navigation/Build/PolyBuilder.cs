using System.Runtime.Intrinsics;

namespace Nav.Build;

/// <summary>
/// Turns contours into convex polygons of up to six vertices: triangulate each contour, flip the
/// triangles to its constrained Delaunay triangulation, merge them across their longest shared edge,
/// drop the helper vertices on tile borders, then find neighbours.
/// </summary>
internal sealed class PolyBuilder
{
	const int Nvp = BuildConfig.VertsPerPoly;
	const ushort None = BuildConfig.NoIndex;
	const int VertexBuckets = 1 << 12;
	const int EdgeBuckets = 256;
	const int NoEar = int.MaxValue;

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

	int maxPolys;

	byte[] vertexFlags = [];
	int[] nextVertex = [];
	readonly int[] firstVertex = new int[VertexBuckets];
	int[] indices = [];
	int[] tris = [];

	// Ear clipping
	int[] order = [];
	int[] xs = [];
	int[] ys = [];
	int[] ears = [];
	ushort[] work = [];

	// Edge hash of the polygons being merged
	readonly int[] buckets = new int[EdgeBuckets];
	int[] edgePoly = [];
	int[] edgeIndex = [];
	int[] edgeV0 = [];
	int[] edgeV1 = [];
	int[] edgeNext = [];

	// What Merge keeps up to date between merges; polyBest is the largest merge value of each polygon
	int[] vertCount = [];
	int[] neighbourPoly = [];
	int[] neighbourEdge = [];
	int[] mergeValue = [];
	int[] polyBest = [];

	// Flips
	int[] flipStack = [];
	bool[] queued = [];

	// Vertex removal
	int[] holeEdges = [];
	int[] hole = [];
	int[] holeArea = [];
	int[] holeVerts = [];
	int[] holeTris = [];
	ushort[] holePolys = [];
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
		if ( !Begin( contours, out error ) ) return false;

		for ( int c = 0; c < contours.Count; c++ )
		{
			int polyCount = Triangles( contours, c, out bool linked );
			if ( polyCount == 0 ) continue;
			polyCount = Merge( work, polyCount, null, linked );

			for ( int j = 0; j < polyCount; j++ )
			{
				Array.Copy( work, j * Nvp, Polys, PolyCount * Nvp, Nvp );
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

		if ( PolyCount > Poly.MaxPerTile )
		{
			error = "Too many polygons";
			return false;
		}

		BuildAdjacency();
		MarkBorderEdges( size );
		return true;
	}

	/// <summary>
	/// Empties the polygons and makes room for those of the contours.
	/// </summary>
	internal bool Begin( ContourBuilder contours, out string? error )
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
		Ensure( ref vertexFlags, maxVertices );
		Ensure( ref nextVertex, maxVertices );
		Ensure( ref indices, maxVertsPerContour );
		Ensure( ref tris, maxVertsPerContour * 3 );
		Ensure( ref work, (maxVertsPerContour + 1) * Nvp );
		Array.Clear( vertexFlags, 0, maxVertices );
		Array.Fill( firstVertex, -1 );
		Array.Fill( Polys, None, 0, maxTris * Nvp );
		Array.Fill( Neis, None, 0, maxTris * Nvp );
		return true;
	}

	/// <summary>
	/// Triangulates contour c, adds its vertices, and flips the triangles to its constrained Delaunay
	/// triangulation: into Work, linked as Merge takes them. linked is false when they are not a clean
	/// surface, and then not flipped. Returns the triangle count.
	/// </summary>
	internal int Triangles( ContourBuilder contours, int c, out bool linked )
	{
		linked = false;
		int count = contours.VertCount[c];
		if ( count < 3 ) return 0;
		int start = contours.Start[c];
		var contourVerts = contours.Verts;

		int triangleCount = Math.Abs( Triangulate( count, contourVerts, start, tris ) );

		for ( int j = 0; j < count; j++ )
		{
			int v = start + j * 4;
			indices[j] = AddVertex( contourVerts[v], contourVerts[v + 1], contourVerts[v + 2] );
			if ( (contourVerts[v + 3] & ContourBuilder.BorderVertex) != 0 ) vertexFlags[indices[j]] = 1;
		}

		int polyCount = 0;
		Array.Fill( work, None, 0, (count + 1) * Nvp );
		for ( int j = 0; j < triangleCount; j++ )
		{
			int t0 = tris[j * 3], t1 = tris[j * 3 + 1], t2 = tris[j * 3 + 2];
			if ( t0 == t1 || t0 == t2 || t1 == t2 ) continue;
			work[polyCount * Nvp] = (ushort)indices[t0];
			work[polyCount * Nvp + 1] = (ushort)indices[t1];
			work[polyCount * Nvp + 2] = (ushort)indices[t2];
			polyCount++;
		}
		if ( polyCount == 0 ) return 0;

		linked = Link( work, polyCount );
		if ( linked ) Flip( work, polyCount );
		return polyCount;
	}

	/// <summary>
	/// Six vertex indices per triangle that Triangles made, unused ones NoIndex.
	/// </summary>
	internal ushort[] Work => work;

	/// <summary>
	/// The polygon across each edge of what Triangles made, -1 for none; six per triangle.
	/// </summary>
	internal int[] Across => neighbourPoly;

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

	// True when the line from position i to position j of the outline runs inside it and crosses none of its
	// edges. Only edges whose box meets the line's box can cross it.
	bool Diagonal( int i, int j, int n, bool loose = false )
	{
		var x = xs;
		var y = ys;
		int previous = Previous( i, n ), next = Next( i, n );
		if ( !GridGeometry.InCone( x[previous], y[previous], x[i], y[i], x[next], y[next], x[j], y[j], loose ) ) return false;

		int ax = x[i], ay = y[i], bx = x[j], by = y[j];
		int minX = Math.Min( ax, bx ), maxX = Math.Max( ax, bx ), minY = Math.Min( ay, by ), maxY = Math.Max( ay, by );
		var lowX = Vector128.Create( minX );
		var highX = Vector128.Create( maxX );
		var lowY = Vector128.Create( minY );
		var highY = Vector128.Create( maxY );
		for ( int k = 0; k < n; k += 4 )
		{
			var x0 = Vector128.Create( x, k );
			var x1 = Vector128.Create( x, k + 1 );
			var y0 = Vector128.Create( y, k );
			var y1 = Vector128.Create( y, k + 1 );
			var apart = (Vector128.LessThan( x0, lowX ) & Vector128.LessThan( x1, lowX )) | (Vector128.GreaterThan( x0, highX ) & Vector128.GreaterThan( x1, highX ))
				| (Vector128.LessThan( y0, lowY ) & Vector128.LessThan( y1, lowY )) | (Vector128.GreaterThan( y0, highY ) & Vector128.GreaterThan( y1, highY ));
			uint near = ~apart.ExtractMostSignificantBits() & 0xF;
			for ( ; near != 0; near &= near - 1 )
			{
				int e = k + System.Numerics.BitOperations.TrailingZeroCount( near );
				if ( e >= n ) break;
				int e1 = Next( e, n );
				if ( e == i || e1 == i || e == j || e1 == j ) continue;
				if ( GridGeometry.Crosses( ax, ay, bx, by, x[e], y[e], x[e + 1], y[e + 1], loose ) ) return false;
			}
		}
		return true;
	}

	// The squared length of the diagonal from position i past the next vertex when that vertex is an ear, else NoEar.
	int Ear( int i, int n )
	{
		int j = Next( Next( i, n ), n );
		if ( !Diagonal( i, j, n ) ) return NoEar;
		int dx = xs[j] - xs[i], dy = ys[j] - ys[i];
		return dx * dx + dy * dy;
	}

	// Ear clipping, shortest diagonal first, the first in outline order on a tie. The vertices are four ints each
	// (x, y, height, flags) starting at offset, as contours have them; tris gets their indices. The outline is kept
	// in order, x, y and the ear of each position (ears[i] is the ear at the vertex after i), position n repeating
	// position 0. Returns the triangle count, negative when it had to give up.
	int Triangulate( int n, int[] verts, int offset, int[] tris )
	{
		Ensure( ref order, n + 1 );
		Ensure( ref xs, n + 5 );
		Ensure( ref ys, n + 5 );
		Ensure( ref ears, n );
		for ( int i = 0; i <= n; i++ )
		{
			int v = offset + (i < n ? i : 0) * 4;
			order[i] = i < n ? i : 0;
			xs[i] = verts[v];
			ys[i] = verts[v + 1];
		}
		for ( int i = 0; i < n; i++ ) ears[i] = Ear( i, n );

		int count = 0;
		while ( n > 3 )
		{
			int ear = -1, shortest = NoEar;
			for ( int i = 0; i < n; i++ )
			{
				if ( ears[i] >= shortest ) continue;
				shortest = ears[i];
				ear = i;
			}

			if ( ear == -1 )
			{
				// Overlapping segments in the contour. Try again accepting diagonals that touch it.
				shortest = -1;
				for ( int i = 0; i < n; i++ )
				{
					int i2 = Next( Next( i, n ), n );
					if ( !Diagonal( i, i2, n, loose: true ) ) continue;

					int i3 = Next( i2, n );
					int dx = xs[i3] - xs[i], dy = ys[i3] - ys[i];
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

				tris[count * 3] = order[i0];
				tris[count * 3 + 1] = order[i1];
				tris[count * 3 + 2] = order[i2];
				count++;

				n--;
				Array.Copy( order, i1 + 1, order, i1, n - i1 );
				Array.Copy( xs, i1 + 1, xs, i1, n - i1 );
				Array.Copy( ys, i1 + 1, ys, i1, n - i1 );
				Array.Copy( ears, i1 + 1, ears, i1, n - i1 );
				order[n] = order[0];
				xs[n] = xs[0];
				ys[n] = ys[0];

				if ( i1 >= n ) i1 = 0;
				i0 = Previous( i1, n );
				ears[Previous( i0, n )] = Ear( Previous( i0, n ), n );
				ears[i0] = Ear( i0, n );
			}
		}

		tris[count * 3] = order[0];
		tris[count * 3 + 1] = order[1];
		tris[count * 3 + 2] = order[2];
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

	// Merges triangles into polygons across shared edges, always the longest edge whose two polygons make a
	// convex one of at most six vertices, the first such edge in polygon order on a tie. The merged polygon
	// takes the place of the lower of the two, the last polygon moves into the place of the other. linked: Link
	// found the triangles a clean surface; else every merge searches from scratch. areaOf follows the polygons
	// when given.
	int Merge( ushort[] polys, int polyCount, int[]? areaOf, bool linked )
	{
		if ( !linked )
		{
			// An edge with three polygons on it, or two that walk it the same way: search from scratch every time
			while ( FindBestMerge( polys, polyCount, out int a, out int b, out int ea, out int eb ) )
			{
				MergePolys( polys, a, b, ea, eb );
				Array.Copy( polys, (polyCount - 1) * Nvp, polys, b * Nvp, Nvp );
				if ( areaOf != null ) areaOf[b] = areaOf[polyCount - 1];
				polyCount--;
			}
			return polyCount;
		}

		for ( int p = 0; p < polyCount; p++ )
		{
			for ( int e = 0; e < vertCount[p]; e++ )
			{
				if ( neighbourPoly[p * Nvp + e] > p ) Rate( polys, p, e );
			}
		}

		Span<int> mergedPoly = stackalloc int[Nvp];
		Span<int> mergedEdge = stackalloc int[Nvp];
		while ( true )
		{
			int best = 0, a = -1;
			for ( int p = 0; p < polyCount; p++ )
			{
				if ( polyBest[p] > best )
				{
					best = polyBest[p];
					a = p;
				}
			}
			if ( a < 0 ) break;

			int ea = 0;
			while ( mergeValue[a * Nvp + ea] != best ) ea++;
			int at = a * Nvp + ea;
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
			polyBest[a] = 0;
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
			if ( areaOf != null ) areaOf[b] = areaOf[last];
			if ( b != last )
			{
				Array.Copy( polys, last * Nvp, polys, b * Nvp, Nvp );
				vertCount[b] = vertCount[last];
				polyBest[b] = 0;
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

	// Finds the polygon on the other side of every edge. False when the polygons are not a clean surface:
	// more than two on an edge, or two walking it in the same direction.
	bool Link( ushort[] polys, int polyCount )
	{
		int maxEdges = polyCount * Nvp;
		Ensure( ref vertCount, polyCount );
		Ensure( ref neighbourPoly, maxEdges );
		Ensure( ref neighbourEdge, maxEdges );
		Ensure( ref mergeValue, maxEdges );
		Ensure( ref polyBest, polyCount );
		Ensure( ref edgeNext, maxEdges );
		Array.Fill( buckets, -1 );
		Array.Fill( neighbourPoly, -1, 0, maxEdges );
		Array.Clear( mergeValue, 0, maxEdges );
		Array.Clear( polyBest, 0, polyCount );

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
		return true;
	}

	// Lawson flips of linked triangles. An edge with a triangle on each side becomes the other diagonal of
	// their quad when the quad is strictly convex and the far corner lies inside the circle through the other
	// three, or on it while the edge touches the greatest of the four corners (by x, then y, then index). That
	// tie rule makes it the Delaunay triangulation of the points lifted by amounts too small to measure that
	// grow with that order, which is unique: the triangles that come out depend neither on the ones that went
	// in nor on the order of the flips; only where each lands in the list does. Edges with one triangle, the
	// outline and its holes, stay; a bridge to a hole has a triangle on each side and may flip. The stack
	// starts with every shared edge, the first triangle's on top, and takes the outer edges of each flipped quad.
	void Flip( ushort[] tris, int count )
	{
		Ensure( ref flipStack, count * 3 );
		Ensure( ref queued, count * Nvp );
		Array.Clear( queued, 0, count * Nvp );
		int top = 0;
		for ( int t = count - 1; t >= 0; t-- )
		{
			for ( int e = 2; e >= 0; e-- ) Push( t * Nvp + e, ref top );
		}

		// A planar triangulation needs far fewer flips; a contour that crosses itself could cycle
		for ( long flips = 0; top > 0 && flips < (long)count * count; )
		{
			int edge = flipStack[--top];
			queued[edge] = false;
			int u = neighbourPoly[edge];
			if ( u < 0 ) continue;

			int t = edge / Nvp, e = edge % Nvp, f = neighbourEdge[edge];
			int a = tris[edge], b = tris[t * Nvp + (e + 1) % 3], c = tris[t * Nvp + (e + 2) % 3], d = tris[u * Nvp + (f + 2) % 3];
			if ( !Flips( a, b, c, d ) ) continue;
			flips++;

			// t becomes (c, a, d) and u becomes (d, b, c)
			int bc = t * Nvp + (e + 1) % 3, ca = t * Nvp + (e + 2) % 3, ad = u * Nvp + (f + 1) % 3, db = u * Nvp + (f + 2) % 3;
			int bcPoly = neighbourPoly[bc], bcEdge = neighbourEdge[bc], caPoly = neighbourPoly[ca], caEdge = neighbourEdge[ca];
			int adPoly = neighbourPoly[ad], adEdge = neighbourEdge[ad], dbPoly = neighbourPoly[db], dbEdge = neighbourEdge[db];
			tris[t * Nvp] = (ushort)c;
			tris[t * Nvp + 1] = (ushort)a;
			tris[t * Nvp + 2] = (ushort)d;
			tris[u * Nvp] = (ushort)d;
			tris[u * Nvp + 1] = (ushort)b;
			tris[u * Nvp + 2] = (ushort)c;
			Join( t * Nvp, caPoly, caEdge );
			Join( t * Nvp + 1, adPoly, adEdge );
			Join( t * Nvp + 2, u, 2 );
			Join( u * Nvp, dbPoly, dbEdge );
			Join( u * Nvp + 1, bcPoly, bcEdge );

			Push( t * Nvp, ref top );
			Push( t * Nvp + 1, ref top );
			Push( u * Nvp, ref top );
			Push( u * Nvp + 1, ref top );
		}
	}

	void Push( int edge, ref int top )
	{
		int other = neighbourPoly[edge];
		if ( other < 0 || queued[edge] || queued[other * Nvp + neighbourEdge[edge]] ) return;
		queued[edge] = true;
		flipStack[top++] = edge;
	}

	void Join( int edge, int poly, int polyEdge )
	{
		neighbourPoly[edge] = poly;
		neighbourEdge[edge] = polyEdge;
		if ( poly < 0 ) return;
		neighbourPoly[poly * Nvp + polyEdge] = edge / Nvp;
		neighbourEdge[poly * Nvp + polyEdge] = edge % Nvp;
	}

	// Whether the edge from a to b, between the triangles (a, b, c) and (b, a, d), should be the edge from c to d.
	bool Flips( int a, int b, int c, int d )
	{
		int ax = Verts[a * 3], ay = Verts[a * 3 + 1], bx = Verts[b * 3], by = Verts[b * 3 + 1];
		int cx = Verts[c * 3], cy = Verts[c * 3 + 1], dx = Verts[d * 3], dy = Verts[d * 3 + 1];
		int abc = GridGeometry.Area( ax, ay, bx, by, cx, cy ), abd = GridGeometry.Area( ax, ay, bx, by, dx, dy );
		int cda = GridGeometry.Area( cx, cy, dx, dy, ax, ay ), cdb = GridGeometry.Area( cx, cy, dx, dy, bx, by );
		if ( abc == 0 || abd == 0 || cda == 0 || cdb == 0 || (abc > 0) == (abd > 0) || (cda > 0) == (cdb > 0) ) return false;

		long inside = GridGeometry.InCircle( ax, ay, bx, by, cx, cy, dx, dy );
		if ( inside != 0 ) return abc > 0 ? inside > 0 : inside < 0;
		int greatest = Greater( Greater( a, b ), Greater( c, d ) );
		return greatest == a || greatest == b;
	}

	int Greater( int p, int q )
	{
		int px = Verts[p * 3], py = Verts[p * 3 + 1], qx = Verts[q * 3], qy = Verts[q * 3 + 1];
		if ( px != qx ) return px > qx ? p : q;
		if ( py != qy ) return py > qy ? p : q;
		return p > q ? p : q;
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
		polyBest[p] = Best( p );
		polyBest[q] = Best( q );
	}

	int Best( int p )
	{
		int best = 0;
		for ( int e = p * Nvp; e < p * Nvp + Nvp; e++ ) best = Math.Max( best, mergeValue[e] );
		return best;
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

		Ensure( ref holeEdges, touching * Nvp * 3 );
		Ensure( ref hole, touching * Nvp );
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
				edges[edgeCount * 3] = a;
				edges[edgeCount * 3 + 1] = b;
				edges[edgeCount * 3 + 2] = Areas[i];
				edgeCount++;
			}

			int last = PolyCount - 1;
			if ( i != last ) Array.Copy( Polys, last * Nvp, Polys, i * Nvp, Nvp );
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
			if ( edges[i * 3] > remove ) edges[i * 3]--;
			if ( edges[i * 3 + 1] > remove ) edges[i * 3 + 1]--;
		}

		if ( edgeCount == 0 ) return true;

		// Chain the edges into the outline of the hole
		int holeCount = 0;
		hole[holeCount] = edges[0];
		holeArea[holeCount] = edges[2];
		holeCount++;

		while ( edgeCount > 0 )
		{
			bool match = false;
			for ( int i = 0; i < edgeCount; i++ )
			{
				int ea = edges[i * 3], eb = edges[i * 3 + 1], a = edges[i * 3 + 2];
				bool add = false;
				if ( hole[0] == eb )
				{
					Array.Copy( hole, 0, hole, 1, holeCount );
					Array.Copy( holeArea, 0, holeArea, 1, holeCount );
					hole[0] = ea;
					holeArea[0] = a;
					holeCount++;
					add = true;
				}
				else if ( hole[holeCount - 1] == ea )
				{
					hole[holeCount] = eb;
					holeArea[holeCount] = a;
					holeCount++;
					add = true;
				}

				if ( !add ) continue;
				Array.Copy( edges, (edgeCount - 1) * 3, edges, i * 3, 3 );
				edgeCount--;
				match = true;
				i--;
			}

			if ( !match ) break;
		}

		// A chain of fewer than three vertices encloses nothing
		if ( holeCount < 3 ) return true;

		Ensure( ref holeTris, holeCount * 3 );
		Ensure( ref holeVerts, holeCount * 4 );
		for ( int i = 0; i < holeCount; i++ )
		{
			int v = hole[i] * 3;
			holeVerts[i * 4] = Verts[v];
			holeVerts[i * 4 + 1] = Verts[v + 1];
			holeVerts[i * 4 + 2] = Verts[v + 2];
			holeVerts[i * 4 + 3] = 0;
		}

		int triangleCount = Math.Abs( Triangulate( holeCount, holeVerts, 0, holeTris ) );

		Ensure( ref holePolys, (triangleCount + 1) * Nvp );
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

			holePolyArea[polyCount] = holeArea[t0];
			polyCount++;
		}
		if ( polyCount == 0 ) return true;

		// Not flipped: a hole is a few triangles around one vertex, and rare
		bool linked = Link( polys, polyCount );
		polyCount = Merge( polys, polyCount, holePolyArea, linked );
		if ( PolyCount + polyCount > maxPolys ) return false;

		for ( int i = 0; i < polyCount; i++ )
		{
			Array.Copy( polys, i * Nvp, Polys, PolyCount * Nvp, Nvp );
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
