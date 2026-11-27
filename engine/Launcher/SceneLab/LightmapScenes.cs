using Sandbox.Tasks;

namespace Sandbox.SceneLab;

/// <summary>
/// Meshes lit from a lightmap baked as the scene loads.
/// </summary>
internal static class LightmapScenes
{
	/// <summary>
	/// A floor, a wall, blocks and static models under the sun and a skybox, with a static lamp in front of the wall.
	/// </summary>
	public static Scene Room()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		Box( new Vector3( -256, -256, -8 ), new Vector3( 256, 256, 0 ) );
		Box( new Vector3( -16, -200, 0 ), new Vector3( 16, 120, 140 ) );
		Box( new Vector3( 90, -60, 0 ), new Vector3( 150, 0, 60 ) );
		Box( new Vector3( -170, 40, 0 ), new Vector3( -110, 100, 90 ) );

		Prop( "models/dev/sphere.vmdl", new Vector2( -240, -60 ), 80 );
		Prop( "models/dev/box.vmdl", new Vector2( -150, -170 ), 50 );
		Prop( "models/citizen/citizen.vmdl", new Vector2( -70, -150 ), 72 );

		var sun = new GameObject( true, "Sun" ).Components.Create<DirectionalLight>();
		sun.WorldRotation = Rotation.LookAt( new Vector3( -0.5f, 0.35f, -0.8f ) );
		sun.LightColor = Color.White;
		sun.MixedShadows = true;

		var lamp = new GameObject( true, "Lamp" ).Components.Create<PointLight>();
		lamp.GameObject.IsStatic = true;
		lamp.WorldPosition = new Vector3( -100, -80, 50 );
		lamp.Radius = 300;
		lamp.LightColor = new Color( 1.0f, 0.5f, 0.2f ) * 4;

		new GameObject( true, "Sky" ).Components.Create<SkyBox2D>();

		var camera = new GameObject( true, "Camera" ).Components.Create<CameraComponent>();
		camera.FieldOfView = 60;
		camera.IsMainCamera = true;
		camera.ZNear = 1;
		camera.WorldPosition = new Vector3( -420, -330, 300 );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 20 ) - camera.WorldPosition );

		scene.LightingMode = SceneLightingMode.Lightmap;
		scene.Lightmap.Size = BakedLightmap.LightmapSize.Size1024;
		scene.Lightmap.Quality = BakedLightmap.BakeQuality.Low;

		SyncContext.RunBlocking( scene.Lightmap.Bake( write: null ) );

		return scene;
	}

	/// <summary>
	/// The same scene lit dynamically.
	/// </summary>
	public static void Clear( Scene scene )
	{
		using var scope = scene.Push();
		scene.Lightmap.Clear();
	}

	/// <summary>
	/// A static model standing on the floor, a size tall.
	/// </summary>
	static void Prop( string model, Vector2 at, float size )
	{
		var renderer = new GameObject( true, "Prop" ).Components.Create<ModelRenderer>();
		renderer.GameObject.IsStatic = true;
		renderer.Model = Model.Load( model );

		var bounds = renderer.Model.Bounds;
		var scale = size / bounds.Size.z;
		renderer.WorldScale = scale;
		renderer.WorldPosition = new Vector3( at.x, at.y, -bounds.Mins.z * scale );
	}

	static void Box( Vector3 mins, Vector3 maxs )
	{
		var mesh = new PolygonMesh();
		var v = Enumerable.Range( 0, 8 ).Select( i => mesh.AddVertex( new Vector3( i % 4 is 1 or 2 ? maxs.x : mins.x, i % 4 >= 2 ? maxs.y : mins.y, i >= 4 ? maxs.z : mins.z ) ) ).ToArray();

		foreach ( int[] face in (int[][])[[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]] )
			mesh.AddFace( face.Select( x => v[x] ).ToArray() );

		new GameObject( true, "Box" ).Components.Create<MeshComponent>().Mesh = mesh;
	}
}
