namespace Sandbox.SceneLab;

/// <summary>
/// Textured transmission showcases and rendering regressions.
/// </summary>
internal static class LightModulatorScenes
{
	/// <summary>
	/// A red transmission filter with receivers above and below it, lit by dynamic or indexed baked sunlight.
	/// </summary>
	public static Scene Sun( int bakeIndex )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		if ( bakeIndex >= 0 )
		{
			var probe = new GameObject( true, "Sun probe" ).Components.Create<LightProbeVolume>();
			probe.Bounds = BBox.FromPositionAndSize( 0, 2000 );
			probe.Texture = Texture.CreateVolume( 1, 1, 6 ).WithData( new byte[24] ).Finish();
			probe.IndicesTexture = Texture.CreateVolume( 1, 1, 1 ).WithData( new byte[] { 0, 255, 255, 255 } ).Finish();
			probe.ScalarsTexture = Texture.CreateVolume( 1, 1, 1 ).WithData( new byte[] { 255, 0, 0, 0 } ).Finish();
		}
		foreach ( float height in new[] { 64.0f, -64.0f } )
		{
			var receiver = new GameObject( true, $"Sun receiver {height}" ).Components.Create<ModelRenderer>();
			receiver.WorldPosition = new Vector3( 0, -height, height );
			receiver.Model = Model.Load( "models/dev/box.vmdl" );
			receiver.MaterialOverride = Material.Load( "materials/dev/primary_white.vmat" );
		}
		var sun = new GameObject( true, "Sun" ).Components.Create<DirectionalLight>();
		sun.WorldRotation = Rotation.LookAt( Vector3.Down );
		sun.LightColor = Color.White;
		sun.Shadows = true;
		sun.ContactShadows = false;
		var camera = new GameObject( true, "Camera" ).Components.Create<CameraComponent>();
		camera.FieldOfView = 60;
		camera.IsMainCamera = true;
		camera.ZNear = 1;
		camera.BackgroundColor = Color.Black;
		var pane = new GameObject( true, "Sun modulator" ).Components.Create<LightModulator>();
		pane.WorldRotation = Rotation.LookAt( Vector3.Down );
		pane.Texture = Texture.White;
		pane.Tint = Color.Red;
		pane.Infinite = true;
		pane.TwoSided = true;
		pane.DistanceFeather = 0;
		sun.BakeLightIndex = bakeIndex;
		camera.WorldPosition = new Vector3( -230, 0, 140 );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 30 ) - camera.WorldPosition );
		return scene;
	}

	/// <summary>
	/// Both renderers share shaders, so check actual above/below-plane pixels as well as parity.
	/// </summary>
	public static string ValidateSun( Scene scene )
	{
		using var scope = scene.Push();
		var sun = scene.GetAllComponents<DirectionalLight>().Single();
		var camera = scene.Camera;
		var pane = scene.GetAllComponents<LightModulator>().Single();
		int bakeIndex = sun.BakeLightIndex;
		camera.FieldOfView = 45;
		foreach ( var receiver in scene.GetAllComponents<ModelRenderer>().OrderByDescending( x => x.WorldPosition.z ) )
		{
			float height = receiver.WorldPosition.z;
			camera.WorldPosition = receiver.WorldPosition + new Vector3( -110, 0, 70 );
			camera.WorldRotation = Rotation.LookAt( receiver.WorldPosition - camera.WorldPosition );
			pane.Enabled = false;
			float unfiltered = CaptureGreen();
			pane.Enabled = true;
			float filtered = CaptureGreen();
			Log.Info( $"Sun modulation: bake index {bakeIndex}, height {height}, green {unfiltered:F4} -> {filtered:F4}" );
			if ( unfiltered <= 0.02f || (height > 0 ? MathF.Abs( unfiltered - filtered ) > 0.01f : filtered >= unfiltered * 0.2f) )
				return $"Sun modulation failed at bake index {bakeIndex}, height {height}: green {unfiltered} -> {filtered}.";
		}
		return null;

		float CaptureGreen()
		{
			using var bitmap = new Bitmap( 96, 96 );
			for ( int frame = 0; frame < 10; ++frame )
			{
				scene.GameTick( 0.0f );
				camera.RenderToBitmap( bitmap );
			}
			return bitmap.GetPixels().Average( p => p.g );
		}
	}

	/// <summary>
	/// Build a dark gallery with an overhead textured light and a low camera looking through its shafts.
	/// </summary>
	public static Scene Gallery()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( -560, -480, 230 ) );
		using var scope = scene.Push();
		camera.BackgroundColor = new Color( 0.003f, 0.009f, 0.018f );
		camera.FieldOfView = 65;
		camera.ZFar = 1800;
		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;

		scene.Directory.FindByName( "Sky" ).First().Enabled = false;
		scene.Directory.FindByName( "Floor" ).First().Enabled = false;
		var sun = scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>();
		sun.LightColor = new Color( 0.3f, 0.5f, 0.7f );
		sun.SkyColor = new Color( 0.08f, 0.12f, 0.18f );
		sun.FogStrength = 0;

		var stone = new Color( 0.45f, 0.53f, 0.56f );
		Block( "Gallery floor", new Vector3( 0, 0, -12 ), new Vector3( 1000, 900, 24 ), stone );
		Block( "Far wall", new Vector3( 350, 0, 350 ), new Vector3( 24, 2000, 700 ), new Color( 0.315f, 0.371f, 0.392f ) );

		Block( "Rear wall", new Vector3( 0, 340, 350 ), new Vector3( 2000, 24, 700 ), stone );

		for ( int i = 0; i < 4; i++ )
		{
			var position = new Vector3( -120 + i * 115, 155, 0 );
			Block( $"Plinth {i}", position.WithZ( 12 ), new Vector3( 70, 70, 24 ), stone );
			Block( $"Column {i}", position.WithZ( 110 ), new Vector3( 38, 38, 180 ), stone );
			Block( $"Capital {i}", position.WithZ( 208 ), new Vector3( 62, 62, 16 ), stone );
		}

		Block( "Lintel", new Vector3( 50, 155, 230 ), new Vector3( 440, 64, 28 ), stone );
		Block( "Dais", new Vector3( 10, -40, 10 ), new Vector3( 170, 150, 20 ), stone );
		Block( "Upper dais", new Vector3( 10, -40, 25 ), new Vector3( 135, 115, 10 ), stone );
		GameScenes.OnFloor( GameScenes.Prop( "Stone sphere", "models/dev/sphere.vmdl", new Vector3( 10, -40, 0 ), stone, scale: 1.1f ) ).GameObject.WorldPosition += Vector3.Up * 30;

		var fogObject = new GameObject( true, "Gallery fog" );
		fogObject.WorldPosition = new Vector3( 0, 0, 180 );
		var fog = fogObject.Components.Create<VolumetricFogVolume>();
		fog.Bounds = BBox.FromPositionAndSize( 0, new Vector3( 1100, 1000, 520 ) );
		fog.Strength = 0.65f;
		fog.FalloffExponent = 0;
		fog.Color = new Color( 0.55f, 0.8f, 0.95f );

		var lampObject = new GameObject( true, "Gallery light" );
		lampObject.WorldPosition = new Vector3( 140, 60, 450 );
		lampObject.WorldRotation = Rotation.LookAt( new Vector3( -110, -70, 0 ) - lampObject.WorldPosition );
		var lamp = lampObject.Components.Create<SpotLight>();
		lamp.LightColor = new Color( 0.45f, 0.85f, 1 ) * 45;
		lamp.Radius = 1100;
		lamp.ConeInner = 32;
		lamp.ConeOuter = 43;
		lamp.Shadows = true;

		// Emphasize the scattered light so the projected pattern is visible in the volume too.
		lamp.FogStrength = 3;

		var pane = new GameObject( true, "Gallery modulator" );
		pane.WorldPosition = new Vector3( 50, 15, 290 );
		pane.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
		var modulator = pane.Components.Create<LightModulator>();
		modulator.Size = new Vector2( 700, 700 );
		modulator.Texture = Caustics;
		modulator.Tiling = new Vector2( 3, 3 );
		modulator.Contrast = 1;
		modulator.Brightness = 3;
		modulator.DistanceFeather = 35;
		modulator.EdgeFeather = 0.05f;

		return scene;
	}

	/// <summary>
	/// Change the projected pattern and light colour while keeping the gallery and fog unchanged.
	/// </summary>
	public static void ChangeGallery( Scene scene )
	{
		var modulator = scene.Directory.FindByName( "Gallery modulator" ).First().Components.Get<LightModulator>();
		modulator.TextureRotation = 35;
		modulator.Offset = new Vector2( 0.2f, 0.15f );
		modulator.Tiling = new Vector2( 2.5f, 2.5f );
		scene.Directory.FindByName( "Gallery light" ).First().Components.Get<SpotLight>().LightColor = new Color( 1, 0.65f, 0.28f ) * 45;
	}

	static Texture caustics;

	/// <summary>
	/// Cache a seamless procedural caustic pattern for this showcase.
	/// </summary>
	static Texture Caustics => caustics ??= CreateCaustics();

	/// <summary>
	/// Warped cell boundaries approximate bright caustic folds.
	/// This is an illustrative projected pattern, not a water simulation.
	/// </summary>
	static Texture CreateCaustics()
	{
		const int size = 512;
		const int cells = 5;
		var pixels = new byte[size * size * 4];

		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				float u = x / (float)size;
				float v = y / (float)size;
				float px = u * cells + 0.3f * MathF.Sin( v * MathF.Tau * 2 + MathF.Sin( u * MathF.Tau ) );
				float py = v * cells + 0.3f * MathF.Sin( u * MathF.Tau * 2 + MathF.Sin( v * MathF.Tau ) );
				float first = float.MaxValue;
				float second = float.MaxValue;

				for ( int cy = -1; cy <= cells; cy++ )
				{
					for ( int cx = -1; cx <= cells; cx++ )
					{
						int ix = (cx + cells) % cells;
						int iy = (cy + cells) % cells;
						float sx = cx + 0.5f + 0.3f * MathF.Sin( ix * 17 + iy * 31 );
						float sy = cy + 0.5f + 0.3f * MathF.Cos( ix * 23 + iy * 13 );
						float distance = MathF.Sqrt( (px - sx) * (px - sx) + (py - sy) * (py - sy) );

						if ( distance < first )
						{
							second = first;
							first = distance;
						}
						else if ( distance < second )
						{
							second = distance;
						}
					}
				}

				byte value = (byte)(255 * (0.025f + 0.975f * MathF.Exp( -(second - first) * 12 )));
				int offset = (y * size + x) * 4;
				pixels[offset] = value;
				pixels[offset + 1] = value;
				pixels[offset + 2] = value;
				pixels[offset + 3] = 255;
			}
		}

		return Texture.Create( size, size ).WithData( pixels ).WithName( "scenelab_caustics" ).Finish();
	}

	/// <summary>
	/// Place one stone block using the built-in box's fifty-unit dimensions.
	/// </summary>
	static void Block( string name, Vector3 position, Vector3 size, Color tint )
	{
		var renderer = GameScenes.Prop( name, "models/dev/box.vmdl", position, tint );
		renderer.GameObject.WorldScale = size / 50;
	}
}
