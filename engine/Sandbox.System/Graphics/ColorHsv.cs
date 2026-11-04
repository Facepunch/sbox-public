using System.Runtime.InteropServices;

/// <summary>
/// A color in <a href="https://upload.wikimedia.org/wikipedia/commons/a/a0/Hsl-hsv_models.svg">Hue-Saturation-Value/Brightness</a> format.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
[Description( "A color described by hue, saturation, brightness and alpha." )]
public struct ColorHsv : IEquatable<ColorHsv>
{
	/// <summary>
	/// Hue component of this color in range 0 to 360.
	/// </summary>
	public float Hue { readonly get; set; }

	/// <summary>
	/// Saturation of this color in range 0 (white) to 1 (full color).
	/// </summary>
	public float Saturation { readonly get; set; }

	/// <summary>
	/// Brightness of this color in range 0 (black) to 1 (full color).
	/// </summary>
	public float Value { readonly get; set; }

	/// <summary>
	/// Transparency of this color in range 0 (fully transparent) to 1 (fully opaque).
	/// </summary>
	public float Alpha { readonly get; set; }

	/// <summary>
	/// Initializes a new HSV/HSB color. Hue is in the range of [0-360] and all other values are in range [0,1]
	/// </summary>
	/// <param name="h">The hue color component.</param>
	/// <param name="s">Saturation of the color.</param>
	/// <param name="v">Brightness of the color.</param>
	/// <param name="a">Alpha of the color.</param>
	public ColorHsv( float h, float s, float v, float a = 1.0f )
	{
		Hue = h;
		Saturation = s;
		Value = v;
		Alpha = a;
	}

	public override string ToString()
	{
		return $"H:{Hue:0.###},S:{Saturation:0.###},V:{Value:0.###},A:{Alpha:0.###}";
	}

	/// <summary>
	/// Convert this object to <see cref="Color"/>.
	/// </summary>
	/// <returns>The converted color struct.</returns>
	public readonly Color ToColor() => this;

	/// <summary>
	/// Returns a copy of this color with given Hue value.
	/// </summary>
	/// <param name="hue">The Hue override.</param>
	/// <returns>The new color.</returns>
	public readonly ColorHsv WithHue( float hue ) => new ColorHsv( hue, Saturation, Value, Alpha );

	/// <summary>
	/// Returns a copy of this color with given Saturation value.
	/// </summary>
	/// <param name="saturation">The Saturation override.</param>
	/// <returns>The new color.</returns>
	public readonly ColorHsv WithSaturation( float saturation ) => new ColorHsv( Hue, saturation, Value, Alpha );

	/// <summary>
	/// Returns a copy of this color with given Brightness value.
	/// </summary>
	/// <param name="value">The Brightness override.</param>
	/// <returns>The new color.</returns>
	public readonly ColorHsv WithValue( float value ) => new ColorHsv( Hue, Saturation, value, Alpha );

	/// <summary>
	/// Returns a copy of this color with given alpha value.
	/// </summary>
	/// <param name="alpha">The alpha override.</param>
	/// <returns>The new color.</returns>
	public readonly ColorHsv WithAlpha( float alpha ) => this with { Alpha = alpha };

	static public implicit operator ColorHsv( Color rgb )
	{
		float delta, min;
		float h = 0, s, v;

		min = MathF.Min( MathF.Min( rgb.r, rgb.g ), rgb.b );
		v = MathF.Max( MathF.Max( rgb.r, rgb.g ), rgb.b );
		delta = v - min;

		if ( v == 0.0 ) s = 0;
		else s = delta / v;

		if ( s == 0 )
		{
			h = 0.0f;
		}
		else
		{
			if ( rgb.r == v ) h = (rgb.g - rgb.b) / delta;
			else if ( rgb.g == v ) h = 2 + (rgb.b - rgb.r) / delta;
			else if ( rgb.b == v ) h = 4 + (rgb.r - rgb.g) / delta;

			h *= 60;

			if ( h < 0.0 )
				h += 360.0f;
		}

		return new ColorHsv( h, s, v, rgb.a );
	}

	static public implicit operator Color( in ColorHsv hsv )
	{
		if ( hsv.Saturation == 0 )
			return new Color( hsv.Value, hsv.Value, hsv.Value, hsv.Alpha );

		int i;
		float f, p, q, t, h;

		h = hsv.Hue % 360.0f;
		h /= 60.0f;

		i = (int)Math.Truncate( h );
		f = h - i;

		p = hsv.Value * (1.0f - hsv.Saturation);
		q = hsv.Value * (1.0f - (hsv.Saturation * f));
		t = hsv.Value * (1.0f - (hsv.Saturation * (1.0f - f)));

		switch ( i )
		{
			case 0:
				return new Color( hsv.Value, t, p, hsv.Alpha );

			case 1:
				return new Color( q, hsv.Value, p, hsv.Alpha );

			case 2:
				return new Color( p, hsv.Value, t, hsv.Alpha );

			case 3:
				return new Color( p, q, hsv.Value, hsv.Alpha );

			case 4:
				return new Color( t, p, hsv.Value, hsv.Alpha );

			default:
				return new Color( hsv.Value, p, q, hsv.Alpha );
		}
	}

	#region equality
	public static bool operator ==( ColorHsv left, ColorHsv right ) => left.Equals( right );
	public static bool operator !=( ColorHsv left, ColorHsv right ) => !(left == right);
	public override bool Equals( object obj ) => obj is ColorHsv o && Equals( o );
	public readonly bool Equals( ColorHsv o ) => (Hue, Saturation, Value, Alpha) == (o.Hue, o.Saturation, o.Value, o.Alpha);
	public readonly override int GetHashCode() => HashCode.Combine( Hue, Saturation, Value, Alpha );
	#endregion
}
