namespace Sandbox;

/// <summary>
/// Adds an ambient light to the scene, applied globally.
/// </summary>
[Title( "Color Ambient Light" )]
[Category( "Light" )]
[Icon( "visibility" )]
[EditorHandle( "materials/gizmo/directionallight.png" )]
public class AmbientLight : Component, Component.ExecuteInEditor
{

	/// <summary>
	/// Ambient light color outside of all light probes.
	/// </summary>
	[Property] public Color Color { get; set; } = Color.Gray;
}
