namespace Sandbox;

public partial class SceneObject
{
	/// <summary>
	/// Whether anything in this process reads a scene object's state from its native twin: Source 2's renderer, in a process
	/// that doesn't draw on Borealis.Gpu, or Source 2's device the editor keeps beside it for its native tools, captures and
	/// bakes. A type whose state is kept in managed fields (decals, sky boxes) writes it through to the twin only then; in the
	/// game under <c>-borealisgpu</c> nothing does, and Borealis reads the managed fields.
	/// </summary>
	internal static bool WritesNative => WritesNativeOverride ?? (!Rendering.ManagedSceneRendering.Enabled || Graphics.HasSource2Device);

	/// <summary>Tests set <see cref="WritesNative"/> either way; null follows the process.</summary>
	internal static bool? WritesNativeOverride { get; set; }
}
