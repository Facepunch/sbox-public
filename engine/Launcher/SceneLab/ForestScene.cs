using Sandbox.Clutter;

namespace Sandbox.SceneLab;

/// <summary>
/// A forest on hills: 260 oaks and 340 bushes from Construct's own props (alpha-tested leaves that move in the world's
/// default wind), placed from a fixed seed, and grass, tall grass, clover and flowers scattered thick over the terrain by a
/// clutter volume, under a low sun's cascades. The camera walks a loop through the trees at eye height. The models come from
/// mounting the facepunch.construct package, which the Construct presets load their map from, without loading the map; the
/// repository has no foliage. The terrain has no material (none ships either), so it draws its grid.
/// </summary>
internal static class ForestScene
{
	const string Construct = "facepunch.construct";
	const float Size = 4096;
	const float HeightRange = 360;
	public const int LoopFrames = 900;
	const float LoopRadius = 380;
	const float EyeHeight = 70;

	static readonly string[] Trees =
	[
		"models/sbox_props/trees/oak/tree_oak_big_a.vmdl", "models/sbox_props/trees/oak/tree_oak_big_b.vmdl",
		"models/sbox_props/trees/oak/tree_oak_medium_a.vmdl", "models/sbox_props/trees/oak/tree_oak_small_a.vmdl",
	];

	static readonly string[] Bushes =
	[
		"models/sbox_props/shrubs/beech/beech_bush_large.vmdl", "models/sbox_props/shrubs/beech/beech_bush_medium.vmdl",
		"models/sbox_props/shrubs/beech/beech_bush_regular_medium_a.vmdl", "models/sbox_props/shrubs/beech/beech_bush_regular_small.vmdl",
		"models/sbox_props/shrubs/beech/beech_shrub_tall_large.vmdl", "models/sbox_props/shrubs/pine/pine_bush_low_b.vmdl",
		"models/sbox_props/shrubs/pine/pine_bush_regular_b.vmdl",
	];

	static readonly (string Model, float Weight)[] Ground =
	[
		("models/sbox_props/nature/grass_clumps/grass_clump_a.vmdl", 3), ("models/sbox_props/nature/grass_clumps/grass_clump_b.vmdl", 3),
		("models/sbox_props/nature/grass_clumps/grass_clump_c.vmdl", 3), ("models/sbox_props/nature/grass_clumps/grass_clump_d.vmdl", 3),
		("models/sbox_props/nature/tallgrass/tallgrass_a.vmdl", 0.4f), ("models/sbox_props/nature/ground_clover/ground_clover_a.vmdl", 1),
		("models/sbox_props/nature/ground_clover/ground_clover_b.vmdl", 1), ("models/props/nature/flowers_01/summer_flowers_01.vmdl", 0.5f),
		("models/props/nature/flowers_01/summer_flowers_03.vmdl", 0.5f),
	];

	static Scene built;
	static int frame;

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( LoopRadius, 0, Height( LoopRadius, 0 ) + EyeHeight ), new Vector3( 0, 0, 40 ) );
		using var _ = scene.Push();

		camera.ZFar = 12000;
		camera.FieldOfView = 80;
		scene.Directory.FindByName( "Floor" ).First().Destroy();

		// A low afternoon sun, so the trees' shadows are long
		var sun = scene.Directory.FindByName( "Sun" ).First();
		sun.WorldRotation = Rotation.LookAt( new Vector3( -0.5f, 0.6f, -0.45f ) );
		var light = sun.Components.Get<DirectionalLight>();
		light.LightColor = new Color( 1, 0.92f, 0.8f ) * 3;
		light.SkyColor = new Color( 0.35f, 0.42f, 0.5f );

		var terrain = new GameObject( true, "Terrain" );
		terrain.WorldPosition = new Vector3( -Size / 2, -Size / 2, 0 );
		terrain.Components.Create<Terrain>().Storage = Hills();

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;

		built = null;
		frame = 0;
		return scene;
	}

	/// <summary>
	/// The ground's height at a point: gentle hills, the same function the heightmap is made from.
	/// </summary>
	static float Height( float x, float y )
	{
		var u = (x + Size / 2) / Size * 128;
		var v = (y + Size / 2) / Size * 128;
		var h = 0.35f + 0.18f * MathF.Sin( u * 0.08f ) * MathF.Cos( v * 0.06f ) + 0.1f * MathF.Sin( (u + v) * 0.035f );
		return Math.Clamp( h, 0, 1 ) * HeightRange;
	}

	static TerrainStorage Hills()
	{
		var storage = new TerrainStorage();
		storage.SetResolution( 128 );
		storage.TerrainSize = Size;
		storage.TerrainHeight = HeightRange;

		for ( int y = 0; y < 128; y++ )
			for ( int x = 0; x < 128; x++ )
				storage.HeightMap[y * 128 + x] = (ushort)(Height( x / 128.0f * Size - Size / 2, y / 128.0f * Size - Size / 2 ) / HeightRange * ushort.MaxValue);

		return storage;
	}

	/// <summary>
	/// The trees, bushes and ground cover, once Construct's package is mounted.
	/// </summary>
	static void Plant( Scene scene )
	{
		using var _ = scene.Push();
		var random = new Random( 2718 );

		void Place( string name, string model, float clear, float scale )
		{
			// Not on the camera's path
			Vector3 at;
			do at = new Vector3( (random.NextSingle() - 0.5f) * (Size - 200), (random.NextSingle() - 0.5f) * (Size - 200), 0 );
			while ( MathF.Abs( at.WithZ( 0 ).Length - LoopRadius ) < clear );

			var prop = GameScenes.Prop( name, model, at.WithZ( Height( at.x, at.y ) - 4 ), scale: scale * (0.8f + random.NextSingle() * 0.5f) );
			prop.GameObject.WorldRotation = Rotation.FromYaw( random.NextSingle() * 360 );
			prop.GameObject.IsStatic = true;
		}

		for ( int i = 0; i < 260; i++ ) Place( $"Tree {i}", Trees[random.Next( Trees.Length )], 90, 1 );
		for ( int i = 0; i < 340; i++ ) Place( $"Bush {i}", Bushes[random.Next( Bushes.Length )], 50, 1 );

		var clutter = new GameObject( true, "Ground Cover" ).Components.Create<ClutterComponent>();
		clutter.Seed = 7;
		clutter.Bounds = new BBox( new Vector3( -Size / 2, -Size / 2, -100 ), new Vector3( Size / 2, Size / 2, HeightRange + 100 ) );
		clutter.Clutter = new ClutterDefinition
		{
			TileSizeEnum = ClutterDefinition.TileSizeOption.Size512,
			Scatterer = new SimpleScatterer { Density = 25, Scale = new RangedFloat( 0.6f, 1.1f ), PlaceOnGround = true, AlignToNormal = true },
			Entries = Ground.Select( x => new ClutterEntry { Model = Model.Load( x.Model ), Weight = x.Weight, EnablePhysics = false } ).ToList(),
		};
		clutter.Generate();
	}

	public static bool Ready( Scene scene ) => built == scene && TerrainScenes.ClutterReady( scene );

	public static void Rewind( Scene scene )
	{
		frame = 0;
		Walk( scene );
	}

	/// <summary>
	/// Plant the forest once Construct is mounted, then walk the camera round its loop: on the ground, looking ahead and a
	/// little to the inside.
	/// </summary>
	public static void Walk( Scene scene )
	{
		if ( built != scene )
		{
			if ( !Scenarios.Mounted( Construct ) ) return;
			Plant( scene );
			built = scene;
		}

		if ( scene.Camera is not { } camera ) return;

		var angle = frame++ % LoopFrames / (float)LoopFrames * MathF.Tau;
		var at = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * LoopRadius;
		var ahead = new Vector3( MathF.Cos( angle + 0.5f ), MathF.Sin( angle + 0.5f ), 0 ) * LoopRadius * 0.9f;
		camera.WorldPosition = at.WithZ( Height( at.x, at.y ) + EyeHeight );
		camera.WorldRotation = Rotation.LookAt( ahead.WithZ( Height( ahead.x, ahead.y ) + EyeHeight * 0.8f ) - camera.WorldPosition );
	}
}
