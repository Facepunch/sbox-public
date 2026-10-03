namespace Sandbox.SceneLab;

/// <summary>
/// Maps, loaded the way a game loads them: a <see cref="MapInstance"/> with a package ident, fetched from the cloud and
/// mounted, its world geometry streamed in by native's world renderer and its entities made into GameObjects.
/// </summary>
internal static class MapScenes
{
	/// <summary>
	/// A camera, and a map instance for <paramref name="map"/> - its own lights, sky and probes come from the map. The map
	/// is named once the backend is up (<see cref="StartWhenOnline"/>): a panel app starts it after its first frame.
	/// </summary>
	static Scene Map( string map, Vector3 cameraPosition, Vector3 lookAt )
	{
		var scene = new Scene();
		using var _ = scene.Push();

		var cameraObject = new GameObject( true, "Camera" );
		cameraObject.WorldPosition = cameraPosition;
		cameraObject.WorldRotation = Rotation.LookAt( lookAt - cameraPosition );
		var camera = cameraObject.Components.Create<CameraComponent>();
		camera.FieldOfView = 80;
		camera.FovAxis = CameraComponent.Axis.Horizontal;
		camera.ZNear = 5;
		camera.ZFar = 30000;
		camera.IsMainCamera = true;

		new GameObject( true, map ).Components.Create<MapInstance>();
		return scene;
	}

	/// <summary>
	/// facepunch.flatgrass: a big flat grass map from the cloud.
	/// </summary>
	public static Scene Flatgrass() => Map( "facepunch.flatgrass", new Vector3( -600, -600, 300 ), new Vector3( 0, 0, 30 ) );

	/// <summary>
	/// facepunch.flatgrass from out on the grass, looking past the building to the horizon: the map's 3D skybox, its
	/// sky world drawn at 16 times its size behind the main world.
	/// </summary>
	public static Scene FlatgrassSkyline() => Map( "facepunch.flatgrass", new Vector3( 3000, 2000, 200 ), new Vector3( 0, 0, 30 ) );

	/// <summary>
	/// facepunch.flatgrass from low over the grass clumps at its edge: props, which have no lightmaps, so they're lit from the
	/// map's light probe volumes (<c>SCENEOBJECTFLAG_NEEDS_LIGHT_PROBE</c>).
	/// </summary>
	public static Scene FlatgrassProps() => Map( "facepunch.flatgrass", new Vector3( 1500, 1000, 70 ), new Vector3( 1300, 770, 0 ) );

	/// <summary>
	/// facepunch.flatgrass inside the building, where the sun comes through its skylights in patches on the floor: their shadows
	/// are the lightmaps', since the baked sun's cascades leave the static building out.
	/// </summary>
	public static Scene FlatgrassInterior() => Map( "facepunch.flatgrass", new Vector3( -200, -100, 40 ), new Vector3( 150, 150, 0 ) );

	/// <summary>
	/// facepunch.construct from the sandbox game's spawn, looking west over the field to the vista trees: cards and clumps in
	/// the map's 3D skybox.
	/// </summary>
	public static Scene ConstructSkyline() => Map( "facepunch.construct", new Vector3( 864, -608, 67 ), new Vector3( -136, -608, 67 ) );

	/// <summary>
	/// The skyline through volumetric fog: a fog volume between the camera and the 3D skybox, which the skybox is fogged by too -
	/// native copies the main view's fog to its skybox view, taking its positions into the main world (<c>Add3DSkyboxLayers</c>).
	/// </summary>
	public static Scene ConstructSkylineFog()
	{
		var scene = ConstructSkyline();
		using var _ = scene.Push();

		var volume = new GameObject( true, "Fog Volume" );
		volume.WorldPosition = new Vector3( -300, -608, 250 );
		var fog = volume.Components.Create<VolumetricFogVolume>();
		fog.Bounds = BBox.FromPositionAndSize( 0, new Vector3( 2400, 2400, 700 ) );
		fog.Strength = 1.0f;
		return scene;
	}

	/// <summary>
	/// Name each map instance after its object once the backend is up, which starts it fetching - a map instance loads
	/// when its name changes.
	/// </summary>
	public static void StartWhenOnline( Scene scene )
	{
		if ( !PanelAppSystem.ApiReady.IsCompleted ) return;

		EnableDownloads();

		foreach ( var map in scene.GetAllComponents<MapInstance>() )
		{
			if ( string.IsNullOrEmpty( map.MapName ) ) map.MapName = map.GameObject.Name;
		}
	}

	/// <summary>
	/// What a game's boot sets up for downloading packages and a panel app's doesn't: the downloads folder and the asset
	/// cache in it (Bootstrap.PreInit), and the server package table packages mount through (GameInstanceDll).
	/// </summary>
	internal static void EnableDownloads()
	{
		if ( ServerPackages.Current is not null ) return;

		if ( EngineFileSystem.DownloadedFiles is null ) EngineFileSystem.InitializeDownloadsFolder();
		EngineFileSystem.DownloadedFiles.CreateDirectory( "/assets" );
		AssetDownloadCache.Initialize( EngineFileSystem.DownloadedFiles.GetFullPath( "/assets" ) );
		_ = new ServerPackages();
	}

	/// <summary>
	/// Every map instance in the scene has loaded.
	/// </summary>
	public static bool Loaded( Scene scene ) => scene.GetAllComponents<MapInstance>().All( x => x.IsLoaded );
}
