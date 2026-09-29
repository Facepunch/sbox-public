namespace Sandbox.SceneLab;

/// <summary>
/// A busy GameObject scene that changes every frame the way a game's does, to measure the renderer and its bridge under
/// change rather than in a still scene: of 400 props, a quarter fade in and out (moving between the opaque and fade
/// layers), a quarter change colour, a quarter bob up and down, and a quarter stay still. Four shadowed point lights
/// circle, so their shadow maps re-render every frame. Sprites and particles draw themselves (custom objects), and a
/// second camera renders the scene into a texture, as a monitor or a mirror would.
/// </summary>
internal static class DynamicScene
{
	const int Side = 20;

	static ModelRenderer[] props = [];
	static Vector3[] homes = [];
	static PointLight[] lights = [];
	static int frame;

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _camera, new Vector3( -700, -600, 450 ) );
		using var _ = scene.Push();

		string[] models = ["models/dev/box.vmdl", "models/dev/sphere.vmdl"];
		props = new ModelRenderer[Side * Side];
		homes = new Vector3[props.Length];
		for ( int i = 0; i < props.Length; i++ )
		{
			var position = new Vector3( (i % Side - Side / 2.0f) * 45, (i / Side - Side / 2.0f) * 45, 15 );
			props[i] = GameScenes.Prop( $"Prop {i}", models[i % 2], position, scale: 0.35f );
			homes[i] = position;
		}

		lights = new PointLight[4];
		for ( int i = 0; i < lights.Length; i++ )
		{
			var light = new GameObject( true, $"Light {i}" ).Components.Create<PointLight>();
			light.LightColor = new ColorHsv( i * 90, 0.6f, 1 ).ToColor() * 3;
			light.Radius = 300;
			light.Shadows = true;
			lights[i] = light;
		}

		var sprite = Sprite.FromTexture( Texture.Load( "textures/particles/base_sprite.vtex" ) );
		for ( int i = 0; i < 4; i++ )
		{
			var go = new GameObject( true, $"Sprite {i}" );
			go.WorldPosition = new Vector3( i % 2 == 0 ? -300 : 300, i < 2 ? -300 : 300, 120 );
			var renderer = go.Components.Create<SpriteRenderer>();
			renderer.Sprite = sprite;
			renderer.Size = 80;
			renderer.Color = new ColorHsv( i * 90 + 45, 0.7f, 1 ).ToColor();
		}

		var fx = new GameObject( true, "Particles" );
		fx.WorldPosition = new Vector3( 0, 0, 80 );
		var effect = fx.Components.Create<ParticleEffect>();
		effect.MaxParticles = 300;
		effect.Lifetime = 30;
		var emitter = fx.Components.Create<ParticleSphereEmitter>();
		emitter.Burst = 200;
		emitter.Rate = 0;
		emitter.Radius = 150;
		emitter.Velocity = 4;
		fx.Components.Create<ParticleSpriteRenderer>().Sprite = sprite;

		// A second camera, rendering into a texture every frame
		var monitor = new GameObject( true, "Monitor Camera" );
		monitor.WorldPosition = new Vector3( 500, 450, 350 );
		monitor.WorldRotation = Rotation.LookAt( -monitor.WorldPosition );
		var monitorCamera = monitor.Components.Create<CameraComponent>();
		monitorCamera.IsMainCamera = false;
		monitorCamera.FieldOfView = 70;
		monitorCamera.ZNear = 5;
		monitorCamera.ZFar = 5000;
		monitorCamera.RenderTarget = Texture.CreateRenderTarget().WithSize( 512, 512 ).WithFormat( ImageFormat.RGBA8888 ).Create( "Dynamic monitor" );

		frame = 0;
		Animate( scene );
		return scene;
	}

	/// <summary>
	/// A frame's changes, the same each run: by frame count, not time.
	/// </summary>
	public static void Animate( Scene scene )
	{
		var t = frame++ / 60.0f;

		for ( int i = 0; i < props.Length; i++ )
		{
			if ( !props[i].IsValid() ) continue;

			switch ( i % 4 )
			{
				case 0:
					// Faded under 1, opaque at 1: across the opaque and fade layers
					props[i].Tint = Color.White.WithAlpha( Math.Min( 1.0f, 0.6f + 0.5f * MathF.Sin( t * 2 + i ) ) );
					break;
				case 1:
					props[i].Tint = new ColorHsv( (t * 60 + i * 7) % 360, 0.5f, 1 ).ToColor();
					break;
				case 2:
					props[i].WorldPosition = homes[i] + Vector3.Up * (10 * MathF.Sin( t * 3 + i ));
					break;
			}
		}

		for ( int i = 0; i < lights.Length; i++ )
		{
			if ( !lights[i].IsValid() ) continue;

			var angle = t * 0.8f + i * MathF.Tau / lights.Length;
			lights[i].WorldPosition = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * 250 + Vector3.Up * 80;
		}
	}
}
