namespace Sandbox.UI;

/// <summary>
/// The pictures the colour picker can't make from styles alone, built once and shared.
/// </summary>
internal static class ColorPickerTextures
{
	/// <summary>
	/// Side of the ring and disc textures, in pixels.
	/// </summary>
	public const int WheelSize = 220;

	/// <summary>
	/// Outer radius of the hue ring.
	/// </summary>
	public const float RingOuter = 108.0f;

	/// <summary>
	/// Inner radius of the hue ring.
	/// </summary>
	public const float RingInner = 95.0f;

	/// <summary>
	/// Radius of the hue/saturation disc.
	/// </summary>
	public const float DiscRadius = 108.0f;

	const int CheckerCell = 8;

	static Texture _checkerboard;
	static Texture _hueRing;
	static Texture _hueDisc;

	/// <summary>
	/// Grey and white squares, to show transparent colours against. Tiles at <see cref="CheckerSize"/>.
	/// </summary>
	public static Texture Checkerboard => _checkerboard ??= Build( CheckerCell * 2, CheckerCell * 2, "colorpicker_checker", ( x, y ) =>
	{
		var light = (x / CheckerCell + y / CheckerCell) % 2 == 0;
		return light ? new Color32( 150, 150, 150 ) : new Color32( 100, 100, 100 );
	} );

	/// <summary>
	/// Size the checkerboard tiles at.
	/// </summary>
	public const int CheckerSize = CheckerCell * 2;

	/// <summary>
	/// Every hue around a ring, red at the right, going clockwise. Transparent inside and out.
	/// </summary>
	public static Texture HueRing => _hueRing ??= Build( WheelSize, WheelSize, "colorpicker_ring", ( x, y ) =>
	{
		var (angle, distance) = Polar( x, y );
		var coverage = Edge( RingOuter - distance ) * Edge( distance - RingInner );
		return WithAlpha( new ColorHsv( angle, 1.0f, 1.0f ).ToColor(), coverage );
	} );

	/// <summary>
	/// Hue by angle and saturation by radius at full value, the way Unreal's wheel is.
	/// </summary>
	public static Texture HueDisc => _hueDisc ??= Build( WheelSize, WheelSize, "colorpicker_disc", ( x, y ) =>
	{
		var (angle, distance) = Polar( x, y );
		var coverage = Edge( DiscRadius - distance );
		return WithAlpha( new ColorHsv( angle, MathF.Min( 1.0f, distance / DiscRadius ), 1.0f ).ToColor(), coverage );
	} );

	/// <summary>
	/// Angle in degrees clockwise from the right, and distance from the middle of the wheel.
	/// </summary>
	static (float angle, float distance) Polar( int x, int y )
	{
		var dx = x + 0.5f - WheelSize * 0.5f;
		var dy = y + 0.5f - WheelSize * 0.5f;
		var angle = (MathF.Atan2( dy, dx ).RadianToDegree() + 360.0f) % 360.0f;
		return (angle, MathF.Sqrt( dx * dx + dy * dy ));
	}

	/// <summary>
	/// One pixel of anti-aliasing at an edge, from how far inside it a pixel is.
	/// </summary>
	static float Edge( float inside ) => Math.Clamp( inside + 0.5f, 0.0f, 1.0f );

	static Color32 WithAlpha( Color color, float alpha )
	{
		var c = color.ToColor32();
		c.a = (byte)(alpha * 255.0f);
		return c;
	}

	/// <summary>
	/// Nothing to draw with headless, and the test host crashes making GPU textures.
	/// </summary>
	static Texture Build( int width, int height, string name, Func<int, int, Color32> pixel )
	{
		if ( Application.IsHeadless || Application.IsUnitTest ) return null;

		var data = new byte[width * height * 4];

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				var c = pixel( x, y );
				var i = (y * width + x) * 4;
				data[i] = c.r;
				data[i + 1] = c.g;
				data[i + 2] = c.b;
				data[i + 3] = c.a;
			}
		}

		return Texture.Create( width, height ).WithName( name ).WithData( data ).Finish();
	}
}
