namespace Sandbox.SceneLab;

/// <summary>
/// Smoke and glass: a street at dusk in volumetric fog, a shopfront across it with refracting glass windows on two floors and a
/// lit room behind them, a glass bus shelter in front, and three smoke grenades going off one after another, each pouring out
/// lit smoke that overlaps the others' (one particle effect, placed by <see cref="Scenarios.Emit"/> from a fixed seed). Two
/// shadowed street lamps shine down through the smoke and fog. The camera dollies along the street and back, so the smoke is
/// seen through the shelter's glass and in front of the shop's. The grenades are a stand-in: the citizen addon's soda can, as
/// the repository has no grenade model. Its camera renders natively alongside, for the fog's history.
/// </summary>
internal static class SmokeAndGlassScene
{
	const int LoopFrames = 600;
	const string BoxModel = "models/dev/box.vmdl";
	const float ShopFront = 260;
	const float ShopHeight = 300;

	static readonly Vector3[] Grenades = [new( -220, 60, 0 ), new( 120, -20, 0 ), new( 380, 120, 0 )];
	static readonly int[] GrenadeStarts = [0, 70, 160];
	const int GrenadeFrames = 420;

	static ParticleEffect smoke;
	static Random random;
	static int frame;

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( -500, -420, 90 ), new Vector3( 0, 200, 110 ) );
		using var _ = scene.Push();

		camera.ZFar = 6000;
		camera.FieldOfView = 80;
		var floor = scene.Directory.FindByName( "Floor" ).First();
		floor.WorldScale = new Vector3( 30, 30, 1 );
		floor.Components.Get<ModelRenderer>().MaterialOverride = Material.Load( "materials/dev/gray_25.vmat" );

		// Dusk: a low, warm sun, a dim sky
		var sun = scene.Directory.FindByName( "Sun" ).First();
		sun.WorldRotation = Rotation.LookAt( new Vector3( 0.6f, 0.5f, -0.25f ) );
		var light = sun.Components.Get<DirectionalLight>();
		light.LightColor = new Color( 1, 0.6f, 0.35f ) * 1.4f;
		light.SkyColor = new Color( 0.06f, 0.07f, 0.1f );
		scene.Directory.FindByName( "Sky" ).First().Components.Get<SkyBox2D>().Tint = new Color( 0.5f, 0.42f, 0.4f );

		Shop();
		Shelter();

		for ( int i = 0; i < 2; i++ )
		{
			var go = new GameObject( true, $"Street Lamp {i}" );
			go.WorldPosition = new Vector3( i == 0 ? -300 : 300, -120, 330 );
			go.WorldRotation = Rotation.LookAt( new Vector3( i == 0 ? 0.25f : -0.25f, 0.2f, -1 ) );
			var spot = go.Components.Create<SpotLight>();
			spot.LightColor = new Color( 1, 0.85f, 0.6f ) * 8;
			spot.Radius = 700;
			spot.ConeOuter = 45;
			spot.ConeInner = 25;
			spot.Shadows = true;
			GameScenes.Prop( $"Street Lamp {i} Post", BoxModel, go.WorldPosition.WithZ( 170 ), new Color( 0.15f, 0.15f, 0.17f ) ).GameObject.WorldScale = new Vector3( 0.12f, 0.12f, 6.8f );
		}

		for ( int i = 0; i < Grenades.Length; i++ )
		{
			var grenade = GameScenes.Prop( $"Grenade {i}", "models/citizen_props/sodacan01.vmdl", Grenades[i], new Color( 0.35f, 0.4f, 0.3f ) );
			grenade.GameObject.WorldRotation = Rotation.From( 0, i * 70, 90 );
			GameScenes.OnFloor( grenade ).GameObject.WorldPosition += Vector3.Up * 2;
		}

		var volume = new GameObject( true, "Fog Volume" );
		volume.WorldPosition = new Vector3( 0, 50, 180 );
		var fog = volume.Components.Create<VolumetricFogVolume>();
		fog.Bounds = BBox.FromPositionAndSize( 0, new Vector3( 1600, 1000, 360 ) );
		fog.Strength = 0.6f;
		fog.FalloffExponent = 0.6f;

		smoke = Scenarios.Particles( "Smoke", "textures/particles/smoke/smoke_loop_a.vtex", 4000, lit: true, alpha: 0.5f, grow: 4, gravity: -6, damping: 0.35f );

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;
		camera.GameObject.Components.Create<Bloom>();

		Rewind( scene );
		return scene;
	}

	/// <summary>
	/// A shopfront: a wall with four windows on each of two floors, glazed with refracting glass, and a warmly lit room behind.
	/// </summary>
	static void Shop()
	{
		const float width = 900;
		const float window = 150;
		const float windowHeight = 110;
		const float wall = 16;
		float[] sills = [40, 190];

		// The wall around the windows: columns between them, bands between the floors
		var columns = 5;
		var column = (width - 4 * window) / columns;
		for ( int i = 0; i < columns; i++ )
		{
			var x = -width / 2 + column / 2 + i * (column + window);
			Box( $"Shop Column {i}", new Vector3( x, ShopFront, ShopHeight / 2 ), new Vector3( column, wall, ShopHeight ), new Color( 0.55f, 0.45f, 0.4f ) );
		}

		Box( "Shop Band 0", new Vector3( 0, ShopFront, sills[0] / 2 ), new Vector3( width, wall, sills[0] ), new Color( 0.5f, 0.42f, 0.38f ) );
		Box( "Shop Band 1", new Vector3( 0, ShopFront, (sills[0] + windowHeight + sills[1]) / 2 ), new Vector3( width, wall, sills[1] - sills[0] - windowHeight ), new Color( 0.5f, 0.42f, 0.38f ) );
		Box( "Shop Band 2", new Vector3( 0, ShopFront, (sills[1] + windowHeight + ShopHeight) / 2 ), new Vector3( width, wall, ShopHeight - sills[1] - windowHeight ), new Color( 0.5f, 0.42f, 0.38f ) );

		var glass = GameScenes.Glass( "scenelab_window_glass", 1.03f, 0.15f );
		for ( int floor = 0; floor < 2; floor++ )
		{
			for ( int i = 0; i < 4; i++ )
			{
				var x = -width / 2 + column + window / 2 + i * (column + window);
				var pane = Box( $"Window {floor} {i}", new Vector3( x, ShopFront, sills[floor] + windowHeight / 2 ), new Vector3( window, 3, windowHeight ), null );
				pane.MaterialOverride = glass;
				pane.RenderType = ModelRenderer.ShadowRenderType.Off;
			}
		}

		// The room behind: floor, back and sides, lit warm, with things in it
		const float depth = 360;
		Box( "Shop Floor", new Vector3( 0, ShopFront + depth / 2, 1 ), new Vector3( width, depth, 2 ), new Color( 0.35f, 0.3f, 0.28f ) );
		Box( "Shop Ceiling", new Vector3( 0, ShopFront + depth / 2, ShopHeight ), new Vector3( width, depth, wall ), new Color( 0.6f, 0.6f, 0.6f ) );
		Box( "Shop Back", new Vector3( 0, ShopFront + depth, ShopHeight / 2 ), new Vector3( width, wall, ShopHeight ), new Color( 0.7f, 0.55f, 0.4f ) );
		Box( "Shop West", new Vector3( -width / 2, ShopFront + depth / 2, ShopHeight / 2 ), new Vector3( wall, depth, ShopHeight ), new Color( 0.7f, 0.55f, 0.4f ) );
		Box( "Shop East", new Vector3( width / 2, ShopFront + depth / 2, ShopHeight / 2 ), new Vector3( wall, depth, ShopHeight ), new Color( 0.7f, 0.55f, 0.4f ) );
		Box( "Shop Middle Floor", new Vector3( 0, ShopFront + depth / 2, sills[1] - 20 ), new Vector3( width, depth, 10 ), new Color( 0.5f, 0.45f, 0.4f ) );

		var props = new Random( 99 );
		string[] things = ["chair01", "chair02", "crate01", "cardboardbox01", "trashcan02", "oldoven", "recyclingbin01", "chair05bluefabric"];
		for ( int i = 0; i < 16; i++ )
		{
			var upstairs = i % 2 == 1;
			var at = new Vector3( (props.NextSingle() - 0.5f) * (width - 120), ShopFront + 60 + props.NextSingle() * (depth - 120), upstairs ? sills[1] - 15 : 0 );
			var prop = GameScenes.Prop( $"Shop Prop {i}", $"models/citizen_props/{things[i % things.Length]}.vmdl", at );
			prop.GameObject.WorldRotation = Rotation.FromYaw( props.NextSingle() * 360 );
			prop.GameObject.WorldPosition = at.WithZ( at.z - prop.Model.Bounds.Mins.z );
		}

		for ( int i = 0; i < 4; i++ )
		{
			var go = new GameObject( true, $"Shop Light {i}" );
			go.WorldPosition = new Vector3( (i % 2 - 0.5f) * 450, ShopFront + depth / 2, i < 2 ? sills[1] - 45 : ShopHeight - 30 );
			var point = go.Components.Create<PointLight>();
			point.LightColor = new Color( 1, 0.75f, 0.45f ) * 3;
			point.Radius = 420;
			point.Shadows = true;
		}
	}

	/// <summary>
	/// A bus shelter on the near pavement: three glass panes and a roof.
	/// </summary>
	static void Shelter()
	{
		var glass = GameScenes.Glass( "scenelab_shelter_glass", 1.06f, 0 );
		var at = new Vector3( -60, -260, 0 );
		(Vector3 Offset, Vector3 Size)[] panes = [(new( 0, 60, 70 ), new( 260, 3, 130 )), (new( -130, 0, 70 ), new( 3, 120, 130 )), (new( 130, 0, 70 ), new( 3, 120, 130 ))];
		for ( int i = 0; i < panes.Length; i++ )
		{
			var pane = Box( $"Shelter Glass {i}", at + panes[i].Offset, panes[i].Size, null );
			pane.MaterialOverride = glass;
			pane.RenderType = ModelRenderer.ShadowRenderType.Off;
		}

		Box( "Shelter Roof", at + new Vector3( 0, 0, 140 ), new Vector3( 270, 130, 6 ), new Color( 0.2f, 0.22f, 0.25f ) );
	}

	static ModelRenderer Box( string name, Vector3 position, Vector3 size, Color? tint )
	{
		var box = GameScenes.Prop( name, BoxModel, position, tint );
		box.GameObject.WorldScale = size / Model.Load( BoxModel ).Bounds.Size.x;
		return box;
	}

	public static void Rewind( Scene scene )
	{
		frame = 0;
		random = new Random( 515 );
		Scenarios.Clear( smoke );
		Animate( scene );
	}

	/// <summary>
	/// A frame: each grenade pours smoke while it burns, thinning as it goes; the camera dollies along the street and back.
	/// </summary>
	public static void Animate( Scene scene )
	{
		var f = frame++ % LoopFrames;

		for ( int g = 0; g < Grenades.Length; g++ )
		{
			var age = f - GrenadeStarts[g];
			if ( age < 0 || age >= GrenadeFrames ) continue;

			// A burst, then a steady pour that tails off
			var count = age < 20 ? 8 : age < GrenadeFrames / 2 ? 4 : 2;
			for ( int i = 0; i < count; i++ )
			{
				var spread = new Vector3( random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, 0 ) * 2;
				var velocity = spread * (40 + random.NextSingle() * 90) + Vector3.Up * (25 + random.NextSingle() * 45) + new Vector3( 12, 6, 0 );
				var grey = 0.8f + random.NextSingle() * 0.2f;
				Scenarios.Emit( smoke, Grenades[g] + Vector3.Up * 8, velocity, 30 + random.NextSingle() * 25, random.NextSingle() * 360, new Color( grey, grey, grey * 0.97f ), 5 + random.NextSingle() * 3 );
			}
		}

		if ( scene.Camera is { } camera )
		{
			// Along the street and back, looking across at the shop
			var along = MathF.Sin( f / (float)LoopFrames * MathF.Tau - MathF.PI / 2 ) * 0.5f + 0.5f;
			var x = MathX.Lerp( -520, 520, along );
			camera.WorldPosition = new Vector3( x, -420, 90 );
			camera.WorldRotation = Rotation.LookAt( new Vector3( x * 0.4f, ShopFront, 120 ) - camera.WorldPosition );
		}
	}
}
