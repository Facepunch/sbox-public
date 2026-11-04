using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A sliced image mapped to a box, independent of its stroke colors and style.
/// The box shader can composite it alongside the background without an extra instance.
/// </summary>
internal readonly record struct NineSliceImage
{
	internal Texture Texture { get; init; }
	internal Vector4 Slices { get; init; }
	internal BorderImageRepeat Repeat { get; init; }
	internal BorderImageFill Fill { get; init; }
	internal Color Tint { get; init; }

	internal NineSliceImage( Texture texture, Vector4 slices, BorderImageRepeat repeat, BorderImageFill fill, Color tint )
	{
		ArgumentNullException.ThrowIfNull( texture );
		for ( int i = 0; i < 4; i++ )
			if ( !float.IsFinite( slices[i] ) || slices[i] < 0 ) throw new ArgumentOutOfRangeException( nameof( slices ) );
		if ( !Enum.IsDefined( repeat ) ) throw new ArgumentOutOfRangeException( nameof( repeat ) );
		if ( !Enum.IsDefined( fill ) ) throw new ArgumentOutOfRangeException( nameof( fill ) );
		Texture = texture;
		Slices = slices;
		Repeat = repeat;
		Fill = fill;
		Tint = tint;
	}

	internal NineSliceImage WithAlphaMultiplied( float opacity ) => this with { Tint = Tint.WithAlphaMultiplied( opacity ) };
}
