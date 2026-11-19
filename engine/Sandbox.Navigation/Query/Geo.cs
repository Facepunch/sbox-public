using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nav;

/// <summary>
/// Ground-plane geometry. Everything here ignores Z unless it says otherwise.
/// </summary>
internal static class Geo
{
	// Positive when c is to the right of the line from a to b, seen from above.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float TriArea2D( Vector3 a, Vector3 b, Vector3 c ) => (c.X - a.X) * (b.Y - a.Y) - (b.X - a.X) * (c.Y - a.Y);

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float DistanceToSegmentSquared( Vector3 point, Vector3 p, Vector3 q, out float t )
	{
		float pqx = q.X - p.X, pqy = q.Y - p.Y;
		float dx = point.X - p.X, dy = point.Y - p.Y;
		float d = pqx * pqx + pqy * pqy;
		t = pqx * dx + pqy * dy;
		if ( d > 0 ) t /= d;
		t = t < 0 ? 0 : t > 1 ? 1 : t;
		dx = p.X + t * pqx - point.X;
		dy = p.Y + t * pqy - point.Y;
		return dx * dx + dy * dy;
	}

	public static bool PointInPolygon( Vector3 point, ReadOnlySpan<Vector3> verts )
	{
		bool inside = false;
		for ( int i = 0, j = verts.Length - 1; i < verts.Length; j = i++ )
		{
			Vector3 a = verts[i], b = verts[j];
			if ( (a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X ) inside = !inside;
		}
		return inside;
	}

	// The height of the triangle's plane under the point, when the point is over the triangle.
	public static bool HeightOnTriangle( Vector3 point, Vector3 a, Vector3 b, Vector3 c, float epsilon, out float height )
	{
		Vector3 v0 = c - a, v1 = b - a, v2 = point - a;
		float denom = v0.X * v1.Y - v0.Y * v1.X;
		height = 0;
		if ( MathF.Abs( denom ) < epsilon ) return false;

		float u = v1.Y * v2.X - v1.X * v2.Y;
		float v = v0.X * v2.Y - v0.Y * v2.X;
		if ( denom < 0 )
		{
			denom = -denom;
			u = -u;
			v = -v;
		}
		if ( u < 0 || v < 0 || u + v > denom ) return false;
		height = a.Z + (v0.Z * u + v1.Z * v) / denom;
		return true;
	}

	// s and t are the positions of the crossing along a and b. False when the lines are parallel.
	public static bool IntersectSegments( Vector3 ap, Vector3 aq, Vector3 bp, Vector3 bq, float epsilon, out float s, out float t )
	{
		Vector3 u = aq - ap, v = bq - bp, w = ap - bp;
		float d = u.X * v.Y - u.Y * v.X;
		s = t = 0;
		if ( MathF.Abs( d ) < epsilon ) return false;
		s = (v.X * w.Y - v.Y * w.X) / d;
		t = (u.X * w.Y - u.Y * w.X) / d;
		return true;
	}

	// Clips the segment from p0 to p1 against a convex polygon. exitEdge is the edge it leaves through,
	// -1 when it ends inside.
	public static bool IntersectSegmentPolygon( Vector3 p0, Vector3 p1, ReadOnlySpan<Vector3> verts, float epsilon, out float tmin, out float tmax, out int exitEdge )
	{
		tmin = 0;
		tmax = 1;
		exitEdge = -1;
		var dir = p1 - p0;
		for ( int i = 0, j = verts.Length - 1; i < verts.Length; j = i++ )
		{
			var edge = verts[i] - verts[j];
			var diff = p0 - verts[j];
			float n = edge.Y * diff.X - edge.X * diff.Y;
			float d = dir.Y * edge.X - dir.X * edge.Y;
			if ( MathF.Abs( d ) < epsilon )
			{
				if ( n < 0 ) return false;
				continue;
			}
			float t = n / d;
			if ( d < 0 )
			{
				if ( t > tmin )
				{
					tmin = t;
					if ( tmin > tmax ) return false;
				}
			}
			else if ( t < tmax )
			{
				tmax = t;
				exitEdge = j;
				if ( tmax < tmin ) return false;
			}
		}
		return true;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool AlmostEqual( Vector3 a, Vector3 b, float epsilon )
	{
		var d = Vector3.Abs( a - b );
		return d.X <= epsilon && d.Y <= epsilon && d.Z <= epsilon;
	}

	public static bool IsFinite( Vector3 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y ) && float.IsFinite( v.Z );
}
