namespace Sandbox;

[Expose]
[Hide]
[Title( "3D Skybox - Map" )]
[Category( "Rendering" )]
[Icon( "visibility" )]
public class MapSkybox3D : Component, Component.ExecuteInEditor
{
	private string TargetMapName { get; set; }
	private Vector3 CameraOffset { get; set; }

	SceneSkybox3D sceneSkybox3D;

	protected override void OnEnabled()
	{
		if ( TargetMapName is null ) return;

		Assert.True( sceneSkybox3D == null );
		Assert.NotNull( Scene );

		sceneSkybox3D = new SceneSkybox3D( Scene.SceneWorld, new SceneWorld() );

		// Load with custom map loader for sky_camera to adjust the SceneSkybox3D
		new SceneMap( sceneSkybox3D.SkyboxWorld, TargetMapName, new SkyboxMapLoader( sceneSkybox3D, CameraOffset ) );

		Transform.OnTransformChanged += OnTransformChanged;
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		sceneSkybox3D?.Delete();
		sceneSkybox3D?.SkyboxWorld.Delete(); // ours, made in OnEnabled
		sceneSkybox3D = null;
	}

	private void OnTransformChanged()
	{
		CameraOffset = WorldPosition;
		sceneSkybox3D.Origin = sceneSkybox3D.CameraOrigin - CameraOffset / sceneSkybox3D.Scale;
		sceneSkybox3D.Update();
	}

	internal static void InitializeFromLegacy( GameObject go, MapLoader.ObjectEntry kv )
	{
		var mapName = kv.GetString( "targetMapName" );
		if ( string.IsNullOrEmpty( mapName ) ) return;

		var component = go.Components.Create<MapSkybox3D>();
		component.TargetMapName = mapName;
		component.CameraOffset = kv.Position;
	}
}

/// <summary>
/// Loader for a 3D skybox which updates the values on the parent sceneworld
/// </summary>
file class SkyboxMapLoader : SceneMapLoader
{
	readonly SceneSkybox3D Skybox3D;
	readonly Vector3 CameraOffset;

	public SkyboxMapLoader( SceneSkybox3D skybox3D, Vector3 cameraOffset ) : base( skybox3D.SkyboxWorld, null )
	{
		Skybox3D = skybox3D;
		CameraOffset = cameraOffset;
	}

	protected override void CreateObject( ObjectEntry kv )
	{
		if ( kv.TypeName == "sky_camera" )
		{
			Skybox3D.CameraOrigin = kv.Position;
			Skybox3D.Origin = Skybox3D.CameraOrigin - CameraOffset / kv.GetValue( "scale", 16 );
			Skybox3D.Angles = kv.Angles;
			Skybox3D.Scale = kv.GetValue( "scale", 16 );
			Skybox3D.Update();

			return;
		}

		if ( kv.TypeName == "env_sky" )
		{
			var skyMaterial = kv.GetResource<Material>( "skyname" );
			var tintColor = kv.GetValue<Color>( "tint_color" );
			var transform = kv.Transform;

			new SceneSkyBox( Skybox3D.SkyboxWorld, skyMaterial )
			{
				SkyTint = tintColor,
				Transform = transform
			};
		}

		base.CreateObject( kv );
	}
}
