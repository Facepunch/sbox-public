namespace Sandbox.Mounting;

/// <summary>
/// Allows hinting about resources, how they can be used
/// </summary>
[Flags]
public enum ResourceFlags
{
	/// <summary>
	/// Hide in game stuff, this is just for developers. Show only in the asset browser.
	/// </summary>
	DeveloperOnly = 1 << 0,

	/// <summary>
	/// This isn't a ragdoll. Never treat it as one.
	/// </summary>
	NeverRagdoll = 1 << 1,

	/// <summary>
	/// This isn't solid. It shouldn't have any physics. In Sandbox mode it should have a handle that lets you move it around.
	/// </summary>
	Effect = 1 << 2
}
