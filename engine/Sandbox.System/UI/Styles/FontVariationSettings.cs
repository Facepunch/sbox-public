using System;
using System.Globalization;

namespace Sandbox.UI;

/// <summary>
/// OpenType axis values from a CSS font-variation-settings declaration. The default value represents normal.
/// </summary>
public readonly record struct FontVariationSettings
{
	/// <summary>
	/// The FILL axis. Material Symbols range from outlined at 0 to filled at 1.
	/// </summary>
	public float? Fill { get; init; }

	/// <summary>
	/// The wght axis. Defaults to the panel's font weight.
	/// </summary>
	public float? Weight { get; init; }

	/// <summary>
	/// The GRAD axis. Adjusts stroke thickness without changing the space a glyph occupies.
	/// </summary>
	public float? Grade { get; init; }

	/// <summary>
	/// The opsz axis. Defaults to the panel's optical sizing behavior.
	/// </summary>
	public float? OpticalSize { get; init; }

	/// <summary>
	/// Reads normal or a comma-separated list of quoted four-character tags and numeric values.
	/// </summary>
	public static bool TryParse( string value, out FontVariationSettings settings )
	{
		settings = default;
		var p = new Parse( value ).SkipWhitespaceAndNewlines();
		if ( p.TrySkip( "normal", ignorecase: true ) ) return p.SkipWhitespaceAndNewlines().IsEnd;

		var parsed = new FontVariationSettings();
		do
		{
			if ( !p.IsOneOf( "\"'" ) ) return false;
			var quote = p.Read( 1 );
			var tag = p.ReadUntil( quote );
			if ( tag is not { Length: 4 } || tag.AsSpan().ContainsAnyExceptInRange( ' ', '~' ) ) return false;
			p.TrySkip( quote );

			p.SkipWhitespaceAndNewlines();
			var token = p.ReadChars( "+-0123456789.eE", readUntilEnd: true );
			if ( !float.TryParse( token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) || !float.IsFinite( number ) ) return false;
			// CSS requires a digit after a decimal point, even before an exponent.
			var dot = token.IndexOf( '.' );
			if ( dot >= 0 && (dot + 1 == token.Length || !char.IsAsciiDigit( token[dot + 1] )) ) return false;

			parsed = tag switch
			{
				"FILL" => parsed with { Fill = number },
				"wght" => parsed with { Weight = number },
				"GRAD" => parsed with { Grade = number },
				"opsz" => parsed with { OpticalSize = number },
				_ => parsed
			};
		}
		while ( p.TrySkipCommaSeparation() );

		if ( !p.IsEnd ) return false;
		settings = parsed;
		return true;
	}

	/// <summary>
	/// Looks up a case-sensitive axis tag.
	/// </summary>
	public bool TryGetValue( string tag, out float value )
	{
		var axis = tag switch
		{
			"FILL" => Fill,
			"wght" => Weight,
			"GRAD" => Grade,
			"opsz" => OpticalSize,
			_ => null
		};
		value = axis.GetValueOrDefault();
		return axis.HasValue && float.IsFinite( value );
	}

	/// <summary>
	/// Distinguishes an omitted axis from zero so both changes invalidate text layout.
	/// </summary>
	public override int GetHashCode() => HashCode.Combine( Fill ?? float.NaN, Weight ?? float.NaN, Grade ?? float.NaN, OpticalSize ?? float.NaN );

	internal static FontVariationSettings Lerp( FontVariationSettings from, FontVariationSettings to, float delta )
	{
		if ( delta <= 0 || from == to ) return from;
		if ( delta >= 1 ) return to;
		if ( from.Fill.HasValue != to.Fill.HasValue || from.Weight.HasValue != to.Weight.HasValue ||
			from.Grade.HasValue != to.Grade.HasValue || from.OpticalSize.HasValue != to.OpticalSize.HasValue )
			return delta < 0.5f ? from : to;

		// Repeat animations reuse the same typefaces and glyphs even when frame timings differ.
		var progress = MathF.Round( delta * 256 ) / 256;
		float? Blend( float? a, float? b ) => a.HasValue ? float.Lerp( a.Value, b.Value, progress ) : null;
		return new FontVariationSettings
		{
			Fill = Blend( from.Fill, to.Fill ),
			Weight = Blend( from.Weight, to.Weight ),
			Grade = Blend( from.Grade, to.Grade ),
			OpticalSize = Blend( from.OpticalSize, to.OpticalSize )
		};
	}
}
