using Sandbox;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

[StructLayout( LayoutKind.Sequential )]
internal struct Color24
{
	public byte r;
	public byte g;
	public byte b;
}

/// <summary>
/// A 32bit color, commonly used by things like vertex buffers.
///
/// The functionality on this is purposely left minimal so we're encouraged to use the regular <see cref="Color"/> struct.
/// </summary>
[JsonConverter( typeof( Sandbox.Internal.JsonConvert.Color32Converter ) )]
[StructLayout( LayoutKind.Sequential )]
public struct Color32 : IEquatable<Color32>
{
	/// <summary>
	/// The red color component, in range of 0-255.
	/// </summary>
	public byte r;

	/// <summary>
	/// The green color component, in range of 0-255.
	/// </summary>
	public byte g;

	/// <summary>
	/// The blue color component, in range of 0-255.
	/// </summary>
	public byte b;

	/// <summary>
	/// The alpha/transparency color component, in range of 0 (fully transparent) to 255 (fully opaque).
	/// </summary>
	public byte a;

	/// <summary>
	/// Initialize a color with each component set to given values, in range [0,255]
	/// </summary>
	public Color32( byte r, byte g, byte b, byte a = 255 )
	{
		this.r = r;
		this.g = g;
		this.b = b;
		this.a = a;
	}

	/// <summary>
	/// Initialize a color with each component set to given value, even alpha.
	/// </summary>
	/// <param name="all">A number in range [0-255]</param>
	public Color32( byte all )
	{
		this.r = all;
		this.g = all;
		this.b = all;
		this.a = all;
	}

	/// <summary>
	/// Initialize from an integer of the form 0xAABBGGRR.
	/// </summary>
	/// <param name="raw">Packed integer of the form 0xAABBGGRR.</param>
	public Color32( uint raw )
	{
		this.r = (byte)(raw & 255);
		this.g = (byte)((raw >> 8) & 255);
		this.b = (byte)((raw >> 16) & 255);
		this.a = (byte)((raw >> 24) & 255);
	}

	/// <summary>
	/// Initialize from an integer of the form 0xAABBGGRR.
	/// </summary>
	/// <param name="raw">Packed integer of the form 0xAABBGGRR.</param>
	public Color32( int raw )
	{
		this.r = (byte)(raw & 255);
		this.g = (byte)((raw >> 8) & 255);
		this.b = (byte)((raw >> 16) & 255);
		this.a = (byte)((raw >> 24) & 255);
	}

	/// <summary>
	/// A constant representing a fully opaque color white.
	/// </summary>
	public static Color32 White { get; } = new Color32( 255, 255, 255 );

	/// <summary>
	/// A constant representing a fully opaque color black.
	/// </summary>
	public static Color32 Black { get; } = new Color32( 0, 0, 0 );

	/// <summary>
	/// A constant representing a fully transparent color.
	/// </summary>
	public static Color32 Transparent { get; } = new Color32( 0, 0, 0, 0 );

	/// <summary>
	/// Converts an integer of the form 0xRRGGBB into the color #RRGGBB with 100% alpha.
	/// </summary>
	/// <param name="rgb">Integer between 0x000000 and 0xffffff representing a color.</param>
	public static Color32 FromRgb( uint rgb )
	{
		return new Color32( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
	}

	/// <summary>
	/// Converts an integer of the form 0xRRGGBBAA into the color #RRGGBBAA.
	/// </summary>
	/// <param name="rgba">Integer between 0x00000000 and 0xffffffff representing a color with alpha.</param>
	public static Color32 FromRgba( uint rgba )
	{
		return new Color32( (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba );
	}

	/// <summary>
	/// Convert this object to <see cref="Color"/>.
	/// </summary>
	/// <returns>The converted color struct.</returns>
	public Color ToColor()
	{
		return new Color( r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f );
	}

	/// <summary>
	/// Convert this object to <see cref="Color"/>.
	/// </summary>
	/// <param name="srgb">If true we'll convert from the srgb color space to linear</param>
	/// <returns>The converted color struct.</returns>
	public Color ToColor( bool srgb )
	{
		var c = ToColor();

		if ( srgb )
		{
			return c.ToLinear();
		}

		return c;
	}

	public static implicit operator Color32( Color value ) => value.ToColor32();

	/// <summary>
	/// Returns a new color with each component being the minimum of the 2 given colors.
	/// </summary>
	/// <param name="a">Color A</param>
	/// <param name="b">Color B</param>
	/// <returns>The new color with minimum values.</returns>
	public static Color32 Min( Color32 a, Color32 b )
	{
		return new Color32(
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
	public static Color32 Max( Color32 a, Color32 b )
	{
		return new Color32(
			Math.Max( a.r, b.r ),
			Math.Max( a.g, b.g ),
			Math.Max( a.b, b.b ),
			Math.Max( a.a, b.a ) );
	}

	/// <summary>
	/// String representation of the form "#RRGGBB[AA]".
	/// </summary>
	public string Hex => a >= 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";

	/// <summary>
	/// String representation in the form of <see href="https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/rgba">rgba</see>( r, g, b, a )
	/// css function notation.
	/// </summary>
	public string Rgba => $"rgba( {r}, {g}, {b}, {a / 255f} )";

	/// <summary>
	/// String representation in the form of <see href="https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/rgb">rgb</see>( r, g, b )
	/// css function notation.
	/// </summary>
	public string Rgb => $"rgb( {r}, {g}, {b} )";

	/// <summary>
	/// Integer representation of the form 0xRRGGBBAA.
	/// </summary>
	public uint RgbaInt => ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;

	/// <summary>
	/// Integer representation of the form 0xRRGGBB.
	/// </summary>
	public uint RgbInt => ((uint)r << 16) | ((uint)g << 8) | b;

	/// <summary>
	/// Integer representation of the form 0xAABBGGRR as used by native code.
	/// </summary>
	public uint RawInt => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

	public override string ToString()
	{
		return $"R:{r:0.00},G:{g:0.00},B:{b:0.00},A:{a:0.00}";
	}


	/// <summary>
	/// Write this color to a binary writer.
	/// </summary>
	/// <param name="writer">Writer to write to.</param>
	public void Write( BinaryWriter writer )
	{
		writer.Write( r );
		writer.Write( g );
		writer.Write( b );
		writer.Write( b );
	}

	/// <summary>
	/// Read a color from binary reader.
	/// </summary>
	/// <param name="reader">Reader to read from.</param>
	/// <returns>The read color.</returns>
	public static Color32 Read( BinaryReader reader )
	{
		return new Color32( reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte() );
	}

	/// <summary>
	/// Parse a string to a color, in format "255 255 255 255" or "255,255,255". Alpha is optional.
	/// </summary>
	/// <param name="value">The value to parse.</param>
	/// <returns>The color parsed from the string, or null if we failed to do so.</returns>
	public static Color32? Parse( string value )
	{
		string[] values = value.Split( ' ', ',' );
		if ( values.Length == 3 || values.Length == 4 )
		{
			var color = White;
			color.r = byte.Parse( values[0] );
			color.g = byte.Parse( values[1] );
			color.b = byte.Parse( values[2] );

			if ( values.Length == 4 ) color.a = byte.Parse( values[3] );
			return color;
		}

		return null;
	}

	#region equality
	public static bool operator ==( Color32 left, Color32 right ) => left.Equals( right );
	public static bool operator !=( Color32 left, Color32 right ) => !(left == right);
	public override bool Equals( object obj ) => obj is Color32 color && Equals( color );
	public readonly bool Equals( Color32 o ) => (r, g, b, a) == (o.r, o.g, o.b, o.a);
	public readonly override int GetHashCode() => HashCode.Combine( r, g, b, a );
	#endregion
}
