namespace Sandbox.SceneLab;

/// <summary>
/// The Water group: an ocean to the horizon with a fleet of bobbing boats, with and without a <see cref="WaterHole"/>
/// in each hull. The water covers most of the screen, so the benchmark's GPU time is mostly the water's, and the
/// difference between the presets is what the holes cost. The Physics presets add floating crates and swimming players,
/// whose buoyancy and swimming query the holes every tick; Outlier adds one hole far away from the fleet.
/// </summary>
internal static class WaterScenes
{
	const int Columns = 20;
	const float Spacing = 260;

	static GameObject[] boats = [];
	static Vector3[] homes = [];
	static int frame;

	public static Scene Ocean() => Build( 0, false );
	public static Scene OceanBoats() => Build( 100, false );
	public static Scene OceanHoles100() => Build( 100, true );
	public static Scene OceanHoles400() => Build( 400, true );
	public static Scene OceanHoles100Outlier() => Build( 100, true, outlier: true );
	public static Scene OceanBoatsPhysics() => Build( 400, false, physics: true );
	public static Scene OceanHoles400Physics() => Build( 400, true, physics: true );

	static Scene Build( int count, bool holes, bool physics = false, bool outlier = false )
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( -900, 0, 420 ), new Vector3( 2000, 0, -150 ) );
		using var _ = scene.Push();

		// Stage's floor would sit just under the surface
		foreach ( var child in scene.Children.ToArray() )
		{
			if ( child.Name == "Floor" ) child.Destroy();
		}

		camera.ZFar = 60000;

		var ocean = new GameObject( true, "Ocean" ).Components.Create<WaterBody>();
		ocean.Ocean = true;
		ocean.WaveHeight = 12;
		ocean.Effects = false;

		// Rows of boats fanning out ahead of the camera, so near ones fill a lot of the screen and far ones a little
		boats = new GameObject[count];
		homes = new Vector3[count];
		for ( int i = 0; i < count; i++ )
		{
			int row = i / Columns;
			float column = i % Columns - (Columns - 1) * 0.5f;
			var position = new Vector3( row * Spacing * 1.5f, column * Spacing * (1 + row * 0.15f), 0 );

			var hull = GameScenes.Prop( $"Boat {i}", "models/dev/box.vmdl", position, new ColorHsv( i * 37 % 360, 0.5f, 0.8f ).ToColor() );
			hull.GameObject.WorldScale = new Vector3( 2.4f, 1.2f, 0.6f );
			hull.GameObject.WorldRotation = Rotation.FromYaw( i * 23 % 90 - 45 );
			if ( holes )
			{
				// The dev box spans -25 to 25. The hole reaches past the hull, so the cut shows as a dry margin around
				// it, and from under the keel to above the crests.
				hull.GameObject.Components.Create<WaterHole>().Bounds = new BBox( new Vector3( -32, -32, -40 ), new Vector3( 32, 32, 40 ) );
			}

			boats[i] = hull.GameObject;
			homes[i] = position;
		}

		// One hole 100 km away. It must not make the fleet's holes cost more.
		if ( outlier )
			new GameObject( true, "Far Hole" ) { WorldPosition = new Vector3( 100000, 100000, 0 ) }.Components.Create<WaterHole>();

		if ( physics )
			AddSwimmers();

		frame = 0;
		Animate( scene );
		return scene;
	}

	/// <summary>
	/// 200 crates floating between the boats and 32 players swimming, so buoyancy, swimming and splashes query the
	/// holes every tick.
	/// </summary>
	static void AddSwimmers()
	{
		for ( int i = 0; i < 200; i++ )
		{
			var crate = new GameObject( true, $"Crate {i}" );
			crate.WorldPosition = new Vector3( (i / 20) * Spacing * 1.5f + 190, (i % 20 - 9.5f) * Spacing + 130, 10 );
			crate.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );
			crate.WorldScale = 0.5f;
			crate.Components.Create<BoxCollider>().Scale = 50;
			crate.Components.Create<Rigidbody>();
		}

		for ( int i = 0; i < 32; i++ )
		{
			var swimmer = new GameObject( true, $"Swimmer {i}" );
			swimmer.WorldPosition = new Vector3( (i / 8) * Spacing * 3 + 190, (i % 8 - 3.5f) * Spacing * 2 + 130, -64 );
			var player = swimmer.Components.Create<PlayerController>();
			player.UseInputControls = false;
			player.UseCameraControls = false;
			player.EnableFootstepSounds = false;
			swimmer.Components.Create<Sandbox.Movement.MoveModeSwim>();
		}
	}

	/// <summary>
	/// The boats bob and drift, so the holes move every frame, as a game's would.
	/// </summary>
	public static void Animate( Scene scene )
	{
		frame++;
		for ( int i = 0; i < boats.Length; i++ )
		{
			if ( !boats[i].IsValid() ) continue;
			float phase = frame * 0.03f + i * 0.7f;
			boats[i].WorldPosition = homes[i] + new Vector3( MathF.Sin( phase * 0.3f ) * 20, 0, MathF.Sin( phase ) * 6 );
		}
	}
}
