using System.Buffers;
using System.Runtime.InteropServices;

namespace Sandbox.Navigation.Generation;

[SkipHotload]
internal class ContourSet
{
	public List<Contour> Contours = new( 128 );
	public Vector3 BMin;
	public Vector3 BMax;
	public float CellSize;
	public float CellHeight;
	public int Width;
	public int Height;
	public int BorderSize;
	public float MaxError;
}

/// <summary>
/// A contour representing a simplified region boundary.
/// Vertices are stored as packed int4 (x, y, z, flags).
/// Instances are pooled by ContourBuilderContext for reuse.
/// </summary>
[SkipHotload]
internal class Contour
{
	public Span<int> Vertices => CollectionsMarshal.AsSpan( verticesList );
	internal readonly List<int> verticesList = new( 64 );
	public int VertexCount => verticesList.Count / 4;
	public int Region;
	public int Area;

	public void Reset()
	{
		verticesList.Clear();
		Region = 0;
		Area = 0;
	}

	/// <summary>
	/// Merge contour cb into ca at the specified vertex indices.
	/// Uses provided buffer list to avoid allocations.
	/// </summary>
	public static void MergeContours( Contour ca, Contour cb, int ia, int ib, List<int> mergeBuffer )
	{
		int caVertexCount = ca.VertexCount;
		int cbVertexCount = cb.VertexCount;
		int maxVerts = caVertexCount + cbVertexCount + 2;

		CollectionsMarshal.SetCount( mergeBuffer, maxVerts * 4 );
		Span<int> mergedSpan = CollectionsMarshal.AsSpan( mergeBuffer );

		int nv = 0;

		// Copy contour A.
		var caVerts = ca.Vertices;
		for ( int i = 0; i <= caVertexCount; ++i )
		{
			int dst = nv * 4;
			int src = ((ia + i) % caVertexCount) * 4;
			mergedSpan[dst + 0] = caVerts[src + 0];
			mergedSpan[dst + 1] = caVerts[src + 1];
			mergedSpan[dst + 2] = caVerts[src + 2];
			mergedSpan[dst + 3] = caVerts[src + 3];
			nv++;
		}

		// Copy contour B
		var cbVerts = cb.Vertices;
		for ( int i = 0; i <= cbVertexCount; ++i )
		{
			int dst = nv * 4;
			int src = ((ib + i) % cbVertexCount) * 4;
			mergedSpan[dst + 0] = cbVerts[src + 0];
			mergedSpan[dst + 1] = cbVerts[src + 1];
			mergedSpan[dst + 2] = cbVerts[src + 2];
			mergedSpan[dst + 3] = cbVerts[src + 3];
			nv++;
		}

		// Resize ca's list and copy merged data
		CollectionsMarshal.SetCount( ca.verticesList, nv * 4 );
		mergedSpan.Slice( 0, nv * 4 ).CopyTo( ca.Vertices );

		cb.verticesList.Clear();
	}
}

[SkipHotload]
internal static class ContourBuilder
{
	[SkipHotload]
	public sealed class ContourBuilderContext
	{
		public List<int> Verts = new( 256 );
		public List<int> VertsSimplified = new( 128 );
		public List<byte> Flags = new( 4096 );
		public ContourSet ContourSet = new();

		/// <summary>
		/// Scratch buffer for MergeContours to avoid allocations.
		/// </summary>
		public List<int> MergeBuffer = new( 512 );

		/// <summary>
		/// Pool of Contour instances for reuse. Contours are rented via RentContour()
		/// and returned to pool when ClearContourSet() is called at the start of BuildContours.
		/// </summary>
		private readonly Stack<Contour> ContourPool = new( 256 );

		/// <summary>
		/// Rent a contour from the pool, sized for the given vertex count.
		/// </summary>
		public Contour RentContour( int vertexCount )
		{
			Contour c = ContourPool.TryPop( out var pooled ) ? pooled : new Contour();
			CollectionsMarshal.SetCount( c.verticesList, vertexCount * 4 );
			return c;
		}

		/// <summary>
		/// Return all contours in ContourSet to the pool and clear the set.
		/// Called at the start of BuildContours to recycle previous contours.
		/// </summary>
		public void ClearContourSet()
		{
			foreach ( var c in ContourSet.Contours )
			{
				c.Reset();
				ContourPool.Push( c );
			}
			ContourSet.Contours.Clear();
		}
	}

	[SkipHotload]
	private static class ContourBuildFlags
	{
		public const int RC_CONTOUR_TESS_WALL_EDGES = 0x01;
		public const int RC_CONTOUR_TESS_AREA_EDGES = 0x02;
	}

	[SkipHotload]
	private struct ContourHole
	{
		public int LeftMost;
		public int MinX;
		public int MinZ;
		public Contour Contour;
	}

	[SkipHotload]
	private struct ContourRegion
	{
		public Contour Outline;
		public int HoleStartIndex;
		public int HoleCount;
	}

	private class ContourHoleComparer : IComparer<ContourHole>
	{
		public static readonly ContourHoleComparer Shared = new();

		private ContourHoleComparer()
		{
		}

		public int Compare( ContourHole a, ContourHole b )
		{
			if ( a.MinX == b.MinX )
			{
				return a.MinZ.CompareTo( b.MinZ );
			}
			else
			{
				return a.MinX.CompareTo( b.MinX );
			}
		}
	}

	private class PotentialDiagonalComparer : IComparer<PotentialDiagonal>
	{
		public static readonly PotentialDiagonalComparer Shared = new();

		private PotentialDiagonalComparer()
		{
		}

		public int Compare( PotentialDiagonal va, PotentialDiagonal vb )
		{
			PotentialDiagonal a = va;
			PotentialDiagonal b = vb;
			return a.dist.CompareTo( b.dist );
		}
	}

	private struct PotentialDiagonal
	{
		public int vert;
		public int dist;
	}

	private static int GetCornerHeight( int x, int y, int i, int dir, CompactHeightfield chf, out bool isBorderVertex )
	{
		isBorderVertex = false;

		CompactSpan s = chf.Spans[i];
		int ch = s.StartY;
		int dirp = (dir + 1) & 0x3;

		Span<int> regs = stackalloc int[]
		{
			0, 0, 0, 0
		};

		// Combine region and area codes in order to prevent
		// border vertices which are in between two areas to be removed.
		regs[0] = chf.Spans[i].Region | (chf.Areas[i] << 16);

		if ( Utils.GetCon( s, dir ) != Constants.NOT_CONNECTED )
		{
			int ax = x + Utils.GetDirOffsetX( dir );
			int ay = y + Utils.GetDirOffsetZ( dir );
			int ai = chf.Cells[ax + ay * chf.Width].Index + Utils.GetCon( s, dir );
			CompactSpan @as = chf.Spans[ai];
			ch = Math.Max( ch, @as.StartY );
			regs[1] = chf.Spans[ai].Region | (chf.Areas[ai] << 16);
			if ( Utils.GetCon( @as, dirp ) != Constants.NOT_CONNECTED )
			{
				int ax2 = ax + Utils.GetDirOffsetX( dirp );
				int ay2 = ay + Utils.GetDirOffsetZ( dirp );
				int ai2 = chf.Cells[ax2 + ay2 * chf.Width].Index + Utils.GetCon( @as, dirp );
				CompactSpan as2 = chf.Spans[ai2];
				ch = Math.Max( ch, as2.StartY );
				regs[2] = chf.Spans[ai2].Region | (chf.Areas[ai2] << 16);
			}
		}

		if ( Utils.GetCon( s, dirp ) != Constants.NOT_CONNECTED )
		{
			int ax = x + Utils.GetDirOffsetX( dirp );
			int ay = y + Utils.GetDirOffsetZ( dirp );
			int ai = chf.Cells[ax + ay * chf.Width].Index + Utils.GetCon( s, dirp );
			CompactSpan @as = chf.Spans[ai];
			ch = Math.Max( ch, @as.StartY );
			regs[3] = chf.Spans[ai].Region | (chf.Areas[ai] << 16);
			if ( Utils.GetCon( @as, dir ) != Constants.NOT_CONNECTED )
			{
				int ax2 = ax + Utils.GetDirOffsetX( dir );
				int ay2 = ay + Utils.GetDirOffsetZ( dir );
				int ai2 = chf.Cells[ax2 + ay2 * chf.Width].Index + Utils.GetCon( @as, dir );
				CompactSpan as2 = chf.Spans[ai2];
				ch = Math.Max( ch, as2.StartY );
				regs[2] = chf.Spans[ai2].Region | (chf.Areas[ai2] << 16);
			}
		}

		// Check if the vertex is special edge vertex, these vertices will be removed later.
		for ( int j = 0; j < 4; ++j )
		{
			int a = j;
			int b = (j + 1) & 0x3;
			int c = (j + 2) & 0x3;
			int d = (j + 3) & 0x3;

			// The vertex is a border vertex there are two same exterior cells in a row,
			// followed by two interior cells and none of the regions are out of bounds.
			bool twoSameExts = (regs[a] & regs[b] & ContourRegionFlags.BORDER_REG) != 0 && regs[a] == regs[b];
			bool twoInts = ((regs[c] | regs[d]) & ContourRegionFlags.BORDER_REG) == 0;
			bool intsSameArea = (regs[c] >> 16) == (regs[d] >> 16);
			bool noZeros = regs[a] != 0 && regs[b] != 0 && regs[c] != 0 && regs[d] != 0;
			if ( twoSameExts && twoInts && intsSameArea && noZeros )
			{
				isBorderVertex = true;
				break;
			}
		}

		return ch;
	}

	private static void WalkContour( int x, int y, int i, CompactHeightfield chf, Span<byte> flags, List<int> points )
	{
		// Choose the first non-connected edge
		int dir = 0;
		while ( (flags[i] & (1 << dir)) == 0 )
			dir++;

		int startDir = dir;
		int starti = i;

		int area = chf.Areas[i];

		int iter = 0;
		while ( ++iter < 40000 )
		{
			if ( (flags[i] & (1 << dir)) != 0 )
			{
				// Choose the edge corner
				bool isBorderVertex = false;
				bool isAreaBorder = false;
				int px = x;
				int py = GetCornerHeight( x, y, i, dir, chf, out isBorderVertex );
				int pz = y;
				switch ( dir )
				{
					case 0:
						pz++;
						break;
					case 1:
						px++;
						pz++;
						break;
					case 2:
						px++;
						break;
				}

				int r = 0;
				CompactSpan s = chf.Spans[i];
				if ( Utils.GetCon( s, dir ) != Constants.NOT_CONNECTED )
				{
					int ax = x + Utils.GetDirOffsetX( dir );
					int ay = y + Utils.GetDirOffsetZ( dir );
					int ai = chf.Cells[ax + ay * chf.Width].Index + Utils.GetCon( s, dir );
					r = chf.Spans[ai].Region;
					if ( area != chf.Areas[ai] )
						isAreaBorder = true;
				}

				if ( isBorderVertex )
					r |= ContourRegionFlags.BORDER_VERTEX;
				if ( isAreaBorder )
					r |= ContourRegionFlags.AREA_BORDER;
				points.Add( px );
				points.Add( py );
				points.Add( pz );
				points.Add( r );

				flags[i] = (byte)(flags[i] & ~(1 << dir)); // Remove visited edges
				dir = (dir + 1) & 0x3; // Rotate CW
			}
			else
			{
				int ni = -1;
				int nx = x + Utils.GetDirOffsetX( dir );
				int ny = y + Utils.GetDirOffsetZ( dir );
				CompactSpan s = chf.Spans[i];
				if ( Utils.GetCon( s, dir ) != Constants.NOT_CONNECTED )
				{
					CompactCell nc = chf.Cells[nx + ny * chf.Width];
					ni = nc.Index + Utils.GetCon( s, dir );
				}

				if ( ni == -1 )
				{
					// Should not happen.
					return;
				}

				x = nx;
				y = ny;
				i = ni;
				dir = (dir + 3) & 0x3; // Rotate CCW
			}

			if ( starti == i && startDir == dir )
			{
				break;
			}
		}
	}

	private static float DistancePtSeg( int x, int z, int px, int pz, int qx, int qz )
	{
		float pqx = qx - px;
		float pqz = qz - pz;
		float d = pqx * pqx + pqz * pqz;
		float t = pqx * (x - px) + pqz * (z - pz);
		if ( d > 0 )
			t /= d;
		t = Math.Clamp( t, 0f, 1f );

		float dx = px + t * pqx - x;
		float dz = pz + t * pqz - z;

		return dx * dx + dz * dz;
	}

	private static void SimplifyContour( List<int> points, List<int> simplified, float maxError, int maxEdgeLen, int buildFlags )
	{
		// Add initial points.
		bool hasConnections = false;
		for ( int i = 0; i < points.Count; i += 4 )
		{
			if ( (points[i + 3] & ContourRegionFlags.CONTOUR_REG_MASK) != 0 )
			{
				hasConnections = true;
				break;
			}
		}

		if ( hasConnections )
		{
			// The contour has some portals to other regions.
			// Add a new point to every location where the region changes.
			for ( int i = 0, ni = points.Count / 4; i < ni; ++i )
			{
				int ii = (i + 1) % ni;
				bool differentRegs = (points[i * 4 + 3] & ContourRegionFlags.CONTOUR_REG_MASK) != (points[ii * 4 + 3] & ContourRegionFlags.CONTOUR_REG_MASK);
				bool areaBorders = (points[i * 4 + 3] & ContourRegionFlags.AREA_BORDER) != (points[ii * 4 + 3] & ContourRegionFlags.AREA_BORDER);
				if ( differentRegs || areaBorders )
				{
					simplified.Add( points[i * 4 + 0] );
					simplified.Add( points[i * 4 + 1] );
					simplified.Add( points[i * 4 + 2] );
					simplified.Add( i );
				}
			}
		}

		if ( simplified.Count == 0 )
		{
			// If there is no connections at all,
			// create some initial points for the simplification process.
			// Find lower-left and upper-right vertices of the contour.
			int llx = points[0];
			int lly = points[1];
			int llz = points[2];
			int lli = 0;
			int urx = points[0];
			int ury = points[1];
			int urz = points[2];
			int uri = 0;
			for ( int i = 0; i < points.Count; i += 4 )
			{
				int x = points[i + 0];
				int y = points[i + 1];
				int z = points[i + 2];
				if ( x < llx || (x == llx && z < llz) )
				{
					llx = x;
					lly = y;
					llz = z;
					lli = i / 4;
				}

				if ( x > urx || (x == urx && z > urz) )
				{
					urx = x;
					ury = y;
					urz = z;
					uri = i / 4;
				}
			}

			simplified.Add( llx );
			simplified.Add( lly );
			simplified.Add( llz );
			simplified.Add( lli );

			simplified.Add( urx );
			simplified.Add( ury );
			simplified.Add( urz );
			simplified.Add( uri );
		}

		// Add points until all raw points are within
		// error tolerance to the simplified shape.
		int pn = points.Count / 4;
		for ( int i = 0; i < simplified.Count / 4; )
		{
			int ii = (i + 1) % (simplified.Count / 4);

			int ax = simplified[i * 4 + 0];
			int az = simplified[i * 4 + 2];
			int ai = simplified[i * 4 + 3];

			int bx = simplified[ii * 4 + 0];
			int bz = simplified[ii * 4 + 2];
			int bi = simplified[ii * 4 + 3];

			// Find maximum deviation from the segment.
			float maxd = 0;
			int maxi = -1;
			int ci, cinc, endi;

			// Traverse the segment in lexilogical order so that the
			// max deviation is calculated similarly when traversing
			// opposite segments.
			if ( bx > ax || (bx == ax && bz > az) )
			{
				cinc = 1;
				ci = (ai + cinc) % pn;
				endi = bi;
			}
			else
			{
				cinc = pn - 1;
				ci = (bi + cinc) % pn;
				endi = ai;
				int temp = ax;
				ax = bx;
				bx = temp;
				temp = az;
				az = bz;
				bz = temp;
			}

			// Tessellate only outer edges or edges between areas.
			if ( (points[ci * 4 + 3] & ContourRegionFlags.CONTOUR_REG_MASK) == 0 || (points[ci * 4 + 3] & ContourRegionFlags.AREA_BORDER) != 0 )
			{
				while ( ci != endi )
				{
					float d = DistancePtSeg( points[ci * 4 + 0], points[ci * 4 + 2], ax, az, bx, bz );
					if ( d > maxd )
					{
						maxd = d;
						maxi = ci;
					}

					ci = (ci + cinc) % pn;
				}
			}

			// If the max deviation is larger than accepted error,
			// add new point, else continue to next segment.
			if ( maxi != -1 && maxd > (maxError * maxError) )
			{
				// Add space for the new point.
				int insertIdx = i + 1;
				CollectionsMarshal.SetCount( simplified, simplified.Count + 4 );
				int n = simplified.Count / 4;
				for ( int j = n - 1; j > insertIdx; --j )
				{
					simplified[j * 4 + 0] = simplified[(j - 1) * 4 + 0];
					simplified[j * 4 + 1] = simplified[(j - 1) * 4 + 1];
					simplified[j * 4 + 2] = simplified[(j - 1) * 4 + 2];
					simplified[j * 4 + 3] = simplified[(j - 1) * 4 + 3];
				}
				// Add the point.
				int maxiBase = maxi * 4;
				simplified[insertIdx * 4 + 0] = points[maxiBase + 0];
				simplified[insertIdx * 4 + 1] = points[maxiBase + 1];
				simplified[insertIdx * 4 + 2] = points[maxiBase + 2];
				simplified[insertIdx * 4 + 3] = maxi;
			}
			else
			{
				++i;
			}
		}

		// Split too long edges.
		if ( maxEdgeLen > 0 && (buildFlags & (ContourBuildFlags.RC_CONTOUR_TESS_WALL_EDGES | ContourBuildFlags.RC_CONTOUR_TESS_AREA_EDGES)) != 0 )
		{
			for ( int i = 0; i < simplified.Count / 4; )
			{
				int ii = (i + 1) % (simplified.Count / 4);

				int ax = simplified[i * 4 + 0];
				int az = simplified[i * 4 + 2];
				int ai = simplified[i * 4 + 3];

				int bx = simplified[ii * 4 + 0];
				int bz = simplified[ii * 4 + 2];
				int bi = simplified[ii * 4 + 3];

				// Find maximum deviation from the segment.
				int maxi = -1;
				int ci = (ai + 1) % pn;

				// Tessellate only outer edges or edges between areas.
				bool tess = false;
				// Wall edges.
				if ( (buildFlags & ContourBuildFlags.RC_CONTOUR_TESS_WALL_EDGES) != 0 && (points[ci * 4 + 3] & ContourRegionFlags.CONTOUR_REG_MASK) == 0 )
				{
					tess = true;
				}

				// Edges between areas.
				if ( (buildFlags & ContourBuildFlags.RC_CONTOUR_TESS_AREA_EDGES) != 0 && (points[ci * 4 + 3] & ContourRegionFlags.AREA_BORDER) != 0 )
				{
					tess = true;
				}

				if ( tess )
				{
					int dx = bx - ax;
					int dz = bz - az;
					if ( dx * dx + dz * dz > maxEdgeLen * maxEdgeLen )
					{
						// Round based on the segments in lexilogical order so that the
						// max tesselation is consistent regardless in which direction
						// segments are traversed.
						int n = bi < ai ? (bi + pn - ai) : (bi - ai);
						if ( n > 1 )
						{
							if ( bx > ax || (bx == ax && bz > az) )
								maxi = (ai + n / 2) % pn;
							else
								maxi = (ai + (n + 1) / 2) % pn;
						}
					}
				}

				// If the max deviation is larger than accepted error,
				// add new point, else continue to next segment.
				if ( maxi != -1 )
				{
					// Add space for the new point.
					int insertIdx = i + 1;
					CollectionsMarshal.SetCount( simplified, simplified.Count + 4 );
					int n = simplified.Count / 4;
					for ( int j = n - 1; j > insertIdx; --j )
					{
						simplified[j * 4 + 0] = simplified[(j - 1) * 4 + 0];
						simplified[j * 4 + 1] = simplified[(j - 1) * 4 + 1];
						simplified[j * 4 + 2] = simplified[(j - 1) * 4 + 2];
						simplified[j * 4 + 3] = simplified[(j - 1) * 4 + 3];
					}
					// Add the point.
					int maxiBase = maxi * 4;
					simplified[insertIdx * 4 + 0] = points[maxiBase + 0];
					simplified[insertIdx * 4 + 1] = points[maxiBase + 1];
					simplified[insertIdx * 4 + 2] = points[maxiBase + 2];
					simplified[insertIdx * 4 + 3] = maxi;
				}
				else
				{
					++i;
				}
			}
		}

		for ( int i = 0; i < simplified.Count / 4; ++i )
		{
			// The edge vertex flag is take from the current raw point,
			// and the neighbour region is take from the next raw point.
			int ai = (simplified[i * 4 + 3] + 1) % pn;
			int bi = simplified[i * 4 + 3];
			simplified[i * 4 + 3] = (points[ai * 4 + 3] & (ContourRegionFlags.CONTOUR_REG_MASK | ContourRegionFlags.AREA_BORDER))
									| points[bi * 4 + 3] & ContourRegionFlags.BORDER_VERTEX;
		}
	}

	private static int CalcAreaOfPolygon2D( Span<int> verts, int nverts )
	{
		int area = 0;
		for ( int i = 0, j = nverts - 1; i < nverts; j = i++ )
		{
			int vi = i * 4;
			int vj = j * 4;
			area += verts[vi + 0] * verts[vj + 2] - verts[vj + 0] * verts[vi + 2];
		}

		return (area + 1) / 2;
	}

	private static bool IntersectSegContour( int d0, int d1, int i, int n, Span<int> verts, Span<int> d0verts, Span<int> d1verts )
	{
		// For each edge (k,k+1) of P
		// Get slices for d0 and d1 vertices once
		var d0Slice = d0verts.Slice( d0, 3 );
		var d1Slice = d1verts.Slice( d1, 3 );

		for ( int k = 0; k < n; k++ )
		{
			int k1 = Utils.Next( k, n );
			// Skip edges incident to i.
			if ( i == k || i == k1 )
				continue;

			var p0Slice = verts.Slice( k * 4, 3 );
			var p1Slice = verts.Slice( k1 * 4, 3 );

			if ( Utils.VEqual2D( d0Slice, p0Slice ) || Utils.VEqual2D( d1Slice, p0Slice ) ||
				Utils.VEqual2D( d0Slice, p1Slice ) || Utils.VEqual2D( d1Slice, p1Slice ) )
				continue;

			if ( Utils.Intersect2D( d0Slice, d1Slice, p0Slice, p1Slice ) )
				return true;
		}

		return false;
	}

	private static bool InCone( int i, int n, Span<int> verts, int pj, Span<int> vertpj )
	{
		// Get slices directly from source arrays to avoid copying
		var piSlice = verts.Slice( i * 4, 3 );
		var pi1Slice = verts.Slice( Utils.Next( i, n ) * 4, 3 );
		var pin1Slice = verts.Slice( Utils.Prev( i, n ) * 4, 3 );
		var pjSlice = vertpj.Slice( pj, 3 );

		// If P[i] is a convex vertex [ i+1 left or on (i-1,i) ].
		if ( Utils.LeftOn2D( pin1Slice, piSlice, pi1Slice ) )
			return Utils.Left2D( piSlice, pjSlice, pin1Slice ) && Utils.Left2D( pjSlice, piSlice, pi1Slice );
		// Assume (i-1,i,i+1) not collinear.
		// else P[i] is reflex.
		return !(Utils.LeftOn2D( piSlice, pjSlice, pi1Slice ) && Utils.LeftOn2D( pjSlice, piSlice, pin1Slice ));
	}

	private static void RemoveDegenerateSegments( List<int> simplified )
	{
		// Remove adjacent vertices which are equal on xz-plane,
		// or else the triangulator will get confused.
		// Use in-place compaction instead of RemoveAt to avoid O(n²) complexity.
		int npts = simplified.Count / 4;
		int writeIdx = 0;

		for ( int i = 0; i < npts; ++i )
		{
			int ni = (i + 1) % npts;
			int iBase = i * 4;
			int niBase = ni * 4;

			// Check if this vertex equals the next (degenerate segment)
			bool isDegenerate = simplified[iBase] == simplified[niBase]
				&& simplified[iBase + 2] == simplified[niBase + 2];

			if ( !isDegenerate )
			{
				// Keep this vertex - copy if needed
				if ( writeIdx != i )
				{
					int writeBase = writeIdx * 4;
					simplified[writeBase] = simplified[iBase];
					simplified[writeBase + 1] = simplified[iBase + 1];
					simplified[writeBase + 2] = simplified[iBase + 2];
					simplified[writeBase + 3] = simplified[iBase + 3];
				}
				writeIdx++;
			}
		}

		// Trim the list to the new size (SetCount directly sets _size, keeps capacity)
		CollectionsMarshal.SetCount( simplified, writeIdx * 4 );
	}

	// Finds the lowest leftmost vertex of a contour.
	private static (int x, int z, int leftmost) FindLeftMostVertex( Contour contour )
	{
		int minx = contour.Vertices[0];
		int minz = contour.Vertices[2];
		int leftmost = 0;
		for ( int i = 1; i < contour.VertexCount; i++ )
		{
			int x = contour.Vertices[i * 4 + 0];
			int z = contour.Vertices[i * 4 + 2];
			if ( x < minx || (x == minx && z < minz) )
			{
				minx = x;
				minz = z;
				leftmost = i;
			}
		}

		return (minx, minz, leftmost);
	}

	private static void MergeRegionHoles( ContourRegion region, Span<ContourHole> regionHoles, List<int> mergeBuffer )
	{
		// Sort holes from left to right.
		for ( int i = 0; i < region.HoleCount; i++ )
		{
			(int minx, int miny, int minleftmost) = FindLeftMostVertex( regionHoles[i].Contour );
			regionHoles[i].MinX = minx;
			regionHoles[i].MinZ = miny;
			regionHoles[i].LeftMost = minleftmost;
		}

		regionHoles.Sort( ContourHoleComparer.Shared );

		int maxVerts = region.Outline.VertexCount;
		for ( int i = 0; i < region.HoleCount; i++ )
			maxVerts += regionHoles[i].Contour.VertexCount;

		using var pooledDiags = new PooledSpan<PotentialDiagonal>( maxVerts );
		Span<PotentialDiagonal> diags = pooledDiags.Span;

		Contour outline = region.Outline;

		// Merge holes into the outline one by one.
		for ( int i = 0; i < region.HoleCount; i++ )
		{
			Contour hole = regionHoles[i].Contour;

			int index = -1;
			int bestVertex = regionHoles[i].LeftMost;
			for ( int iter = 0; iter < hole.VertexCount; iter++ )
			{
				// Find potential diagonals.
				// The 'best' vertex must be in the cone described by 3 consecutive vertices of the outline.
				// ..o j-1
				// |
				// | * best
				// |
				// j o-----o j+1
				// :
				int ndiags = 0;
				int corner = bestVertex * 4;
				for ( int j = 0; j < outline.VertexCount; j++ )
				{
					if ( InCone( j, outline.VertexCount, outline.Vertices, corner, hole.Vertices ) )
					{
						int dx = outline.Vertices[j * 4 + 0] - hole.Vertices[corner + 0];
						int dz = outline.Vertices[j * 4 + 2] - hole.Vertices[corner + 2];
						diags[ndiags].vert = j;
						diags[ndiags].dist = dx * dx + dz * dz;
						ndiags++;
					}
				}

				// Sort potential diagonals by distance, we want to make the connection as short as possible.
				diags.Slice( 0, ndiags ).Sort( PotentialDiagonalComparer.Shared );

				// Find a diagonal that is not intersecting the outline not the remaining holes.
				index = -1;
				for ( int j = 0; j < ndiags; j++ )
				{
					int pt = diags[j].vert * 4;
					bool intersect = IntersectSegContour( pt, corner, diags[j].vert, outline.VertexCount, outline.Vertices,
						outline.Vertices, hole.Vertices );
					for ( int k = i; k < region.HoleCount && !intersect; k++ )
						intersect |= IntersectSegContour( pt, corner, -1, regionHoles[k].Contour.VertexCount,
							regionHoles[k].Contour.Vertices, outline.Vertices, hole.Vertices );
					if ( !intersect )
					{
						index = diags[j].vert;
						break;
					}
				}

				// If found non-intersecting diagonal, stop looking.
				if ( index != -1 )
					break;
				// All the potential diagonals for the current vertex were intersecting, try next vertex.
				bestVertex = (bestVertex + 1) % hole.VertexCount;
			}

			if ( index == -1 )
			{
				Log.Warning( "mergeHoles: Failed to find merge points for" );
				continue;
			}

			Contour.MergeContours( region.Outline, hole, index, bestVertex, mergeBuffer );
		}
	}

	/// @par
	///
	/// The raw contours will match the region outlines exactly. The @p maxError and @p maxEdgeLen
	/// parameters control how closely the simplified contours will match the raw contours.
	///
	/// Simplified contours are generated such that the vertices for portals between areas match up.
	/// (They are considered mandatory vertices.)
	///
	/// Setting @p maxEdgeLength to zero will disabled the edge length feature.
	///
	/// See the #rcConfig documentation for more information on the configuration parameters.
	///
	/// @see rcAllocContourSet, CompactHeightfield, ContourSet, rcConfig
	public static ContourSet BuildContours( CompactHeightfield chf, float maxError, int maxEdgeLen, ContourBuilderContext ctx, int buildFlags = ContourBuildFlags.RC_CONTOUR_TESS_WALL_EDGES )
	{
		int w = chf.Width;
		int h = chf.Height;
		int borderSize = chf.BorderSize;

		ctx.ClearContourSet();

		ctx.ContourSet.BMin = chf.BMin;
		ctx.ContourSet.BMax = chf.BMax;
		if ( borderSize > 0 )
		{
			// If the heightfield was build with bordersize, remove the offset.
			float pad = borderSize * chf.CellSize;
			ctx.ContourSet.BMin.x += pad;
			ctx.ContourSet.BMin.z += pad;
			ctx.ContourSet.BMax.x -= pad;
			ctx.ContourSet.BMax.z -= pad;
		}

		ctx.ContourSet.CellSize = chf.CellSize;
		ctx.ContourSet.CellHeight = chf.CellHeight;
		ctx.ContourSet.Width = chf.Width - chf.BorderSize * 2;
		ctx.ContourSet.Height = chf.Height - chf.BorderSize * 2;
		ctx.ContourSet.BorderSize = chf.BorderSize;
		ctx.ContourSet.MaxError = maxError;

		CollectionsMarshal.SetCount( ctx.Flags, chf.SpanCount );
		Span<byte> flags = CollectionsMarshal.AsSpan( ctx.Flags );

		var spans = chf.Spans;
		var cells = chf.Cells;

		// Mark boundaries.
		for ( int y = 0; y < h; ++y )
		{
			int yOffset = y * w;
			int yOffsetPlus = (y + 1) * w;
			int yOffsetMinus = (y - 1) * w;

			for ( int x = 0; x < w; ++x )
			{
				CompactCell c = cells[x + yOffset];
				for ( int i = c.Index, ni = c.Index + c.Count; i < ni; ++i )
				{
					CompactSpan s = spans[i];
					int region = s.Region;
					if ( region == 0 || (region & ContourRegionFlags.BORDER_REG) != 0 )
					{
						flags[i] = 0;
						continue;
					}

					int res = 0;
					int con0 = Utils.GetCon( s, 0 );
					int con1 = Utils.GetCon( s, 1 );
					int con2 = Utils.GetCon( s, 2 );
					int con3 = Utils.GetCon( s, 3 );

					if ( con0 != Constants.NOT_CONNECTED )
					{
						int ai = cells[(x - 1) + yOffset].Index + con0;
						if ( spans[ai].Region == region )
							res |= 1;
					}
					if ( con1 != Constants.NOT_CONNECTED )
					{
						int ai = cells[x + yOffsetPlus].Index + con1;
						if ( spans[ai].Region == region )
							res |= 2;
					}
					if ( con2 != Constants.NOT_CONNECTED )
					{
						int ai = cells[(x + 1) + yOffset].Index + con2;
						if ( spans[ai].Region == region )
							res |= 4;
					}
					if ( con3 != Constants.NOT_CONNECTED )
					{
						int ai = cells[x + yOffsetMinus].Index + con3;
						if ( spans[ai].Region == region )
							res |= 8;
					}

					flags[i] = (byte)(res ^ 0xf); // Inverse, mark non connected edges.
				}
			}
		}

		for ( int y = 0; y < h; ++y )
		{
			int yOffset = y * w;
			for ( int x = 0; x < w; ++x )
			{
				CompactCell c = cells[x + yOffset];
				for ( int i = c.Index, ni = c.Index + c.Count; i < ni; ++i )
				{
					if ( flags[i] == 0 || flags[i] == 0xf )
					{
						flags[i] = 0;
						continue;
					}

					int reg = spans[i].Region;
					if ( reg == 0 || (reg & ContourRegionFlags.BORDER_REG) != 0 )
						continue;
					int area = chf.Areas[i];

					ctx.Verts.Clear();
					ctx.VertsSimplified.Clear();

					WalkContour( x, y, i, chf, flags, ctx.Verts );
					SimplifyContour( ctx.Verts, ctx.VertsSimplified, maxError, maxEdgeLen, buildFlags );
					RemoveDegenerateSegments( ctx.VertsSimplified );

					// Store region->contour remap info.
					// Create contour.
					if ( ctx.VertsSimplified.Count / 4 >= 3 )
					{
						Contour cont = ctx.RentContour( ctx.VertsSimplified.Count / 4 );
						ctx.ContourSet.Contours.Add( cont );

						CollectionsMarshal.AsSpan( ctx.VertsSimplified ).CopyTo( cont.Vertices );

						if ( borderSize > 0 )
						{
							// If the heightfield was build with bordersize, remove the offset.
							for ( int j = 0; j < cont.VertexCount; ++j )
							{
								cont.Vertices[j * 4] -= borderSize;
								cont.Vertices[j * 4 + 2] -= borderSize;
							}
						}

						cont.Region = reg;
						cont.Area = area;
					}
				}
			}
		}

		// Merge holes if needed.
		if ( ctx.ContourSet.Contours.Count > 0 )
		{
			// Calculate winding of all polygons.
			using var pooledWinding = new PooledSpan<int>( ctx.ContourSet.Contours.Count );
			Span<int> winding = pooledWinding.Span;
			int nholes = 0;
			for ( int i = 0; i < ctx.ContourSet.Contours.Count; ++i )
			{
				Contour cont = ctx.ContourSet.Contours[i];
				// If the contour is wound backwards, it is a hole.
				winding[i] = CalcAreaOfPolygon2D( cont.Vertices, cont.VertexCount ) < 0 ? -1 : 1;
				if ( winding[i] < 0 )
					nholes++;
			}

			if ( nholes > 0 )
			{
				// Collect outline contour and holes contours per region.
				// We assume that there is one outline and multiple holes.
				int nregions = chf.MaxRegions + 1;

				using var pooledRegions = new PooledSpan<ContourRegion>( nregions );
				Span<ContourRegion> regions = pooledRegions.Span;
				regions.Clear();

				using var pooledHoles = new PooledSpan<ContourHole>( nholes );
				Span<ContourHole> holes = pooledHoles.Span;

				for ( int i = 0; i < ctx.ContourSet.Contours.Count; ++i )
				{
					Contour cont = ctx.ContourSet.Contours[i];
					// Positively wound contours are outlines, negative holes.
					if ( winding[i] > 0 )
					{
						if ( regions[cont.Region].Outline != null )
						{
							throw new Exception(
								"rcBuildContours: Multiple outlines for region " + cont.Region + "." );
						}

						regions[cont.Region].Outline = cont;
					}
					else
					{
						regions[cont.Region].HoleCount++;
					}
				}

				var currentHoleIndex = 0;
				for ( int i = 0; i < nregions; i++ )
				{
					if ( regions[i].HoleCount > 0 )
					{
						regions[i].HoleStartIndex = currentHoleIndex;
						currentHoleIndex += regions[i].HoleCount;
						// we increment this again in the next loop
						regions[i].HoleCount = 0; // reuse as write cursor
					}
				}
				Assert.Equals( currentHoleIndex, nholes );

				for ( int i = 0; i < ctx.ContourSet.Contours.Count; ++i )
				{
					Contour cont = ctx.ContourSet.Contours[i];
					if ( winding[i] < 0 )
					{
						ContourRegion reg = regions[cont.Region];
						Assert.True( reg.HoleStartIndex + reg.HoleCount < nholes );
						holes[reg.HoleStartIndex + reg.HoleCount].Contour = cont;
						regions[cont.Region].HoleCount++;
					}
				}

				// Finally merge each regions holes into the outline.
				for ( int i = 0; i < nregions; i++ )
				{
					ContourRegion reg = regions[i];
					if ( reg.HoleCount == 0 )
						continue;

					if ( reg.Outline != null )
					{
						MergeRegionHoles( reg, holes.Slice( reg.HoleStartIndex, reg.HoleCount ), ctx.MergeBuffer );
					}
					else
					{
						// The region does not have an outline.
						// This can happen if the contour becaomes selfoverlapping because of
						// too aggressive simplification settings.
						throw new Exception( "rcBuildContours: Bad outline for region " + i
																						+ ", contour simplification is likely too aggressive." );
					}
				}
			}
		}

		return ctx.ContourSet;
	}
}

