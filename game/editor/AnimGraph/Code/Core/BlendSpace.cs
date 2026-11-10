namespace Editor.AnimGraph;

/// <summary>
/// Blend space helpers for the editor. Triangulation delegates to the engine's
/// baker (<see cref="GraphBuilder.TriangulateBlendSpace"/>) so the canvas
/// preview, tools compiler and runtime builder always agree on the bake.
/// </summary>
public static class BlendSpace
{
	public static bool Triangulate( IReadOnlyList<Vector2> points, out List<int> triangles, out List<int> hull )
		=> GraphBuilder.TriangulateBlendSpace( points, out triangles, out hull );

	/// <summary>
	/// Per-point blend weights for a parameter position, matching the runtime:
	/// barycentric weights inside the enclosing triangle, or a two-point blend
	/// along the closest hull edge when outside. Also returns the effective
	/// parameter position, clamped onto the hull.
	/// </summary>
	public static float[] CalculateWeights( IReadOnlyList<Vector2> points, List<int> triangles, List<int> hull, Vector2 point, out Vector2 clamped )
	{
		var weights = new float[points.Count];
		clamped = point;

		for ( var i = 0; i < triangles.Count; i += 3 )
		{
			int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];

			if ( Barycentric( point, points[i0], points[i1], points[i2], out var bcc ) )
			{
				weights[i0] = bcc.x;
				weights[i1] = bcc.y;
				weights[i2] = bcc.z;
				return weights;
			}
		}

		// Outside the hull - blend along the closest hull edge
		var closestDistance = float.MaxValue;
		var closestEdge = -1;
		var closestT = 0.0f;

		for ( var i = 1; i < hull.Count; i++ )
		{
			var start = points[hull[i - 1]];
			var segment = points[hull[i]] - start;
			var lengthSq = segment.LengthSquared;
			var t = lengthSq > 0.0f ? Math.Clamp( Vector2.Dot( point - start, segment ) / lengthSq, 0.0f, 1.0f ) : 0.0f;
			var cp = start + segment * t;

			var distance = (cp - point).LengthSquared;
			if ( distance < closestDistance )
			{
				closestDistance = distance;
				closestEdge = i - 1;
				closestT = t;
				clamped = cp;
			}
		}

		if ( closestEdge < 0 )
			return weights;

		weights[hull[closestEdge]] = 1.0f - closestT;
		weights[hull[closestEdge + 1]] = closestT;
		return weights;
	}

	private static bool Barycentric( Vector2 point, Vector2 a, Vector2 b, Vector2 c, out Vector3 coords )
	{
		Vector2 v0 = b - a, v1 = c - a, v2 = point - a;

		var denominator = 1.0f / (v0.x * v1.y - v1.x * v0.y);
		var y = (v2.x * v1.y - v1.x * v2.y) * denominator;
		var z = (v0.x * v2.y - v2.x * v0.y) * denominator;
		coords = new Vector3( 1.0f - y - z, y, z );

		return coords.x is >= 0.0f and <= 1.0f
			&& coords.y is >= 0.0f and <= 1.0f
			&& coords.z is >= 0.0f and <= 1.0f;
	}
}
