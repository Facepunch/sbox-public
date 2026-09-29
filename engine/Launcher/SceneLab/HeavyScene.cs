namespace Sandbox.SceneLab;

/// <summary>
/// A scene as heavy as a busy game's, for measuring the frame as a whole, GPU included, rather than one feature: 900 props
/// of the citizen addon's 47 models, scattered and turned, many tinted, some with another material and some with attributes of
/// their own (which native and the bridge draw on their own, as they can't instance them), a tenth of them moving; 40 citizens
/// animating, half dressed; 24 shadowed point and spot lights circling, so their shadow maps re-render every frame; the sun's
/// cascades and contact shadows; glass, sprites and particles; and a camera with tonemapping and bloom. Changes by frame count,
/// so every run is the same.
/// </summary>
internal static class HeavyScene
{
	const int PropCount = 900;
	const int CitizenCount = 40;
	const int LightCount = 24;
	const float Extent = 700;

	static readonly string[] Models =
	[
		"balloonears01", "balloonheart01", "balloonregular01", "balloontall01", "bathroomsink01", "beachball", "beertankard01",
		"broom01", "cardboardbox01", "chair01", "chair02", "chair03", "chair04blackleather", "chair05bluefabric", "chippacket01",
		"coffeemug01", "coin01", "concreteroaddivider01", "crate01", "crowbar01", "foamhand", "gritbin01_combined", "hotdog01",
		"icecreamcone01", "newspaper01", "oldoven", "recyclingbin01", "roadcone01", "sodacan01", "trashbag02", "trashcan01",
		"trashcan02", "wheel01", "wheel02", "wineglass01", "wineglass02", "crate01/piece", "crate01/piece1", "crate01/piece2",
		"crate01/piece3", "crate01/piece4", "crate01/piece5", "crate01/piece6", "crate01/piece7", "crate01/piece8",
		"hotdog01/hotdog01_lod02", "hotdog01/hotdogbun01_lod02",
	];

	static readonly string[] Materials =
	[
		"materials/dev/dev_metal_rough10.vmat", "materials/dev/primary_white_emissive.vmat", "materials/dev/black_grid_8.vmat",
		"materials/dev/dev_grating.vmat",
	];

	static ModelRenderer[] movers = [];
	static Vector3[] homes = [];
	static SkinnedModelRenderer[] citizens = [];
	static Light[] lights = [];
	static float[] lightRadii = [];
	static int frame;

	/// <summary>
	/// The same scene with ambient occlusion on its camera: GTAO's compute, and the depth-normals prepass it reads. Without its
	/// particles, which are random: AO's history is the camera's, so it compares isolated, each renderer from its own load.
	/// </summary>
	public static Scene BuildWithOcclusion()
	{
		var scene = Build();
		using var _ = scene.Push();
		scene.Directory.FindByName( "Camera" ).First().Components.Create<Sandbox.AmbientOcclusion>();
		for ( int i = 0; i < 2; i++ ) scene.Directory.FindByName( $"Particles {i}" ).First().Destroy();
		return scene;
	}

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( -820, -640, 420 ), new Vector3( 0, 0, 0 ) );
		using var _ = scene.Push();

		var random = new Random( 1234 );
		scene.Directory.FindByName( "Floor" ).First().WorldScale = new Vector3( 16, 16, 1 );
		scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>().ContactShadows = true;

		var moving = new List<ModelRenderer>();
		for ( int i = 0; i < PropCount; i++ )
		{
			var position = new Vector3( Scatter( random ), Scatter( random ), 0 );
			var prop = GameScenes.Prop( $"Prop {i}", $"models/citizen_props/{Models[i % Models.Length]}.vmdl", position, scale: 0.8f + random.NextSingle() * 0.8f );
			prop.GameObject.WorldRotation = Rotation.FromYaw( random.NextSingle() * 360 );
			GameScenes.OnFloor( prop );

			if ( i % 2 == 0 ) prop.Tint = new ColorHsv( random.NextSingle() * 360, 0.4f, 1 ).ToColor();
			if ( i % 7 == 0 ) prop.MaterialOverride = Material.Load( Materials[i / 7 % Materials.Length] );
			if ( i % 9 == 0 ) prop.Attributes.Set( "scenelab_prop", i );
			if ( i % 10 == 5 ) moving.Add( prop );
		}

		movers = moving.ToArray();
		homes = new Vector3[movers.Length];
		for ( int i = 0; i < movers.Length; i++ ) homes[i] = movers[i].WorldPosition;

		citizens = new SkinnedModelRenderer[CitizenCount];
		for ( int i = 0; i < citizens.Length; i++ )
		{
			var position = new Vector3( Scatter( random ) * 0.6f, Scatter( random ) * 0.6f, 0 );
			var citizen = i % 2 == 0 ? GameScenes.DressedCitizen( $"Citizen {i}", position ) : GameScenes.Citizen( $"Citizen {i}", position );
			citizen.GameObject.WorldRotation = Rotation.FromYaw( random.NextSingle() * 360 );
			citizens[i] = citizen;
		}

		lights = new Light[LightCount];
		lightRadii = new float[LightCount];
		for ( int i = 0; i < lights.Length; i++ )
		{
			var go = new GameObject( true, $"Light {i}" );
			if ( i % 2 == 0 )
			{
				var point = go.Components.Create<PointLight>();
				point.Radius = 260;
				lights[i] = point;
			}
			else
			{
				var spot = go.Components.Create<SpotLight>();
				spot.Radius = 500;
				spot.ConeOuter = 40;
				spot.ConeInner = 25;
				lights[i] = spot;
			}

			lights[i].LightColor = new ColorHsv( i * 360.0f / LightCount, 0.6f, 1 ).ToColor() * 3;
			lights[i].Shadows = true;
			lightRadii[i] = 150 + random.NextSingle() * (Extent - 200);
		}

		for ( int i = 0; i < 6; i++ )
		{
			var angle = i * MathF.Tau / 6;
			GameScenes.Prop( $"Glass {i}", "models/dev/box.vmdl", new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0.1f ) * 320, new Color( 0.6f, 0.8f, 1 ), "materials/dev/primary_white_trans.vmat", 0.7f );
		}

		var sprite = Sprite.FromTexture( Texture.Load( "textures/particles/base_sprite.vtex" ) );
		for ( int i = 0; i < 16; i++ )
		{
			var go = new GameObject( true, $"Sprite {i}" );
			go.WorldPosition = new Vector3( Scatter( random ), Scatter( random ), 90 + random.NextSingle() * 60 );
			var renderer = go.Components.Create<SpriteRenderer>();
			renderer.Sprite = sprite;
			renderer.Size = 50;
			renderer.Color = new ColorHsv( i * 22.5f, 0.7f, 1 ).ToColor();
			renderer.Additive = i % 2 == 0;
		}

		for ( int i = 0; i < 2; i++ )
		{
			var fx = new GameObject( true, $"Particles {i}" );
			fx.WorldPosition = new Vector3( i == 0 ? -250 : 250, 0, 80 );
			var effect = fx.Components.Create<ParticleEffect>();
			effect.MaxParticles = 300;
			effect.Lifetime = 30;
			var emitter = fx.Components.Create<ParticleSphereEmitter>();
			emitter.Burst = 200;
			emitter.Rate = 0;
			emitter.Radius = 120;
			emitter.Velocity = 4;
			fx.Components.Create<ParticleSpriteRenderer>().Sprite = sprite;
		}

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;
		camera.GameObject.Components.Create<Bloom>();

		frame = 0;
		Animate( scene );
		return scene;
	}

	static float Scatter( Random random ) => (random.NextSingle() * 2 - 1) * Extent;

	/// <summary>
	/// A frame's changes, by frame count: the movers bob and turn, the citizens play through their sequence, the lights circle.
	/// </summary>
	public static void Animate( Scene scene )
	{
		var t = frame++ / 60.0f;

		for ( int i = 0; i < movers.Length; i++ )
		{
			if ( !movers[i].IsValid() ) continue;

			movers[i].WorldPosition = homes[i] + Vector3.Up * (20 + 20 * MathF.Sin( t * 2 + i ));
			movers[i].WorldRotation = Rotation.FromYaw( t * 40 + i * 13 );
		}

		for ( int i = 0; i < citizens.Length; i++ )
		{
			if ( citizens[i].IsValid() ) citizens[i].Sequence.TimeNormalized = (t * 0.25f + i * 0.037f) % 1;
		}

		for ( int i = 0; i < lights.Length; i++ )
		{
			if ( !lights[i].IsValid() ) continue;

			var angle = t * (i % 3 == 0 ? -0.5f : 0.35f) + i * MathF.Tau / lights.Length;
			var position = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * lightRadii[i] + Vector3.Up * 120;
			lights[i].WorldPosition = position;

			// Spots look down and out, at the floor ahead of them
			if ( lights[i] is SpotLight ) lights[i].WorldRotation = Rotation.LookAt( position.WithZ( 0 ) * 1.2f - position );
		}
	}
}
