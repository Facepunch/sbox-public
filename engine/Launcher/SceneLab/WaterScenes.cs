namespace Sandbox.SceneLab;

/// <summary>
/// The Water Holes preset: an ocean to the horizon with 400 bobbing open hulls, each kept dry by a <see cref="WaterHole"/>,
/// and 200 floating crates and 32 swimming players between them, so buoyancy, swimming and splashes query the holes
/// every tick. The water fills most of the screen, so the benchmark's GPU time is mostly the water's.
/// <c>-noholes</c> leaves the holes out, for the cost they add.
/// </summary>
internal static class WaterScenes
{
	const int Columns = 20;
	const float RowSpacing = 450;
	const float ColumnSpacing = 300;

	static GameObject[] boats = [];
	static Vector3[] homes = [];
	static int frame;

	public static Scene Holes()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( -900, 0, 420 ) );
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

		// Built from this repo's shaders, so caustics skip the holes; the dev box's own material isn't yet.
		var material = Material.Load( "materials/dev/simple/simple_blend.vmat" );
		bool holes = !Args.Has( "-noholes" );
		boats = new GameObject[Columns * Columns];
		homes = new Vector3[boats.Length];
		for ( int i = 0; i < boats.Length; i++ )
		{
			homes[i] = new Vector3( i / Columns * RowSpacing, (i % Columns - (Columns - 1) * 0.5f) * ColumnSpacing, 0 );
			boats[i] = OpenHull( $"Boat {i}", homes[i], Rotation.FromYaw( i * 23 % 90 - 45 ), material, holes );
		}

		// Between the rows, clear of the hulls
		for ( int i = 0; i < 200; i++ )
		{
			var crate = new GameObject( true, $"Crate {i}" );
			crate.WorldPosition = new Vector3( i / Columns * RowSpacing + RowSpacing / 2, (i % Columns - (Columns - 1) * 0.5f) * ColumnSpacing, 10 );
			crate.WorldScale = 0.5f;
			crate.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );
			crate.Components.Create<BoxCollider>().Scale = 50;
			crate.Components.Create<Rigidbody>();
		}

		for ( int i = 0; i < 32; i++ )
		{
			var swimmer = new GameObject( true, $"Swimmer {i}" );
			swimmer.WorldPosition = new Vector3( i / 8 * RowSpacing * 2 + RowSpacing / 2, (i % 8 - 3.5f) * ColumnSpacing * 2 + ColumnSpacing / 2, -64 );
			var player = swimmer.Components.Create<PlayerController>();
			player.UseInputControls = false;
			player.UseCameraControls = false;
			player.EnableFootstepSounds = false;
			swimmer.Components.Create<Sandbox.Movement.MoveModeSwim>();
		}

		frame = 0;
		Animate( scene );
		return scene;
	}

	/// <summary>
	/// An open box: a floor below the waterline and walls above the crests, with a hole filling the inside.
	/// </summary>
	static GameObject OpenHull( string name, Vector3 position, Rotation rotation, Material material, bool hole )
	{
		const float length = 240, width = 130, bottom = -30, top = 26, wall = 6;
		var boat = new GameObject( true, name ) { WorldPosition = position, WorldRotation = rotation };
		Piece( new Vector3( 0, 0, bottom + wall / 2 ), new Vector3( length, width, wall ) );
		Piece( new Vector3( length / 2 - wall / 2, 0, (bottom + top) / 2 ), new Vector3( wall, width, top - bottom ) );
		Piece( new Vector3( -length / 2 + wall / 2, 0, (bottom + top) / 2 ), new Vector3( wall, width, top - bottom ) );
		Piece( new Vector3( 0, width / 2 - wall / 2, (bottom + top) / 2 ), new Vector3( length, wall, top - bottom ) );
		Piece( new Vector3( 0, -width / 2 + wall / 2, (bottom + top) / 2 ), new Vector3( length, wall, top - bottom ) );

		if ( hole )
		{
			boat.Components.Create<WaterHole>().Bounds = new BBox( new Vector3( -length / 2 + wall, -width / 2 + wall, bottom + wall ),
				new Vector3( length / 2 - wall, width / 2 - wall, top + 20 ) );
		}
		return boat;

		void Piece( Vector3 offset, Vector3 size )
		{
			var piece = new GameObject( boat, true, "Hull" ) { LocalPosition = offset, LocalScale = size / 50 };
			var renderer = piece.Components.Create<ModelRenderer>();
			renderer.Model = Model.Load( "models/dev/box.vmdl" );
			renderer.MaterialOverride = material;
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
