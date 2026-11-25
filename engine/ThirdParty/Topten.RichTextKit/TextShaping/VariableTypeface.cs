using Sandbox.UI;
using SkiaSharp;
using System.Runtime.CompilerServices;
using Coordinate = SkiaSharp.SKFontVariationPositionCoordinate;

namespace Topten.RichTextKit;

/// <summary>
/// Reuses a Skia typeface for each distinct set of effective coordinates. The glyph atlas and
/// text shaper both use that typeface as their key, including when a font supplies a fallback glyph.
/// </summary>
internal sealed class VariableTypeface
{
	static readonly ConditionalWeakTable<SKTypeface, VariableTypeface> Fonts = new();
	static readonly IEqualityComparer<Coordinate[]> CoordinateComparer = EqualityComparer<Coordinate[]>.Create( ( a, b ) => a.AsSpan().SequenceEqual( b ), GetCoordinateHash );
	readonly (SKFontVariationAxis Axis, string Tag, float Initial)[] _axes;
	readonly Coordinate[] _coordinates;
	readonly Dictionary<Coordinate[], SKTypeface> _variants;

	VariableTypeface( SKTypeface face )
	{
		if ( face.VariationDesignParameterCount <= 0 ) return;

		var axes = face.VariationDesignParameters;
		if ( axes.Any( axis => axis.Min > axis.Max ) ) return;

		var positions = new Dictionary<SKFourByteTag, float>();
		foreach ( var position in face.VariationDesignPosition ) positions[position.Axis] = position.Value;

		_axes = axes.Select( axis => (axis, axis.Tag.ToString(), positions.GetValueOrDefault( axis.Tag, axis.Default )) ).ToArray();
		_coordinates = axes.Select( axis => new Coordinate { Axis = axis.Tag } ).ToArray();
		_variants = new( CoordinateComparer );
	}

	internal static SKTypeface Resolve( SKTypeface face, IStyle style )
	{
		var font = Fonts.GetValue( face, static f => new( f ) );
		if ( font._axes is null ) return face;

		lock ( font._variants )
		{
			return font.ResolveCoordinates( face, style );
		}
	}

	SKTypeface ResolveCoordinates( SKTypeface face, IStyle style )
	{
		bool changed = false;
		for ( int i = 0; i < _axes.Length; i++ )
		{
			var (axis, tag, initial) = _axes[i];
			if ( !style.FontVariationSettings.TryGetValue( tag, out var value ) )
			{
				value = tag switch
				{
					"wght" => style.FontWeight,
					"opsz" when style.FontOpticalSizing == FontOpticalSizing.Auto => MathF.Round( style.FontSize ),
					_ => initial
				};
			}
			_coordinates[i].Value = Math.Clamp( value, axis.Min, axis.Max );
			changed |= _coordinates[i].Value != initial;
		}
		if ( !changed ) return face;
		if ( _variants.TryGetValue( _coordinates, out var variant ) ) return variant;

		var coordinates = _coordinates.AsSpan().ToArray();
		variant = face.Clone( coordinates ) ?? face;
		if ( variant != face ) TextShaper.RegisterVariation( face, variant );
		_variants.Add( coordinates, variant );
		return variant;
	}

	static int GetCoordinateHash( Coordinate[] coordinates )
	{
		var hash = new HashCode();
		foreach ( var coordinate in coordinates ) hash.Add( coordinate.Value );
		return hash.ToHashCode();
	}
}
