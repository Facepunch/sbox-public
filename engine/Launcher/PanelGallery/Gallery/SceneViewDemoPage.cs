namespace Sandbox.PanelGallery;

/// <summary>
/// A miniature animated orrery, rendered in an owned ScenePanel.
/// </summary>
public sealed class SceneViewDemoPage : GalleryPage
{
	readonly GameObject camera;
	readonly List<GameObject> planets = [];
	readonly GameObject moon;
	readonly List<GameObject> atlasRing = [];
	readonly List<GameObject> probePanels = [];
	readonly CameraComponent lens;
	readonly ScenePanel view;
	readonly List<Sandbox.UI.Button> labels = [];
	readonly Sandbox.UI.Label selection;
	int selected = -1;
	float time;
	float distance = 650;
	bool orbitCamera = true;
	bool running = true;

	public SceneViewDemoPage() : base( "Scene View", "A kinetic orbital sculpture in a real 3D scene. A solar probe and ringed planet sweep through inclined orbits above a lit display plinth. An emissive sun, bloom and volumetric fog light the scene. Orbit the camera and zoom in to inspect the construction." )
	{
		var controls = Add.Panel( "demo-actions" );
		controls.AddChild( new Sandbox.UI.Checkbox { LabelText = "Run orbits", Checked = true, ValueChanged = value => running = value } );
		controls.AddChild( new Sandbox.UI.Checkbox { LabelText = "Orbit camera", Checked = true, ValueChanged = value => orbitCamera = value } );
		controls.Add.Label( "Zoom" );
		var zoom = new Sandbox.UI.SliderControl( 420, 900, 1 ) { Value = distance };
		zoom.OnValueChanged = value => distance = value;
		controls.AddChild( zoom );
		view = new ScenePanel();
		view.AddClass( "scene-control-demo orbital-demo" );
		AddChild( view );
		var scene = view.RenderScene;
		var sky = scene.CreateObject().Components.Create<SkyBox2D>();
		sky.SkyMaterial = Material.Load( "materials/skybox/skybox_dark_01.vmat" );
		sky.Tint = Color.White * 0.2f;
		scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White * 0.12f;
		camera = scene.CreateObject();
		lens = camera.Components.Create<CameraComponent>();
		lens.BackgroundColor = "#101820";
		lens.FieldOfView = 60;
		lens.EnablePostProcessing = true;
		var bloom = camera.Components.Create<Bloom>();
		bloom.Strength = 1.3f;
		bloom.Threshold = 0.85f;
		var tonemapping = camera.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;
		tonemapping.ExposureCompensation = 0.75f;
		var vignette = camera.Components.Create<Vignette>();
		vignette.Intensity = 0.25f;
		vignette.Smoothness = 0.8f;
		var fog = scene.CreateObject().Components.Create<VolumetricFogVolume>();
		fog.Bounds = new BBox( new Vector3( -420, -420, -190 ), new Vector3( 420, 420, 270 ) );
		fog.Strength = 0.18f;
		fog.FalloffExponent = 0.6f;
		fog.Color = (Color)"#9cb6cd";
		GameObject Sphere( Vector3 position, float scale, Color tint )
		{
			var obj = scene.CreateObject();
			obj.WorldPosition = position;
			obj.WorldScale = Vector3.One * scale;
			var model = obj.Components.Create<ModelRenderer>();
			model.Model = Model.Load( "models/dev/sphere.vmdl" );
			model.Tint = tint;
			return obj;
		}
		// A physical base and pedestal anchor the sculpture in the scene.
		var plinth = Sphere( new Vector3( 0, 0, -175 ), 1, (Color)"#253140" );
		plinth.WorldScale = new Vector3( 5, 5, 0.18f );
		var pedestal = Sphere( new Vector3( 0, 0, -95 ), 1, (Color)"#4a5665" );
		pedestal.WorldScale = new Vector3( 0.2f, 0.2f, 1.5f );
		var sun = Sphere( Vector3.Zero, 0.7f, Color.White );
		var sunMaterial = Material.Load( "materials/dev/bright_100.vmat" ).CreateCopy();
		sunMaterial.Set( "g_vColorTint", GalleryPalette.Lime );
		sunMaterial.Set( "g_vSelfIllumTint", GalleryPalette.Lime );
		sunMaterial.Set( "g_flSelfIllumScale", 5.0f );
		var sunRenderer = sun.Components.Get<ModelRenderer>();
		sunRenderer.MaterialOverride = sunMaterial;
		// The emissive shell must not block the light emitted from its centre.
		sunRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		var sunlight = sun.Components.Create<PointLight>();
		sunlight.LightColor = GalleryPalette.Lime * 4;
		sunlight.Attenuation = 0;
		sunlight.Radius = 650;
		sunlight.Shadows = true;
		sunlight.FogStrength = 1;
		for ( int j = 0; j < 80; j++ )
		{
			float angle = j * MathF.Tau / 80;
			Sphere( new Vector3( MathF.Cos( angle ) * 224, MathF.Sin( angle ) * 224, -164 ), 0.025f, GalleryPalette.Cyan );
		}
		for ( int i = 0; i < 3; i++ )
		{
			var tint = i == 1 ? GalleryPalette.Pink : GalleryPalette.Cyan;
			planets.Add( Sphere( Vector3.Zero, 0.18f + i * 0.06f, tint ) );
			// Small markers describe each orbital path without obscuring the planets.
			for ( int j = 0; j < 160; j++ )
			{
				float angle = j * MathF.Tau / 160;
				Sphere( OrbitPoint( i, angle ), 0.023f, tint );
			}
		}
		// Solar arrays make the inner orbiter visibly directional as it travels.
		planets[0].Components.Get<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );
		for ( int side = 0; side < 2; side++ )
		{
			var panel = Sphere( Vector3.Zero, 1, GalleryPalette.Cyan );
			panel.Components.Get<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );
			panel.WorldScale = new Vector3( 0.36f, 0.32f, 0.035f );
			probePanels.Add( panel );
		}
		for ( int j = 0; j < 80; j++ ) atlasRing.Add( Sphere( Vector3.Zero, 0.025f, GalleryPalette.Lime ) );
		string[] names = ["Courier probe", "Pulse", "Atlas"];
		selection = Add.Label( "Select a planet in the scene to inspect its orbit.", "scene-selection" );
		for ( int i = 0; i < names.Length; i++ )
		{
			int index = i;
			var label = new Sandbox.UI.Button( names[i], null, "planet-label", () =>
			{
				selected = index;
				selection.Text = $"{names[index]} — orbit radius {75 + index * 48}, period {MathF.Tau / (0.7f - index * 0.18f):0.0}s";
			} );
			view.AddChild( label );
			labels.Add( label );
		}
		moon = Sphere( Vector3.Zero, 0.09f, GalleryPalette.Lime );
		var key = scene.CreateObject();
		key.WorldPosition = new Vector3( -180, -100, 220 );
		var keyLight = key.Components.Create<PointLight>();
		keyLight.Radius = 900;
		keyLight.LightColor = Color.White * 8;
		keyLight.FogStrength = 0.2f;
		keyLight.Shadows = true;
		var light = scene.CreateObject();
		light.WorldRotation = Rotation.From( 55, 25, 0 );
		light.Components.Create<DirectionalLight>().LightColor = Color.White * 2;
		var fill = scene.CreateObject();
		fill.WorldRotation = Rotation.From( -30, 180, 0 );
		fill.Components.Create<DirectionalLight>().LightColor = GalleryPalette.Cyan * 0.5f;
	}

	/// <summary>
	/// Places each orbit in a different inclined plane so its path crosses in front of and behind the sculpture.
	/// </summary>
	static Vector3 OrbitPoint( int orbit, float angle )
	{
		var tilt = orbit switch { 0 => Rotation.From( 20, 0, 15 ), 1 => Rotation.From( -45, 35, 0 ), _ => Rotation.From( 55, -25, 0 ) };
		return tilt * new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * (75 + orbit * 48);
	}

	public override void Tick()
	{
		base.Tick();
		if ( running ) time += RealTime.Delta;
		for ( int i = 0; i < planets.Count; i++ )
		{
			float angle = time * (0.7f - i * 0.18f) + i * 2;
			planets[i].WorldPosition = OrbitPoint( i, angle );
			planets[i].WorldRotation = Rotation.From( 15, angle.RadianToDegree() + 90, 20 );
		}
		moon.WorldPosition = planets[2].WorldPosition + new Vector3( MathF.Cos( time * 2 ) * 28, MathF.Sin( time * 2 ) * 28, MathF.Sin( time * 2 ) * 12 );
		for ( int j = 0; j < atlasRing.Count; j++ )
		{
			float angle = j * MathF.Tau / atlasRing.Count;
			atlasRing[j].WorldPosition = planets[2].WorldPosition + Rotation.From( 25, 0, 30 ) * new Vector3( MathF.Cos( angle ) * 27, MathF.Sin( angle ) * 27, 0 );
		}
		for ( int j = 0; j < probePanels.Count; j++ )
		{
			probePanels[j].WorldPosition = planets[0].WorldPosition + planets[0].WorldRotation * new Vector3( 0, j == 0 ? -18 : 18, 0 );
			probePanels[j].WorldRotation = planets[0].WorldRotation;
		}
		float orbit = orbitCamera ? time * 0.16f : 0;
		camera.WorldPosition = new Vector3( -MathF.Cos( orbit ) * distance, MathF.Sin( orbit ) * distance, distance * 0.32f );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, -35 ) - camera.WorldPosition );
		lens.CustomSize = view.Box.RectInner.Size;
		var labelPositions = new List<Vector2>();
		for ( int i = 0; i < labels.Count; i++ )
		{
			var point = lens.PointToScreenNormal( planets[i].WorldPosition );
			var size = view.Box.RectInner.Size / ScaleToScreen;
			var position = new Vector2( Math.Clamp( point.x * size.x, 60, MathF.Max( 60, size.x - 60 ) ), Math.Clamp( point.y * size.y, 0, MathF.Max( 0, size.y - 60 ) ) );
			foreach ( var previous in labelPositions )
				if ( MathF.Abs( position.x - previous.x ) < 120 && MathF.Abs( position.y - previous.y ) < 30 ) position.y = previous.y + 30;
			labelPositions.Add( position );
			labels[i].Style.Left = position.x;
			labels[i].Style.Top = position.y;
			labels[i].SetClass( "selected", selected == i );
		}
	}
}
