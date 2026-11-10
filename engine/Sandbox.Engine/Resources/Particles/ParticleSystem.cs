namespace Sandbox;

/// <summary>
/// A particle effect system that allows for complex visual effects, such as
/// explosions, muzzle flashes, impact effects, etc.
/// </summary>
public sealed partial class ParticleSystem : Resource
{
	public override bool IsValid => true;

	/// <summary>
	/// Whether the particle system is invalid, or has not yet loaded.
	/// </summary>
	public override bool IsError => default;

	/// <summary>
	/// Particle system file name.
	/// </summary>
	public string Name { get; internal set; }

	/// <summary>
	/// Static bounding box of the resource.
	/// </summary>
	public BBox Bounds { get; set; }

	~ParticleSystem()
	{
	}

	/// <summary>
	/// How many child particle systems do we have
	/// </summary>
	public int ChildCount => default;

	/// <summary>
	/// Returns child particle at given index.
	/// </summary>
	/// <param name="index">Index of child particle system, starting at 0.</param>
	/// <returns>Particle system</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when given index exceeds range of [0,ChildCount-1]</exception>
	public ParticleSystem GetChild( int index ) => default;
}
