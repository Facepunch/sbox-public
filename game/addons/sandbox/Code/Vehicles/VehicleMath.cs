/// <summary>
/// Small maths helpers for the vehicle code.
/// </summary>
internal static class VehicleMath
{
	/// <summary>
	/// Move <paramref name="value"/> towards <paramref name="target"/> by at most <paramref name="speed"/>.
	/// </summary>
	public static float Approach( float target, float value, float speed )
	{
		var delta = target - value;
		if ( delta > speed ) return value + speed;
		if ( delta < -speed ) return value - speed;
		return target;
	}

	/// <summary>
	/// Map <paramref name="val"/> from [a, b] to [c, d], clamped to the range.
	/// </summary>
	public static float RemapValClamped( float val, float a, float b, float c, float d )
	{
		if ( a == b )
			return val >= b ? d : c;

		var t = ((val - a) / (b - a)).Clamp( 0f, 1f );
		return c + (d - c) * t;
	}

	/// <summary>
	/// Zero counts as positive.
	/// </summary>
	public static float Sign( float x ) => x < 0.0f ? -1.0f : 1.0f;
}
