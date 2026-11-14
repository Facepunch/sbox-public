namespace Borealis.Culling;

/// <summary>
/// Screen-size culling using <c>CFrustum::ComputeScreenSize</c>, compared squared to avoid per-object square roots. A sun
/// cascade culls by its own texel size instead (<see cref="ForPass"/>), which doesn't depend on where the camera is.
/// </summary>
internal readonly struct SizeCull
{
	readonly Vector3 _cameraPosition;
	readonly float _thresholdSquared;

	// Texel-sized: the squared bounding-sphere radius below which an object is culled, wherever it is
	readonly float _minRadiusSquared;

	public SizeCull( RenderView view )
	{
		_cameraPosition = view.Position;
		var threshold = view.SizeCullThreshold * MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
		_thresholdSquared = threshold * threshold;
	}

	SizeCull( float minRadius )
	{
		_minRadiusSquared = minRadius * minRadius;
	}

	/// <summary>
	/// How many of a cascade's texels wide a caster's bounding sphere must be to draw into it.
	/// </summary>
	public static float CascadeTexels { get; set; } = 1.0f;

	/// <summary>
	/// The size cull a view's objects take: a sun cascade's by its own texel size (<see cref="ViewPass.TexelSized"/>),
	/// every other view's by its root view's screen.
	/// </summary>
	public static SizeCull ForPass( ViewPass pass )
	{
		if ( !pass.TexelSized ) return new SizeCull( pass.Root );

		var view = pass.View;
		var texel = view.OrthoSize.x / MathF.Max( view.Viewport.Width, 1 );
		return new SizeCull( texel * CascadeTexels * 0.5f );
	}

	public bool Enabled => _thresholdSquared > 0 || _minRadiusSquared > 0;

	/// <summary>
	/// The camera and squared threshold, for culling on the GPU with the same test. A texel-sized cull sends its squared
	/// minimum radius negated, and no camera.
	/// </summary>
	public Vector4 CameraAndThreshold => _minRadiusSquared > 0 ? new( 0, 0, 0, -_minRadiusSquared )
		: new( _cameraPosition.x, _cameraPosition.y, _cameraPosition.z, _thresholdSquared );

	/// <summary>
	/// Whether an object with these world bounds is too small to draw.
	/// </summary>
	public bool Culls( in Vector3 center, in Vector3 extents )
	{
		if ( _minRadiusSquared > 0 ) return extents.LengthSquared < _minRadiusSquared;
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
		if ( _minRadiusSquared > 0 ) return ((max - min) * 0.5f).LengthSquared < _minRadiusSquared;
		if ( _thresholdSquared <= 0 ) return false;

		var radiusSquared = ((max - min) * 0.5f).LengthSquared;
		var distanceSquared = _cameraPosition.Clamp( min, max ).DistanceSquared( _cameraPosition );
		return distanceSquared > radiusSquared && radiusSquared < _thresholdSquared * distanceSquared;
	}
}
