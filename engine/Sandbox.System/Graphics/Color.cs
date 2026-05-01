
using Sandbox;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

/// <summary>
/// Represents a color using 4 floats (rgba), with 0-1 range.
/// </summary>
[JsonConverter( typeof( Sandbox.Internal.JsonConvert.ColorConverter ) )]
[StructLayout( LayoutKind.Sequential )]
public partial struct Color : IEquatable<Color>
{
	/// <summary>
	/// The red color component, in range of 0-1, which <b>can be exceeded</b>.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float r;

	/// <summary>
	/// The green color component, in range of 0-1, which <b>can be exceeded</b>.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float g;

	/// <summary>
	/// The blue color component, in range of 0-1, which <b>can be exceeded</b>.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float b;

	/// <summary>
	/// The alpha/transparency color component, in range of 0 (fully transparent) to 1 (fully opaque), which <b>can be exceeded</b>.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float a;

	/// <summary>
	/// Initialize a color with each component set to given values, in range [0,1]
	/// </summary>
	[ActionGraphNode( "color.new" ), Title( "Color" ), Group( "Graphics/Color" ), Icon( "palette" )]
	public Color( float r, float g, float b, float a = 1.0f )
	{
		this.r = r;
		this.g = g;
		this.b = b;
		this.a = a;
	}

	/// <summary>
	/// Initialize a color with the same value for each color, but a different value for alpha
	/// </summary>
	public Color( in float rgb, in float a )
	{
		this.r = rgb;
		this.g = rgb;
		this.b = rgb;
		this.a = a;
	}

	/// <summary>
	/// Initialize a color with each component set to given value, even alpha.
	/// </summary>
	/// <param name="all">A number in range [0-1]</param>
	public Color( float all )
	{
		this.r = (float)all;
		this.g = (float)all;
		this.b = (float)all;
		this.a = (float)all;
	}

	/// <summary>
	/// Initialize from an integer of the form 0xAABBGGRR.
	/// </summary>
	/// <param name="raw">Packed integer of the form 0xAABBGGRR.</param>
	public Color( uint raw )
	{
		this.r = (raw & 255) / 255.0f;
		this.g = ((raw >> 8) & 255) / 255.0f;
		this.b = ((raw >> 16) & 255) / 255.0f;
		this.a = ((raw >> 24) & 255) / 255.0f;
	}

	/// <summary>
	/// Initialize from an integer of the form 0xAABBGGRR.
	/// </summary>
	/// <param name="raw">Packed integer of the form 0xAABBGGRR.</param>
	public Color( int raw )
	{
		this.r = (raw & 255) / 255.0f;
		this.g = ((raw >> 8) & 255) / 255.0f;
		this.b = ((raw >> 16) & 255) / 255.0f;
		this.a = ((raw >> 24) & 255) / 255.0f;
	}

	/// <summary>
	/// Returns this color with its alpha value changed
	/// </summary>
	/// <param name="alpha">The required alpha value, usually between 0-1</param>
	[ActionGraphInclude]
	public readonly Color WithAlpha( float alpha )
	{
		return this with { a = alpha };
	}

	/// <summary>
	/// Similar to <see cref="WithAlpha"/> but multiplies the alpha instead of replacing.
	/// </summary>
	public readonly Color WithAlphaMultiplied( float alpha )
	{
		return new Color( r, g, b, a * alpha );
	}

	/// <summary>
	/// Returns a new version with only the red, green, blue components multiplied
	/// </summary>
	public readonly Color WithColorMultiplied( float amount )
	{
		return new Color( r * amount, g * amount, b * amount, a );
	}

	/// <summary>
	/// Returns this color with its red value changed
	/// </summary>
	[ActionGraphInclude]
	public readonly Color WithRed( float red )
	{
		return new Color( red, g, b, a );
	}

	/// <summary>
	/// Returns this color with its green value changed
	/// </summary>
	[ActionGraphInclude]
	public readonly Color WithGreen( float green )
	{
		return new Color( r, green, b, a );
	}

	/// <summary>
	/// Returns this color with its blue value changed
	/// </summary>
	[ActionGraphInclude]
	public readonly Color WithBlue( float blue )
	{
		return new Color( r, g, blue, a );
	}

	/// <summary>
	/// Converts this color to a HSV format.
	/// </summary>
	/// <returns>The HSV color.</returns>
	public readonly ColorHsv ToHsv()
	{
		return this;
	}

	/// <summary>
	/// Converts this color to a RGBE format. You will lose opacity information as the last component is used for exponent.
	/// </summary>
	/// <returns></returns>
	internal readonly Color32 ToRgbe()
	{
		var max = MathF.Max( MathF.Abs( r ), MathF.Max( MathF.Abs( g ), MathF.Abs( b ) ) );
		if ( max < 1e-16f ) return new Color32( 0, 0, 0, 0 );

		var exp = MathF.ILogB( max ) + 1;
		var scale = MathF.ScaleB( 127.0f, -exp );

		return new Color32(
			(byte)(Math.Clamp( (int)(r * scale + 0.5f), sbyte.MinValue, sbyte.MaxValue ) + 128),
			(byte)(Math.Clamp( (int)(g * scale + 0.5f), sbyte.MinValue, sbyte.MaxValue ) + 128),
			(byte)(Math.Clamp( (int)(b * scale + 0.5f), sbyte.MinValue, sbyte.MaxValue ) + 128),
			(byte)(Math.Clamp( exp, sbyte.MinValue, sbyte.MaxValue ) + 128)
		);
	}

	/// <summary>
	/// Convert to a Color32 (a 32 bit color value)
	/// </summary>
	/// <param name="srgb">If true we'll convert to the srgb color space</param>
	public readonly Color32 ToColor32( bool srgb = false )
	{
		var FloatR = r;
		var FloatG = g;
		var FloatB = b;
		var FloatA = Math.Clamp( a, 0.0f, 1.0f );

		// if it's a HDR color, normalize it
		if ( FloatR > 1 || FloatG > 1 || FloatB > 1 )
		{
			var max = MathF.Max( MathF.Max( FloatR, FloatG ), FloatB );

			FloatR /= max;
			FloatG /= max;
			FloatB /= max;
		}

		if ( srgb )
		{
			FloatR = SrgbLinearToGamma( FloatR );
			FloatG = SrgbLinearToGamma( FloatG );
			FloatB = SrgbLinearToGamma( FloatB );
		}

		return new Color32
		{
			r = (byte)(FloatR * 255.999f).FloorToInt(),
			g = (byte)(FloatG * 255.999f).FloorToInt(),
			b = (byte)(FloatB * 255.999f).FloorToInt(),
			a = (byte)(FloatA * 255.999f).FloorToInt(),
		};
	}

	private static float SrgbGammaToLinear( float c )
	{
		return c <= 0.04045f
			? c / 12.92f
			: MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
	}

	private static float SrgbLinearToGamma( float c )
	{
		return c <= 0.0031308f
			? c * 12.92f
			: 1.055f * MathF.Pow( c, 1.0f / 2.4f ) - 0.055f;
	}

	/// <summary>
	/// Convert from sRGB to linear space, preserving alpha.
	/// </summary>
	internal Color ToLinear() => new Color( SrgbGammaToLinear( r ), SrgbGammaToLinear( g ), SrgbGammaToLinear( b ), a );

	/// <summary>
	/// Convert from linear space to sRGB, preserving alpha.
	/// </summary>
	internal Color ToSrgb() => new Color( SrgbLinearToGamma( r ), SrgbLinearToGamma( g ), SrgbLinearToGamma( b ), a );

	/// <summary>
	/// Returns a new color with each component being the minimum of the 2 given colors.
	/// </summary>
	/// <param name="a">Color A</param>
	/// <param name="b">Color B</param>
	/// <returns>The new color with minimum values.</returns>
	public static Color Min( in Color a, in Color b )
	{
		return new Color(
			Math.Min( a.r, b.r ),
			Math.Min( a.g, b.g ),
			Math.Min( a.b, b.b ),
			Math.Min( a.a, b.a ) );
	}

	/// <summary>
	/// Returns a new color with each component being the maximum of the 2 given colors.
	/// </summary>
	/// <param name="a">Color A</param>
	/// <param name="b">Color B</param>
	/// <returns>The new color with maximum values.</returns>
	public static Color Max( in Color a, in Color b )
	{
		return new Color(
			Math.Max( a.r, b.r ),
			Math.Max( a.g, b.g ),
			Math.Max( a.b, b.b ),
			Math.Max( a.a, b.a ) );
	}

	/// <summary>
	/// Returns the luminance of the color, basically it's grayscale value or "black and white version".
	/// </summary>
	[JsonIgnore]
	public readonly float Luminance => 0.299f * r + 0.587f * g + 0.114f * b;

	/// <summary>
	/// Returns true if this color can be represented in hexadecimal format (#RRGGBB[AA]).
	/// This may not be the case if the color components are outside of [0,1] range.
	/// </summary>
	public readonly bool IsRepresentableInHex => r >= 0 && r <= 1 && g >= 0 && g <= 1 && b >= 0 && b <= 1;

	/// <summary>
	/// Returns true if all components are between 0 and 1
	/// </summary>
	public readonly bool IsSdr => !IsHdr;

	/// <summary>
	/// Returns true if any component exceeds 1
	/// </summary>
	public readonly bool IsHdr => r > 1 || b > 1 || g > 1;

	/// <summary>
	/// Fully opaque white color.
	/// </summary>
	public static readonly Color White = new Color( 1, 1, 1 );

	/// <summary>
	/// Fully opaque gray color, right between white and black.
	/// </summary>
	public static readonly Color Gray = new Color( 0.5f, 0.5f, 0.5f );

	/// <summary>
	/// Fully opaque black color.
	/// </summary>
	public static readonly Color Black = new Color( 0, 0, 0 );

	/// <summary>
	/// Fully opaque pure red color.
	/// </summary>
	public static readonly Color Red = new Color( 1, 0, 0 );

	/// <summary>
	/// Fully opaque pure green color.
	/// </summary>
	public static readonly Color Green = new Color( 0, 1, 0 );

	/// <summary>
	/// Fully opaque pure blue color.
	/// </summary>
	public static readonly Color Blue = new Color( 0, 0, 1 );

	/// <summary>
	/// Fully opaque yellow color.
	/// </summary>
	public static readonly Color Yellow = new Color( 1, 1, 0 );

	/// <summary>
	/// Fully opaque orange color.
	/// </summary>
	public static readonly Color Orange = new Color( 1, 0.6f, 0 );

	/// <summary>
	/// Fully opaque cyan color.
	/// </summary>
	public static readonly Color Cyan = new Color( 0, 1, 1 );

	/// <summary>
	/// Fully opaque magenta color.
	/// </summary>
	public static readonly Color Magenta = new Color( 1, 0, 1 );

	/// <summary>
	/// Fully transparent color.
	/// </summary>
	public static readonly Color Transparent = new Color( 0, 0, 0, 0 );

	/// <summary>
	/// String representation of the form "#RRGGBB[AA]".
	/// </summary>
	[JsonIgnore] public readonly string Hex => ToColor32().Hex;

	/// <summary>
	/// String representation in the form of <see href="https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/rgba">rgba</see>( r, g, b, a )
	/// css function notation.
	/// </summary>
	[JsonIgnore] public readonly string Rgba => ToColor32().Rgba;

	/// <summary>
	/// String representation in the form of <see href="https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/rgb">rgb</see>( r, g, b )
	/// css function notation.
	/// </summary>
	[JsonIgnore] public readonly string Rgb => ToColor32().Rgb;

	/// <summary>
	/// Integer representation of the form 0xRRGGBBAA.
	/// </summary>
	[JsonIgnore] public readonly uint RgbaInt => ToColor32().RgbaInt;

	/// <summary>
	/// Integer representation of the form 0xRRGGBB.
	/// </summary>
	[JsonIgnore] public readonly uint RgbInt => ToColor32().RgbInt;

	/// <summary>
	/// Integer representation of the form 0xAABBGGRR as used by native code.
	/// </summary>
	[JsonIgnore] public readonly uint RawInt => ToColor32().RawInt;

	/// <summary>
	/// Returns a random color out of 8 preset colors.
	/// </summary>
	public static Color Random
	{
		get
		{
			var r = SandboxSystem.Random.Int( 6 );

			switch ( r )
			{
				case 0: return White;
				case 1: return Red;
				case 2: return Green;
				case 3: return Blue;
				case 4: return Yellow;
				case 5: return Cyan;
				case 6: return Magenta;
				default: return Black;
			}
		}
	}

	/// <summary>
	/// Converts the color to a string with given parameters.
	/// </summary>
	/// <param name="hex">Convert to Hex string if possible.</param>
	/// <param name="rgba">Convert to CSS rgba function</param>
	/// <returns>The string representation of this color.</returns>
	public readonly string ToString( bool hex, bool rgba )
	{
		if ( IsRepresentableInHex && hex )
		{
			return Hex;
		}

		if ( rgba && IsSdr )
		{
			return Rgba;
		}

		return ToString();
	}

	/// <summary>
	/// Returns a color whose components are averaged of all given colors.
	/// </summary>
	/// <param name="values">The colors to get average of.</param>
	/// <returns>The average color.</returns>
	public static Color Average( Color[] values )
	{
		float r = 0;
		float g = 0;
		float b = 0;
		float a = 0;

		foreach ( var v in values )
		{
			r += v.r;
			g += v.g;
			b += v.b;
			a += v.a;
		}

		var count = values.Length;

		return new Color( r / count, g / count, b / count, a / count );
	}

	/// <summary>
	/// Performs linear interpolation between two colors.
	/// </summary>
	/// <param name="a">The source color.</param>
	/// <param name="b">The target color.</param>
	/// <param name="frac">Fraction to the target color. 0 will return source color, 1 will return target color, 0.5 will "mix" the 2 colors equally.</param>
	/// <param name="clamped">Clamp fraction to range of [0,1]. If not clamped, the color will be extrapolated.</param>
	/// <returns>The interpolated color.</returns>
	[ActionGraphNode( "geom.lerp" ), Pure, Group( "Math/Geometry" ), Icon( "timeline" )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Color Lerp( in Color a, in Color b, [Range( 0f, 1f )] float frac, bool clamped = true )
	{
		if ( clamped && frac < 0 ) return a;
		if ( clamped && frac > 1 ) return b;

		return new Color( a.r.LerpTo( b.r, frac, clamped ), a.g.LerpTo( b.g, frac, clamped ), a.b.LerpTo( b.b, frac, clamped ), a.a.LerpTo( b.a, frac, clamped ) );
	}

	/// <summary>
	/// Performs linear interpolation between this and given colors.
	/// </summary>
	/// <param name="target">Color B</param>
	/// <param name="frac">Fraction, where 0 would return this, 0.5 would return a point between this and given colors, and 1 would return the given color.</param>
	/// <param name="clamp">Whether to clamp the fraction argument between [0,1]</param>
	/// <returns></returns>
	public readonly Color LerpTo( in Color target, float frac, bool clamp = true ) => Lerp( this, target, frac, clamp );

	/// <summary>
	/// Creates a color from 0-255 range inputs, converting them to 0-1 range.
	/// </summary>
	/// <param name="r">The red component.</param>
	/// <param name="g">The green component.</param>
	/// <param name="b">The blue component.</param>
	/// <param name="a">The alpha/transparency component.</param>
	/// <returns></returns>
	public static Color FromBytes( int r, int g, int b, int a = 255 )
	{
		return new Color( r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f );
	}

	/// <summary>
	/// Converts an integer of the form 0xRRGGBB into the color #RRGGBB with 100% alpha.
	/// </summary>
	/// <param name="rgb">Integer between 0x000000 and 0xffffff representing a color.</param>
	public static Color FromRgb( uint rgb ) => Color32.FromRgb( rgb );

	/// <summary>
	/// Converts an integer of the form 0xRRGGBBAA into the color #RRGGBBAA.
	/// </summary>
	/// <param name="rgba">Integer between 0x00000000 and 0xffffffff representing a color with alpha.</param>
	public static Color FromRgba( uint rgba ) => Color32.FromRgba( rgba );

	/// <summary>
	/// Increases or decreases this color's hue
	/// </summary>
	/// <param name="amount">A number between -360 and 360 to add to the color's hue</param>
	/// <returns>The adjusted color</returns>
	public readonly Color AdjustHue( float amount )
	{
		if ( amount < -360 || amount > 360 )
			throw new( $"Hue must be between -360 and 360 (got {amount})" );

		var hsv = ToHsv();
		hsv.Hue += amount;
		return hsv.ToColor();
	}

	/// <summary>
	/// Darkens the color by given amount.
	/// </summary>
	/// <param name="fraction">How much to darken the color by, in range of 0 (not at all) to 1 (fully black). Negative values will lighten the color.</param>
	/// <returns>The darkened color.</returns>
	public readonly Color Darken( float fraction )
	{
		var c = this;
		c.r *= 1.0f - fraction;
		c.g *= 1.0f - fraction;
		c.b *= 1.0f - fraction;
		return c;
	}

	/// <summary>
	/// Lightens the color by given amount.
	/// </summary>
	/// <param name="fraction">How much to lighten the color by, in range of 0 (not at all) to 1 (double the color). Negative values will darken the color.</param>
	/// <returns>The lightened color.</returns>
	public readonly Color Lighten( float fraction )
	{
		var c = this;
		c.r *= 1.0f + fraction;
		c.g *= 1.0f + fraction;
		c.b *= 1.0f + fraction;
		return c;
	}

	/// <summary>
	/// Returns inverted color. Alpha is unchanged.
	/// </summary>
	/// <returns>The inverted color.</returns>
	public readonly Color Invert()
	{
		var c = this;
		c.r = 1 - c.r;
		c.g = 1 - c.g;
		c.b = 1 - c.b;
		return c;
	}

	/// <summary>
	/// Desaturates the color by given amount.
	/// </summary>
	/// <param name="fraction">How much to desaturate the color by, in range of 0 (not at all) to 1 (no saturation, i.e. fully white). Negative values will saturate the color.</param>
	/// <returns>The desaturated color.</returns>
	public readonly Color Desaturate( float fraction )
	{
		ColorHsv c = this;
		c.Saturation *= (1.0f - fraction);
		return c;
	}

	/// <summary>
	/// Saturates the color by given amount.
	/// </summary>
	/// <param name="fraction">How much to saturate the color by, in range of 0 (not at all) to 1 (double the saturation). Negative values will desaturate the color.</param>
	/// <returns>The saturated color.</returns>
	public readonly Color Saturate( float fraction )
	{
		ColorHsv c = this;
		c.Saturation *= 1.0f + fraction;
		return c;
	}

	/// <summary>
	/// Returns how many color components would be changed between this color and another color
	/// </summary>
	public readonly int ComponentCountChangedBetweenColors( Color b )
	{
		int componentsChanged = 0;
		Color colorDifference = this - b;
		// We ignore the alpha for the changed components here
		for ( int i = 0; i < 3; i++ )
			componentsChanged += colorDifference[i] != 0.0f ? 1 : 0;
		return componentsChanged;
	}

	/// <summary>
	/// All the web colors by name that can be used in style sheets.
	/// </summary>
	readonly static Dictionary<string, string> WebColours = new Dictionary<string, string>
	{
		{ "aliceblue", "#F0F8FF" },
		{ "antiquewhite", "#FAEBD7" },
		{ "aqua", "#00FFFF" },
		{ "aquamarine", "#7FFFD4" },
		{ "azure", "#F0FFFF" },
		{ "beige", "#F5F5DC" },
		{ "bisque", "#FFE4C4" },
		{ "black", "#000000" },
		{ "blanchedalmond", "#FFEBCD" },
		{ "blue", "#0000FF" },
		{ "blueviolet", "#8A2BE2" },
		{ "brown", "#A52A2A" },
		{ "burlywood", "#DEB887" },
		{ "cadetblue", "#5F9EA0" },
		{ "chartreuse", "#7FFF00" },
		{ "chocolate", "#D2691E" },
		{ "coral", "#FF7F50" },
		{ "cornflowerblue", "#6495ED" },
		{ "cornsilk", "#FFF8DC" },
		{ "crimson", "#DC143C" },
		{ "cyan", "#00FFFF" },
		{ "darkblue", "#00008B" },
		{ "darkcyan", "#008B8B" },
		{ "darkgoldenrod", "#B8860B" },
		{ "darkgray", "#A9A9A9" },
		{ "darkgreen", "#006400" },
		{ "darkgrey", "#A9A9A9" },
		{ "darkkhaki", "#BDB76B" },
		{ "darkmagenta", "#8B008B" },
		{ "darkolivegreen", "#556B2F" },
		{ "darkorange", "#FF8C00" },
		{ "darkorchid", "#9932CC" },
		{ "darkred", "#8B0000" },
		{ "darksalmon", "#E9967A" },
		{ "darkseagreen", "#8FBC8F" },
		{ "darkslateblue", "#483D8B" },
		{ "darkslategray", "#2F4F4F" },
		{ "darkslategrey", "#2F4F4F" },
		{ "darkturquoise", "#00CED1" },
		{ "darkviolet", "#9400D3" },
		{ "deeppink", "#FF1493" },
		{ "deepskyblue", "#00BFFF" },
		{ "dimgray", "#696969" },
		{ "dimgrey", "#696969" },
		{ "dodgerblue", "#1E90FF" },
		{ "firebrick", "#B22222" },
		{ "floralwhite", "#FFFAF0" },
		{ "forestgreen", "#228B22" },
		{ "fuchsia", "#FF00FF" },
		{ "gainsboro", "#DCDCDC" },
		{ "ghostwhite", "#F8F8FF" },
		{ "gold", "#FFD700" },
		{ "goldenrod", "#DAA520" },
		{ "gray", "#808080" },
		{ "green", "#008000" },
		{ "greenyellow", "#ADFF2F" },
		{ "grey", "#808080" },
		{ "honeydew", "#F0FFF0" },
		{ "hotpink", "#FF69B4" },
		{ "indianred", "#CD5C5C" },
		{ "indigo", "#4B0082" },
		{ "ivory", "#FFFFF0" },
		{ "khaki", "#F0E68C" },
		{ "lavender", "#E6E6FA" },
		{ "lavenderblush", "#FFF0F5" },
		{ "lawngreen", "#7CFC00" },
		{ "lemonchiffon", "#FFFACD" },
		{ "lightblue", "#ADD8E6" },
		{ "lightcoral", "#F08080" },
		{ "lightcyan", "#E0FFFF" },
		{ "lightgoldenrodyellow", "#FAFAD2" },
		{ "lightgray", "#D3D3D3" },
		{ "lightgreen", "#90EE90" },
		{ "lightgrey", "#D3D3D3" },
		{ "lightpink", "#FFB6C1" },
		{ "lightsalmon", "#FFA07A" },
		{ "lightseagreen", "#20B2AA" },
		{ "lightskyblue", "#87CEFA" },
		{ "lightslategray", "#778899" },
		{ "lightslategrey", "#778899" },
		{ "lightsteelblue", "#B0C4DE" },
		{ "lightyellow", "#FFFFE0" },
		{ "lime", "#00FF00" },
		{ "limegreen", "#32CD32" },
		{ "linen", "#FAF0E6" },
		{ "magenta", "#FF00FF" },
		{ "maroon", "#800000" },
		{ "mediumaquamarine", "#66CDAA" },
		{ "mediumblue", "#0000CD" },
		{ "mediumorchid", "#BA55D3" },
		{ "mediumpurple", "#9370DB" },
		{ "mediumseagreen", "#3CB371" },
		{ "mediumslateblue", "#7B68EE" },
		{ "mediumspringgreen", "#00FA9A" },
		{ "mediumturquoise", "#48D1CC" },
		{ "mediumvioletred", "#C71585" },
		{ "midnightblue", "#191970" },
		{ "mintcream", "#F5FFFA" },
		{ "mistyrose", "#FFE4E1" },
		{ "moccasin", "#FFE4B5" },
		{ "navajowhite", "#FFDEAD" },
		{ "navy", "#000080" },
		{ "oldlace", "#FDF5E6" },
		{ "olive", "#808000" },
		{ "olivedrab", "#6B8E23" },
		{ "orange", "#FFA500" },
		{ "orangered", "#FF4500" },
		{ "orchid", "#DA70D6" },
		{ "palegoldenrod", "#EEE8AA" },
		{ "palegreen", "#98FB98" },
		{ "paleturquoise", "#AFEEEE" },
		{ "palevioletred", "#DB7093" },
		{ "papayawhip", "#FFEFD5" },
		{ "peachpuff", "#FFDAB9" },
		{ "peru", "#CD853F" },
		{ "pink", "#FFC0CB" },
		{ "plum", "#DDA0DD" },
		{ "powderblue", "#B0E0E6" },
		{ "purple", "#800080" },
		{ "rebeccapurple", "#663399" },
		{ "red", "#FF0000" },
		{ "rosybrown", "#BC8F8F" },
		{ "royalblue", "#4169E1" },
		{ "saddlebrown", "#8B4513" },
		{ "salmon", "#FA8072" },
		{ "sandybrown", "#F4A460" },
		{ "seagreen", "#2E8B57" },
		{ "seashell", "#FFF5EE" },
		{ "sienna", "#A0522D" },
		{ "silver", "#C0C0C0" },
		{ "skyblue", "#87CEEB" },
		{ "slateblue", "#6A5ACD" },
		{ "slategray", "#708090" },
		{ "slategrey", "#708090" },
		{ "snow", "#FFFAFA" },
		{ "springgreen", "#00FF7F" },
		{ "steelblue", "#4682B4" },
		{ "tan", "#D2B48C" },
		{ "teal", "#008080" },
		{ "thistle", "#D8BFD8" },
		{ "tomato", "#FF6347" },
		{ "transparent", "#AAAAAA00" },
		{ "turquoise", "#40E0D0" },
		{ "violet", "#EE82EE" },
		{ "wheat", "#F5DEB3" },
		{ "white", "#FFFFFF" },
		{ "whitesmoke", "#F5F5F5" },
		{ "yellow", "#FFFF00" },
		{ "yellowgreen", "#9ACD32" },
	};

	/// <summary>
	/// Parse the color from a string. Many common formats are supported.
	/// </summary>
	/// <param name="value">The string to parse.</param>
	/// <returns>The parsed color if operation completed successfully.</returns>
	public static Color? Parse( string value )
	{
		var p = new Parse( value );
		return Parse( ref p );
	}

	/// <summary>
	/// Try to parse the color. Returns true on success
	/// </summary>
	public static bool TryParse( string value, out Color color )
	{
		color = default;

		if ( string.IsNullOrWhiteSpace( value ) )
			return false;

		Color? parsed = Parse( value );
		if ( !parsed.HasValue )
			return false;

		color = parsed.Value;
		return true;
	}

	internal static Color? Parse( ref Parse p, bool isColorFunction = false )
	{
		p = p.SkipWhitespaceAndNewlines();

		if ( p.Current == '#' )
		{
			var restoreP = p;

			p.Pointer++;

			var hex = p.ReadUntilOrEnd( " ", true );
			if ( hex == null ) return null;

			//
			// #F0F
			//
			if ( hex.Length == 3 )
			{
				var r = hex.Substring( 0, 1 );
				var g = hex.Substring( 1, 1 );
				var b = hex.Substring( 2, 1 );

				hex = $"{r}{r}{g}{g}{b}{b}FF";
			}

			//
			// #F0FF
			//
			if ( hex.Length == 4 )
			{
				var r = hex.Substring( 0, 1 );
				var g = hex.Substring( 1, 1 );
				var b = hex.Substring( 2, 1 );
				var a = hex.Substring( 3, 1 );

				hex = $"{r}{r}{g}{g}{b}{b}{a}{a}";
			}

			//
			// #FF00FF
			//
			if ( hex.Length == 6 )
			{
				hex = $"{hex}FF";
			}

			//
			// #FF00FFFF
			//
			if ( hex.Length == 8 )
			{
				if ( Int32.TryParse( hex, System.Globalization.NumberStyles.HexNumber, null, out int color32 ) )
				{
					var R = (byte)((color32 >> 24) & byte.MaxValue /*255*/);
					var G = (byte)((color32 >> 16) & byte.MaxValue);
					var B = (byte)((color32 >> 8) & byte.MaxValue);
					var A = (byte)((color32) & byte.MaxValue);

					return FromBytes( R, G, B, A );
				}
			}

			p = restoreP;
		}

		if ( p.Is( "rgb" ) )
		{
			var restoreP = p;

			p.Pointer += 3;

			// rgba is just an alias for rgb
			if ( p.Current == 'a' )
			{
				p.Pointer++;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return null;

			if ( p.Current == '(' || isColorFunction )
			{
				if ( !isColorFunction )
					p.Pointer++;

				float r = 0;
				float g = 0;
				float b = 0;
				float alpha = 1.0f;

				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) return null;

				if ( p.IsDigit )
				{
					if ( !p.TryReadFloat( out r ) ) return null;
					if ( p.Current == '%' )
					{
						r = (r / 100.0f) * 255.0f;
						p.Pointer++;
					}

					p = p.SkipWhitespaceAndNewlines( "," );
					if ( !p.TryReadFloat( out g ) ) return null;
					if ( p.Current == '%' )
					{
						g = (g / 100.0f) * 255.0f;
						p.Pointer++;
					}

					p = p.SkipWhitespaceAndNewlines( "," );
					if ( !p.TryReadFloat( out b ) ) return null;
					if ( p.Current == '%' )
					{
						b = (b / 100.0f) * 255.0f;
						p.Pointer++;
					}

					p = p.SkipWhitespaceAndNewlines( ",/" );

					r /= 255.0f;
					g /= 255.0f;
					b /= 255.0f;
				}
				else
				{
					if ( !p.TryReadColor( out var rgb ) )
						return null;

					r = rgb.r;
					g = rgb.g;
					b = rgb.b;
					alpha = rgb.a;

					p = p.SkipWhitespaceAndNewlines( ",/" );
				}

				if ( p.TryReadFloat( out float _alpha ) )
				{
					if ( p.Current == '%' )
					{
						alpha = _alpha / 100.0f;
						p.Pointer++;
					}
					else
					{
						alpha = _alpha;
					}
					p = p.SkipWhitespaceAndNewlines();
				}

				if ( p.Is( ')' ) )
				{
					p.Pointer++;
					return new Color( r, g, b, alpha );
				}
			}

			p = restoreP;
		}

		if ( p.Is( "hsl" ) )
		{
			var restoreP = p;

			p.Pointer += 3;

			if ( p.Current == 'a' )
			{
				p.Pointer++;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return null;

			if ( p.Current == '(' || isColorFunction )
			{
				if ( !isColorFunction )
					p.Pointer++;

				float h = 0.0f;
				float s = 0.0f;
				float l = 0.0f;
				float a = 1.0f;

				if ( p.IsDigit )
				{
					if ( !p.TryReadFloat( out h ) ) return null;

					if ( p.IsLetter )
					{
						h = StyleHelpers.RotationDegrees( h, p.ReadUntilWhitespaceOrNewlineOrEnd( "," ) );
					}

					p = p.SkipWhitespaceAndNewlines( "," );
					if ( !p.TryReadFloat( out s ) ) return null;
					p = p.SkipWhitespaceAndNewlines( ",%" );
					if ( !p.TryReadFloat( out l ) ) return null;
					p = p.SkipWhitespaceAndNewlines( ",%" );

					h %= 360.0f;
					s /= 100.0f;
					l /= 100.0f;

					// Functional syntax
					p = p.SkipWhitespaceAndNewlines( "/" );

					// Alpha is optional even in HSLA
					// Alpha can ALSO be used with HSL, so lets attempt to fetch alpha data
					if ( p.TryReadFloat( out a ) )
					{
						if ( p.Current == '%' )
						{
							a /= 100.0f;
							p.Pointer++;
						}
					}
					else
					{
						a = 1.0f;
					}
				}
				else
				{
					return null;
				}

				if ( p.Current != ')' )
				{
					return null;
				}
				p.Pointer++;

				// Convert from HSL to HSV instead of doing HSL->RGB
				if ( l < 0.5f )
				{
					s *= l;
				}
				else
				{
					s *= 1.0f - l;
				}

				return new ColorHsv( h, 2.0f * s / (l + s), l + s, a );
			}


			p = restoreP;

		}

		//
		// raw color rgba - "2.0f, 1.0f, 1.0f, 1.0f"
		//
		if ( p.IsDigit )
		{
			var restoreP = p;

			if ( p.TryReadFloat( out var r ) )
			{
				p.SkipWhitespaceAndNewlines();
				if ( p.Current == ',' )
				{
					p.TrySkipCommaSeparation();
					p.TryReadFloat( out var g );
					p.TrySkipCommaSeparation();
					p.TryReadFloat( out var b );
					p.TrySkipCommaSeparation();
					p.TryReadFloat( out var a );
					return new Color( r, g, b, a );
				}
			}

			p = restoreP;
		}

		// "255 255 255 255" format for FGD
		if ( p.IsDigit )
		{
			var restoreP = p;

			var r = p.ReadUntilOrEnd( " ", true );
			p = p.SkipWhitespaceAndNewlines();
			var g = p.ReadUntilOrEnd( " ", true );
			p = p.SkipWhitespaceAndNewlines();
			var b = p.ReadUntilOrEnd( " ", true );
			p = p.SkipWhitespaceAndNewlines();

			var a = "255";
			if ( p.IsDigit )
			{
				a = p.ReadUntilOrEnd( " ", true );
				p = p.SkipWhitespaceAndNewlines();
			}

			if ( int.TryParse( r, NumberStyles.None, CultureInfo.InvariantCulture, out var rValue ) &&
				 int.TryParse( g, NumberStyles.None, CultureInfo.InvariantCulture, out var gValue ) &&
				 int.TryParse( b, NumberStyles.None, CultureInfo.InvariantCulture, out var bValue ) &&
				 int.TryParse( a, NumberStyles.None, CultureInfo.InvariantCulture, out var aValue ) )
			{
				return FromBytes( rValue, gValue, bValue, aValue );
			}

			p = restoreP;
		}

		// adjust-hue( $color, $degrees ) => color
		if ( p.TrySkip( "adjust-hue(" ) )
		{
			p.TryReadColor( out var c );
			p.SkipWhitespaceAndNewlines();

			if ( !p.TrySkip( "," ) )
				return null;

			p.SkipWhitespaceAndNewlines();

			if ( !p.TryReadLength( out var len ) )
				return null;

			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( ")" ) )
				return null;

			return c.AdjustHue( len.GetPixels( 1.0f ) );
		}

		// darken( $color, $amount ) => color
		if ( p.TrySkip( "darken(" ) )
		{
			p.TryReadColor( out var c );
			p.SkipWhitespaceAndNewlines();

			if ( !p.TrySkip( "," ) )
				return null;

			p.SkipWhitespaceAndNewlines();

			if ( !p.TryReadLength( out var len ) )
				return null;

			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( ")" ) )
				return null;

			return c.Darken( len.GetFraction() );
		}

		// lighten( $color, $amount ) => color
		if ( p.TrySkip( "lighten(" ) )
		{
			p.TryReadColor( out var c );
			p.SkipWhitespaceAndNewlines();

			if ( !p.TrySkip( "," ) )
				return null;

			p.SkipWhitespaceAndNewlines();

			if ( !p.TryReadLength( out var len ) )
				return null;

			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( ")" ) )
				return null;

			return c.Lighten( len.GetFraction() );
		}

		// invert( $color ) => color
		if ( p.TrySkip( "invert(" ) )
		{
			p.TryReadColor( out var c );

			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( ")" ) )
				return null;

			return c.Invert();
		}

		// mix( $color-a, $color-b, $amount ) => color
		if ( p.TrySkip( "mix(" ) || p.TrySkip( "lerp(" ) )
		{
			p.TryReadColor( out var a );
			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( "," ) )
				return null;

			p.TryReadColor( out var b );
			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( "," ) )
				return null;

			p.SkipWhitespaceAndNewlines();
			if ( !p.TryReadLength( out var len ) )
				return null;

			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( ")" ) )
				return null;

			return Lerp( a, b, len.GetFraction() );
		}

		// desaturate( $color, $amount ) => color
		if ( p.TrySkip( "desaturate(" ) )
		{
			p.TryReadColor( out var c );
			p.SkipWhitespaceAndNewlines();

			if ( !p.TrySkip( "," ) )
				return null;

			p.SkipWhitespaceAndNewlines();

			if ( !p.TryReadLength( out var len ) )
				return null;

			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( ")" ) )
				return null;

			return c.Desaturate( len.GetFraction() );
		}

		// saturate( $color, $amount ) => color
		if ( p.TrySkip( "saturate(" ) )
		{
			p.TryReadColor( out var c );
			p.SkipWhitespaceAndNewlines();

			if ( !p.TrySkip( "," ) )
				return null;

			p.SkipWhitespaceAndNewlines();

			if ( !p.TryReadLength( out var len ) )
				return null;

			p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( ")" ) )
				return null;

			return c.Saturate( len.GetFraction() );
		}

		// color( $color ) => color
		if ( p.TrySkip( "color(" ) )
		{
			return Color.Parse( ref p, true );
		}

		if ( p.IsLetter )
		{
			var restoreP = p;

			var color = p.ReadWord( ",)", true );
			if ( color != null && WebColours.TryGetValue( color, out var colorValue ) )
				return Parse( colorValue );

			p = restoreP;
		}

		return null;
	}

	public override readonly string ToString()
	{
		return $"{r:0.00}, {g:0.00}, {b:0.00}, {a:0.00}";
	}

	#region operators
	/// <summary>
	/// Multiply each component of this color by given value.
	/// </summary>
	/// <param name="c1">Color to multiply.</param>
	/// <param name="f">Scalar value to multiply each color component by.</param>
	/// <returns>The multiplication result.</returns>
	public static Color operator *( in Color c1, float f ) => new Color( c1.r * f, c1.g * f, c1.b * f, c1.a * f );
	public static Color operator *( in Color c1, in Color c2 ) => new Color( c1.r * c2.r, c1.g * c2.g, c1.b * c2.b, c1.a * c2.a );
	public static Color operator +( in Color c1, in Color c2 ) => new Color( c1.r + c2.r, c1.g + c2.g, c1.b + c2.b, c1.a + c2.a );
	public static Color operator -( in Color c1, in Color c2 ) => new Color( c1.r - c2.r, c1.g - c2.g, c1.b - c2.b, c1.a - c2.a );
	public static implicit operator Color( in Vector4 value ) => new Color( (float)value.x, (float)value.y, (float)value.z, (float)value.w );
	public static implicit operator Color( in Vector3 value ) => new Color( value.x, value.y, value.z );
	public static implicit operator Color( in Color32 color ) => color.ToColor();
	public static implicit operator Color( string value ) => Parse( value ) ?? new Color( 1, 0, 1, 1 );

	/// <summary>
	/// Get color components by numerical index.
	/// </summary>
	/// <param name="index">Index of the color component to request, 0-3 being RGBA</param>
	/// <returns>The requested color component</returns>
	/// <exception cref="IndexOutOfRangeException">Thrown when requested index is out of range of [0,3]</exception>
	[JsonIgnore]
	public float this[int index]
	{
		readonly get
		{
			switch ( index )
			{
				case 0: return r;
				case 1: return g;
				case 2: return b;
				case 3: return a;
				default:
					throw new IndexOutOfRangeException();
			}
		}
		set
		{
			switch ( index )
			{
				case 0: r = value; break;
				case 1: g = value; break;
				case 2: b = value; break;
				case 3: a = value; break;
				default:
					throw new IndexOutOfRangeException();
			}
		}
	}
	#endregion

	#region equality
	public static bool operator ==( in Color left, in Color right ) => left.Equals( right );
	public static bool operator !=( in Color left, in Color right ) => !(left == right);
	public override readonly bool Equals( object obj ) => obj is Color o && Equals( o );
	public readonly bool Equals( Color o ) => (r, g, b, a) == (o.r, o.g, o.b, o.a);
	public readonly override int GetHashCode() => HashCode.Combine( r, g, b, a );
	#endregion
}
