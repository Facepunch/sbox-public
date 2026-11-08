namespace Basalt;

/// <summary>
/// Distance and height gradient fog (s&amp;box's <c>GradientFogSetup</c>).
/// </summary>
public struct GradientFogSettings
{
	/// <summary>
	/// Whether the fog is enabled.
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// Start distance of the fog.
	/// </summary>
	public float StartDistance { get; set; }

	/// <summary>
	/// End distance of the fog.
	/// </summary>
	public float EndDistance { get; set; }

	/// <summary>
	/// The starting height of the gradient fog.
	/// </summary>
	public float StartHeight { get; set; }

	/// <summary>
	/// The ending height of the gradient fog.
	/// </summary>
	public float EndHeight { get; set; }

	/// <summary>
	/// The maximum opacity of the gradient fog.
	/// </summary>
	public float MaximumOpacity { get; set; }

	/// <summary>
	/// The color of the gradient fog.
	/// </summary>
	public Color Color { get; set; }

	/// <summary>
	/// The exponent controlling the distance-based falloff of the fog.
	/// </summary>
	public float DistanceFalloffExponent { get; set; }

	/// <summary>
	/// The exponent controlling the vertical falloff of the fog.
	/// </summary>
	public float VerticalFalloffExponent { get; set; }
}
