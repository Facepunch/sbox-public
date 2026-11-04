namespace Sandbox;

/// <summary>
/// Which physics simulation a <see cref="Scene"/> runs. For use with <see cref="Scene.PhysicsMode"/>.
/// </summary>
[Expose]
public enum ScenePhysicsMode
{
	/// <summary>
	/// Full 3D rigid body physics. This is the default.
	/// </summary>
	[Title( "3D" )]
	Physics3D,

	/// <summary>
	/// 2D physics, simulated by Box2D. Bodies are constrained to a single plane.
	/// </summary>
	[Title( "2D" )]
	Physics2D
}
