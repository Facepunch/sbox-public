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

	public SceneSkybox3D( SceneWorld parentWorld, SceneWorld skyboxWorld )
	{
		ParentWorld = parentWorld ?? throw new ArgumentNullException( nameof( parentWorld ) );
		SkyboxWorld = skyboxWorld ?? throw new ArgumentNullException( nameof( skyboxWorld ) );

		ParentWorld.native.Add3DSkyboxWorld( SkyboxWorld );
		ParentWorld.InternalSkyboxWorlds.Add( this );
		ParentWorld.ActiveSkybox = this;
	}

	public void Update()
	{
		if ( !IsValid || !ParentWorld.IsValid() ) return;
		ParentWorld.native.Set3DSkyboxParameters( Origin, Angles, Scale );
	}

	public bool IsValid => SkyboxWorld.IsValid();

	/// <summary>
	/// Delete this fog volume. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{
		if ( !IsValid ) return;

		if ( ParentWorld.IsValid() )
			ParentWorld.native.Remove3DSkyboxWorld( SkyboxWorld );
		ParentWorld.InternalSkyboxWorlds.Remove( this );
		ParentWorld.ActiveSkybox = null;
	}
}
