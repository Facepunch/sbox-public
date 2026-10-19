namespace Sandbox;

/// <summary>
/// Displays a Vector2 as a two-handle slider between two numeric fields on one row,
/// with x as the minimum and y as the maximum. Can be used on any Vector2 property or field.
/// Numeric entry can exceed the slider limits, like <see cref="RangeAttribute"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class MinMaxSliderAttribute : RangeAttribute
{
	/// <summary>
	/// Sets the lower and upper limits of the slider.
	/// </summary>
	public MinMaxSliderAttribute( float min, float max ) : base( min, max )
	{
	}
}
