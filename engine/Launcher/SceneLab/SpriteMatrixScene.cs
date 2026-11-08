namespace Sandbox.SceneLab;

/// <summary>
/// The Sprite Matrix preset: every kind of sprite the batch draws, all casting shadows from the sun's cascades, three point
/// lights and a spot, over one floor - translucent, sorted, opaque, additive, lit, yaw-only and flipped, camera-faded and
/// feathered, refractive, particle groups (plain, sorted with motion blur and facing their velocity, opaque and lit, text)
/// and an ordinary text renderer. Particles are emitted by hand so every run is the same. By frame, sprites move, a
/// cluster grows, another is destroyed, one stops casting and one is switched off, so the batches rebuild under it.
/// The Sprite Shadow Stress preset is the same idea at the other extreme: thousands of component sprites in two batches
/// under eight shadowed lights, for what a per-view test over every sprite costs.
/// </summary>
internal static class SpriteMatrixScene
{
	static int frame;
	static Sprite sprite;

	const int Columns = 4;
	const int Rows = 3;

	static Vector3 Cell( int index ) => new( (index % Columns - 1.5f) * 220, (index / Columns - 1) * 200, 0 );

	static void Lights( Scene scene, bool sunShadows )
	{
		var sun = scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>();
		sun.Shadows = sunShadows;
		sun.LightColor = new Color( 1.0f, 0.95f, 0.85f ) * 0.6f;
		sun.WorldRotation = new Angles( 38, 25, 0 );
	}

	static GameObject Point( string name, Vector3 position, float radius, float hue )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;

		var light = go.Components.Create<PointLight>();
		light.LightColor = new ColorHsv( hue, 0.3f, 1 ).ToColor() * 6;
		light.Radius = radius;
		light.Shadows = true;
		return go;
	}

	static SpriteRenderer Cast( string name, Vector3 position, float size, Color color )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;

		var renderer = go.Components.Create<SpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Size = size;
		renderer.Color = color;
		renderer.Shadows = true;
		return renderer;
	}

	static ParticleSpriteRenderer Particles( string name, Vector3 center, int count, float size, Vector3 velocity )
	{
		var fx = new GameObject( true, name );
		fx.WorldPosition = center;

		var effect = fx.Components.Create<ParticleEffect>();
		effect.Lifetime = 3600;
		effect.StartVelocity = 0;

		var renderer = fx.Components.Create<ParticleSpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Shadows = true;

		for ( int i = 0; i < count; i++ )
		{
			var particle = effect.Emit( center + new Vector3( MathF.Cos( i * 1.1f ) * 55, MathF.Sin( i * 1.1f ) * 55, 45 + i * 11 ), 0 );
			particle.Size = size;
			particle.Velocity = velocity;
			particle.Angles = new Angles( 0, 0, i * 30 );
		}

		return renderer;
	}

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( 0, -760, 620 ), new Vector3( 0, 0, 20 ) );
		using var _ = scene.Push();
		Lights( scene, true );

		Point( "Light 0", new Vector3( -240, -100, 170 ), 430, 20 );
		Point( "Light 1", new Vector3( 240, -100, 170 ), 430, 140 );
		Point( "Light 2", new Vector3( 0, 170, 170 ), 430, 260 );

		var spot = new GameObject( true, "Spot" );
		spot.WorldPosition = new Vector3( 0, -300, 320 );
		spot.WorldRotation = Rotation.LookAt( new Vector3( 0, 40, 0 ) - spot.WorldPosition );
		var spotLight = spot.Components.Create<SpotLight>();
		spotLight.LightColor = new Color( 1, 0.9f, 0.7f ) * 8;
		spotLight.Radius = 700;
		spotLight.ConeOuter = 45;
		spotLight.Shadows = true;

		sprite = Sprite.FromTexture( Texture.Load( "textures/particles/base_sprite.vtex" ) );
		var normal = Texture.Create( 1, 1 ).WithData( new byte[] { 255, 128, 128, 255 } ).WithName( "scenelab_matrix_normal" ).Finish();

		for ( int i = 0; i < 6; i++ )
		{
			var around = new Vector3( MathF.Cos( i * 1.05f ) * 60, MathF.Sin( i * 1.05f ) * 60, 40 + i * 14 );
			Color Tint( float hue ) => new ColorHsv( hue + i * 18, 0.6f, 1 ).ToColor();

			// 0-2: translucent, sorted, opaque
			Cast( $"Plain {i}", Cell( 0 ) + around, 46, Tint( 0 ) );
			Cast( $"Sorted {i}", Cell( 1 ) + around, 46, Tint( 40 ) ).IsSorted = true;
			Cast( $"Opaque {i}", Cell( 2 ) + around, 46, Tint( 80 ) ).Opaque = true;

			// 3-4: additive (it can't cast), lit
			Cast( $"Additive {i}", Cell( 3 ) + around, 46, Tint( 120 ) ).Additive = true;
			Cast( $"Lit {i}", Cell( 4 ) + around, 46, Tint( 160 ) ).Lighting = true;

			// 5: yaw only, flipped
			var yaw = Cast( $"Yaw {i}", Cell( 5 ) + around, 46, Tint( 200 ) );
			yaw.Billboard = i % 3 == 0 ? SpriteRenderer.BillboardMode.YOnly : i % 3 == 1 ? SpriteRenderer.BillboardMode.None : SpriteRenderer.BillboardMode.Particle;
			yaw.FlipHorizontal = i % 2 == 0;

			// 6: faded by distance to the camera, feathered into the floor
			var faded = Cast( $"Faded {i}", Cell( 6 ) + around, 60, Tint( 240 ) );
			faded.CameraFadeNear = 300;
			faded.CameraFadeFar = 900;
			faded.DepthFeather = 20;

			// 7: refractive
			var refractive = Cast( $"Refractive {i}", Cell( 7 ) + around, 46, Tint( 280 ) );
			refractive.Refraction = true;
			refractive.NormalTexture = normal;
		}

		// 8-10: particle groups
		Particles( "Particles plain", Cell( 8 ), 6, 50, Vector3.Zero );

		var blurred = Particles( "Particles blurred", Cell( 9 ), 6, 50, new Vector3( 14, 6, 0 ) );
		blurred.SortMode = ParticleSpriteRenderer.ParticleSortMode.ByDistance;
		blurred.MotionBlur = true;
		blurred.FaceVelocity = true;

		var lit = Particles( "Particles lit", Cell( 10 ), 6, 90, Vector3.Zero );
		lit.Opaque = true;
		lit.Lighting = true;

		// 11: text, as particles that cast and as an ordinary renderer that doesn't
		var textFx = new GameObject( true, "Particles text" );
		textFx.WorldPosition = Cell( 11 );
		var textEffect = textFx.Components.Create<ParticleEffect>();
		textEffect.Lifetime = 3600;
		textEffect.StartVelocity = 0;
		var textRenderer = textFx.Components.Create<ParticleTextRenderer>();
		textRenderer.Text = new TextRendering.Scope( "Cast", Color.White, 60, "Poppins", 700 );
		textRenderer.Shadows = true;
		for ( int i = 0; i < 3; i++ )
			textEffect.Emit( Cell( 11 ) + new Vector3( (i - 1) * 60, 0, 70 + i * 20 ), 0 ).Size = 60;

		var plainText = new GameObject( true, "Text" );
		plainText.WorldPosition = Cell( 11 ) + new Vector3( 0, 60, 40 );
		plainText.Components.Create<TextRenderer>().TextScope = new TextRendering.Scope( "Text", Color.White, 48, "Poppins", 700 );

		frame = 0;
		return scene;
	}

	public static void Animate( Scene scene )
	{
		frame++;
		using var _ = scene.Push();

		// Component sprites that move every frame: their batch's bounds and its upload change with them
		for ( int i = 0; i < 6; i++ )
		{
			var go = scene.Directory.FindByName( $"Plain {i}" ).FirstOrDefault();
			if ( go is null ) continue;
			var angle = frame * 2.5f + i * 60;
			go.WorldPosition = Cell( 0 ) + new Vector3( MathF.Cos( angle * MathF.PI / 180 ) * 90, MathF.Sin( angle * MathF.PI / 180 ) * 90, 50 + i * 12 );
		}

		// A cluster grows
		if ( frame == 60 )
		{
			for ( int i = 0; i < 6; i++ )
				Cast( $"Late {i}", Cell( 1 ) + new Vector3( (i - 2.5f) * 28, 90, 60 + i * 10 ), 46, new ColorHsv( 30 + i * 12, 0.7f, 1 ).ToColor() ).IsSorted = true;
		}

		// Another is destroyed, so a group of the batch goes
		if ( frame == 100 )
		{
			for ( int i = 0; i < 6; i++ )
				scene.Directory.FindByName( $"Opaque {i}" ).FirstOrDefault()?.Destroy();
		}

		// One stops casting: the batch it was in is left for another
		if ( frame == 140 )
		{
			for ( int i = 0; i < 6; i++ )
				scene.Directory.FindByName( $"Lit {i}" ).FirstOrDefault()?.Components.Get<SpriteRenderer>().Shadows = false;
		}

		// One is switched off
		if ( frame == 180 )
		{
			for ( int i = 0; i < 6; i++ )
				scene.Directory.FindByName( $"Yaw {i}" ).FirstOrDefault()?.Enabled = false;
		}
	}

	public static Scene BuildStress()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( 0, -1500, 1100 ), new Vector3( 0, 0, 20 ) );
		using var _ = scene.Push();
		Lights( scene, true );
		scene.Directory.FindByName( "Floor" ).First().WorldScale = new Vector3( 16, 16, 1 );

		for ( int i = 0; i < 8; i++ )
		{
			var angle = i * 45f * MathF.PI / 180;
			Point( $"Light {i}", new Vector3( MathF.Cos( angle ) * 600, MathF.Sin( angle ) * 600, 200 ), 700, i * 45 );
		}

		sprite = Sprite.FromTexture( Texture.Load( "textures/particles/base_sprite.vtex" ) );

		var random = new Random( 7 );
		for ( int i = 0; i < 3000; i++ )
		{
			var renderer = Cast( $"Stress {i}", new Vector3( random.Float( -900, 900 ), random.Float( -900, 900 ), random.Float( 15, 160 ) ), random.Float( 20, 46 ), new ColorHsv( i % 360, 0.5f, 1 ).ToColor() );

			// Two batches
			renderer.IsSorted = i % 2 == 0;
		}

		frame = 0;
		return scene;
	}

	/// <summary>
	/// Every sprite of the stress scene moves a little every frame, so its batches re-upload and recompute their bounds as
	/// a game's would.
	/// </summary>
	public static void AnimateStress( Scene scene )
	{
		frame++;
		using var _ = scene.Push();

		foreach ( var renderer in scene.GetAllComponents<SpriteRenderer>() )
			renderer.WorldPosition += new Vector3( MathF.Sin( frame * 0.1f + renderer.WorldPosition.y * 0.01f ), MathF.Cos( frame * 0.1f + renderer.WorldPosition.x * 0.01f ), 0 ) * 0.8f;
	}
}
