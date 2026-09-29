namespace Sandbox.SceneRenderer.Culling;

/// <summary>
/// Screen-size culling using <c>CFrustum::ComputeScreenSize</c>, compared squared to avoid per-object square roots.
/// </summary>
internal readonly struct SizeCull
{
	readonly Vector3 _cameraPosition;
	readonly float _thresholdSquared;

	public SizeCull( RenderView view )
	{
		_cameraPosition = view.Position;
		var threshold = view.SizeCullThreshold * MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
		_thresholdSquared = threshold * threshold;
	}

	public bool Enabled => _thresholdSquared > 0;

	/// <summary>
	/// Whether an object with these world bounds is too small to draw.
	/// </summary>
	public bool Culls( in Vector3 center, in Vector3 extents )
	{
		if ( _thresholdSquared <= 0 ) return false;

		var radiusSquared = extents.LengthSquared;
		var distanceSquared = center.DistanceSquared( _cameraPosition );

		// Inside the bounding sphere counts as full screen.
		return distanceSquared > radiusSquared && radiusSquared < _thresholdSquared * distanceSquared;
	}

	/// <summary>
	/// Conservatively reject a whole box using its maximum radius and nearest camera distance.
	/// </summary>
	public bool CullsAll( in Vector3 min, in Vector3 max )
	{
		if ( _thresholdSquared <= 0 ) return false;

		var radiusSquared = ((max - min) * 0.5f).LengthSquared;
		var distanceSquared = _cameraPosition.Clamp( min, max ).DistanceSquared( _cameraPosition );
		return distanceSquared > radiusSquared && radiusSquared < _thresholdSquared * distanceSquared;
	}
}
