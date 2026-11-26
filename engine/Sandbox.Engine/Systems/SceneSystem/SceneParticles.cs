namespace Sandbox;

/// <summary>
/// A SceneObject used to render particles.
/// We need to be careful with what we do here, because this object is created for in-engine particles
/// as well as custom scene object particles.
/// With custom particles there's no automatic Simulate, or deletion.. You're completely on your own. This
/// is perhaps a good thing though, it's maybe what you want to happen. To be completely isolated and completely
/// in control. But at the same time maybe it's not and it's something we need to sort out.
/// </summary>
[Obsolete]
public class SceneParticles : SceneObject
{
	/// <summary>
	/// Create scene particles.
	/// </summary>
	/// <param name="world">The scene world to create the particles in.</param>
	/// <param name="particleSystem">Path to the particle system file.</param>
	[Obsolete]
	public SceneParticles( SceneWorld world, string particleSystem )
	{

	}

	/// <summary>
	/// Create scene particles.
	/// </summary>
	/// <param name="world">The scene world to create the particles in.</param>
	/// <param name="particleSystem">Particle system resource.</param>
	[Obsolete]
	public SceneParticles( SceneWorld world, ParticleSystem particleSystem )
	{

	}

	/// <summary>
	/// Whether to render the particles or not.
	/// </summary>
	public bool RenderParticles { get; set; }

	/// <summary>
	/// Stop (or start) the particle system emission.
	/// </summary>
	public bool EmissionStopped { get; set; }

	/// <summary>
	/// Particle collisions use this physics world to perform traces.
	/// </summary>
	public PhysicsWorld PhysicsWorld { get; set; }

	/// <summary>
	/// Whether given control point has any data set.
	/// </summary>
	/// <param name="index">The control point index. Range is 0-63.</param>
	public bool IsControlPointSet( int index ) => default;

	/// <summary>
	/// Returns the position set on a given control point.
	/// </summary>
	/// <param name="index">The control point index. Range is 0-63.</param>
	public Vector3 GetControlPointPosition( int index ) => default;

	/// <summary>
	/// Set position on given control point.
	/// </summary>
	/// <param name="i">The control point index. Range is 0-63.</param>
	/// <param name="position">The position to set.</param>
	public void SetControlPoint( int i, Vector3 position ) { }
	/// <summary>
	/// Set rotation on given control point.
	/// </summary>
	/// <param name="i">The control point index. Range is 0-63.</param>
	/// <param name="rotation">The rotation to set.</param>
	public void SetControlPoint( int i, Rotation rotation ) { }

	/// <summary>
	/// Set transform on given control point.
	/// </summary>
	/// <param name="i">The control point index. Range is 0-63.</param>
	/// <param name="transform">The transform to set.</param>
	public void SetControlPoint( int i, Transform transform ) { }

	/// <summary>
	/// Set snapshot on given control point.
	/// </summary>
	/// <param name="i">The control point index. Range is 0-63.</param>
	/// <param name="snapshot">The snapshot to set.</param>
	public void SetControlPoint( int i, ParticleSnapshot snapshot ) { }

	/// <summary>
	/// Set model on given control point.
	/// </summary>
	/// <param name="i">The control point index. Range is 0-63.</param>
	/// <param name="model">The model to set.</param>
	public void SetControlPoint( int i, Model model ) { }

	/// <summary>
	/// Set vector on given named value.
	/// </summary>
	/// <param name="name">The name of the key.</param>
	/// <param name="value">The value to set.</param>
	public void SetNamedValue( string name, Vector3 value ) { }

	/// <summary>
	/// Simulate the particles for given amount of time.
	/// </summary>
	/// <param name="f">Amount of time has passed since last simulation.</param>
	public void Simulate( float f ) { }

	/// <summary>
	/// The amount of particles 
	/// </summary>
	public int ActiveParticlesSelf => default;

	/// <summary>
	/// The amount of particles including child systems
	/// </summary>
	public int ActiveParticlesTotal => default;

	/// <summary>
	/// The total allowed particle count
	/// </summary>
	public int MaximumParticles => default;

	/// <summary>
	/// Manually emit a bunch of particles
	/// </summary>
	public void Emit( int count ) { }

	/// <summary>
	/// True if particle system has reached the end
	/// </summary>
	public bool Finished => default;

	/// <summary>
	/// Get or set the simulation time
	/// </summary>
	public float SimulationTime { get; set; }

}
