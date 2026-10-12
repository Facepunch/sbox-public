namespace Sandbox.Rendering;

/// <summary>
/// Controls how a decal's color affects the surface it is projected onto.
/// </summary>
[Expose]
public enum DecalBlendMode
{
	/// <summary>
	/// Blends the decal's color over the surface using the decal's opacity.
	/// </summary>
	Alpha,

	/// <summary>
	/// Multiplies the surface color by the decal's color. Useful for stains and darkening;
	/// white leaves the surface unchanged.
	/// </summary>
	Multiply,

	/// <summary>
	/// Adds the decal's color as emission. Useful for glowing marks that remain visible without lighting.
	/// </summary>
	Additive
}
