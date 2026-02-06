using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Navigation.Generation;

[SkipHotload]
internal static class PolyMeshBuilder
{
	private const int RC_MULTIPLE_REGS = 0;
	private const int VERTEX_BUCKET_COUNT = (1 << 12);

	[SkipHotload]
	private struct Edge
	{
		public ushort Vert0;
		public ushort Vert1;

		public ushort PolyEdge0;
		public ushort PolyEdge1;

		public ushort Poly0;
		public ushort Poly1;
	}

	private const int EDGE_BUCKET_COUNT = 256;

	/// <summary>
	/// Edge hash map using parallel arrays with separate chaining.
	/// Enables O(1) lookup of polygon edges for merge candidate search.
	/// </summary>
	[SkipHotload]
	public sealed class PolyMeshBuilderContext
	{
		public List<int> Buckets = new( EDGE_BUCKET_COUNT );
		public List<int> PolyIndices = new( 256 );
		public List<int> EdgeIndices = new( 256 );
		public List<int> V0s = new( 256 );
		public List<int> V1s = new( 256 );
		public List<int> Nexts = new( 256 );

		public void Build( Span<ushort> polys, int npolys, int maxVerts )
		{
			int maxEdges = npolys * maxVerts;

			CollectionsMarshal.SetCount( Buckets, EDGE_BUCKET_COUNT );
			CollectionsMarshal.SetCount( PolyIndices, maxEdges );
			CollectionsMarshal.SetCount( EdgeIndices, maxEdges );
			CollectionsMarshal.SetCount( V0s, maxEdges );
			CollectionsMarshal.SetCount( V1s, maxEdges );
			CollectionsMarshal.SetCount( Nexts, maxEdges );

			CollectionsMarshal.AsSpan( Buckets ).Fill( -1 );
			int count = 0;

			var buckets = CollectionsMarshal.AsSpan( Buckets );
			var polyIndices = CollectionsMarshal.AsSpan( PolyIndices );
			var edgeIndices = CollectionsMarshal.AsSpan( EdgeIndices );
			var v0s = CollectionsMarshal.AsSpan( V0s );
			var v1s = CollectionsMarshal.AsSpan( V1s );
			var nexts = CollectionsMarshal.AsSpan( Nexts );

			for ( int p = 0; p < npolys; ++p )
			{
				var poly = polys.Slice( p * maxVerts, maxVerts );
				int nv = CountPolyVerts( poly );
				for ( int e = 0; e < nv; ++e )
				{
					ushort ev0 = poly[e], ev1 = poly[(e + 1) % nv];
					if ( ev0 > ev1 ) (ev0, ev1) = (ev1, ev0);

					int bucket = ((ev0 * 31 + ev1) & 0x7FFFFFFF) % EDGE_BUCKET_COUNT;
					polyIndices[count] = p;
					edgeIndices[count] = e;
					v0s[count] = ev0;
					v1s[count] = ev1;
					nexts[count] = buckets[bucket];
					buckets[bucket] = count++;
				}
			}
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public int GetBucket( ushort v0, ushort v1 )
		{
			if ( v0 > v1 ) (v0, v1) = (v1, v0);
			return Buckets[((v0 * 31 + v1) & 0x7FFFFFFF) % EDGE_BUCKET_COUNT];
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public bool TryGet( int idx, ushort ev0, ushort ev1, out int poly, out int edge, out int next )
		{
			next = Nexts[idx];
			if ( V0s[idx] != ev0 || V1s[idx] != ev1 )
			{
				poly = edge = 0;
				return false;
			}
			poly = PolyIndices[idx];
			edge = EdgeIndices[idx];
			return true;
		}
	}

	/// <summary>
	/// Builds a polygon mesh from a contour set.
	/// </summary>
	public static PolyMesh BuildPolyMesh( ContourSet cset, int maxVertsPerPoly, PolyMeshBuilderContext ctx )
	{
		int maxVertices = 0;
		int maxTris = 0;
		int maxVertsPerCont = 0;

		for ( int i = 0; i < cset.Contours.Count; ++i )
		{
			// Skip null contours.
			Contour cont = cset.Contours[i];
			if ( cont.VertexCount < 3 ) continue;
			maxVertices += cont.VertexCount;
			maxTris += cont.VertexCount - 2;
			maxVertsPerCont = Math.Max( maxVertsPerCont, cont.VertexCount );
		}

		if ( maxVertices >= 0xfffe )
		{
			Log.Error( $"rcBuildPolyMesh: Too many vertices {maxVertices}." );
			return null;
		}

		using var pooledVflags = new PooledSpan<byte>( maxVertices );
		var vflags = pooledVflags.Span;
		vflags.Clear();

#pragma warning disable CA2000 // Dispose objects before losing scope
		// Mesh is returned by this function caller takes ownership
		var mesh = PolyMesh.GetPooled();
#pragma warning restore CA2000 // Dispose objects before losing scope
		mesh.Init( cset, maxVertsPerPoly, maxTris, maxVertices );

		using var pooledRegionIds = new PooledSpan<int>( maxTris );
		var regionIds = pooledRegionIds.Span;
		regionIds.Clear();

		using var pooledNextVert = new PooledSpan<int>( maxVertices );
		var nextVert = pooledNextVert.Span;
		nextVert.Clear();

		using var pooledFirstVert = new PooledSpan<int>( VERTEX_BUCKET_COUNT );
		var firstVert = pooledFirstVert.Span;
		firstVert.Fill( -1 );

		using var pooledIndices = new PooledSpan<int>( maxVertsPerCont );
		var indices = pooledIndices.Span;
		indices.Clear();

		using var pooledTris = new PooledSpan<int>( maxVertsPerCont * 3 );
		var tris = pooledTris.Span;
		tris.Clear();

		using var pooledPolys = new PooledSpan<ushort>( (maxVertsPerCont + 1) * maxVertsPerPoly );
		var polys = pooledPolys.Span;
		polys.Clear();

		Span<ushort> pj = stackalloc ushort[maxVertsPerPoly];
		Span<ushort> pa = stackalloc ushort[maxVertsPerPoly];
		Span<ushort> pb = stackalloc ushort[maxVertsPerPoly];
		Span<ushort> tmpPoly = stackalloc ushort[maxVertsPerPoly];

		for ( int i = 0; i < cset.Contours.Count; ++i )
		{
			Contour cont = cset.Contours[i];

			// Skip null contours.
			if ( cont.VertexCount < 3 )
				continue;

			// Triangulate contour
			for ( int j = 0; j < cont.VertexCount; ++j )
				indices[j] = j;

			int ntris = Triangulate( cont.VertexCount, cont.Vertices, indices, tris );
			if ( ntris <= 0 )
			{
				// Bad triangulation, should not happen.
				Log.Warning( $"rcBuildPolyMesh: Bad triangulation Contour {i}." );
				ntris = -ntris;
			}

			// Add and merge vertices.
			for ( int j = 0; j < cont.VertexCount; ++j )
			{
				int vIndex = j * 4;
				int v0 = cont.Vertices[vIndex + 0];
				int v1 = cont.Vertices[vIndex + 1];
				int v2 = cont.Vertices[vIndex + 2];
				var newVertexCount = mesh.VertCount;
				indices[j] = AddVertex( (ushort)v0, (ushort)v1, (ushort)v2, mesh.Verts, firstVert, nextVert, ref newVertexCount );
				mesh.VertCount = newVertexCount;

				if ( (cont.Vertices[vIndex + 3] & ContourRegionFlags.BORDER_VERTEX) != 0 )
				{
					// This vertex should be removed.
					vflags[indices[j]] = 1;
				}
			}

			// Build initial polygons.
			int npolys = 0;
			polys.Fill( Constants.MESH_NULL_IDX );

			for ( int j = 0; j < ntris; ++j )
			{
				int t0 = tris[j * 3 + 0];
				int t1 = tris[j * 3 + 1];
				int t2 = tris[j * 3 + 2];

				if ( t0 != t1 && t0 != t2 && t1 != t2 )
				{
					polys[npolys * maxVertsPerPoly + 0] = (ushort)indices[t0];
					polys[npolys * maxVertsPerPoly + 1] = (ushort)indices[t1];
					polys[npolys * maxVertsPerPoly + 2] = (ushort)indices[t2];
					npolys++;
				}
			}

			if ( npolys == 0 )
				continue;

			// Merge triangles into larger convex polygons.
			if ( maxVertsPerPoly > 3 )
			{
				while ( TryFindBestMerge( polys, npolys, maxVertsPerPoly, mesh.Verts, ctx,
					out int a, out int b, out int ea, out int eb ) )
				{
					polys.Slice( a * maxVertsPerPoly, maxVertsPerPoly ).CopyTo( pa );
					polys.Slice( b * maxVertsPerPoly, maxVertsPerPoly ).CopyTo( pb );
					MergePolyVerts( pa, pb, ea, eb, tmpPoly );

					pa.CopyTo( polys.Slice( a * maxVertsPerPoly, maxVertsPerPoly ) );
					polys.Slice( (npolys - 1) * maxVertsPerPoly, maxVertsPerPoly )
						.CopyTo( polys.Slice( b * maxVertsPerPoly, maxVertsPerPoly ) );
					npolys--;
				}
			}

			// Store polygons.
			for ( int j = 0; j < npolys; ++j )
			{
				pj.Fill( Constants.MESH_NULL_IDX );

				for ( int k = 0; k < maxVertsPerPoly; ++k )
					pj[k] = polys[j * maxVertsPerPoly + k];

				pj.CopyTo( mesh.Polys.Slice( mesh.PolyCount * maxVertsPerPoly * 2, maxVertsPerPoly * 2 ) );

				regionIds[mesh.PolyCount] = cont.Region;
				mesh.Areas[mesh.PolyCount] = cont.Area;
				mesh.PolyCount++;
			}
		}

		// Remove edge vertices.
		for ( int i = 0; i < mesh.VertCount; ++i )
		{
			if ( vflags[i] != 0 )
			{
				if ( !CanRemoveVertex( mesh, (ushort)i ) )
					continue;

				if ( !RemoveVertex( mesh, regionIds, (ushort)i, mesh.MaxPolys, ctx ) )
				{
					// Failed to remove vertex
					Log.Error( $"rcBuildPolyMesh: Failed to remove edge vertex {i}." );
					return null;
				}

				// Remove vertex
				// Note: mesh.VertCount is already decremented inside RemoveVertex()!
				// Fixup vertex flags
				for ( int j = i; j < mesh.VertCount; ++j )
					vflags[j] = vflags[j + 1];
				--i;
			}
		}

		// Calculate adjacency.
		if ( !BuildMeshAdjacency( mesh.Polys, mesh.PolyCount, mesh.VertCount, mesh.MaxVertsPerPoly ) )
		{
			Log.Error( "rcBuildPolyMesh: Adjacency failed." );
			return null;
		}

		Span<ushort> p = stackalloc ushort[maxVertsPerPoly * 2];
		Span<ushort> va = stackalloc ushort[3];
		Span<ushort> vb = stackalloc ushort[3];

		// Find portal edges
		if ( mesh.BorderSize > 0 )
		{
			int w = cset.Width;
			int h = cset.Height;
			for ( int i = 0; i < mesh.PolyCount; ++i )
			{
				int polyStart = i * 2 * maxVertsPerPoly;
				mesh.Polys.Slice( polyStart, 2 * maxVertsPerPoly ).CopyTo( p );

				for ( int j = 0; j < maxVertsPerPoly; ++j )
				{
					if ( p[j] == Constants.MESH_NULL_IDX ) break;
					// Skip connected edges.
					if ( p[maxVertsPerPoly + j] != Constants.MESH_NULL_IDX )
						continue;

					int nj = j + 1;
					if ( nj >= maxVertsPerPoly || p[nj] == Constants.MESH_NULL_IDX ) nj = 0;

					mesh.Verts.Slice( p[j] * 3, 3 ).CopyTo( va );
					mesh.Verts.Slice( p[nj] * 3, 3 ).CopyTo( vb );

					if ( (int)va[0] == 0 && (int)vb[0] == 0 )
						p[maxVertsPerPoly + j] = 0x8000 | 0;
					else if ( (int)va[2] == h && (int)vb[2] == h )
						p[maxVertsPerPoly + j] = 0x8000 | 1;
					else if ( (int)va[0] == w && (int)vb[0] == w )
						p[maxVertsPerPoly + j] = 0x8000 | 2;
					else if ( (int)va[2] == 0 && (int)vb[2] == 0 )
						p[maxVertsPerPoly + j] = 0x8000 | 3;

					// Update the original array
					mesh.Polys[polyStart + maxVertsPerPoly + j] = p[maxVertsPerPoly + j];
				}
			}
		}

		if ( mesh.VertCount > 0xffff )
		{
			Log.Error( $"rcBuildPolyMesh: The resulting mesh has too many vertices {mesh.VertCount} (max {0xffff}). Data can be corrupted." );
		}

		if ( mesh.PolyCount > 0xffff )
		{
			Log.Error( $"rcBuildPolyMesh: The resulting mesh has too many polygons {mesh.PolyCount} (max {0xffff}). Data can be corrupted." );
		}

		return mesh;
	}

	private static bool BuildMeshAdjacency( Span<ushort> polys, int npolys, int nverts, int vertsPerPoly )
	{
		// Based on code by Eric Lengyel from:
		// https://web.archive.org/web/20080704083314/http://www.terathon.com/code/edges.php

		int maxEdgeCount = npolys * vertsPerPoly;
		using var pooledEdgeRelations = new PooledSpan<ushort>( nverts + maxEdgeCount );
		var edgeRelations = pooledEdgeRelations.Span;

		Span<ushort> firstEdge = edgeRelations.Slice( 0, nverts );
		firstEdge.Fill( Constants.MESH_NULL_IDX );

		Span<ushort> nextEdge = edgeRelations.Slice( nverts, maxEdgeCount );
		int edgeCount = 0;

		using var pooledEdges = new PooledSpan<Edge>( maxEdgeCount );
		var edges = pooledEdges.Span;
		edges.Clear();


		Span<ushort> t = stackalloc ushort[vertsPerPoly * 2];
		// Create edges for each polygon
		for ( int i = 0; i < npolys; ++i )
		{
			polys.Slice( i * vertsPerPoly * 2, vertsPerPoly * 2 ).CopyTo( t );

			for ( int j = 0; j < vertsPerPoly; ++j )
			{
				if ( t[j] == Constants.MESH_NULL_IDX ) break;
				ushort v0 = t[j];
				ushort v1 = (j + 1 >= vertsPerPoly || t[j + 1] == Constants.MESH_NULL_IDX) ? t[0] : t[j + 1];
				if ( v0 < v1 )
				{
					Edge edge = edges[edgeCount];
					edge.Vert0 = v0;
					edge.Vert1 = v1;
					edge.Poly0 = (ushort)i;
					edge.PolyEdge0 = (ushort)j;
					edge.Poly1 = (ushort)i;
					edge.PolyEdge1 = 0;

					// Insert edge
					nextEdge[edgeCount] = firstEdge[v0];
					firstEdge[v0] = (ushort)edgeCount;

					edges[edgeCount] = edge;
					edgeCount++;
				}
			}
		}

		// Connect matching edges
		for ( int i = 0; i < npolys; ++i )
		{
			polys.Slice( i * vertsPerPoly * 2, vertsPerPoly * 2 ).CopyTo( t );

			for ( int j = 0; j < vertsPerPoly; ++j )
			{
				if ( t[j] == Constants.MESH_NULL_IDX ) break;
				ushort v0 = t[j];
				ushort v1 = (j + 1 >= vertsPerPoly || t[j + 1] == Constants.MESH_NULL_IDX) ? t[0] : t[j + 1];
				if ( v0 > v1 )
				{
					for ( ushort e = firstEdge[v1]; e != Constants.MESH_NULL_IDX; e = nextEdge[e] )
					{
						Edge edge = edges[e];
						if ( edge.Vert1 == v0 && edge.Poly0 == edge.Poly1 )
						{
							edge.Poly1 = (ushort)i;
							edge.PolyEdge1 = (ushort)j;
							edges[e] = edge;
							break;
						}
					}
				}
			}
		}

		// Store adjacency
		for ( int i = 0; i < edgeCount; ++i )
		{
			Edge e = edges[i];
			if ( e.Poly0 != e.Poly1 )
			{
				int p0Idx = e.Poly0 * vertsPerPoly * 2;
				int p1Idx = e.Poly1 * vertsPerPoly * 2;

				polys[p0Idx + vertsPerPoly + e.PolyEdge0] = e.Poly1;
				polys[p1Idx + vertsPerPoly + e.PolyEdge1] = e.Poly0;
			}
		}

		return true;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static int ComputeVertexHash( int x, int y, int z )
	{
		const uint h1 = 0x8da6b343; // Large multiplicative constants;
		const uint h2 = 0xd8163841; // here arbitrarily chosen primes
		const uint h3 = 0xcb1ab31f;
		uint n = h1 * (uint)x + h2 * (uint)y + h3 * (uint)z;
		return (int)(n & (VERTEX_BUCKET_COUNT - 1));
	}

	private static ushort AddVertex( ushort x, ushort y, ushort z, Span<ushort> verts, Span<int> firstVert, Span<int> nextVert, ref int nv )
	{
		int bucket = ComputeVertexHash( x, 0, z );
		int i = firstVert[bucket];

		Span<ushort> v = stackalloc ushort[3];
		while ( i != -1 )
		{
			verts.Slice( i * 3, 3 ).CopyTo( v );

			if ( v[0] == x && (Math.Abs( v[1] - y ) <= 2) && v[2] == z )
				return (ushort)i;

			i = nextVert[i]; // next
		}

		// Could not find, create new.
		i = nv++;
		Span<ushort> newV = [x, y, z];
		newV.CopyTo( verts.Slice( i * 3, 3 ) );

		nextVert[i] = firstVert[bucket];
		firstVert[bucket] = i;

		return (ushort)i;
	}

	private static int Triangulate( int n, Span<int> verts, Span<int> indices, Span<int> tris )
	{
		int ntris = 0;

		// The last bit of the index is used to indicate if the vertex can be removed.
		for ( int i = 0; i < n; i++ )
		{
			int i1 = Utils.Next( i, n );
			int i2 = Utils.Next( i1, n );
			if ( Utils.Diagonal( i, i2, n, verts, indices ) )
			{
				indices[i1] |= int.MinValue; // 0x80000000;
			}
		}

		while ( n > 3 )
		{
			int minLen = -1;
			int mini = -1;
			for ( int minIdx = 0; minIdx < n; minIdx++ )
			{
				int nextIdx1 = Utils.Next( minIdx, n );
				if ( (indices[nextIdx1] & 0x80000000) != 0 )
				{
					int p0 = (indices[minIdx] & 0x0fffffff) * 4;
					int p2 = (indices[Utils.Next( nextIdx1, n )] & 0x0fffffff) * 4;

					int dx = verts[p2 + 0] - verts[p0 + 0];
					int dy = verts[p2 + 2] - verts[p0 + 2];
					int len = dx * dx + dy * dy;

					if ( minLen < 0 || len < minLen )
					{
						minLen = len;
						mini = minIdx;
					}
				}
			}

			if ( mini == -1 )
			{
				// We might get here because the contour has overlapping segments, like this:
				//
				// A o-o=====o---o B
				// / |C D| \
				// o o o o
				// : : : :
				// We'll try to recover by loosing up the inCone test a bit so that a diagonal
				// like A-B or C-D can be found and we can continue.
				minLen = -1;
				mini = -1;
				for ( int minIdx = 0; minIdx < n; minIdx++ )
				{
					int nextIdx1 = Utils.Next( minIdx, n );
					int nextIdx2 = Utils.Next( nextIdx1, n );
					if ( Utils.DiagonalLoose( minIdx, nextIdx2, n, verts, indices ) )
					{
						int p0 = (indices[minIdx] & 0x0fffffff) * 4;
						int p2 = (indices[Utils.Next( nextIdx2, n )] & 0x0fffffff) * 4;
						int dx = verts[p2 + 0] - verts[p0 + 0];
						int dy = verts[p2 + 2] - verts[p0 + 2];
						int len = dx * dx + dy * dy;

						if ( minLen < 0 || len < minLen )
						{
							minLen = len;
							mini = minIdx;
						}
					}
				}

				if ( mini == -1 )
				{
					// The contour is messed up. This sometimes happens
					// if the contour simplification is too aggressive.
					return -ntris;
				}
			}

			int i0 = mini;
			int i1 = Utils.Next( i0, n );
			int i2 = Utils.Next( i1, n );

			tris[ntris * 3] = indices[i0] & 0x0fffffff;
			tris[ntris * 3 + 1] = indices[i1] & 0x0fffffff;
			tris[ntris * 3 + 2] = indices[i2] & 0x0fffffff;
			ntris++;

			// Removes P[i1] by copying P[i+1]...P[n-1] left one index.
			n--;
			for ( int k = i1; k < n; k++ )
				indices[k] = indices[k + 1];

			if ( i1 >= n ) i1 = 0;
			i0 = Utils.Prev( i1, n );
			// Update diagonal flags.
			if ( Utils.Diagonal( Utils.Prev( i0, n ), i1, n, verts, indices ) )
				indices[i0] |= int.MinValue; // 0x80000000;
			else
				indices[i0] &= 0x0fffffff;

			if ( Utils.Diagonal( i0, Utils.Next( i1, n ), n, verts, indices ) )
				indices[i1] |= int.MinValue; // 0x80000000;
			else
				indices[i1] &= 0x0fffffff;
		}

		// Append the remaining triangle.
		tris[ntris * 3] = indices[0] & 0x0fffffff;
		tris[ntris * 3 + 1] = indices[1] & 0x0fffffff;
		tris[ntris * 3 + 2] = indices[2] & 0x0fffffff;
		ntris++;

		return ntris;
	}

	private static int CountPolyVerts( ReadOnlySpan<ushort> p )
	{
		for ( int i = 0; i < p.Length; ++i )
			if ( p[i] == Constants.MESH_NULL_IDX )
				return i;
		return p.Length;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool ULeft( ReadOnlySpan<ushort> verts, int a, int b, int c )
	{
		int ax = verts[a * 3], az = verts[a * 3 + 2];
		int bx = verts[b * 3], bz = verts[b * 3 + 2];
		int cx = verts[c * 3], cz = verts[c * 3 + 2];
		return (bx - ax) * (cz - az) - (cx - ax) * (bz - az) < 0;
	}

	/// <summary>
	/// Finds the best pair of polygons to merge (longest shared edge that preserves convexity).
	/// </summary>
	private static bool TryFindBestMerge( Span<ushort> polys, int npolys, int maxVerts,
		ReadOnlySpan<ushort> verts, PolyMeshBuilderContext ctx,
		out int bestPa, out int bestPb, out int bestEa, out int bestEb )
	{
		ctx.Build( polys, npolys, maxVerts );

		int bestLen = 0;
		bestPa = bestPb = bestEa = bestEb = 0;

		for ( int p = 0; p < npolys; ++p )
		{
			var polyP = polys.Slice( p * maxVerts, maxVerts );
			int nvP = CountPolyVerts( polyP );

			for ( int e = 0; e < nvP; ++e )
			{
				ushort ev0 = polyP[e], ev1 = polyP[(e + 1) % nvP];
				if ( ev0 > ev1 ) (ev0, ev1) = (ev1, ev0);

				for ( int idx = ctx.GetBucket( ev0, ev1 ); idx != -1; )
				{
					if ( !ctx.TryGet( idx, ev0, ev1, out int q, out int eq, out idx ) )
						continue;
					if ( q <= p ) continue;

					var polyQ = polys.Slice( q * maxVerts, maxVerts );
					int nvQ = CountPolyVerts( polyQ );

					if ( nvP + nvQ - 2 > maxVerts ) continue;
					if ( !CanMerge( polyP, nvP, e, polyQ, nvQ, eq, verts ) ) continue;

					int len = EdgeLengthSq( polyP, e, nvP, verts );
					if ( len > bestLen )
					{
						bestLen = len;
						bestPa = p;
						bestPb = q;
						bestEa = e;
						bestEb = eq;
					}
				}
			}
		}

		return bestLen > 0;
	}

	/// <summary>
	/// Checks if two polygons can be merged along a shared edge while preserving convexity.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool CanMerge( ReadOnlySpan<ushort> polyA, int nvA, int edgeA,
								  ReadOnlySpan<ushort> polyB, int nvB, int edgeB,
								  ReadOnlySpan<ushort> verts )
	{
		return ULeft( verts, polyA[(edgeA + nvA - 1) % nvA], polyA[edgeA], polyB[(edgeB + 2) % nvB] )
			&& ULeft( verts, polyB[(edgeB + nvB - 1) % nvB], polyB[edgeB], polyA[(edgeA + 2) % nvA] );
	}

	/// <summary>
	/// Computes the squared length of an edge (used for merge priority).
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static int EdgeLengthSq( ReadOnlySpan<ushort> poly, int edge, int nv, ReadOnlySpan<ushort> verts )
	{
		int v0 = poly[edge] * 3, v1 = poly[(edge + 1) % nv] * 3;
		int dx = verts[v0] - verts[v1], dz = verts[v0 + 2] - verts[v1 + 2];
		return dx * dx + dz * dz;
	}

	private static void MergePolyVerts( Span<ushort> pa, ReadOnlySpan<ushort> pb, int ea, int eb, Span<ushort> tmp )
	{
		int na = CountPolyVerts( pa );
		int nb = CountPolyVerts( pb );

		// Merge polygons.
		tmp.Fill( 0xffff );
		int n = 0;
		// Add pa
		for ( int i = 0; i < na - 1; ++i )
			tmp[n++] = pa[(ea + 1 + i) % na];
		// Add pb
		for ( int i = 0; i < nb - 1; ++i )
			tmp[n++] = pb[(eb + 1 + i) % nb];

		tmp.CopyTo( pa );
	}

	private static void PushFront( int v, Span<int> arr, ref int an )
	{
		an++;
		for ( int i = an - 1; i > 0; --i ) arr[i] = arr[i - 1];
		arr[0] = v;
	}

	private static void PushBack( int v, Span<int> arr, ref int an )
	{
		arr[an] = v;
		an++;
	}

	private static bool CanRemoveVertex( PolyMesh mesh, ushort rem )
	{
		int nvp = mesh.MaxVertsPerPoly;

		// Count number of polygons to remove.
		int numTouchedVerts = 0;
		int numRemainingEdges = 0;

		for ( int i = 0; i < mesh.PolyCount; ++i )
		{
			Span<ushort> p = mesh.Polys.Slice( i * nvp * 2, nvp * 2 );
			int nv = CountPolyVerts( p );
			int numRemoved = 0;
			int numVerts = 0;

			for ( int j = 0; j < nv; ++j )
			{
				if ( p[j] == rem )
				{
					numTouchedVerts++;
					numRemoved++;
				}
				numVerts++;
			}

			if ( numRemoved > 0 )
			{
				numRemainingEdges += numVerts - (numRemoved + 1);
			}
		}

		// There would be too few edges remaining to create a polygon.
		// This can happen for example when a tip of a triangle is marked
		// as deletion, but there are no other polys that share the vertex.
		// In this case, the vertex should not be removed.
		if ( numRemainingEdges <= 2 )
			return false;

		// Find edges which share the removed vertex.
		int maxEdges = numTouchedVerts * 2;
		int nedges = 0;

		using var pooledEdges = new PooledSpan<int>( maxEdges * 3 );
		var edges = pooledEdges.Span;

		for ( int i = 0; i < mesh.PolyCount; ++i )
		{
			Span<ushort> p = mesh.Polys.Slice( i * nvp * 2, nvp * 2 );
			int nv = CountPolyVerts( p );

			// Collect edges which touches the removed vertex.
			for ( int j = 0, k = nv - 1; j < nv; k = j++ )
			{
				if ( p[j] == rem || p[k] == rem )
				{
					// Arrange edge so that a=rem.
					int a = p[j], b = p[k];
					if ( b == rem ) (a, b) = (b, a);

					// Check if the edge exists
					bool exists = false;
					for ( int m = 0; m < nedges; ++m )
					{
						int eIndex = m * 3;
						if ( edges[eIndex + 1] == b )
						{
							// Exists, increment vertex share count.
							edges[eIndex + 2]++;
							exists = true;
						}
					}

					// Add new edge.
					if ( !exists )
					{
						int eIndex = nedges * 3;
						edges[eIndex + 0] = a;
						edges[eIndex + 1] = b;
						edges[eIndex + 2] = 1;
						nedges++;
					}
				}
			}
		}

		// There should be no more than 2 open edges.
		// This catches the case that two non-adjacent polygons
		// share the removed vertex. In that case, do not remove the vertex.
		int numOpenEdges = 0;
		for ( int i = 0; i < nedges; ++i )
		{
			if ( edges[i * 3 + 2] < 2 )
				numOpenEdges++;
		}

		return numOpenEdges <= 2;
	}

	private static bool RemoveVertex( PolyMesh mesh, Span<int> regionIds, ushort rem, int maxTris, PolyMeshBuilderContext ctx )
	{
		int nvp = mesh.MaxVertsPerPoly;

		// Count number of polygons to remove.
		int numRemovedVerts = 0;
		for ( int i = 0; i < mesh.PolyCount; ++i )
		{
			Span<ushort> p1 = mesh.Polys.Slice( i * nvp * 2, nvp * 2 );
			int nv = CountPolyVerts( p1 );
			for ( int j = 0; j < nv; ++j )
			{
				if ( p1[j] == rem )
					numRemovedVerts++;
			}
		}

		int nedges = 0;
		using var pooledEdges = new PooledSpan<int>( numRemovedVerts * nvp * 4 );
		var edges = pooledEdges.Span;

		int nhole = 0;
		using var pooledHole = new PooledSpan<int>( numRemovedVerts * nvp );
		var hole = pooledHole.Span;

		int nhreg = 0;
		using var pooledHreg = new PooledSpan<int>( numRemovedVerts * nvp );
		var hreg = pooledHreg.Span;

		int nharea = 0;
		using var pooledHarea = new PooledSpan<int>( numRemovedVerts * nvp );
		var harea = pooledHarea.Span;

		for ( int i = 0; i < mesh.PolyCount; ++i )
		{
			Span<ushort> p2 = mesh.Polys.Slice( i * nvp * 2, nvp );
			int nv = CountPolyVerts( p2 );
			bool hasRem = false;
			for ( int j = 0; j < nv; ++j )
				if ( p2[j] == rem ) hasRem = true;

			if ( hasRem )
			{
				// Collect edges which does not touch the removed vertex.
				for ( int j = 0, k = nv - 1; j < nv; k = j++ )
				{
					if ( p2[j] != rem && p2[k] != rem )
					{
						int eIndex = nedges * 4;
						edges[eIndex + 0] = p2[k];
						edges[eIndex + 1] = p2[j];
						edges[eIndex + 2] = regionIds[i];
						edges[eIndex + 3] = mesh.Areas[i];
						nedges++;
					}
				}

				// Remove the polygon.
				int lastPolyIdx = (mesh.PolyCount - 1) * nvp * 2;
				if ( i * nvp * 2 != lastPolyIdx )
				{
					mesh.Polys.Slice( lastPolyIdx, nvp * 2 ).CopyTo( mesh.Polys.Slice( i * nvp * 2, nvp * 2 ) );
				}

				// Clear the last half (adjacency info)
				mesh.Polys.Slice( (i * nvp * 2) + nvp, nvp ).Fill( Constants.MESH_NULL_IDX );

				regionIds[i] = regionIds[mesh.PolyCount - 1];
				mesh.Areas[i] = mesh.Areas[mesh.PolyCount - 1];
				mesh.PolyCount--;
				--i;
			}
		}

		// Remove vertex.
		for ( int i = (int)rem; i < mesh.VertCount - 1; ++i )
		{
			mesh.Verts[i * 3 + 0] = mesh.Verts[(i + 1) * 3 + 0];
			mesh.Verts[i * 3 + 1] = mesh.Verts[(i + 1) * 3 + 1];
			mesh.Verts[i * 3 + 2] = mesh.Verts[(i + 1) * 3 + 2];
		}
		mesh.VertCount--;

		// Adjust indices to match the removed vertex layout.
		for ( int i = 0; i < mesh.PolyCount; ++i )
		{
			Span<ushort> p3 = mesh.Polys.Slice( i * nvp * 2, nvp * 2 );
			int nv = CountPolyVerts( p3 );
			for ( int j = 0; j < nv; ++j )
				if ( p3[j] > rem ) p3[j]--;
		}

		for ( int i = 0; i < nedges; ++i )
		{
			if ( edges[i * 4 + 0] > rem ) edges[i * 4 + 0]--;
			if ( edges[i * 4 + 1] > rem ) edges[i * 4 + 1]--;
		}

		if ( nedges == 0 )
		{
			return true;
		}

		// Start with one vertex, keep appending connected
		// segments to the start and end of the hole.
		PushBack( edges[0], hole, ref nhole );
		PushBack( edges[2], hreg, ref nhreg );
		PushBack( edges[3], harea, ref nharea );

		while ( nedges > 0 )
		{
			bool match = false;

			for ( int i = 0; i < nedges; ++i )
			{
				int ea = edges[i * 4 + 0];
				int eb = edges[i * 4 + 1];
				int r = edges[i * 4 + 2];
				int a = edges[i * 4 + 3];
				bool add = false;

				if ( hole[0] == eb )
				{
					// The segment matches the beginning of the hole boundary.
					PushFront( ea, hole, ref nhole );
					PushFront( r, hreg, ref nhreg );
					PushFront( a, harea, ref nharea );
					add = true;
				}
				else if ( hole[nhole - 1] == ea )
				{
					// The segment matches the end of the hole boundary.
					PushBack( eb, hole, ref nhole );
					PushBack( r, hreg, ref nhreg );
					PushBack( a, harea, ref nharea );
					add = true;
				}

				if ( add )
				{
					// The edge segment was added, remove it.
					edges[i * 4 + 0] = edges[(nedges - 1) * 4 + 0];
					edges[i * 4 + 1] = edges[(nedges - 1) * 4 + 1];
					edges[i * 4 + 2] = edges[(nedges - 1) * 4 + 2];
					edges[i * 4 + 3] = edges[(nedges - 1) * 4 + 3];
					--nedges;
					match = true;
					--i;
				}
			}

			if ( !match )
				break;
		}

		using var pooledTris = new PooledSpan<int>( nhole * 3 );
		var tris = pooledTris.Span;

		using var pooledTverts = new PooledSpan<int>( nhole * 4 );
		var tverts = pooledTverts.Span;

		using var pooledThole = new PooledSpan<int>( nhole );
		var thole = pooledThole.Span;

		// Generate temp vertex array for triangulation.
		for ( int i = 0; i < nhole; ++i )
		{
			int pi = hole[i];
			tverts[i * 4 + 0] = mesh.Verts[pi * 3 + 0];
			tverts[i * 4 + 1] = mesh.Verts[pi * 3 + 1];
			tverts[i * 4 + 2] = mesh.Verts[pi * 3 + 2];
			tverts[i * 4 + 3] = 0;
			thole[i] = i;
		}

		// Triangulate the hole.
		int ntris = Triangulate( nhole, tverts, thole, tris );
		if ( ntris < 0 )
		{
			ntris = -ntris;
			Log.Warning( "removeVertex: triangulate() returned bad results." );
		}

		// Merge the hole triangles back to polygons.
		using var pooledPolys = new PooledSpan<ushort>( (ntris + 1) * nvp );
		var polys = pooledPolys.Span;

		using var pooledPregs = new PooledSpan<ushort>( ntris );
		var pregs = pooledPregs.Span;

		using var pooledPareas = new PooledSpan<int>( ntris );
		var pareas = pooledPareas.Span;

		Span<ushort> tmpPoly = stackalloc ushort[nvp];

		// Build initial polygons.
		int npolys = 0;
		polys.Fill( Constants.MESH_NULL_IDX );

		Span<int> t = stackalloc int[3];

		for ( int j = 0; j < ntris; ++j )
		{
			tris.Slice( j * 3, 3 ).CopyTo( t );

			if ( t[0] != t[1] && t[0] != t[2] && t[1] != t[2] )
			{
				polys[npolys * nvp + 0] = (ushort)hole[t[0]];
				polys[npolys * nvp + 1] = (ushort)hole[t[1]];
				polys[npolys * nvp + 2] = (ushort)hole[t[2]];

				// If this polygon covers multiple region types then
				// mark it as such
				if ( hreg[t[0]] != hreg[t[1]] || hreg[t[1]] != hreg[t[2]] )
					pregs[npolys] = RC_MULTIPLE_REGS;
				else
					pregs[npolys] = (ushort)hreg[t[0]];

				pareas[npolys] = harea[t[0]];
				npolys++;
			}
		}

		if ( npolys == 0 )
		{
			return true;
		}

		Span<ushort> pa = stackalloc ushort[nvp];
		Span<ushort> pb = stackalloc ushort[nvp];

		// Merge polygons using shared edge lookup.
		if ( nvp > 3 )
		{
			while ( TryFindBestMerge( polys, npolys, nvp, mesh.Verts, ctx,
				out int a, out int b, out int ea, out int eb ) )
			{
				polys.Slice( a * nvp, nvp ).CopyTo( pa );
				polys.Slice( b * nvp, nvp ).CopyTo( pb );
				MergePolyVerts( pa, pb, ea, eb, tmpPoly );

				if ( pregs[a] != pregs[b] )
					pregs[a] = RC_MULTIPLE_REGS;

				pa.CopyTo( polys.Slice( a * nvp, nvp ) );
				polys.Slice( (npolys - 1) * nvp, nvp ).CopyTo( polys.Slice( b * nvp, nvp ) );
				pregs[b] = pregs[npolys - 1];
				pareas[b] = pareas[npolys - 1];
				npolys--;
			}
		}

		Span<ushort> p = stackalloc ushort[nvp * 2];

		// Store polygons.
		for ( int i = 0; i < npolys; ++i )
		{
			if ( mesh.PolyCount >= maxTris ) break;
			p.Fill( Constants.MESH_NULL_IDX );

			for ( int j = 0; j < nvp; ++j )
				p[j] = polys[i * nvp + j];

			p.CopyTo( mesh.Polys.Slice( mesh.PolyCount * nvp * 2, nvp * 2 ) );
			regionIds[mesh.PolyCount] = pregs[i];
			mesh.Areas[mesh.PolyCount] = pareas[i];
			mesh.PolyCount++;

			if ( mesh.PolyCount > maxTris )
			{
				Log.Error( $"removeVertex: Too many polygons {mesh.PolyCount} (max:{maxTris})." );
				return false;
			}
		}

		return true;
	}
}
