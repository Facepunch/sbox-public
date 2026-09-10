namespace Sandbox.Navigation.Pathfinding
{
	internal static class Geometry
	{
		public static int NextPow2( int v )
		{
			v--;
			v |= v >> 1;
			v |= v >> 2;
			v |= v >> 4;
			v |= v >> 8;
			v |= v >> 16;
			v++;
			return v;
		}

		public static int Ilog2( int v )
		{
			int r;
			int shift;
			r = (v > 0xffff ? 1 : 0) << 4;
			v >>= r;
			shift = (v > 0xff ? 1 : 0) << 3;
			v >>= shift;
			r |= shift;
			shift = (v > 0xf ? 1 : 0) << 2;
			v >>= shift;
			r |= shift;
			shift = (v > 0x3 ? 1 : 0) << 1;
			v >>= shift;
			r |= shift;
			r |= (v >> 1);
			return r;
		}

		/// Determines if two axis-aligned bounding boxes overlap.
		/// @param[in] amin Minimum bounds of box A. [(x, y, z)]
		/// @param[in] amax Maximum bounds of box A. [(x, y, z)]
		/// @param[in] bmin Minimum bounds of box B. [(x, y, z)]
		/// @param[in] bmax Maximum bounds of box B. [(x, y, z)]
		/// @return True if the two AABB's overlap.
		/// @see OverlapBounds
		public static bool OverlapQuantBounds( ref Vector3Int amin, ref Vector3Int amax, ref Vector3Int bmin, ref Vector3Int bmax )
		{
			bool overlap = true;
			overlap = (amin.x > bmax.x || amax.x < bmin.x) ? false : overlap;
			overlap = (amin.y > bmax.y || amax.y < bmin.y) ? false : overlap;
			overlap = (amin.z > bmax.z || amax.z < bmin.z) ? false : overlap;
			return overlap;
		}

		/// Determines if two axis-aligned bounding boxes overlap.
		/// @param[in] amin Minimum bounds of box A. [(x, y, z)]
		/// @param[in] amax Maximum bounds of box A. [(x, y, z)]
		/// @param[in] bmin Minimum bounds of box B. [(x, y, z)]
		/// @param[in] bmax Maximum bounds of box B. [(x, y, z)]
		/// @return True if the two AABB's overlap.
		/// @see OverlapQuantBounds
		public static bool OverlapBounds( Vector3 amin, Vector3 amax, Vector3 bmin, Vector3 bmax )
		{
			bool overlap = true;
			overlap = (amin.x > bmax.x || amax.x < bmin.x) ? false : overlap;
			overlap = (amin.y > bmax.y || amax.y < bmin.y) ? false : overlap;
			overlap = (amin.z > bmax.z || amax.z < bmin.z) ? false : overlap;
			return overlap;
		}

		public static bool OverlapRange( float amin, float amax, float bmin, float bmax, float eps )
		{
			return ((amin + eps) > bmax || (amax - eps) < bmin) ? false : true;
		}

		/// @par
		///
		/// All vertices are projected onto the xz-plane, so the y-values are ignored.
		public static bool OverlapPolyPoly2D( Span<Vector3> polya, Span<Vector3> polyb )
		{
			const float eps = 1e-4f;
			for ( int i = 0, j = polya.Length - 1; i < polya.Length; j = i++ )
			{
				int va = j;
				int vb = i;

				Vector3 n = new Vector3( polya[vb].z - polya[va].z, 0, -(polya[vb].x - polya[va].x) );

				Vector2 aminmax = ProjectPoly( n, polya );
				Vector2 bminmax = ProjectPoly( n, polyb );
				if ( !OverlapRange( aminmax.x, aminmax.y, bminmax.x, bminmax.y, eps ) )
				{
					// Found separating axis
					return false;
				}
			}

			for ( int i = 0, j = polyb.Length - 1; i < polyb.Length; j = i++ )
			{
				int va = j;
				int vb = i;

				Vector3 n = new Vector3( polyb[vb].z - polyb[va].z, 0, -(polyb[vb].x - polyb[va].x) );

				Vector2 aminmax = ProjectPoly( n, polya );
				Vector2 bminmax = ProjectPoly( n, polyb );
				if ( !OverlapRange( aminmax.x, aminmax.y, bminmax.x, bminmax.y, eps ) )
				{
					// Found separating axis
					return false;
				}
			}

			return true;
		}

		/// @}
		/// @name Computational geometry helper functions.
		/// @{
		/// Derives the signed xz-plane area of the triangle ABC, or the
		/// relationship of line AB to point C.
		/// @param[in] a Vertex A. [(x, y, z)]
		/// @param[in] b Vertex B. [(x, y, z)]
		/// @param[in] c Vertex C. [(x, y, z)]
		/// @return The signed xz-plane area of the triangle.
		public static float TriArea2D( Vector3 a, Vector3 b, Vector3 c )
		{
			float abx = b.x - a.x;
			float abz = b.z - a.z;
			float acx = c.x - a.x;
			float acz = c.z - a.z;
			return acx * abz - abx * acz;
		}

		// Returns a random point in a convex polygon.
		// Adapted from Graphics Gems article.
		public static void RandomPointInConvexPoly( Span<Vector3> pts, float s, float t, out Vector3 @out )
		{
			Span<float> areas = stackalloc float[pts.Length];

			// Calc triangle araes
			float areasum = 0.0f;
			for ( int i = 2; i < pts.Length; i++ )
			{
				areas[i] = TriArea2D( pts[0], pts[(i - 1)], pts[i] );
				areasum += Math.Max( 0.001f, areas[i] );
			}

			// Find sub triangle weighted by area.
			float thr = s * areasum;
			float acc = 0.0f;
			float u = 1.0f;
			int tri = pts.Length - 1;
			for ( int i = 2; i < pts.Length; i++ )
			{
				float dacc = areas[i];
				if ( thr >= acc && thr < (acc + dacc) )
				{
					u = (thr - acc) / dacc;
					tri = i;
					break;
				}

				acc += dacc;
			}

			float v = MathF.Sqrt( t );

			float a = 1 - v;
			float b = (1 - u) * v;
			float c = u * v;
			int pa = 0;
			int pb = (tri - 1);
			int pc = tri;

			@out = new Vector3()
			{
				x = a * pts[pa].x + b * pts[pb].x + c * pts[pc].x,
				y = a * pts[pa].y + b * pts[pb].y + c * pts[pc].y,
				z = a * pts[pa].z + b * pts[pb].z + c * pts[pc].z
			};
		}

		public static bool ClosestHeightPointTriangle( Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float h )
		{
			const float EPS = 1e-6f;

			h = 0;
			Vector3 v0 = c - a;
			Vector3 v1 = b - a;
			Vector3 v2 = p - a;

			// Compute scaled barycentric coordinates
			float denom = v0.x * v1.z - v0.z * v1.x;
			if ( MathF.Abs( denom ) < EPS )
			{
				return false;
			}

			float u = v1.z * v2.x - v1.x * v2.z;
			float v = v0.x * v2.z - v0.z * v2.x;

			if ( denom < 0 )
			{
				denom = -denom;
				u = -u;
				v = -v;
			}

			// If point lies inside the triangle, return interpolated ycoord.
			if ( u >= 0.0f && v >= 0.0f && (u + v) <= denom )
			{
				h = a.y + (v0.y * u + v1.y * v) / denom;
				return true;
			}

			return false;
		}

		public static Vector2 ProjectPoly( Vector3 axis, Span<Vector3> poly )
		{
			float rmin, rmax;
			rmin = rmax = Perp2D( axis, poly[0] );
			for ( int i = 1; i < poly.Length; ++i )
			{
				float d = Perp2D( axis, poly[i] );
				rmin = Math.Min( rmin, d );
				rmax = Math.Max( rmax, d );
			}

			return new Vector2
			{
				x = rmin,
				y = rmax,
			};
		}

		/// @par
		///
		/// All points are projected onto the xz-plane, so the y-values are ignored.
		public static bool PointInPolygon( Vector3 pt, Span<Vector3> verts )
		{
			// TODO: Replace pnpoly with triArea2D tests?
			int i, j;
			bool c = false;
			for ( i = 0, j = verts.Length - 1; i < verts.Length; j = i++ )
			{
				int vi = i;
				int vj = j;
				if ( ((verts[vi].z > pt.z) != (verts[vj].z > pt.z)) && (pt.x < (verts[vj].x - verts[vi].x)
						* (pt.z - verts[vi].z) / (verts[vj].z - verts[vi].z) + verts[vi].x) )
				{
					c = !c;
				}
			}

			return c;
		}

		public static bool DistancePtPolyEdgesSqr( Vector3 pt, Span<Vector3> verts, Span<float> ed, Span<float> et )
		{
			// TODO: Replace pnpoly with triArea2D tests?
			int i, j;
			bool c = false;
			for ( i = 0, j = verts.Length - 1; i < verts.Length; j = i++ )
			{
				int vi = i;
				int vj = j;
				if ( ((verts[vi].z > pt.z) != (verts[vj].z > pt.z)) &&
					(pt.x < (verts[vj].x - verts[vi].x) * (pt.z - verts[vi].z) / (verts[vj].z - verts[vi].z) + verts[vi].x) )
				{
					c = !c;
				}

				ed[j] = DistancePtSegSqr2D( pt, verts[vj], verts[vi], out et[j] );
			}

			return c;
		}

		public static float DistancePtSegSqr2D( Vector3 pt, Vector3 p, Vector3 q, out float t )
		{
			float pqx = q.x - p.x;
			float pqz = q.z - p.z;
			float dx = pt.x - p.x;
			float dz = pt.z - p.z;
			float d = pqx * pqx + pqz * pqz;
			t = pqx * dx + pqz * dz;
			if ( d > 0 )
			{
				t /= d;
			}

			if ( t < 0 )
			{
				t = 0;
			}
			else if ( t > 1 )
			{
				t = 1;
			}

			dx = p.x + t * pqx - pt.x;
			dz = p.z + t * pqz - pt.z;
			return dx * dx + dz * dz;
		}

		public static bool IntersectSegmentPoly2D( Vector3 p0, Vector3 p1,
			Span<Vector3> verts,
			out float tmin, out float tmax,
			out int segMin, out int segMax )
		{
			const float EPS = 0.000001f;

			tmin = 0;
			tmax = 1;
			segMin = -1;
			segMax = -1;

			var dir = p1 - p0;

			var p0v = p0;
			for ( int i = 0, j = verts.Length - 1; i < verts.Length; j = i++ )
			{
				Vector3 vpj = verts[j];
				Vector3 vpi = verts[i];
				var edge = vpi - vpj;
				var diff = p0v - vpj;
				float n = Perp2D( edge, diff );
				float d = Perp2D( dir, edge );
				if ( MathF.Abs( d ) < EPS )
				{
					// S is nearly parallel to this edge
					if ( n < 0 )
					{
						return false;
					}
					else
					{
						continue;
					}
				}

				float t = n / d;
				if ( d < 0 )
				{
					// segment S is entering across this edge
					if ( t > tmin )
					{
						tmin = t;
						segMin = j;
						// S enters after leaving polygon
						if ( tmin > tmax )
						{
							return false;
						}
					}
				}
				else
				{
					// segment S is leaving across this edge
					if ( t < tmax )
					{
						tmax = t;
						segMax = j;
						// S leaves before entering polygon
						if ( tmax < tmin )
						{
							return false;
						}
					}
				}
			}

			return true;
		}

		public static int OppositeTile( int side )
		{
			return (side + 4) & 0x7;
		}

		public static bool IntersectSegSeg2D( Vector3 ap, Vector3 aq, Vector3 bp, Vector3 bq, out float s, out float t )
		{
			s = 0;
			t = 0;

			Vector3 u = aq - ap;
			Vector3 v = bq - bp;
			Vector3 w = ap - bp;
			float d = PerpXZ( u, v );
			if ( MathF.Abs( d ) < 1e-6f )
			{
				return false;
			}

			s = PerpXZ( v, w ) / d;
			t = PerpXZ( u, w ) / d;

			return true;
		}

		// TODO same as perp2d
		public static float PerpXZ( Vector3 a, Vector3 b )
		{
			return (a.x * b.z) - (a.z * b.x);
		}

		/// Derives the xz-plane 2D perp product of the two vectors. (uz*vx - ux*vz)
		/// @param[in] u The LHV vector [(x, y, z)]
		/// @param[in] v The RHV vector [(x, y, z)]
		/// @return The dot product on the xz-plane.
		///
		/// The vectors are projected onto the xz-plane, so the y-values are
		/// ignored.
		/// TODO should be called DOT2D
		public static float Perp2D( Vector3 u, Vector3 v )
		{
			return u.z * v.x - u.x * v.z;
		}

		public static float DistanceBetween2DSqr( Vector3 a, Vector3 b )
		{
			float dx = b.x - a.x;
			float dz = b.z - a.z;
			return dx * dx + dz * dz;
		}

		public static float DistanceBetween2D( Vector3 a, Vector3 b )
		{
			return MathF.Sqrt( DistanceBetween2DSqr( a, b ) );
		}

		public static bool IsFinite( float v )
		{
			return !float.IsNaN( v ) && !float.IsInfinity( v );
		}

		public static bool IsFinite( Vector3 v )
		{
			return IsFinite( v.x ) && IsFinite( v.y ) && IsFinite( v.z );
		}

		public static bool IsFinite2D( Vector3 v )
		{
			return IsFinite( v.x ) && IsFinite( v.z );
		}

		public static bool IntersectSegmentTriangle( Vector3 sp, Vector3 sq, Vector3 a, Vector3 b, Vector3 c, out float t )
		{
			t = 0;
			float v, w;
			Vector3 ab = b - a;
			Vector3 ac = c - a;
			Vector3 qp = sp - sq;

			// Compute triangle normal. Can be precalculated or cached if
			// intersecting multiple segments against the same triangle
			Vector3 norm = Vector3.Cross( ab, ac );

			// Compute denominator d. If d <= 0, segment is parallel to or points
			// away from triangle, so exit early
			float d = Vector3.Dot( qp, norm );
			if ( d <= 0.0f )
			{
				return false;
			}

			// Compute intersection t value of pq with plane of triangle. A ray
			// intersects iff 0 <= t. Segment intersects iff 0 <= t <= 1. Delay
			// dividing by d until intersection has been found to pierce triangle
			Vector3 ap = sp - a;
			t = Vector3.Dot( ap, norm );
			if ( t < 0.0f )
			{
				return false;
			}

			if ( t > d )
			{
				return false; // For segment; exclude this code line for a ray test
			}

			// Compute barycentric coordinate components and test if within bounds
			Vector3 e = Vector3.Cross( qp, ap );
			v = Vector3.Dot( ac, e );
			if ( v < 0.0f || v > d )
			{
				return false;
			}

			w = -Vector3.Dot( ab, e );
			if ( w < 0.0f || v + w > d )
			{
				return false;
			}

			// Segment/ray intersects triangle. Perform delayed division
			t /= d;

			return true;
		}

	}
}
