using Sandbox.Rendering;

namespace Sandbox.SceneLab;

/// <summary>
/// Views a <see cref="ViewSetup"/> sets up beyond the camera: monitors rendering props into textures, each with its own setup
/// (<see cref="SceneLabScene.TargetSetup"/>), shown on screens the main camera looks at. Compared isolated, so each renderer
/// draws its own monitors: View Projections for projections, clip-space bounds and flips, View Oblique for oblique
/// projections, View Overrides for the ambient light, gradient fog, light modulators and render time.
/// </summary>
internal static class ViewSetupScenes
{
	// Where the monitors look, away from the screens round the main camera's focus
	static readonly Vector3 PropsCentre = new( 1200, 0, 0 );

	const string ScreenTag = "screen";
	const int MonitorSize = 256;

	/// <summary>
	/// Five monitors: a camera's own off-centre projection, clip-space bounds, and flips into an 8-bit and a float texture, and
	/// both ways at once.
	/// </summary>
	public static Scene Projections()
	{
		// The fourth renders into a float target, which is flipped top to bottom
		var scene = Screens( 5, out var monitors, floatTarget: 3 );
		using var _ = scene.Push();

		// The camera's own projection, off-centre (SceneCamera.CustomProjectionMatrix)
		var offCentre = monitors[0];
		offCentre.CustomProjectionMatrix = Matrix.CreateProjection( offCentre.ZNear, offCentre.ZFar, offCentre.FieldOfView, 1, new Vector4( -0.7f, -0.3f, 0.9f, 1.0f ) );
		return scene;
	}

	public static ViewSetup ProjectionSetup( CameraComponent monitor ) => Index( monitor ) switch
	{
		1 => new ViewSetup { ClipSpaceBounds = new Vector4( -0.4f, -0.6f, 0.5f, 0.3f ) },
		2 => new ViewSetup { FlipX = true },
		3 => new ViewSetup { FlipY = true },
		4 => new ViewSetup { FlipX = true, FlipY = true, ClipSpaceBounds = new Vector4( -1, -1, 0, 0 ) },
		_ => default,
	};

	/// <summary>
	/// Two monitors with oblique projections, near planes moved onto clip planes through the props, as water's refraction is
	/// clipped by its surface: one the view's (<see cref="ViewSetup.ProjectionMatrix"/>) keeping what's below, one the camera's
	/// own keeping what's above a tilted plane. The sun casts no shadows here: an oblique view's cascades differ by design (native
	/// fits them to the clip plane, which shrinks them to a few units round the camera; Basalt fits a near plane square to the view).
	/// </summary>
	public static Scene Oblique()
	{
		var scene = Screens( 2, out var monitors );
		using var _ = scene.Push();
		scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>().Shadows = false;

		var above = monitors[1];
		above.CustomProjectionMatrix = Oblique( above, new Plane( PropsCentre + Vector3.Up * 20, new Vector3( 0.3f, 0, 1 ).Normal ) );
		return scene;
	}

	public static ViewSetup ObliqueSetup( CameraComponent monitor ) => Index( monitor ) switch
	{
		0 => new ViewSetup { ProjectionMatrix = Oblique( monitor, new Plane( PropsCentre + Vector3.Up * 25, Vector3.Down ) ) },
		_ => default,
	};

	/// <summary>
	/// Four monitors: the ambient light tinted and added to, gradient fog of the view's own, light modulators off, and a fixed
	/// render time with a material scrolling by it.
	/// </summary>
	public static Scene Overrides()
	{
		var scene = Screens( 4, out _ );
		using var scope = scene.Push();

		// A spot over the props, coloured by a light modulator
		var lamp = new GameObject( true, "Modulated Light" );
		lamp.WorldPosition = PropsCentre + new Vector3( 0, 0, 260 );
		lamp.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
		var spot = lamp.Components.Create<SpotLight>();
		spot.LightColor = new Color( 1, 0.95f, 0.85f ) * 6;
		spot.Radius = 700;
		spot.ConeOuter = 45;
		spot.ConeInner = 35;

		var pane = new GameObject( true, "Light modulator" );
		pane.WorldPosition = PropsCentre + new Vector3( 0, 0, 160 );
		pane.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
		var modulator = pane.Components.Create<LightModulator>();
		modulator.Size = new Vector2( 160, 160 );
		modulator.Texture = Texture.Load( "textures/lightcookies/lightcookies_01.vtex" );
		modulator.Tint = new Color( 0.12f, 0.8f, 0.3f );

		// A texture scrolling with shader time (complex's g_vTexCoordScrollSpeed)
		var scrolling = Material.Load( "materials/dev/primary_white.vmat" ).CreateCopy();
		scrolling.Set( "g_tColor", Texture.Load( "materials/dev/color_test_image.vtex" ) );
		scrolling.Set( "g_vTexCoordScrollSpeed", new Vector2( 0.37f, 0.21f ) );
		var panel = GameScenes.Prop( "Scrolling", "models/dev/box.vmdl", PropsCentre + new Vector3( -60, 120, 40 ) );
		panel.MaterialOverride = scrolling;
		return scene;
	}

	public static ViewSetup OverrideSetup( CameraComponent monitor ) => Index( monitor ) switch
	{
		// Each at a time of its own, so the scrolling texture is where it is in both renderers' frames
		0 => new ViewSetup { Time = 1.5f, AmbientLightTint = new Color( 0.3f, 1.6f, 0.5f ), AmbientLightAdd = new Color( 0.25f, 0.05f, 0.4f, 0.3f ) },
		1 => new ViewSetup
		{
			Time = 2.0f,
			GradientFog = new GradientFogSetup
			{
				Enabled = true,
				StartDistance = 100,
				EndDistance = 700,
				StartHeight = 0,
				EndHeight = 160,
				MaximumOpacity = 0.85f,
				Color = new Color( 1.0f, 0.55f, 0.2f ),
				DistanceFalloffExponent = 1,
				VerticalFalloffExponent = 1,
			}
		},
		2 => new ViewSetup { Time = 2.5f, EnableLightModulators = false },
		3 => new ViewSetup { Time = 3.25f },
		_ => default,
	};

	/// <summary>
	/// A grid of screens around the main camera's focus, each showing a monitor's texture, and props lit by the sun and
	/// three point lights far off where the monitors look, each monitor from another side. Monitors don't see the screens.
	/// </summary>
	static Scene Screens( int count, out CameraComponent[] monitors, int floatTarget = -1 )
	{
		var scene = GameScenes.Stage( out _, new Vector3( -330, 0, 120 ), new Vector3( 0, 0, 30 ) );
		using var scope = scene.Push();

		// The floor under the props: the screens hang in the sky
		scene.Directory.FindByName( "Floor" ).First().WorldPosition = PropsCentre + Vector3.Down;

		for ( int i = 0; i < 6; i++ )
		{
			var angle = i * MathF.Tau / 6;
			var position = PropsCentre + new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * 110 + Vector3.Up * 20;
			GameScenes.Prop( $"Prop {i}", i % 2 == 0 ? "models/dev/box.vmdl" : "models/dev/sphere.vmdl", position, new ColorHsv( i * 60, 0.7f, 0.9f ).ToColor(), scale: 0.8f );
		}

		GameScenes.Prop( "Tower", "models/dev/box.vmdl", PropsCentre + Vector3.Up * 40, new Color( 0.85f, 0.85f, 0.9f ) ).GameObject.WorldScale = new Vector3( 0.8f, 0.8f, 3 );

		for ( int i = 0; i < 3; i++ )
		{
			var light = new GameObject( true, $"Point {i}" );
			var angle = i * MathF.Tau / 3 + 0.5f;
			light.WorldPosition = PropsCentre + new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * 170 + Vector3.Up * 60;
			var point = light.Components.Create<PointLight>();
			point.LightColor = new ColorHsv( i * 120 + 30, 0.8f, 1 ).ToColor() * 4;
			point.Radius = 260;
		}

		monitors = new CameraComponent[count];
		var columns = (count + 1) / 2;
		for ( int i = 0; i < count; i++ )
		{
			var angle = -0.9f + i * 1.8f / Math.Max( count - 1, 1 );
			var monitorObject = new GameObject( true, $"Monitor {i}" );
			monitorObject.WorldPosition = PropsCentre + new Vector3( -MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * 420 + Vector3.Up * (140 + i * 15);
			monitorObject.WorldRotation = Rotation.LookAt( PropsCentre + Vector3.Up * 30 - monitorObject.WorldPosition );

			var monitor = monitorObject.Components.Create<CameraComponent>();
			monitor.IsMainCamera = false;
			monitor.FieldOfView = 60;
			monitor.ZNear = 5;
			monitor.ZFar = 3000;
			monitor.BackgroundColor = new Color( 0.1f, 0.12f, 0.16f );
			monitor.RenderExcludeTags.Add( ScreenTag );

			// Each a little bigger than the last: native reuses the last view's sun cascades this frame for one no bigger, near
			// enough, whatever its projection or tags, where Basalt also asks for the same tags
			var format = i == floatTarget ? ImageFormat.RGBA16161616F : ImageFormat.RGBA8888;
			var size = MonitorSize + i * 8;
			monitor.RenderTarget = Texture.CreateRenderTarget().WithSize( size, size ).WithFormat( format ).Create( $"Scene Lab monitor {i}" );
			monitors[i] = monitor;

			// Its screen, facing the main camera
			var material = Material.Load( "materials/dev/primary_white.vmat" ).CreateCopy();
			material.Set( "g_tColor", monitor.RenderTarget );
			var column = i % columns;
			var row = i / columns;
			var screen = GameScenes.Prop( $"Screen {i}", "models/dev/box.vmdl", new Vector3( 0, (column - (columns - 1) * 0.5f) * 110, 85 - row * 110 ) );
			screen.GameObject.WorldScale = new Vector3( 0.05f, 2, 2 );
			screen.GameObject.Tags.Add( ScreenTag );
			screen.MaterialOverride = material;
		}

		return scene;
	}

	/// <summary>
	/// A monitor's number, from its name.
	/// </summary>
	static int Index( CameraComponent monitor ) => int.Parse( monitor.GameObject.Name.AsSpan( "Monitor ".Length ) );

	/// <summary>
	/// The monitor's projection with its near plane moved onto a clip plane, as <c>CommandList.DrawRefraction</c> makes one.
	/// </summary>
	static Matrix Oblique( CameraComponent monitor, Plane clipPlane )
	{
		var projection = Matrix.CreateProjection( monitor.ZNear, monitor.ZFar, monitor.FieldOfView, 1 );
		return Matrix.CreateObliqueProjection( monitor.WorldTransform, clipPlane, projection );
	}
}
