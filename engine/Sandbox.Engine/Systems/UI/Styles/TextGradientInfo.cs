using System.Collections.Immutable;

namespace Sandbox.UI;

/// <summary>Text gradients are rasterized by RichTextKit and do not have the UI shader's eight-stop limit.</summary>
[SkipHotload]
internal struct TextGradientInfo : IEquatable<TextGradientInfo>
{
	public float Angle;
	public Length OffsetX;
	public Length OffsetY;
	public GradientInfo.RadialSizeMode SizeMode;
	public GradientInfo.GradientTypes GradientType;
	public ImmutableArray<Styles.GradientColorOffset> ColorOffsets;

	public readonly bool Equals( TextGradientInfo other ) => Angle.Equals( other.Angle ) && OffsetX.Equals( other.OffsetX )
		&& OffsetY.Equals( other.OffsetY ) && SizeMode == other.SizeMode && GradientType == other.GradientType
		&& ColorOffsets.Equals( other.ColorOffsets );

	public readonly override bool Equals( object obj ) => obj is TextGradientInfo other && Equals( other );

	public readonly override int GetHashCode() => ColorOffsets.IsDefaultOrEmpty
		? 0
		: HashCode.Combine( Angle, SizeMode, OffsetX, OffsetY, GradientType, ColorOffsets );
}
