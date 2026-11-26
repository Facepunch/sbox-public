namespace Sandbox;

// I doubt this should ever be promoted, so it's a bit messy
internal sealed class SceneSkybox3D : IValid
{
	public SceneWorld ParentWorld { get; set; }
	public SceneWorld SkyboxWorld { get; set; }

	public Vector3 Origin { get; set; }
	public Vector3 CameraOrigin { get; set; }
	public Angles Angles { get; set; }
	public float Scale { get; set; } = 16.0f;

	/// <summary>
	/// How the skybox is turned against the main world (Hammer's sky_reference angles): the skybox view sees the main view
	/// turned and moved by its inverse. Identity for the game's skyboxes.
	/// </summary>
	public Rotation Rotation { get; set; } = Rotation.Identity;
	public Fog FogOverride { get; set; }

	internal Rendering.FogSetup? ResolveFog( SceneCamera camera ) =>
		FogOverride.IsValid() ? FogOverride.GetSettings( camera.World, camera.RenderTags, camera.ExcludeTags ) : null;

	public SceneSkybox3D( SceneWorld parentWorld, SceneWorld skyboxWorld )
	{
		ParentWorld = parentWorld ?? throw new ArgumentNullException( nameof( parentWorld ) );
		SkyboxWorld = skyboxWorld ?? throw new ArgumentNullException( nameof( skyboxWorld ) );

		ParentWorld.InternalSkyboxWorlds.Add( this );
		ParentWorld.ActiveSkybox = this;
	}

	public bool IsValid => SkyboxWorld.IsValid();

	/// <summary>
	/// Delete this fog volume. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{
		if ( !IsValid ) return;

		ParentWorld.InternalSkyboxWorlds.Remove( this );
		ParentWorld.ActiveSkybox = null;
	}
}
