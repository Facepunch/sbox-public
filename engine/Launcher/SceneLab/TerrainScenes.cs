using Sandbox.Clutter;

namespace Sandbox.SceneLab;

/// <summary>
/// Terrain and clutter: both are <c>SceneCustomObject</c>s that draw themselves through <c>Graphics</c> - the terrain's
/// clipmap as instanced meshlets it culls on the CPU per view, clutter as batches it culls on the GPU per view and draws
/// indirect - so they reach the managed renderer as custom objects.
/// </summary>
internal static class TerrainScenes
{
	/// <summary>
	/// Rolling terrain with no materials (its grid), with boxes and spheres scattered over it by a clutter volume, under the
	/// stage's sun. The stage's floor is gone.
	/// </summary>
	public static Scene TerrainAndClutter()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( -700, -500, 260 ), new Vector3( 0, 0, 40 ) );
		using var _ = scene.Push();

		camera.ZFar = 10000;
		scene.Directory.FindByName( "Floor" ).First().Destroy();

		var terrain = new GameObject( true, "Terrain" );
		terrain.WorldPosition = new Vector3( -1024, -1024, -60 );
		var component = terrain.Components.Create<Terrain>();
		component.Storage = Hills();

		// An ordinary caster standing in the terrain, whose shadow falls across it
		var pillar = GameScenes.Prop( "Pillar", "models/dev/box.vmdl", new Vector3( 150, -150, 60 ), new Color( 0.8f, 0.5f, 0.3f ) );
		pillar.GameObject.WorldScale = new Vector3( 0.6f, 0.6f, 5 );

		var clutter = new GameObject( true, "Clutter" );
		var volume = clutter.Components.Create<ClutterComponent>();
		volume.Seed = 7;
		volume.Bounds = new BBox( new Vector3( -600, -600, -200 ), new Vector3( 600, 600, 400 ) );
		volume.Clutter = new ClutterDefinition
		{
			TileSizeEnum = ClutterDefinition.TileSizeOption.Size512,
			Scatterer = new SimpleScatterer { Density = 1.5f, Scale = new RangedFloat( 0.1f, 0.3f ), PlaceOnGround = true, AlignToNormal = true },
			Entries =
			[
				new ClutterEntry { Model = Model.Load( "models/dev/box.vmdl" ), Weight = 2 },
				new ClutterEntry { Model = Model.Load( "models/dev/sphere.vmdl" ) },
			],
		};
		volume.Generate();

		return scene;
	}

	/// <summary>
	/// Ready once the clutter has scattered and its batches exist, beside the terrain's own object.
	/// </summary>
	/// <summary>
	/// The terrain scene with ambient occlusion: terrain is a custom object, which native's depth-normals prepass draws, so
	/// AO - run after the prepass, before the opaque pass - sees its depth and normals. Compare it isolated.
	/// </summary>
	public static Scene TerrainAmbientOcclusion()
	{
		var scene = TerrainAndClutter();
		using var _ = scene.Push();
		scene.Camera.GameObject.Components.Create<AmbientOcclusion>();
		return scene;
	}

	public static bool ClutterReady( Scene scene ) =>
		scene.GetAllComponents<ClutterComponent>().All( x => x.Storage?.TotalCount > 0 )
		&& scene.SceneWorld.SceneObjects.OfType<SceneCustomObject>().Count() > 1;

	/// <summary>
	/// The terrain raised in the middle and the clutter scattered again with another seed - the terrain rebuilds its
	/// height texture, and clutter makes new batches.
	/// </summary>
	public static void ChangeTerrainAndClutter( Scene scene )
	{
		using var _ = scene.Push();

		var terrain = scene.GetAllComponents<Terrain>().First();
		var storage = terrain.Storage;
		var resolution = storage.Resolution;
		for ( int y = resolution / 3; y < resolution * 2 / 3; y++ )
			for ( int x = resolution / 3; x < resolution * 2 / 3; x++ )
				storage.HeightMap[y * resolution + x] = (ushort)Math.Min( ushort.MaxValue, storage.HeightMap[y * resolution + x] + 12000 );
		terrain.ApplyStorageChanges( Terrain.SyncFlags.Height, new RectInt( 0, 0, resolution, resolution ) );

		var clutter = scene.GetAllComponents<ClutterComponent>().First();
		clutter.Seed = 11;
		clutter.Generate();
	}

	/// <summary>
	/// A 128 square heightmap 2048 units across and 400 tall: two crossing waves over a slope.
	/// </summary>
	static TerrainStorage Hills()
	{
		var storage = new TerrainStorage();
		storage.SetResolution( 128 );
		storage.TerrainSize = 2048;
		storage.TerrainHeight = 400;

		for ( int y = 0; y < 128; y++ )
			for ( int x = 0; x < 128; x++ )
			{
				var h = 0.3f + 0.15f * MathF.Sin( x * 0.09f ) * MathF.Cos( y * 0.07f ) + 0.1f * MathF.Sin( (x + y) * 0.04f ) + x / 128.0f * 0.15f;
				storage.HeightMap[y * 128 + x] = (ushort)(Math.Clamp( h, 0, 1 ) * ushort.MaxValue);
			}

		return storage;
	}
}
