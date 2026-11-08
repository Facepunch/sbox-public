namespace Basalt;

/// <summary>
/// Fog coloured from a cubemap (s&amp;box's <c>CubemapFogController</c>).
/// </summary>

public sealed class CubemapFogSettings
{
	/// <summary>
	/// Adjust how quickly the cubemap blurs out at closer distances. A value of 0.0 always uses the lowest resolution MIP over the entire range, while a value of 1.0 uses the highest.
	/// </summary>
	public float LodBias { get; set; }

	/// <summary>
	/// The distance from the player at which the fog will start to fade in.
	/// </summary>
	public float StartDistance { get; set; }

	/// <summary>
	/// The distance from the player at which the fog will be at full strength.
	/// </summary>
	public float EndDistance { get; set; }

	/// <summary>
	/// Exponent for distance falloff. For example, 2.0 is proportional to square of distance.
	/// </summary>
	public float FalloffExponent { get; set; }

	/// <summary>
	/// The distance between the start of the height fog and where it is fully opaque. Setting this to 0 will disable height based blending.
	/// </summary>
	public float HeightWidth { get; set; }

	/// <summary>
	/// The absolute height in the map at which the height fog will start to fade in.
	/// </summary>
	public float HeightStart { get; set; }

	/// <summary>
	/// Exponent for height falloff. For example, 2.0 is proportional to square of distance.
	/// </summary>
	public float HeightExponent { get; set; }

	/// <summary>
	/// Is this cubemap fog active?
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// Cubemap texture to use for the fog.
	/// </summary>
	public GpuTexture Texture { get; set; }

	/// <summary>
	/// Location of the fog.
	/// </summary>
	public Transform Transform { get; set; }

	/// <summary>
	/// Tint of the fog.
	/// </summary>
	public Color Tint { get; set; }
}
