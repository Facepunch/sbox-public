namespace Sandbox;

public partial class SceneObject
{
	/// <summary>
	/// Whether a scene object's state is written through to its native twin: in a process that doesn't draw on Borealis.Gpu.
	/// A type whose state is kept in managed fields (decals, sky boxes) writes it through only then; under <c>-borealisgpu</c>
	/// Borealis reads the managed fields.
	/// </summary>
	internal static bool WritesNative => WritesNativeOverride ?? !Rendering.ManagedSceneRendering.Enabled;

	/// <summary>Tests set <see cref="WritesNative"/> either way; null follows the process.</summary>
	internal static bool? WritesNativeOverride { get; set; }
}
