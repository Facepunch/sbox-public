/*
Copyright (c) 2011 Ole Kniemeyer, MAXON, www.maxon.net

This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it freely,
subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.*/
// Managed adaptation of Bullet's divide-and-conquer convex hull. Edge traversal order
// breaks ties when Hammer reduces a hull to its final vertices.
using System;

namespace Editor.MapEditor;

internal static partial class ConvexDecomposition
{
	sealed class Boundary
	{
		record struct Vec( long x, long y, long z )
		{
			public static Vec operator -( Vec a, Vec b ) => new( a.x - b.x, a.y - b.y, a.z - b.z );
			public Vec cross( Vec b ) => new( y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x );
			public long dot( Vec b ) => x * b.x + y * b.y + z * b.z;
		}

		class Vertex( Vec value, Point position )
		{
			public Vec point = value;
			public Point output = position;
			public Vertex next, prev;
			public Edge edges;
			public static implicit operator bool( Vertex v ) => v is not null;
		}

		class Edge
		{
			public Edge next, prev, reverse;
			public Vertex target;
			public int copy;
			public static implicit operator bool( Edge e ) => e is not null;
			public void link( Edge e )
			{
				next = e;
				e.prev = this;
			}
		}

		struct IntermediateHull
		{
			public Vertex minXy, maxXy, minYx, maxYx;
		}

		readonly record struct Ratio( long n, long d )
		{
			public bool isNaN() => n == 0 && d == 0;
			public bool isNegativeInfinity() => d == 0 && n < 0;
			public int compare( Ratio b )
			{
				if ( d == 0 || b.d == 0 )
					return (d == 0 ? Math.Sign( n ) : 0).CompareTo( b.d == 0 ? Math.Sign( b.n ) : 0 );
				return ((Int128)n * b.d).CompareTo( (Int128)b.n * d ) * Math.Sign( d ) * Math.Sign( b.d );
			}
		}

		const int COUNTER_CLOCKWISE = 1, CLOCKWISE = -1, NONE = 0;
		int mergeStamp;
		Vertex[] originalVertices = [];
		// Reuse the scratch topology across clusters of the same input mesh.
		readonly List<Edge> edgePairs = [];
		int usedPairs;
		Edge availablePair;
		Edge newEdgePair( Vertex a, Vertex b )
		{
			Edge edge;
			if ( availablePair != null )
			{
				edge = availablePair;
				availablePair = edge.next;
			}
			else
			{
				if ( usedPairs == edgePairs.Count )
				{
					var first = new Edge();
					first.reverse = new Edge
					{
						reverse = first
					};
					edgePairs.Add( first );
				}

				edge = edgePairs[usedPairs++];
			}

			edge.next = edge.prev = edge.reverse.next = edge.reverse.prev = null;
			edge.target = b;
			edge.reverse.target = a;
			edge.copy = edge.reverse.copy = mergeStamp;
			return edge;
		}

		void removeEdgePair( Edge edge )
		{
			Remove( edge );
			Remove( edge.reverse );
			edge.next = availablePair;
			availablePair = edge;
			static void Remove( Edge a )
			{
				if ( a.next == a )
					a.reverse.target.edges = null;
				else
				{
					a.prev.link( a.next );
					a.reverse.target.edges = a.next;
				}
			}
		}

		public List<Point> Compute( IReadOnlyCollection<Point> input )
		{
			usedPairs = 0;
			availablePair = null;
			mergeStamp = 0;
			var lo = input.Aggregate( Point.Min );
			var hi = input.Aggregate( Point.Max );
			lo = new Point( (float)lo.X, (float)lo.Y, (float)lo.Z );
			hi = new Point( (float)hi.X, (float)hi.Y, (float)hi.Z );
			float[] size = [(float)(hi.X - lo.X), (float)(hi.Y - lo.Y), (float)(hi.Z - lo.Z)];
			float[] center = [(float)(hi.X + lo.X) * 0.5f, (float)(hi.Y + lo.Y) * 0.5f, (float)(hi.Z + lo.Z) * 0.5f];
			int max = size[0] > size[1] ? (size[0] > size[2] ? 0 : 2) : (size[1] > size[2] ? 1 : 2);
			int min = size[0] < size[1] ? (size[0] < size[2] ? 0 : 2) : (size[1] < size[2] ? 1 : 2);
			if ( min == max )
				min = (max + 1) % 3;
			int med = 3 - max - min;
			for ( int i = 0; i < 3; i++ )
				size[i] *= ((med + 1) % 3 == max ? 1f : -1f) / 10216f;
			long Encode( float value, int axis ) => size[axis] == 0 ? 0 : (long)((value - center[axis]) * (1f / size[axis]));
			if ( input.Count > originalVertices.Length )
				Array.Resize( ref originalVertices, input.Count );
			Span<long> coordinates = stackalloc long[3];
			int count = 0;
			foreach ( var point in input )
			{
				coordinates[0] = Encode( (float)point.X, 0 );
				coordinates[1] = Encode( (float)point.Y, 1 );
				coordinates[2] = Encode( (float)point.Z, 2 );
				var position = new Point( coordinates[0] * size[0] + center[0], coordinates[1] * size[1] + center[1], coordinates[2] * size[2] + center[2] );
				var vertex = originalVertices[count] ??= new Vertex( default, default );
				vertex.point = new Vec( coordinates[med], coordinates[max], coordinates[min] );
				vertex.output = position;
				vertex.next = vertex.prev = null;
				vertex.edges = null;
				count++;
			}

			Array.Sort( originalVertices, 0, count, Comparer<Vertex>.Create( static ( a, b ) =>
			{
				int order = a.point.y.CompareTo( b.point.y );
				if ( order == 0 )
					order = a.point.x.CompareTo( b.point.x );
				return order != 0 ? order : a.point.z.CompareTo( b.point.z );
			} ) );
			IntermediateHull h = default;
			computeInternal( 0, count, ref h );
			var list = new List<Vertex>
			{
				h.minXy
			};
			var seen = new HashSet<Vertex>( list );
			for ( int i = 0; i < list.Count; i++ )
			{
				var head = list[i].edges;
				var e = head;
				if ( e == null )
					continue;
				do
				{
					if ( seen.Add( e.target ) )
						list.Add( e.target );
					e = e.next;
				}
				while ( e != head );
			}

			return list.Select( v => v.output ).ToList();
		}

		bool mergeProjection( ref IntermediateHull h0, ref IntermediateHull h1, ref Vertex c0, ref Vertex c1 )
		{
			Vertex v0 = h0.maxYx;
			Vertex v1 = h1.minYx;
			if ( (v0.point.x == v1.point.x) && (v0.point.y == v1.point.y) )
			{
				Vertex v1p = v1.prev;
				if ( v1p == v1 )
				{
					c0 = v0;
					if ( v1.edges )
					{
						v1 = v1.edges.target;
					}

					c1 = v1;
					return false;
				}

				Vertex v1n = v1.next;
				v1p.next = v1n;
				v1n.prev = v1p;
				if ( v1 == h1.minXy )
				{
					if ( (v1n.point.x < v1p.point.x) || ((v1n.point.x == v1p.point.x) && (v1n.point.y < v1p.point.y)) )
					{
						h1.minXy = v1n;
					}
					else
					{
						h1.minXy = v1p;
					}
				}

				if ( v1 == h1.maxXy )
				{
					if ( (v1n.point.x > v1p.point.x) || ((v1n.point.x == v1p.point.x) && (v1n.point.y > v1p.point.y)) )
					{
						h1.maxXy = v1n;
					}
					else
					{
						h1.maxXy = v1p;
					}
				}
			}

			v0 = h0.maxXy;
			v1 = h1.maxXy;
			Vertex v00 = null;
			Vertex v10 = null;
			long sign = 1;
			for ( int side = 0; side <= 1; side++ )
			{
				long dx = (v1.point.x - v0.point.x) * sign;
				if ( dx > 0 )
				{
					while ( true )
					{
						long dy = v1.point.y - v0.point.y;
						Vertex w0 = side != 0 ? v0.next : v0.prev;
						if ( w0 != v0 )
						{
							long dx0 = (w0.point.x - v0.point.x) * sign;
							long dy0 = w0.point.y - v0.point.y;
							if ( (dy0 <= 0) && ((dx0 == 0) || ((dx0 < 0) && (dy0 * dx <= dy * dx0))) )
							{
								v0 = w0;
								dx = (v1.point.x - v0.point.x) * sign;
								continue;
							}
						}

						Vertex w1 = side != 0 ? v1.next : v1.prev;
						if ( w1 != v1 )
						{
							long dx1 = (w1.point.x - v1.point.x) * sign;
							long dy1 = w1.point.y - v1.point.y;
							long dxn = (w1.point.x - v0.point.x) * sign;
							if ( (dxn > 0) && (dy1 < 0) && ((dx1 == 0) || ((dx1 < 0) && (dy1 * dx < dy * dx1))) )
							{
								v1 = w1;
								dx = dxn;
								continue;
							}
						}

						break;
					}
				}
				else if ( dx < 0 )
				{
					while ( true )
					{
						long dy = v1.point.y - v0.point.y;
						Vertex w1 = side != 0 ? v1.prev : v1.next;
						if ( w1 != v1 )
						{
							long dx1 = (w1.point.x - v1.point.x) * sign;
							long dy1 = w1.point.y - v1.point.y;
							if ( (dy1 >= 0) && ((dx1 == 0) || ((dx1 < 0) && (dy1 * dx <= dy * dx1))) )
							{
								v1 = w1;
								dx = (v1.point.x - v0.point.x) * sign;
								continue;
							}
						}

						Vertex w0 = side != 0 ? v0.prev : v0.next;
						if ( w0 != v0 )
						{
							long dx0 = (w0.point.x - v0.point.x) * sign;
							long dy0 = w0.point.y - v0.point.y;
							long dxn = (v1.point.x - w0.point.x) * sign;
							if ( (dxn < 0) && (dy0 > 0) && ((dx0 == 0) || ((dx0 < 0) && (dy0 * dx < dy * dx0))) )
							{
								v0 = w0;
								dx = dxn;
								continue;
							}
						}

						break;
					}
				}
				else
				{
					long x = v0.point.x;
					long y0 = v0.point.y;
					Vertex w0 = v0;
					Vertex t;
					while ( ((t = side != 0 ? w0.next : w0.prev) != v0) && (t.point.x == x) && (t.point.y <= y0) )
					{
						w0 = t;
						y0 = t.point.y;
					}

					v0 = w0;
					long y1 = v1.point.y;
					Vertex w1 = v1;
					while ( ((t = side != 0 ? w1.prev : w1.next) != v1) && (t.point.x == x) && (t.point.y >= y1) )
					{
						w1 = t;
						y1 = t.point.y;
					}

					v1 = w1;
				}

				if ( side == 0 )
				{
					v00 = v0;
					v10 = v1;
					v0 = h0.minXy;
					v1 = h1.minXy;
					sign = -1;
				}
			}

			v0.prev = v1;
			v1.next = v0;
			v00.next = v10;
			v10.prev = v00;
			if ( h1.minXy.point.x < h0.minXy.point.x )
			{
				h0.minXy = h1.minXy;
			}

			if ( h1.maxXy.point.x >= h0.maxXy.point.x )
			{
				h0.maxXy = h1.maxXy;
			}

			h0.maxYx = h1.maxYx;
			c0 = v00;
			c1 = v10;
			return true;
		}

		void computeInternal( int start, int end, ref IntermediateHull result )
		{
			int n = end - start;
			switch ( n )
			{
				case 0:
					result.minXy = null;
					result.maxXy = null;
					result.minYx = null;
					result.maxYx = null;
					return;
				case 2:
					{
						Vertex v = originalVertices[start];
						Vertex w = originalVertices[start + 1];
						if ( v.point != w.point )
						{
							long dx = v.point.x - w.point.x;
							long dy = v.point.y - w.point.y;
							if ( (dx == 0) && (dy == 0) )
							{
								if ( v.point.z > w.point.z )
								{
									Vertex t = w;
									w = v;
									v = t;
								}

								v.next = v;
								v.prev = v;
								result.minXy = v;
								result.maxXy = v;
								result.minYx = v;
								result.maxYx = v;
							}
							else
							{
								v.next = w;
								v.prev = w;
								w.next = v;
								w.prev = v;
								if ( (dx < 0) || ((dx == 0) && (dy < 0)) )
								{
									result.minXy = v;
									result.maxXy = w;
								}
								else
								{
									result.minXy = w;
									result.maxXy = v;
								}

								if ( (dy < 0) || ((dy == 0) && (dx < 0)) )
								{
									result.minYx = v;
									result.maxYx = w;
								}
								else
								{
									result.minYx = w;
									result.maxYx = v;
								}
							}

							Edge e = newEdgePair( v, w );
							e.link( e );
							v.edges = e;
							e = e.reverse;
							e.link( e );
							w.edges = e;
							return;
						}
					}

					goto case 1;
				case 1:
					{
						Vertex v = originalVertices[start];
						v.edges = null;
						v.next = v;
						v.prev = v;
						result.minXy = v;
						result.maxXy = v;
						result.minYx = v;
						result.maxYx = v;
						return;
					}
			}

			int split0 = start + n / 2;
			Vec p = originalVertices[split0 - 1].point;
			int split1 = split0;
			while ( (split1 < end) && (originalVertices[split1].point == p) )
			{
				split1++;
			}

			computeInternal( start, split0, ref result );
			IntermediateHull hull1 = default;
			computeInternal( split1, end, ref hull1 );
			merge( ref result, ref hull1 );
		}

		int getOrientation( Edge prev, Edge next, Vec s, Vec t )
		{
			if ( prev.next == next )
			{
				if ( prev.prev == next )
				{
					Vec n = t.cross( s );
					Vec m = (prev.target.point - next.reverse.target.point).cross( next.target.point - next.reverse.target.point );
					long dot = n.dot( m );
					return (dot > 0) ? COUNTER_CLOCKWISE : CLOCKWISE;
				}

				return COUNTER_CLOCKWISE;
			}
			else if ( prev.prev == next )
			{
				return CLOCKWISE;
			}
			else
			{
				return NONE;
			}
		}

		Edge findMaxAngle( bool ccw, Vertex start, Vec s, Vec rxs, Vec sxrxs, ref Ratio minCot )
		{
			Edge minEdge = null;
			Edge e = start.edges;
			if ( e )
			{
				do
				{
					if ( e.copy > mergeStamp )
					{
						Vec t = e.target.point - start.point;
						Ratio cot = new( t.dot( sxrxs ), t.dot( rxs ) );
						if ( cot.isNaN() )
						{
						}
						else
						{
							int cmp;
							if ( minEdge == null )
							{
								minCot = cot;
								minEdge = e;
							}
							else if ( (cmp = cot.compare( minCot )) < 0 )
							{
								minCot = cot;
								minEdge = e;
							}
							else if ( (cmp == 0) && (ccw == (getOrientation( minEdge, e, s, t ) == COUNTER_CLOCKWISE)) )
							{
								minEdge = e;
							}
						}
					}

					e = e.next;
				}
				while ( e != start.edges );
			}

			return minEdge;
		}

		void findEdgeForCoplanarFaces( Vertex c0, Vertex c1, ref Edge e0, ref Edge e1, Vertex stop0, Vertex stop1 )
		{
			Edge start0 = e0;
			Edge start1 = e1;
			Vec et0 = start0 ? start0.target.point : c0.point;
			Vec et1 = start1 ? start1.target.point : c1.point;
			Vec s = c1.point - c0.point;
			Vec normal = ((start0 ? start0 : start1).target.point - c0.point).cross( s );
			long dist = c0.point.dot( normal );
			Vec perp = s.cross( normal );
			long maxDot0 = et0.dot( perp );
			if ( e0 )
			{
				while ( e0.target != stop0 )
				{
					Edge e = e0.reverse.prev;
					if ( e.target.point.dot( normal ) < dist )
					{
						break;
					}

					if ( e.copy == mergeStamp )
					{
						break;
					}

					long dot = e.target.point.dot( perp );
					if ( dot <= maxDot0 )
					{
						break;
					}

					maxDot0 = dot;
					e0 = e;
					et0 = e.target.point;
				}
			}

			long maxDot1 = et1.dot( perp );
			if ( e1 )
			{
				while ( e1.target != stop1 )
				{
					Edge e = e1.reverse.next;
					if ( e.target.point.dot( normal ) < dist )
					{
						break;
					}

					if ( e.copy == mergeStamp )
					{
						break;
					}

					long dot = e.target.point.dot( perp );
					if ( dot <= maxDot1 )
					{
						break;
					}

					maxDot1 = dot;
					e1 = e;
					et1 = e.target.point;
				}
			}

			long dx = maxDot1 - maxDot0;
			if ( dx > 0 )
			{
				while ( true )
				{
					long dy = (et1 - et0).dot( s );
					if ( e0 && (e0.target != stop0) )
					{
						Edge f0 = e0.next.reverse;
						if ( f0.copy > mergeStamp )
						{
							long dx0 = (f0.target.point - et0).dot( perp );
							long dy0 = (f0.target.point - et0).dot( s );
							if ( (dx0 == 0) ? (dy0 < 0) : ((dx0 < 0) && (new Ratio( dy0, dx0 ).compare( new Ratio( dy, dx ) ) >= 0)) )
							{
								et0 = f0.target.point;
								dx = (et1 - et0).dot( perp );
								e0 = (e0 == start0) ? null : f0;
								continue;
							}
						}
					}

					if ( e1 && (e1.target != stop1) )
					{
						Edge f1 = e1.reverse.next;
						if ( f1.copy > mergeStamp )
						{
							Vec d1 = f1.target.point - et1;
							if ( d1.dot( normal ) == 0 )
							{
								long dx1 = d1.dot( perp );
								long dy1 = d1.dot( s );
								long dxn = (f1.target.point - et0).dot( perp );
								if ( (dxn > 0) && ((dx1 == 0) ? (dy1 < 0) : ((dx1 < 0) && (new Ratio( dy1, dx1 ).compare( new Ratio( dy, dx ) ) > 0))) )
								{
									e1 = f1;
									et1 = e1.target.point;
									dx = dxn;
									continue;
								}
							}
							else
							{
							}
						}
					}

					break;
				}
			}
			else if ( dx < 0 )
			{
				while ( true )
				{
					long dy = (et1 - et0).dot( s );
					if ( e1 && (e1.target != stop1) )
					{
						Edge f1 = e1.prev.reverse;
						if ( f1.copy > mergeStamp )
						{
							long dx1 = (f1.target.point - et1).dot( perp );
							long dy1 = (f1.target.point - et1).dot( s );
							if ( (dx1 == 0) ? (dy1 > 0) : ((dx1 < 0) && (new Ratio( dy1, dx1 ).compare( new Ratio( dy, dx ) ) <= 0)) )
							{
								et1 = f1.target.point;
								dx = (et1 - et0).dot( perp );
								e1 = (e1 == start1) ? null : f1;
								continue;
							}
						}
					}

					if ( e0 && (e0.target != stop0) )
					{
						Edge f0 = e0.reverse.prev;
						if ( f0.copy > mergeStamp )
						{
							Vec d0 = f0.target.point - et0;
							if ( d0.dot( normal ) == 0 )
							{
								long dx0 = d0.dot( perp );
								long dy0 = d0.dot( s );
								long dxn = (et1 - f0.target.point).dot( perp );
								if ( (dxn < 0) && ((dx0 == 0) ? (dy0 > 0) : ((dx0 < 0) && (new Ratio( dy0, dx0 ).compare( new Ratio( dy, dx ) ) < 0))) )
								{
									e0 = f0;
									et0 = e0.target.point;
									dx = dxn;
									continue;
								}
							}
							else
							{
							}
						}
					}

					break;
				}
			}
		}

		void merge( ref IntermediateHull h0, ref IntermediateHull h1 )
		{
			if ( !h1.maxXy )
			{
				return;
			}

			if ( !h0.maxXy )
			{
				h0 = h1;
				return;
			}

			mergeStamp--;
			Vertex c0 = null;
			Edge toPrev0 = null;
			Edge firstNew0 = null;
			Edge pendingHead0 = null;
			Edge pendingTail0 = null;
			Vertex c1 = null;
			Edge toPrev1 = null;
			Edge firstNew1 = null;
			Edge pendingHead1 = null;
			Edge pendingTail1 = null;
			Vec prevPoint;
			if ( mergeProjection( ref h0, ref h1, ref c0, ref c1 ) )
			{
				Vec s = c1.point - c0.point;
				Vec normal = new Vec( 0, 0, -1 ).cross( s );
				Vec t = s.cross( normal );
				Edge e = c0.edges;
				Edge start0 = null;
				if ( e )
				{
					do
					{
						long dot = (e.target.point - c0.point).dot( normal );
						if ( (dot == 0) && ((e.target.point - c0.point).dot( t ) > 0) )
						{
							if ( !start0 || (getOrientation( start0, e, s, new Vec( 0, 0, -1 ) ) == CLOCKWISE) )
							{
								start0 = e;
							}
						}

						e = e.next;
					}
					while ( e != c0.edges );
				}

				e = c1.edges;
				Edge start1 = null;
				if ( e )
				{
					do
					{
						long dot = (e.target.point - c1.point).dot( normal );
						if ( (dot == 0) && ((e.target.point - c1.point).dot( t ) > 0) )
						{
							if ( !start1 || (getOrientation( start1, e, s, new Vec( 0, 0, -1 ) ) == COUNTER_CLOCKWISE) )
							{
								start1 = e;
							}
						}

						e = e.next;
					}
					while ( e != c1.edges );
				}

				if ( start0 || start1 )
				{
					findEdgeForCoplanarFaces( c0, c1, ref start0, ref start1, null, null );
					if ( start0 )
					{
						c0 = start0.target;
					}

					if ( start1 )
					{
						c1 = start1.target;
					}
				}

				prevPoint = c1.point;
				prevPoint.z++;
			}
			else
			{
				prevPoint = c1.point;
				prevPoint.x++;
			}

			Vertex first0 = c0;
			Vertex first1 = c1;
			bool firstRun = true;
			while ( true )
			{
				Vec s = c1.point - c0.point;
				Vec r = prevPoint - c0.point;
				Vec rxs = r.cross( s );
				Vec sxrxs = s.cross( rxs );
				Ratio minCot0 = new( 0, 0 );
				Edge min0 = findMaxAngle( false, c0, s, rxs, sxrxs, ref minCot0 );
				Ratio minCot1 = new( 0, 0 );
				Edge min1 = findMaxAngle( true, c1, s, rxs, sxrxs, ref minCot1 );
				if ( !min0 && !min1 )
				{
					Edge e = newEdgePair( c0, c1 );
					e.link( e );
					c0.edges = e;
					e = e.reverse;
					e.link( e );
					c1.edges = e;
					return;
				}
				else
				{
					int cmp = !min0 ? 1 : !min1 ? -1 : minCot0.compare( minCot1 );
					if ( firstRun || ((cmp >= 0) ? !minCot1.isNegativeInfinity() : !minCot0.isNegativeInfinity()) )
					{
						Edge e = newEdgePair( c0, c1 );
						if ( pendingTail0 )
						{
							pendingTail0.prev = e;
						}
						else
						{
							pendingHead0 = e;
						}

						e.next = pendingTail0;
						pendingTail0 = e;
						e = e.reverse;
						if ( pendingTail1 )
						{
							pendingTail1.next = e;
						}
						else
						{
							pendingHead1 = e;
						}

						e.prev = pendingTail1;
						pendingTail1 = e;
					}

					Edge e0 = min0;
					Edge e1 = min1;
					if ( cmp == 0 )
					{
						findEdgeForCoplanarFaces( c0, c1, ref e0, ref e1, null, null );
					}

					if ( (cmp >= 0) && e1 )
					{
						if ( toPrev1 )
						{
							for ( Edge e = toPrev1.next, n = null; e != min1; e = n )
							{
								n = e.next;
								removeEdgePair( e );
							}
						}

						if ( pendingTail1 )
						{
							if ( toPrev1 )
							{
								toPrev1.link( pendingHead1 );
							}
							else
							{
								min1.prev.link( pendingHead1 );
								firstNew1 = pendingHead1;
							}

							pendingTail1.link( min1 );
							pendingHead1 = null;
							pendingTail1 = null;
						}
						else if ( !toPrev1 )
						{
							firstNew1 = min1;
						}

						prevPoint = c1.point;
						c1 = e1.target;
						toPrev1 = e1.reverse;
					}

					if ( (cmp <= 0) && e0 )
					{
						if ( toPrev0 )
						{
							for ( Edge e = toPrev0.prev, n = null; e != min0; e = n )
							{
								n = e.prev;
								removeEdgePair( e );
							}
						}

						if ( pendingTail0 )
						{
							if ( toPrev0 )
							{
								pendingHead0.link( toPrev0 );
							}
							else
							{
								pendingHead0.link( min0.next );
								firstNew0 = pendingHead0;
							}

							min0.link( pendingTail0 );
							pendingHead0 = null;
							pendingTail0 = null;
						}
						else if ( !toPrev0 )
						{
							firstNew0 = min0;
						}

						prevPoint = c0.point;
						c0 = e0.target;
						toPrev0 = e0.reverse;
					}
				}

				if ( (c0 == first0) && (c1 == first1) )
				{
					if ( toPrev0 == null )
					{
						pendingHead0.link( pendingTail0 );
						c0.edges = pendingTail0;
					}
					else
					{
						for ( Edge e = toPrev0.prev, n = null; e != firstNew0; e = n )
						{
							n = e.prev;
							removeEdgePair( e );
						}

						if ( pendingTail0 )
						{
							pendingHead0.link( toPrev0 );
							firstNew0.link( pendingTail0 );
						}
					}

					if ( toPrev1 == null )
					{
						pendingTail1.link( pendingHead1 );
						c1.edges = pendingTail1;
					}
					else
					{
						for ( Edge e = toPrev1.next, n = null; e != firstNew1; e = n )
						{
							n = e.next;
							removeEdgePair( e );
						}

						if ( pendingTail1 )
						{
							toPrev1.link( pendingHead1 );
							pendingTail1.link( firstNew1 );
						}
					}

					return;
				}

				firstRun = false;
			}
		}
	}
}
