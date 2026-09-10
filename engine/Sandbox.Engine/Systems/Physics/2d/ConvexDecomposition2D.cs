namespace Sandbox;

internal static class ConvexDecomposition2D
{
	const int MaxPolygonVertices = 8;
	const float AreaEpsilon = 0.01f;
	const float ConvexEpsilon = 1e-4f;

	static List<Vector2> ComputeConvexHull( ReadOnlySpan<Vector2> points )
	{
		if ( points.Length < 3 )
		{
			var result = new List<Vector2>( points.Length );
			foreach ( var p in points )
				result.Add( p );
			return result;
		}

		var sorted = new Vector2[points.Length];
		points.CopyTo( sorted );
		Array.Sort( sorted, ( a, b ) =>
		{
			int cmp = a.x.CompareTo( b.x );
			return cmp != 0 ? cmp : a.y.CompareTo( b.y );
		} );

		var unique = new List<Vector2>( sorted.Length );
		foreach ( var p in sorted )
		{
			if ( unique.Count > 0 && (unique[^1] - p).LengthSquared < 0.001f )
				continue;
			unique.Add( p );
		}

		if ( unique.Count < 3 )
			return unique;

		var hull = new Vector2[unique.Count * 2];
		int k = 0;

		for ( int i = 0; i < unique.Count; i++ )
		{
			while ( k >= 2 && Cross( hull[k - 2], hull[k - 1], unique[i] ) <= 0 )
				k--;
			hull[k++] = unique[i];
		}

		int lower = k + 1;
		for ( int i = unique.Count - 2; i >= 0; i-- )
		{
			while ( k >= lower && Cross( hull[k - 2], hull[k - 1], unique[i] ) <= 0 )
				k--;
			hull[k++] = unique[i];
		}

		var result2 = new List<Vector2>( k - 1 );
		for ( int i = 0; i < k - 1; i++ )
			result2.Add( hull[i] );

		return result2;
	}

	static List<Vector2[]> DecomposeConvexPolygon( List<Vector2> hull, int maxVerts = MaxPolygonVertices )
	{
		var results = new List<Vector2[]>();

		int n = hull.Count;
		if ( n < 3 )
			return results;

		if ( n <= maxVerts )
		{
			results.Add( hull.ToArray() );
			return results;
		}

		if ( maxVerts < 3 )
			maxVerts = 3;

		var centroid = Vector2.Zero;
		foreach ( var p in hull )
			centroid += p;
		centroid /= n;

		Span<Vector2> candidate = stackalloc Vector2[maxVerts];

		int startV = 0;
		while ( startV < n )
		{
			int endV = startV + 1;

			while ( endV < n )
			{
				int tryEnd = endV + 1;
				int polyVerts = (tryEnd - startV + 1) + 1;
				if ( polyVerts > maxVerts )
					break;

				candidate[0] = centroid;
				for ( int j = 0; j <= tryEnd - startV; j++ )
					candidate[j + 1] = hull[(startV + j) % n];

				if ( !IsConvex( candidate[..polyVerts] ) )
					break;

				endV = tryEnd;
			}

			int count = endV - startV + 1;
			var poly = new Vector2[count + 1];
			poly[0] = centroid;
			for ( int j = 0; j < count; j++ )
				poly[j + 1] = hull[(startV + j) % n];

			results.Add( poly );

			startV = endV;
		}

		return results;
	}

	public static List<Vector2[]> DecomposeAsHull( ReadOnlySpan<Vector3> points, Transform transform )
	{
		if ( points.Length == 0 )
			return [];

		Span<Vector2> buffer = points.Length <= 1024
			? stackalloc Vector2[points.Length]
			: new Vector2[points.Length];

		int count = ProjectFinite( points, transform, buffer );
		if ( count < 3 )
			return [];

		var hull = ComputeConvexHull( buffer[..count] );
		if ( hull.Count < 3 )
			return [];

		return DecomposeConvexPolygon( hull );
	}

	public static List<Vector2[]> DecomposeAsTriangles( ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, Transform transform )
	{
		var results = new List<Vector2[]>();

		if ( vertices.Length == 0 || indices.Length < 3 )
			return results;

		Span<Vector2> verts2d = vertices.Length <= 1024
			? stackalloc Vector2[vertices.Length]
			: new Vector2[vertices.Length];

		for ( int i = 0; i < vertices.Length; i++ )
		{
			var world = transform.PointToWorld( vertices[i] );
			verts2d[i] = new Vector2( world.x, world.y );
		}

		for ( int i = 0; i + 2 < indices.Length; i += 3 )
		{
			int ia = indices[i];
			int ib = indices[i + 1];
			int ic = indices[i + 2];

			if ( (uint)ia >= (uint)vertices.Length || (uint)ib >= (uint)vertices.Length || (uint)ic >= (uint)vertices.Length )
				continue;

			var a = verts2d[ia];
			var b = verts2d[ib];
			var c = verts2d[ic];

			if ( !IsFinite( a ) || !IsFinite( b ) || !IsFinite( c ) )
				continue;

			float area = MathF.Abs( Cross( a, b, c ) ) * 0.5f;
			if ( area < AreaEpsilon )
				continue;

			results.Add( [a, b, c] );
		}

		return results;
	}

	static int ProjectFinite( ReadOnlySpan<Vector3> source, Transform transform, Span<Vector2> destination )
	{
		int count = 0;
		for ( int i = 0; i < source.Length; i++ )
		{
			var world = transform.PointToWorld( source[i] );
			var p = new Vector2( world.x, world.y );
			if ( !IsFinite( p ) )
				continue;

			destination[count++] = p;
		}
		return count;
	}

	static bool IsConvex( ReadOnlySpan<Vector2> poly )
	{
		if ( poly.Length < 3 )
			return false;

		bool positive = false;
		bool negative = false;

		for ( int i = 0; i < poly.Length; i++ )
		{
			float turn = Cross( poly[i], poly[(i + 1) % poly.Length], poly[(i + 2) % poly.Length] );
			if ( turn > ConvexEpsilon )
				positive = true;
			else if ( turn < -ConvexEpsilon )
				negative = true;

			if ( positive && negative )
				return false;
		}

		return true;
	}

	static bool IsFinite( Vector2 p ) => float.IsFinite( p.x ) && float.IsFinite( p.y );

	static float Cross( Vector2 o, Vector2 a, Vector2 b )
	{
		return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
	}
}
