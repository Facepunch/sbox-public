namespace Sandbox.SceneLab;

/// <summary>
/// A building at night, walked through: a corridor with four rooms either side, each furnished with the citizen addon's props,
/// lit by 48 shadowed lights - a ceiling light, a floor lamp, a sconce and a desk lamp in each room, ceiling lights down the
/// corridor and spots over the doors - every sixth flickering like a failing tube. The walls, floors and ceilings are static, as a map's are, so lights
/// keep their shadows in a cache; the props are not. Outside is a dim moonlit night. The camera walks down the corridor,
/// turns into a room on each side, looks round, and walks back (<see cref="Path"/>).
/// </summary>
internal static class NightInteriorScene
{
	const float RoomSize = 400;
	const float CorridorWidth = 200;
	const float Height = 150;
	const float Wall = 10;
	const int RoomsPerSide = 4;
	const float Length = RoomsPerSide * RoomSize;
	const string BoxModel = "models/dev/box.vmdl";

	static readonly string[] Furniture =
	[
		"chair01", "chair02", "chair03", "chair04blackleather", "chair05bluefabric", "cardboardbox01", "crate01", "trashcan01",
		"recyclingbin01", "oldoven", "bathroomsink01", "coffeemug01", "beertankard01", "newspaper01", "broom01", "trashbag02",
	];

	static readonly CameraPath Path = new CameraPath( new Vector3( 40, 0, 64 ), 0, -4 )
		.MoveTo( new Vector3( RoomSize * 1.5f, 0, 64 ), 150 )
		.Turn( 90, 0.6f )
		.MoveTo( new Vector3( RoomSize * 1.5f, CorridorWidth / 2 + RoomSize * 0.55f, 64 ), 120 )
		.Turn( -150, 2.5f )
		.Turn( 240, 3.5f )
		.MoveTo( new Vector3( RoomSize * 1.5f, 0, 64 ), 150 )
		.MoveTo( new Vector3( RoomSize * 2.5f, 0, 64 ), 150 )
		.MoveTo( new Vector3( RoomSize * 2.5f, -(CorridorWidth / 2 + RoomSize * 0.5f), 64 ), 120 )
		.Turn( 200, 3 )
		.MoveTo( new Vector3( RoomSize * 2.5f, 0, 64 ), 150 )
		.MoveTo( new Vector3( Length - 60, 0, 64 ), 150 )
		.Turn( 180, 0.8f )
		.MoveTo( new Vector3( 40, 0, 64 ), 220 )
		.Turn( 180, 0.8f );

	public static int Frames => Path.Frames;

	static Light[] lights = [];
	static Color[] colors = [];
	static int frame;

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var camera );
		using var _ = scene.Push();

		camera.ZFar = 4000;
		camera.FieldOfView = 80;

		// Night: a dim blue moon, its cascades still drawn, and a dark sky
		var sun = scene.Directory.FindByName( "Sun" ).First();
		sun.WorldRotation = Rotation.LookAt( new Vector3( 0.3f, 0.5f, -0.8f ) );
		var moon = sun.Components.Get<DirectionalLight>();
		moon.LightColor = new Color( 0.25f, 0.3f, 0.45f ) * 0.4f;
		moon.SkyColor = new Color( 0.01f, 0.012f, 0.02f );
		scene.Directory.FindByName( "Sky" ).First().Components.Get<SkyBox2D>().Tint = new Color( 0.03f, 0.035f, 0.06f );
		scene.Directory.FindByName( "Floor" ).First().WorldScale = new Vector3( 40, 40, 1 );

		var random = new Random( 606 );
		var lit = new List<Light>();

		// The shell: floor, ceiling, outer walls, the corridor's walls with doorways into each room
		var middle = new Vector3( Length / 2, 0, 0 );
		var depth = CorridorWidth + 2 * RoomSize;
		Box( "Interior Floor", middle + Vector3.Up * 1, new Vector3( Length, depth, 2 ), "materials/dev/reflectivity_30.vmat" );
		Box( "Ceiling", middle + Vector3.Up * (Height + Wall / 2), new Vector3( Length, depth, Wall ), "materials/dev/gray_50.vmat" );
		Box( "Outer Wall North", new Vector3( Length / 2, depth / 2, Height / 2 ), new Vector3( Length, Wall, Height ) );
		Box( "Outer Wall South", new Vector3( Length / 2, -depth / 2, Height / 2 ), new Vector3( Length, Wall, Height ) );
		Box( "Outer Wall West", new Vector3( 0, 0, Height / 2 ), new Vector3( Wall, depth, Height ) );
		Box( "Outer Wall East", new Vector3( Length, 0, Height / 2 ), new Vector3( Wall, depth, Height ) );

		for ( int side = -1; side <= 1; side += 2 )
		{
			var wallY = side * CorridorWidth / 2;
			for ( int room = 0; room < RoomsPerSide; room++ )
			{
				var x0 = room * RoomSize;
				var name = $"Room {(side < 0 ? "S" : "N")}{room}";

				// The corridor wall, with a doorway in the middle and a lintel over it
				const float door = 70;
				var segment = (RoomSize - door) / 2;
				Box( $"{name} Wall A", new Vector3( x0 + segment / 2, wallY, Height / 2 ), new Vector3( segment, Wall, Height ) );
				Box( $"{name} Wall B", new Vector3( x0 + RoomSize - segment / 2, wallY, Height / 2 ), new Vector3( segment, Wall, Height ) );
				Box( $"{name} Lintel", new Vector3( x0 + RoomSize / 2, wallY, Height - 20 ), new Vector3( door, Wall, 40 ) );

				// The wall to the next room
				if ( room > 0 ) Box( $"{name} Partition", new Vector3( x0, side * (CorridorWidth / 2 + RoomSize / 2), Height / 2 ), new Vector3( Wall, RoomSize, Height ) );

				var centre = new Vector3( x0 + RoomSize / 2, side * (CorridorWidth / 2 + RoomSize / 2), 0 );
				var warm = new ColorHsv( 25 + random.NextSingle() * 25, 0.45f + random.NextSingle() * 0.2f, 1 ).ToColor();

				lit.Add( Point( $"{name} Ceiling Light", centre + Vector3.Up * (Height - 12), warm * 2.2f, 420 ) );
				Fixture( centre + Vector3.Up * (Height - 4) );

				var lamp = centre + new Vector3( (random.NextSingle() - 0.5f) * RoomSize * 0.6f, (random.NextSingle() - 0.5f) * RoomSize * 0.6f, 0 );
				lit.Add( Spot( $"{name} Lamp", lamp + Vector3.Up * 70, lamp + Vector3.Up * 70 + new Vector3( random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, -1 ), warm * 3, 260, 55 ) );
				GameScenes.OnFloor( GameScenes.Prop( $"{name} Lamp Stand", BoxModel, lamp, new Color( 0.2f, 0.2f, 0.22f ) ) ).GameObject.WorldScale = new Vector3( 0.12f, 0.12f, 1.3f );

				// A sconce on the back wall, and a desk lamp in a corner
				var back = centre + new Vector3( 0, side * (RoomSize / 2 - 25), 95 );
				lit.Add( Point( $"{name} Sconce", back, warm * 1.2f, 200 ) );
				var desk = centre + new Vector3( -side * RoomSize * 0.3f, side * RoomSize * 0.3f, 45 );
				lit.Add( Spot( $"{name} Desk Lamp", desk, desk + new Vector3( 0.4f, 0, -1 ), new Color( 1, 0.95f, 0.85f ) * 4, 160, 50 ) );

				for ( int i = 0; i < 6; i++ )
				{
					var spot = centre + new Vector3( (random.NextSingle() - 0.5f) * RoomSize * 0.75f, (random.NextSingle() - 0.5f) * RoomSize * 0.75f, 0 );
					var prop = GameScenes.Prop( $"{name} Prop {i}", $"models/citizen_props/{Furniture[random.Next( Furniture.Length )]}.vmdl", spot, scale: 1.1f );
					prop.GameObject.WorldRotation = Rotation.FromYaw( random.NextSingle() * 360 );
					GameScenes.OnFloor( prop ).GameObject.WorldPosition += Vector3.Up * 2;
				}
			}
		}

		// Down the corridor: ceiling lights, and spots over every doorway washing the walls
		for ( int i = 0; i < RoomsPerSide * 2; i++ )
		{
			var x = (i + 0.5f) * Length / (RoomsPerSide * 2);
			lit.Add( Point( $"Corridor Light {i}", new Vector3( x, 0, Height - 12 ), new Color( 0.75f, 0.85f, 1 ) * 1.6f, 300 ) );
			Fixture( new Vector3( x, 0, Height - 4 ) );
		}

		for ( int i = 0; i < RoomsPerSide * 2; i++ )
		{
			var side = i % 2 == 0 ? 1 : -1;
			var x = (i / 2 + 0.5f) * RoomSize;
			var at = new Vector3( x, side * (CorridorWidth / 2 - 20), Height - 15 );
			lit.Add( Spot( $"Door Spot {i}", at, at + new Vector3( 0, side * 0.3f, -1 ), new Color( 1, 0.8f, 0.6f ) * 2, 220, 40 ) );
		}

		lights = lit.ToArray();
		colors = lights.Select( x => x.LightColor ).ToArray();

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;
		tonemapping.ExposureCompensation = 1;
		camera.GameObject.Components.Create<Bloom>();

		Rewind( scene );
		return scene;
	}

	static ModelRenderer Box( string name, Vector3 position, Vector3 size, string material = "materials/dev/gray_75.vmat" )
	{
		var box = GameScenes.Prop( name, BoxModel, position, null, material );
		box.GameObject.WorldScale = size / Model.Load( BoxModel ).Bounds.Size.x;
		box.GameObject.IsStatic = true;
		return box;
	}

	/// <summary>
	/// A light fitting: a small glowing panel on the ceiling, casting no shadow of its own.
	/// </summary>
	static void Fixture( Vector3 position )
	{
		var panel = GameScenes.Prop( "Fixture", BoxModel, position, new Color( 3, 2.8f, 2.5f ), "materials/dev/primary_white_emissive.vmat" );
		panel.GameObject.WorldScale = new Vector3( 0.6f, 0.25f, 0.05f );
		panel.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	static Light Point( string name, Vector3 position, Color color, float radius )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;
		var light = go.Components.Create<PointLight>();
		light.LightColor = color;
		light.Radius = radius;
		light.Shadows = true;
		return light;
	}

	static Light Spot( string name, Vector3 position, Vector3 target, Color color, float radius, float cone )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;
		go.WorldRotation = Rotation.LookAt( target - position );
		var light = go.Components.Create<SpotLight>();
		light.LightColor = color;
		light.Radius = radius;
		light.ConeOuter = cone;
		light.ConeInner = cone * 0.6f;
		light.Shadows = true;
		return light;
	}

	public static void Rewind( Scene scene )
	{
		frame = 0;
		Walk( scene );
	}

	/// <summary>
	/// A frame: the camera along its walk, every sixth light flickering.
	/// </summary>
	public static void Walk( Scene scene )
	{
		var f = frame++;
		if ( scene.Camera is { } camera ) Path.Apply( camera, f );

		for ( int i = 0; i < lights.Length; i += 6 )
		{
			if ( lights[i].IsValid() ) lights[i].LightColor = colors[i] * Scenarios.Flicker( i, f % Path.Frames );
		}
	}
}
