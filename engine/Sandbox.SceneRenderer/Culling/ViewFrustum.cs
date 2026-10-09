namespace Sandbox.SceneRenderer.Culling;

/// <summary>
/// Six unnormalized world-to-projection planes. Box tests compare signs, so normalization is unnecessary.
/// </summary>
internal readonly struct ViewFrustum
{
	readonly Vector4 _left, _right, _bottom, _top, _near, _far;

	ViewFrustum( Vector4 left, Vector4 right, Vector4 bottom, Vector4 top, Vector4 near, Vector4 far )
	{
		_left = left;
		_right = right;
		_bottom = bottom;
		_top = top;
		_near = near;
		_far = far;
	}

	/// <summary>
	/// Planes for a row-vector, reverse-Z projection: clip = v * M, inside when -w &lt;= x,y &lt;= w and
	/// 0 &lt;= z &lt;= w.
	/// </summary>
	public static ViewFrustum FromReverseZ( in Matrix matrix )
	{
		var column1 = new Vector4( matrix.M11, matrix.M21, matrix.M31, matrix.M41 );
		var column2 = new Vector4( matrix.M12, matrix.M22, matrix.M32, matrix.M42 );
		var column3 = new Vector4( matrix.M13, matrix.M23, matrix.M33, matrix.M43 );
		var column4 = new Vector4( matrix.M14, matrix.M24, matrix.M34, matrix.M44 );

		return new( column4 + column1, column4 - column1, column4 + column2, column4 - column2, column4 - column3, column3 );
	}

	/// <summary>
	/// Conservative world-space box test; frustum corners can produce false positives.
	/// </summary>
	public bool Intersects( in Vector3 center, in Vector3 extents )
	{
		return Inside( _left, center, extents )
			&& Inside( _right, center, extents )
			&& Inside( _bottom, center, extents )
			&& Inside( _top, center, extents )
			&& Inside( _near, center, extents )
			&& Inside( _far, center, extents );
	}

	/// <summary>
	/// Whether a world space box is outside, crossing a plane, or entirely inside.
	/// </summary>
	public Containment Classify( in Vector3 center, in Vector3 extents )
	{
		var result = Containment.Inside;
		if ( !Classify( _left, center, extents, ref result ) ) return Containment.Outside;
		if ( !Classify( _right, center, extents, ref result ) ) return Containment.Outside;
		if ( !Classify( _bottom, center, extents, ref result ) ) return Containment.Outside;
		if ( !Classify( _top, center, extents, ref result ) ) return Containment.Outside;
		if ( !Classify( _near, center, extents, ref result ) ) return Containment.Outside;
		if ( !Classify( _far, center, extents, ref result ) ) return Containment.Outside;
		return result;
	}

	static bool Classify( in Vector4 plane, in Vector3 center, in Vector3 extents, ref Containment result )
	{
		var normal = new Vector3( plane.x, plane.y, plane.z );
		var distance = Vector3.Dot( normal, center ) + plane.w;
		var radius = Vector3.Dot( extents, normal.Abs() );
		if ( distance + radius < 0 ) return false;
		if ( distance - radius < 0 ) result = Containment.Intersects;
		return true;
	}

	/// <summary>
	/// Whether a world space box is entirely inside.
	/// </summary>
	public bool Contains( in Vector3 center, in Vector3 extents )
	{
		return Contained( _left, center, extents )
			&& Contained( _right, center, extents )
			&& Contained( _bottom, center, extents )
			&& Contained( _top, center, extents )
			&& Contained( _near, center, extents )
			&& Contained( _far, center, extents );
	}

	static bool Contained( in Vector4 plane, in Vector3 center, in Vector3 extents )
	{
		var normal = new Vector3( plane.x, plane.y, plane.z );
		var distance = Vector3.Dot( normal, center ) + plane.w;
		var radius = Vector3.Dot( extents, normal.Abs() );
		return distance - radius >= 0;
	}

	static bool Inside( in Vector4 plane, in Vector3 center, in Vector3 extents )
	{
		var normal = new Vector3( plane.x, plane.y, plane.z );
		var distance = Vector3.Dot( normal, center ) + plane.w;
		var radius = Vector3.Dot( extents, normal.Abs() );
		return distance + radius >= 0;
	}
}
