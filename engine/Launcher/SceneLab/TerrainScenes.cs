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

	const float ForestSize = 32768;
	const float TreeScaleMax = 5;

	// A turn takes the benchmark's default measured run, so every run sees the sun from every side
	const int TurnFrames = 600;
	static int forestFrame;

	/// <summary>
	/// The forest the two clutter stress presets share: a flat forest 32768 units across, about 100k spheres scattered
	/// by one clutter volume in 4096 tiles, seen from just above the canopy with a 90° field of view and a 100k far plane.
	/// </summary>
	static Scene Forest( out CameraComponent camera )
	{
		var tree = Model.Load( "models/dev/sphere.vmdl" );
		var eye = tree.Bounds.Maxs.z * TreeScaleMax + 100;
		var scene = GameScenes.Stage( out camera, new Vector3( 0, 0, eye ), new Vector3( 1000, 0, eye ) );
		using var _ = scene.Push();

		camera.FieldOfView = 90;
		camera.ZFar = 100000;
		var floorScale = ForestSize / Model.Load( "models/dev/plane.vmdl" ).Bounds.Size.x;
		scene.Directory.FindByName( "Floor" ).First().WorldScale = new Vector3( floorScale, floorScale, 1 );

		var volume = new GameObject( true, "Forest" ).Components.Create<ClutterComponent>();
		volume.Seed = 7;
		volume.Bounds = new BBox( new Vector3( -ForestSize / 2, -ForestSize / 2, -100 ), new Vector3( ForestSize / 2, ForestSize / 2, 100 ) );
		volume.Clutter = new ClutterDefinition
		{
			TileSizeEnum = ClutterDefinition.TileSizeOption.Size4096,
			Scatterer = new SimpleScatterer { Density = 1.5f, Scale = new RangedFloat( 2, TreeScaleMax ), PlaceOnGround = false },
			Entries = [new ClutterEntry { Model = tree }],
		};
		volume.Generate();

		forestFrame = 0;
		return scene;
	}

	/// <summary>
	/// The forest under the sun: its cascades are full of clutter, most of it off-screen - what caster culling is for.
	/// The camera turns (<see cref="TurnAround"/>), so the sun comes from every side.
	/// </summary>
	public static Scene ClutterForest()
	{
		var scene = Forest( out _ );
		return scene;
	}

	// A ring of lanterns around the camera, each lighting a good slice of forest. Their radius reaches well past the
	// ring, so a lantern behind the camera still lights what's in front of it and the light binner keeps it.
	const int LanternCount = 12;
	const float LanternRing = 3000;
	const float LanternRadius = 4000;
	const float LanternHeight = 300;

	/// <summary>
	/// The same forest lit by a ring of shadow-casting point lights, the sun's own shadows off: what's measured is the
	/// lanterns' cube maps alone. Each is six 90° views of a forest, and as the camera turns most of those views face
	/// away from everything it can see - a lantern off to the side or behind it casts nothing anybody looks at, but
	/// without caster culling every face still renders its share of 100k trees. The camera turns
	/// (<see cref="TurnAround"/>) so each lantern passes through being in front, beside and behind it.
	/// </summary>
	public static Scene ClutterPointLights()
	{
		var scene = Forest( out _ );
		using var _scope = scene.Push();

		// The sun's cascades would swamp what the lanterns cost
		scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>().Shadows = false;

		for ( int i = 0; i < LanternCount; i++ )
		{
			float yaw = i * 360f / LanternCount;
			var go = new GameObject( true, $"Lantern {i}" );
			go.WorldPosition = Rotation.FromYaw( yaw ).Forward * LanternRing + Vector3.Up * LanternHeight;

			var light = go.Components.Create<PointLight>();
			light.LightColor = new ColorHsv( yaw, 0.5f, 1 ).ToColor() * 20;
			light.Radius = LanternRadius;
			light.Shadows = true;
		}

		return scene;
	}

	// Hills tall enough to hide a good part of the forest from the camera, with the eye just over the ridges
	const float TerrainForestHeight = 700;
	const float TerrainForestEye = 600;

	/// <summary>
	/// Rolling terrain the size of the forest, with the trees scattered onto it rather than onto a flat plane, and
	/// the eye just over the ridge tops. Closer to what a game's outdoor view costs than the flat forest: the terrain
	/// clipmap draws into the cascades beside the clutter, the hills hide part of the forest from the camera while
	/// still casting into the cascades, and the trees stand at every height. The camera turns
	/// (<see cref="TurnAround"/>), so the sun comes from every side.
	/// </summary>
	public static Scene ClutterTerrainForest()
	{
		var tree = Model.Load( "models/dev/sphere.vmdl" );
		var scene = GameScenes.Stage( out var camera, new Vector3( 0, 0, TerrainForestEye ), new Vector3( 1000, 0, TerrainForestEye ) );
		using var _ = scene.Push();

		camera.FieldOfView = 90;
		camera.ZFar = 100000;

		// The terrain is the ground here
		scene.Directory.FindByName( "Floor" ).First().Destroy();

		var terrain = new GameObject( true, "Terrain" );
		terrain.WorldPosition = new Vector3( -ForestSize / 2, -ForestSize / 2, 0 );
		terrain.Components.Create<Terrain>().Storage = RollingHills();

		var volume = new GameObject( true, "Forest" ).Components.Create<ClutterComponent>();
		volume.Seed = 7;
		volume.Bounds = new BBox( new Vector3( -ForestSize / 2, -ForestSize / 2, 0 ), new Vector3( ForestSize / 2, ForestSize / 2, TerrainForestHeight ) );
		volume.Clutter = new ClutterDefinition
		{
			TileSizeEnum = ClutterDefinition.TileSizeOption.Size4096,
			Scatterer = new SimpleScatterer { Density = 1.5f, Scale = new RangedFloat( 2, TreeScaleMax ), PlaceOnGround = true, AlignToNormal = true },
			Entries = [new ClutterEntry { Model = tree }],
		};
		volume.Generate();

		forestFrame = 0;
		return scene;
	}

	/// <summary>
	/// A 512 square heightmap across the forest: two crossing waves of hills, the tallest about 450 units.
	/// </summary>
	static TerrainStorage RollingHills()
	{
		const int resolution = 512;

		var storage = new TerrainStorage();
		storage.SetResolution( resolution );
		storage.TerrainSize = ForestSize;
		storage.TerrainHeight = TerrainForestHeight;

		for ( int y = 0; y < resolution; y++ )
			for ( int x = 0; x < resolution; x++ )
			{
				var h = 0.35f + 0.18f * MathF.Sin( x * 0.021f ) * MathF.Cos( y * 0.017f ) + 0.12f * MathF.Sin( (x + y) * 0.009f );
				storage.HeightMap[y * resolution + x] = (ushort)(Math.Clamp( h, 0, 1 ) * ushort.MaxValue);
			}

		return storage;
	}

	/// <summary>
	/// Turns the camera a frame around its circle, by frame count so every run is the same.
	/// </summary>
	public static void TurnAround( Scene scene )
	{
		scene.Camera.WorldRotation = Rotation.FromYaw( (float)(forestFrame++ % TurnFrames) / TurnFrames * 360 );
	}

	/// <summary>
	/// Ready once every tile has scattered, so no run measures a forest still filling in.
	/// </summary>
	public static bool ForestReady( Scene scene ) =>
		scene.GetAllComponents<ClutterComponent>().All( x => !x.Generating && x.Storage?.TotalCount > 0 );

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
