namespace Nav.Build;

/// <summary>
/// Exact predicates on integer grid points. Positive area means c is to the right of a to b.
/// </summary>
internal static class GridGeometry
{
	public static int Area( int ax, int ay, int bx, int by, int cx, int cy ) => (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);

	public static bool Left( int ax, int ay, int bx, int by, int cx, int cy ) => Area( ax, ay, bx, by, cx, cy ) < 0;

	public static bool LeftOn( int ax, int ay, int bx, int by, int cx, int cy ) => Area( ax, ay, bx, by, cx, cy ) <= 0;

	static bool Collinear( int ax, int ay, int bx, int by, int cx, int cy ) => Area( ax, ay, bx, by, cx, cy ) == 0;

	/// <summary>
	/// The segments cross at a point inside both.
	/// </summary>
	public static bool IntersectProper( int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy )
	{
		if ( Collinear( ax, ay, bx, by, cx, cy ) || Collinear( ax, ay, bx, by, dx, dy ) || Collinear( cx, cy, dx, dy, ax, ay ) || Collinear( cx, cy, dx, dy, bx, by ) )
			return false;

		return (Left( ax, ay, bx, by, cx, cy ) != Left( ax, ay, bx, by, dx, dy )) && (Left( cx, cy, dx, dy, ax, ay ) != Left( cx, cy, dx, dy, bx, by ));
	}

	static bool Between( int ax, int ay, int bx, int by, int cx, int cy )
	{
		if ( !Collinear( ax, ay, bx, by, cx, cy ) ) return false;
		if ( ax != bx ) return (ax <= cx && cx <= bx) || (ax >= cx && cx >= bx);
		return (ay <= cy && cy <= by) || (ay >= cy && cy >= by);
	}

	/// <summary>
	/// The segments share any point, ends included.
	/// </summary>
	public static bool Intersect( int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy )
	{
		if ( IntersectProper( ax, ay, bx, by, cx, cy, dx, dy ) ) return true;
		return Between( ax, ay, bx, by, cx, cy ) || Between( ax, ay, bx, by, dx, dy ) || Between( cx, cy, dx, dy, ax, ay ) || Between( cx, cy, dx, dy, bx, by );
	}

	/// <summary>
	/// The segments share a point. Segments that start or end on the same point do not count. With loose
	/// only a crossing inside both counts.
	/// </summary>
	public static bool Crosses( int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy, bool loose = false )
	{
		if ( (ax == cx && ay == cy) || (bx == cx && by == cy) || (ax == dx && ay == dy) || (bx == dx && by == dy) ) return false;
		return loose ? IntersectProper( ax, ay, bx, by, cx, cy, dx, dy ) : Intersect( ax, ay, bx, by, cx, cy, dx, dy );
	}

	/// <summary>
	/// True when p lies inside the corner an outline makes at i, coming from b and going on to n.
	/// With loose a point on one of the corner's edges is inside too.
	/// </summary>
	public static bool InCone( int bx, int by, int ix, int iy, int nx, int ny, int px, int py, bool loose = false )
	{
		if ( LeftOn( bx, by, ix, iy, nx, ny ) )
		{
			return loose
				? LeftOn( ix, iy, px, py, bx, by ) && LeftOn( px, py, ix, iy, nx, ny )
				: Left( ix, iy, px, py, bx, by ) && Left( px, py, ix, iy, nx, ny );
		}

		return !(LeftOn( ix, iy, px, py, nx, ny ) && LeftOn( px, py, ix, iy, bx, by ));
	}
}
