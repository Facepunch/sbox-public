using System.Collections.Immutable;

namespace Sandbox.UI;

[SkipHotload]
internal struct GradientInfo
{
	/// <summary>The most stops the batched UI shader can evaluate per gradient.</summary>
	public const int MaxStops = 8;

	/// <summary>Linear: the direction, 0 being "to bottom". Conic: the start angle, clockwise from "up".</summary>
	public float Angle;

	/// <summary>Centre of a radial or conic gradient. Percentages are a fraction of the box.</summary>
	public Length OffsetX;
	public Length OffsetY;
	public RadialSizeMode SizeMode;
	public GradientTypes GradientType;

	/// <summary>Radial only - a circle rather than an ellipse.</summary>
	public bool Circle;

	/// <summary>
	/// Linear only - the corner "to top left" and friends point at, or None for an angle. A corner's
	/// direction depends on the box's aspect, so the shader works it out rather than the parser.
	/// </summary>
	public Corners Corner;

	public ImmutableArray<Styles.GradientColorOffset> ColorOffsets;

	public override int GetHashCode()
	{
		if ( ColorOffsets.IsDefaultOrEmpty )
			return 0;

		return HashCode.Combine( HashCode.Combine( Angle, SizeMode, OffsetX, OffsetY, GradientType, ColorOffsets ), Circle, Corner );
	}

	public enum Corners
	{
		None = 0,
		TopLeft = 1,
		TopRight = 2,
		BottomLeft = 3,
		BottomRight = 4
	}

	public enum RadialSizeMode
	{
		FarthestSide = 0,
		FarthestCorner = 1,
		ClosestSide = 2,
		ClosestCorner = 3,
		Circle = 4
	}

	public enum GradientTypes
	{
		Linear = 0,
		Radial = 1,
		Conic = 2
	}

}
