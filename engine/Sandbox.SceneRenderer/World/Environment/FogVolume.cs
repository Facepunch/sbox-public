namespace Sandbox.SceneRenderer;

/// <summary>
/// Tinted fog volume with centre-to-edge density falloff (<c>SceneVolumetricFogVolume_t</c>).
/// Requires enabled camera volumetric fog.
/// </summary>
public sealed class FogVolume
{
	/// <summary>
	/// World transform for local bounds. Scale is ignored, matching native.
	/// </summary>
	public Transform Transform { get; set; } = Transform.Zero;

	/// <summary>
	/// The box, in the volume's space.
	/// </summary>
	public BBox Bounds { get; set; }

	/// <summary>
	/// Peak fog density, 0–1.
	/// </summary>
	public float Strength { get; set; } = 1.0f;

	/// <summary>
	/// How the density falls off towards its edges: higher is sharper.
	/// </summary>
	public float Exponent { get; set; } = 1.0f;

	/// <summary>
	/// The tint of the light scattered in it.
	/// </summary>
	public Color Color { get; set; } = Color.White;

	/// <summary>
	/// Use radial rather than max-axis falloff. Native <c>SceneFogVolume</c> leaves this off.
	/// </summary>
	public bool Spherical { get; set; }
}

/// <summary>
/// Camera froxel-fog settings, matching <c>SceneCamera.VolumetricFog</c>.
/// </summary>
public sealed class VolumetricFogSettings
{
	/// <summary>
	/// Enable volumetric fog.
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// How much light scatters forward rather than back (the phase function's g).
	/// </summary>
	public float Anisotropy { get; set; }

	/// <summary>
	/// How much light the fog scatters: 0 switches it off, after native's 100 frames.
	/// </summary>
	public float Scattering { get; set; }

	/// <summary>
	/// How far the fog volume reaches from the camera.
	/// </summary>
	public float DrawDistance { get; set; }

	/// <summary>
	/// Where the fog starts fading in from the camera.
	/// </summary>
	public float FadeInStart { get; set; }

	/// <summary>
	/// Where the fog has fully faded in.
	/// </summary>
	public float FadeInEnd { get; set; }

	/// <summary>
	/// Indirect scattering strength, using baked lighting or environment maps.
	/// </summary>
	public float IndirectStrength { get; set; }

	/// <summary>
	/// A map's baked indirect light for its fog (<c>VolumetricFogController</c>), or null.
	/// </summary>
	public Texture BakedIndirectTexture { get; set; }
}
