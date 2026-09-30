namespace Sandbox.SceneRenderer;

/// <summary>
/// Scaled background world drawn at depth 0–0.02 (<c>CSceneWorld::Set3DSkyboxParameters</c>,
/// <c>CRenderingPipelineStandard::Add3DSkyboxLayers</c>). Uses its own lights and sky, with main-world ambient and scaled fog.
/// Lighting is baked; no shadow maps.
/// </summary>
public sealed class Skybox3D
{
	/// <summary>
	/// A 3D skybox drawing <paramref name="world"/>.
	/// </summary>
	public Skybox3D( RenderWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		World = world;
	}

	/// <summary>
	/// What the skybox draws.
	/// </summary>
	public RenderWorld World { get; }

	/// <summary>
	/// Main-world origin in skybox space (<c>SceneSkybox3D.Origin</c>).
	/// </summary>
	public Vector3 Origin { get; set; }

	/// <summary>
	/// Main-world units per skybox unit. Defaults to 16; nonpositive values disable drawing.
	/// </summary>
	public float Scale { get; set; } = 16.0f;
}
