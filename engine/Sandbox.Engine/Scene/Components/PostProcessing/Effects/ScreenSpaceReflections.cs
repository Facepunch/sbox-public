namespace Sandbox;

/// <summary>
/// Screen-space reflections are part of the renderer: every camera renders them, and the graphics settings choose their
/// quality or turn them off. This component does nothing; it remains so scenes that have it still load.
/// </summary>
[Obsolete( "Screen-space reflections are part of the renderer and every camera renders them; this component does nothing." )]
[Hide]
[Title( "Screen-Space Reflections" )]
[Category( "Post Processing" )]
[Icon( "local_mall" )]
public class ScreenSpaceReflections : Component
{
	/// <summary>
	/// The roughness the renderer stops tracing reflections at.
	/// </summary>
	public float RoughnessCutoff => 0.5f;
}
