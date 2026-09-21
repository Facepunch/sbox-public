using System.Globalization;

namespace Sandbox.UI;

/// <summary>
/// A colour the way the picker edits it: a <see cref="ColorHsv"/> for the part the controls
/// show, and a brightness multiplier carrying anything over 1 so the controls only ever deal
/// in 0..1.
/// </summary>
internal struct PickerColor
{
	/// <summary>
	/// Hue, saturation, value and alpha, all within range.
	/// </summary>
	public ColorHsv Hsv;

	/// <summary>
	/// Multiplier on the colour in linear space. 1 is SDR, anything above is HDR. Never below 1 -
	/// darker is a lower value.
	/// </summary>
	public float Brightness;

	public PickerColor( ColorHsv hsv, float brightness = 1.0f )
	{
		Hsv = hsv;
		Brightness = brightness;
	}

	/// <summary>
	/// Split a colour into something the picker can show. Components over 1 fold into
	/// <see cref="Brightness"/>, split in linear space so the "* N" text form round-trips through
	/// <see cref="Color.Parse(string)"/>. Greys have no hue of their own, so they keep <paramref name="hueIfGrey"/>.
	/// </summary>
	public static PickerColor FromColor( Color color, float hueIfGrey = 0.0f )
	{
		var brightness = 1.0f;
		var baseColor = color;

		var linear = color.ToLinear();
		var max = MathF.Max( linear.r, MathF.Max( linear.g, linear.b ) );
		if ( max > 1.0f )
		{
			brightness = max;
			baseColor = new Color( linear.r / max, linear.g / max, linear.b / max, color.a ).ToSrgb();
		}

		var hsv = baseColor.ToHsv();
		if ( hsv.Saturation <= 0.0f || hsv.Value <= 0.0f ) hsv.Hue = hueIfGrey;

		return new PickerColor( hsv, brightness );
	}

	/// <summary>
	/// The colour with brightness left out - what the swatches and strips show.
	/// </summary>
	public readonly Color BaseColor => Hsv.ToColor();

	/// <summary>
	/// The real colour, brightness included.
	/// </summary>
	public readonly Color ToColor() => Brightness == 1.0f ? BaseColor : BaseColor.ScaleBrightness( Brightness );

	/// <summary>
	/// Hex, with alpha when it isn't 1, and "* N" when it's brighter than SDR.
	/// </summary>
	public readonly string ToText()
	{
		var text = BaseColor.Hex;
		if ( Brightness == 1.0f ) return text;

		return $"{text} * {Brightness.ToString( "0.##", CultureInfo.InvariantCulture )}";
	}

	/// <summary>
	/// Anything <see cref="Color.Parse(string)"/> takes: hex, rgb(), names, "r, g, b, a", each with an optional "* N".
	/// A "* N" on the end is kept as typed, rather than being re-split the way a raw HDR value is.
	/// </summary>
	public static bool TryParse( string text, float hueIfGrey, out PickerColor result )
	{
		result = default;

		if ( string.IsNullOrWhiteSpace( text ) )
			return false;

		// Below 1 isn't brightness, it's a darker colour - that goes through the whole parse below
		var star = text.LastIndexOf( '*' );
		if ( star >= 0
			&& float.TryParse( text[(star + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var brightness )
			&& brightness >= 1.0f
			&& Color.TryParse( text[..star], out var baseColor )
			&& baseColor.IsSdr )
		{
			result = FromColor( baseColor, hueIfGrey );
			result.Brightness = brightness;
			return true;
		}

		if ( !Color.TryParse( text, out var color ) )
			return false;

		result = FromColor( color, hueIfGrey );
		return true;
	}
}
