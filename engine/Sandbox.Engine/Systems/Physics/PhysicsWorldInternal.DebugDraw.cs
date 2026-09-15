using System.ComponentModel;

namespace Sandbox;

partial class PhysicsWorldInternal
{
	internal Scene Scene { get; set; }

	/// <summary>
	/// A SceneWorld where debug SceneObjects exist.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public virtual SceneWorld DebugSceneWorld { get; set; }

	/// <summary>
	/// Draws the physics debug overlays enabled via the physics_debug_draw convars,
	/// updating the SceneObjects in the <see cref="DebugSceneWorld"/>. Call once per tick or frame.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public virtual void DebugDraw() { }
}
