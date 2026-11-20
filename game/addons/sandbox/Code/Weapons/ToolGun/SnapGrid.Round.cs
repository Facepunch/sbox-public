public sealed partial class SnapGrid
{
	private float _roundRadius;
	private float _roundAngleStep;

	private bool TryRoundSurface( GameObject go )
	{
		if ( _surfaceTriangles.Count == 0 ) return false;
		var normalSum = Vector3.Zero;
		var centerSum = Vector3.Zero;
		var totalArea = 0f;
		foreach ( var t in _surfaceTriangles )
		{
			var area = Vector3.Cross( t.B - t.A, t.C - t.A ).Length;
			normalSum += t.Normal * area;
			centerSum += (t.A + t.B + t.C) * (area / 3f);
			totalArea += area;
		}
		if ( totalArea < 1 || normalSum.Length < totalArea * 0.9f ) return false;
		var normal = normalSum.Normal;
		var reference = go.WorldRotation * Vector3.Forward;
		if ( MathF.Abs( Vector3.Dot( reference, normal ) ) > 0.9f ) reference = go.WorldRotation * Vector3.Up;
		var right = (reference - normal * Vector3.Dot( reference, normal )).Normal;
		var up = Vector3.Cross( normal, right ).Normal;
		var center = centerSum / totalArea;
		float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
		foreach ( var v in _surfaceVertices )
		{
			var u = Vector3.Dot( v.Position - center, right ); var w = Vector3.Dot( v.Position - center, up );
			minU = MathF.Min( minU, u ); maxU = MathF.Max( maxU, u );
			minV = MathF.Min( minV, w ); maxV = MathF.Max( maxV, w );
		}
		center += right * ((minU + maxU) * 0.5f) + up * ((minV + maxV) * 0.5f);
		var radii = new float[16];
		foreach ( var vertex in _surfaceVertices )
		{
			var offset = vertex.Position - center;
			var u = Vector3.Dot( offset, right ); var v = Vector3.Dot( offset, up );
			var angle = MathF.Atan2( v, u ) + MathF.PI;
			var sector = ((int)MathF.Round( angle * 16 / MathF.Tau ) % 16 + 16) % 16;
			radii[sector] = MathF.Max( radii[sector], MathF.Sqrt( u * u + v * v ) );
		}
		var radius = radii.Max();
		// Full angular coverage and a nearly constant outer radius distinguish a disk from a box.
		if ( radius < CellSize || radii.Min() < radius * 0.94f ) return false;
		if ( totalArea * 0.5f < MathF.PI * radius * radius * 0.65f ) return false;
		_cachedOrigin = center; _cachedNormal = normal; _cachedRight = right; _cachedUp = up;
		_cachedHalfExtents = new Vector2( radius, radius );
		_roundRadius = radius;
		var angles = new HashSet<int>();
		foreach ( var vertex in _surfaceVertices )
		{
			var p = vertex.Position - center;
			if ( p.Length > radius * 0.99f )
				angles.Add( (int)MathF.Round( (MathF.Atan2( Vector3.Dot( p, up ), Vector3.Dot( p, right ) ) + MathF.Tau) % MathF.Tau * 1000f ) );
		}
		var sides = Math.Max( 3, angles.Count );
		// Keep the smooth ring inside every collision facet.
		_roundRadius = radius * MathF.Cos( MathF.PI / sides );
		var lastRing = MathF.Floor( _roundRadius / CellSize ) * CellSize;
		if ( lastRing > 0 && _roundRadius - lastRing < CellSize * 0.25f ) _roundRadius = lastRing;
		var spokes = Math.Max( 8, (int)MathF.Round( MathF.Tau * radius / CellSize / 4f ) * 4 );
		_roundAngleStep = MathF.Tau / spokes;
		return true;
	}

	private Vector3 ComputeRoundSnap( Vector3 aim, float cellSize )
	{
		var offset = aim - _cachedOrigin;
		var u = Vector3.Dot( offset, _cachedRight ); var v = Vector3.Dot( offset, _cachedUp );
		var radius = MathF.Sqrt( u * u + v * v );
		var snappedRadius = Math.Clamp( MathF.Round( radius / cellSize ) * cellSize, 0, _roundRadius );
		if ( MathF.Abs( radius - _roundRadius ) < MathF.Abs( radius - snappedRadius ) ) snappedRadius = _roundRadius;
		var angle = MathF.Round( MathF.Atan2( v, u ) / _roundAngleStep ) * _roundAngleStep;
		return _cachedOrigin + (_cachedRight * MathF.Cos( angle ) + _cachedUp * MathF.Sin( angle )) * snappedRadius;
	}
}