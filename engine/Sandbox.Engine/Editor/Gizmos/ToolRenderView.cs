namespace Sandbox;

public readonly record struct ToolRenderView( Matrix WorldToProjection, Vector2 Size, bool Orthographic )
{
	public ToolRenderView( SceneCamera camera )
		: this( camera.ReverseZViewProjectionMatrix, camera.Size, camera.Ortho )
	{
	}

	public bool ToScreen( Vector3 world, out Vector2 screen )
	{
		var clip = WorldToProjection.Transform( new Vector4( world, 1 ) );
		screen = default;
		if ( clip.w <= 0 )
			return false;

		screen = new Vector2( (clip.x / clip.w + 1) * Size.x * 0.5f, (1 - clip.y / clip.w) * Size.y * 0.5f );
		return float.IsFinite( screen.x ) && float.IsFinite( screen.y );
	}

	public bool ClipLineToFrustum( ref Vector3 start, ref Vector3 end )
	{
		var a = WorldToProjection.Transform( new Vector4( start, 1 ) );
		var b = WorldToProjection.Transform( new Vector4( end, 1 ) );
		var min = 0f;
		var max = 1f;
		if ( !Clip( a.w + a.x, b.w + b.x, ref min, ref max )
			|| !Clip( a.w - a.x, b.w - b.x, ref min, ref max )
			|| !Clip( a.w + a.y, b.w + b.y, ref min, ref max )
			|| !Clip( a.w - a.y, b.w - b.y, ref min, ref max )
			|| !Clip( a.z, b.z, ref min, ref max )
			|| !Clip( a.w - a.z, b.w - b.z, ref min, ref max ) )
			return false;

		var original = start;
		start = Vector3.Lerp( original, end, min );
		end = Vector3.Lerp( original, end, max );
		return true;
	}

	public bool ClipLineToViewport( ref Vector2 start, ref Vector2 end )
	{
		var min = 0f;
		var max = 1f;
		if ( !Clip( start.x, end.x, ref min, ref max )
			|| !Clip( Size.x - start.x, Size.x - end.x, ref min, ref max )
			|| !Clip( start.y, end.y, ref min, ref max )
			|| !Clip( Size.y - start.y, Size.y - end.y, ref min, ref max ) )
			return false;

		var original = start;
		start = Vector2.Lerp( original, end, min );
		end = Vector2.Lerp( original, end, max );
		return true;
	}

	static bool Clip( float start, float end, ref float min, ref float max )
	{
		if ( !float.IsFinite( start ) || !float.IsFinite( end ) || (start < 0 && end < 0) )
			return false;

		if ( start < 0 )
			min = MathF.Max( min, start / (start - end) );
		else if ( end < 0 )
			max = MathF.Min( max, start / (start - end) );
		return min <= max;
	}
}
