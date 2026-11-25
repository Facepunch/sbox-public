public sealed partial class SnapGrid
{
	// Fit a broad face, including shallow recesses, using a stable mesh-space chart.
	private bool TryPlanarSurface( Vector3 aim )
	{
		var planes = new Dictionary<int, (float Area, float Depth)>();
		foreach ( var t in _surfaceTriangles )
		{
			var area = Vector3.Cross( t.B - t.A, t.C - t.A ).Length * 0.5f;
			if ( Vector3.Dot( t.Normal, _cachedNormal ) < 0.9999f ) continue;
			var depth = Vector3.Dot( t.A - _cachedOrigin, _cachedNormal );
			var key = (int)MathF.Round( depth * 50f );
			planes.TryGetValue( key, out var p );
			planes[key] = (p.Area + area, p.Depth + depth * area);
		}
		if ( planes.Count == 0 ) return false;
		var aimDepth = Vector3.Dot( aim - _cachedOrigin, _cachedNormal );
		var largestArea = planes.Values.Max( p => p.Area );
		var plane = planes.Values.Where( p => p.Area >= largestArea * 0.2f )
			.OrderBy( p => MathF.Abs( p.Depth / p.Area - aimDepth ) ).First();

		var planeOrigin = _cachedOrigin + _cachedNormal * (plane.Depth / plane.Area);
		if ( Vector3.Dot( aim - planeOrigin, _cachedNormal ) < -CellSize * 0.5f ) return false;
		// Shallow panels and their bevels share the surrounding face grid. Keep their actual depths.
		var depthTolerance = MathF.Max( 0.025f, CellSize * 0.125f );
		float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
		// Enumerable.Sum accumulated in double, keep that so the area test is unchanged.
		var areaSum = 0.0;
		foreach ( var t in _surfaceTriangles )
		{
			if ( !(MathF.Abs( Vector3.Dot( t.A - planeOrigin, _cachedNormal ) ) < depthTolerance &&
				MathF.Abs( Vector3.Dot( t.B - planeOrigin, _cachedNormal ) ) < depthTolerance &&
				MathF.Abs( Vector3.Dot( t.C - planeOrigin, _cachedNormal ) ) < depthTolerance) ) continue;
			Include( t.A ); Include( t.B ); Include( t.C );
			areaSum += MathF.Abs( Vector3.Dot( Vector3.Cross( t.B - t.A, t.C - t.A ), _cachedNormal ) ) * 0.5f;
		}
		var width = maxU - minU; var height = maxV - minV;
		var projectedArea = (float)areaSum;
		if ( width < 0.1f || height < 0.1f || projectedArea < width * height * 0.9f ) return false;
		_cachedOrigin = planeOrigin + _cachedRight * ((minU + maxU) * 0.5f) + _cachedUp * ((minV + maxV) * 0.5f);
		_cachedHalfExtents = new Vector2( width, height ) * 0.5f;
		// Raised controls use the parent face's lattice without changing its bounds.
		// Keep their real geometry so drawing and snapping use the same intersections.
		_surfaceTriangles.RemoveAll( t => !(OnFace( t.A ) && OnFace( t.B ) && OnFace( t.C )) );
		_surfaceVertices.Clear();
		foreach ( var t in _surfaceTriangles )
		{
			_surfaceVertices.Add( new Vertex( t.A ) );
			_surfaceVertices.Add( new Vertex( t.B ) );
			_surfaceVertices.Add( new Vertex( t.C ) );
		}
		return true;
		bool OnFace( Vector3 p )
		{
			var offset = p - planeOrigin;
			var u = Vector3.Dot( offset, _cachedRight );
			var v = Vector3.Dot( offset, _cachedUp );
			return Vector3.Dot( offset, _cachedNormal ) >= -depthTolerance
				&& u >= minU - 0.025f && u <= maxU + 0.025f
				&& v >= minV - 0.025f && v <= maxV + 0.025f;
		}
		void Include( Vector3 p )
		{
			var u = Vector3.Dot( p - planeOrigin, _cachedRight ); var v = Vector3.Dot( p - planeOrigin, _cachedUp );
			minU = MathF.Min( minU, u ); maxU = MathF.Max( maxU, u );
			minV = MathF.Min( minV, v ); maxV = MathF.Max( maxV, v );
		}
	}
}