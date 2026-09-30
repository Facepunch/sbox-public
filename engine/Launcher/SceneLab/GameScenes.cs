namespace Sandbox.SceneLab;

/// <summary>
/// The GameObject scenes in the Scene menu: what the managed renderer draws through its GameObject bridge
/// (<c>r_managed_scene</c>) that only exists as components - post processing, UI, particles, decals. Each is built from
/// engine components and core and citizen assets, compared with native the way <c>r_managed_scene_compare</c> does, and
/// where it has a <c>Change</c>, compared again after changing it through its components.
///
/// They share a stage (<see cref="Stage"/>): a camera, the sun with sky light, the default sky and a floor.
/// </summary>
internal static class GameScenes
{
	/// <summary>
	/// A camera looking at the middle of the floor from above and to the side, the sun, the default sky and a floor -
	/// with contact shadows off, so the presets compare what they're about; the Contact Shadows preset has them on.
	/// </summary>
	public static Scene Stage( out CameraComponent camera, Vector3? cameraPosition = null, Vector3? lookAt = null )
	{
		var scene = new Scene();
		using var _ = scene.Push();

		var cameraObject = new GameObject( true, "Camera" );
		cameraObject.WorldPosition = cameraPosition ?? new Vector3( -260, -200, 170 );
		cameraObject.WorldRotation = Rotation.LookAt( (lookAt ?? new Vector3( 0, 0, 30 )) - cameraObject.WorldPosition );
		camera = cameraObject.Components.Create<CameraComponent>();
		camera.FieldOfView = 70;
		camera.FovAxis = CameraComponent.Axis.Horizontal;
		camera.ZNear = 5;
		camera.ZFar = 5000;
		camera.BackgroundColor = new Color( 0.08f, 0.09f, 0.11f );
		camera.IsMainCamera = true;

		var sun = new GameObject( true, "Sun" );
		sun.WorldRotation = Rotation.LookAt( new Vector3( -0.4f, 0.7f, -0.55f ) );
		var sunLight = sun.Components.Create<DirectionalLight>();
		sunLight.LightColor = new Color( 1.0f, 0.95f, 0.85f ) * 1.5f;
		sunLight.SkyColor = new Color( 0.12f, 0.14f, 0.18f );
		sunLight.Shadows = true;
		sunLight.ContactShadows = false;

		new GameObject( true, "Sky" ).Components.Create<SkyBox2D>();

		var floor = new GameObject( true, "Floor" );
		floor.WorldPosition = new Vector3( 0, 0, -1 );
		floor.WorldScale = new Vector3( 8, 8, 1 );
		floor.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/plane.vmdl" );

		return scene;
	}

	/// <summary>
	/// A game resource - clothing, a decal definition - loaded from its compiled file under <paramref name="root"/> the
	/// first time, as <c>ResourceLibrary.LoadGameResource</c> loads them: a panel app doesn't register game resources the
	/// way a game does, and its mounted file system hasn't the addons.
	/// </summary>
	public static T LoadResource<T>( string path, string root ) where T : GameResource
	{
		if ( ResourceLibrary.TryGet<T>( path, out var resource ) ) return resource;

		var file = System.IO.Path.Combine( root, path + "_c" );
		if ( System.IO.File.Exists( file ) && GameResource.GetPromise( typeof( T ), path ) is T promise && promise.TryLoadFromData( System.IO.File.ReadAllBytes( file ) ) )
			return promise;

		Log.Warning( $"Scene Lab: no {typeof( T ).Name} {path}" );
		return null;
	}

	/// <summary>
	/// A model at a place, with a tint and optionally another material.
	/// </summary>
	public static ModelRenderer Prop( string name, string model, Vector3 position, Color? tint = null, string material = null, float scale = 1 )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;
		go.WorldScale = scale;

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( model );
		if ( tint is { } color ) renderer.Tint = color;
		if ( material is not null ) renderer.MaterialOverride = Material.Load( material );
		return renderer;
	}

	/// <summary>
	/// Something for post processing to work on: bright emissive spheres for bloom, saturated colours for grading,
	/// glossy metal, near and far objects for depth of field.
	/// </summary>
	static void PostProps()
	{
		for ( int i = 0; i < 8; i++ )
		{
			var angle = i * MathF.Tau / 8;
			var position = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * 140 + Vector3.Up * 10;
			Prop( $"Prop {i}", i % 2 == 0 ? "models/dev/box.vmdl" : "models/dev/sphere.vmdl", position, new ColorHsv( i * 45, 0.8f, 1 ).ToColor() );
		}

		Prop( "Glow", "models/dev/sphere.vmdl", new Vector3( 0, 0, 40 ), new Color( 4, 3, 2 ), "materials/dev/primary_white_emissive.vmat", 0.6f );
		Prop( "Metal", "models/dev/sphere.vmdl", new Vector3( 60, -40, 20 ), null, "materials/dev/dev_metal_rough10.vmat", 0.5f );
		Prop( "Near", "models/dev/box.vmdl", new Vector3( -180, -120, 60 ), new Color( 0.9f, 0.9f, 1 ), scale: 0.4f );
		Prop( "Far", "models/dev/box.vmdl", new Vector3( 300, 260, 20 ), new Color( 1, 0.6f, 0.3f ) );
	}

	/// <summary>
	/// A camera's post processing stack, on its GameObject: ACES tonemapping at a fixed exposure, bloom, colour
	/// adjustments, chromatic aberration, sharpening and a vignette.
	/// </summary>
	public static Scene PostProcessing()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var go = camera.GameObject;
		var tonemapping = go.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;

		go.Components.Create<Bloom>();
		var color = go.Components.Create<ColorAdjustments>();
		color.Saturation = 1.3f;
		color.Contrast = 1.1f;
		go.Components.Create<ChromaticAberration>();
		go.Components.Create<Sharpen>();
		go.Components.Create<Vignette>();

		return scene;
	}

	/// <summary>
	/// A spot light with a cookie - a texture projected through the light (<c>SpotLight.Cookie</c>) - down onto the floor and a
	/// box, in a dim sun. Native packs the cookie's bindless index into the light (<c>PackGPULight</c>), which the bridge's
	/// lights are packed with.
	/// </summary>
	public static Scene LightCookie()
	{
		var scene = Stage( out var camera, new Vector3( -180, -140, 160 ), new Vector3( 10, 10, 0 ) );
		using var _ = scene.Push();

		scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>().LightColor = new Color( 0.15f, 0.15f, 0.18f );
		OnFloor( Prop( "Box", "models/dev/box.vmdl", new Vector3( 20, 10, 0 ), new Color( 0.8f, 0.8f, 0.8f ), scale: 0.4f ) );

		var go = new GameObject( true, "Cookie Light" );
		go.WorldPosition = new Vector3( 0, 0, 220 );
		go.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
		var spot = go.Components.Create<SpotLight>();
		spot.LightColor = new Color( 1, 0.95f, 0.85f ) * 8;
		spot.Radius = 600;
		spot.ConeOuter = 40;
		spot.ConeInner = 30;
		spot.Cookie = Texture.Load( "textures/lightcookies/lightcookies_01.vtex" );

		return scene;
	}

	/// <summary>
	/// The cookie swapped for another, and the light turned.
	/// </summary>
	public static void ChangeLightCookie( Scene scene )
	{
		using var _ = scene.Push();

		var go = scene.Directory.FindByName( "Cookie Light" ).First();
		go.WorldRotation = Rotation.LookAt( new Vector3( 0.3f, 0.2f, -1 ), Vector3.Forward );
		go.Components.Get<SpotLight>().Cookie = Texture.Load( "textures/lightcookies/lightcookies_02.vtex" );
	}

	/// <summary>
	/// The Highlight post process outlining a box, a sphere half behind a wall (its obscured colour) and a citizen - drawn again
	/// by <c>Graphics.Render( SceneObject )</c> with the outline shader, which native draws through its layer's view and a managed
	/// frame from its mirror, the citizen with this frame's skinning.
	/// </summary>
	public static Scene HighlightOutlines()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();

		camera.GameObject.Components.Create<Highlight>();

		var box = OnFloor( Prop( "Outlined Box", "models/dev/box.vmdl", new Vector3( 40, -70, 0 ), new Color( 0.7f, 0.7f, 0.75f ), scale: 0.5f ) );
		var boxOutline = box.GameObject.Components.Create<HighlightOutline>();
		boxOutline.Color = new Color( 1, 0.6f, 0.1f );
		boxOutline.Width = 0.5f;

		OnFloor( Prop( "Wall", "models/dev/box.vmdl", new Vector3( -60, 60, 0 ), new Color( 0.4f, 0.45f, 0.5f ), scale: 0.6f ) );
		var sphere = OnFloor( Prop( "Outlined Sphere", "models/dev/sphere.vmdl", new Vector3( 20, 90, 0 ), new Color( 0.3f, 0.6f, 1 ), scale: 0.5f ) );
		var sphereOutline = sphere.GameObject.Components.Create<HighlightOutline>();
		sphereOutline.Color = new Color( 0.2f, 1, 0.4f );
		sphereOutline.ObscuredColor = new Color( 0.2f, 1, 0.4f, 0.5f );
		sphereOutline.InsideObscuredColor = new Color( 0.1f, 0.5f, 0.2f, 0.3f );

		var citizen = Citizen( "Outlined Citizen", new Vector3( 0, 0, 0 ) );
		var citizenOutline = citizen.GameObject.Components.Create<HighlightOutline>();
		citizenOutline.Color = new Color( 0.3f, 0.7f, 1 );
		citizenOutline.InsideColor = new Color( 0.3f, 0.7f, 1, 0.15f );
		citizenOutline.Width = 0.4f;

		return scene;
	}

	/// <summary>
	/// Scene objects parented with <c>SceneObject.AddChild</c>: a sphere and a small box above a prop's box, and a sphere on
	/// the child box - each native moves with its parent (<c>CHILD_SCENEOBJECT_INHERIT_TRANSFORM</c>), with nothing on the
	/// managed side setting it. The change moves and turns the prop, which only native carries through to the children.
	/// </summary>
	public static Scene SceneObjectChildren()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();

		var parent = OnFloor( Prop( "Parent", "models/dev/box.vmdl", new Vector3( 0, 0, 0 ), new Color( 0.8f, 0.5f, 0.3f ), scale: 0.5f ) );
		var parentObject = parent.SceneObject;
		var world = parentObject.World;

		var sphere = new SceneModel( world, Model.Load( "models/dev/sphere.vmdl" ), parentObject.Transform.ToWorld( new Transform( new Vector3( 0, 0, 90 ), Rotation.Identity, 0.6f ) ) );
		sphere.ColorTint = new Color( 0.3f, 0.6f, 1 );
		parentObject.AddChild( "sphere", sphere );

		var box = new SceneModel( world, Model.Load( "models/dev/box.vmdl" ), parentObject.Transform.ToWorld( new Transform( new Vector3( 80, 0, 40 ), Rotation.FromYaw( 30 ), 0.5f ) ) );
		box.ColorTint = new Color( 0.3f, 0.9f, 0.4f );
		parentObject.AddChild( "box", box );

		var grandchild = new SceneModel( world, Model.Load( "models/dev/sphere.vmdl" ), box.Transform.ToWorld( new Transform( new Vector3( 0, 0, 70 ), Rotation.Identity, 0.5f ) ) );
		grandchild.ColorTint = new Color( 1, 0.9f, 0.2f );
		box.AddChild( "grandchild", grandchild );

		return scene;
	}

	/// <summary>
	/// The parent moved and turned: its children follow natively.
	/// </summary>
	public static void ChangeSceneObjectChildren( Scene scene )
	{
		using var _ = scene.Push();

		var parent = scene.Directory.FindByName( "Parent" ).First();
		parent.WorldPosition += new Vector3( -60, 80, 0 );
		parent.WorldRotation = Rotation.FromYaw( 70 );
	}

	/// <summary>
	/// A scene's render attributes (<c>Scene.RenderAttributes</c>, as DDGI's volumes and games' shader globals are set): boxes in a
	/// test shader tinted by <c>SceneLabTint</c>, which nothing but the scene sets. The camera merges them into its own attributes,
	/// which native's camera renderer merges into its view.
	/// </summary>
	public static Scene SceneAttributes()
	{
		var scene = Stage( out _ );
		using var __ = scene.Push();

		var material = Material.Create( "scenelab_attribute_test", "shaders/scene_renderer_attribute_test.shader" );
		for ( int i = 0; i < 3; i++ )
		{
			var box = OnFloor( Prop( $"Tinted Box {i}", "models/dev/box.vmdl", new Vector3( 0, (i - 1) * 90, 0 ), scale: 0.5f ) );
			box.MaterialOverride = material;
		}

		scene.RenderAttributes.Set( "SceneLabTint", new Vector3( 1, 0.35f, 0.2f ) );
		return scene;
	}

	/// <summary>
	/// The scene's tint changed.
	/// </summary>
	public static void ChangeSceneAttributes( Scene scene )
	{
		scene.RenderAttributes.Set( "SceneLabTint", new Vector3( 0.2f, 0.9f, 0.4f ) );
	}

	/// <summary>
	/// Ambient occlusion (<c>AmbientOcclusion</c>, GTAO): two walls meeting in a corner over a metal floor, with a box in the
	/// corner, a sphere and a small box. GTAO reads the depth chain and the normals the depth-normals prepass writes, and
	/// the forward pass applies its result through a pipeline texture slot. Its history is the camera's, so compare it
	/// isolated (<c>scenelab.exe -parity -isolated</c>).
	/// </summary>
	public static Scene AmbientOcclusion() => OcclusionStage( ao: true, ssr: false );

	/// <summary>
	/// Screen space reflections (<c>ScreenSpaceReflections</c>): the ambient occlusion scene reflected in its metal floor.
	/// SSR reads the prepass's normals and roughness, last frame's colour and its own history. Compare it isolated.
	/// </summary>
	public static Scene ScreenSpaceReflections() => OcclusionStage( ao: false, ssr: true );

	static Scene OcclusionStage( bool ao, bool ssr )
	{
		var scene = Stage( out var camera, new Vector3( -200, -150, 110 ), new Vector3( 20, 20, 20 ) );
		using var _ = scene.Push();

		scene.Directory.FindByName( "Floor" ).First().Components.Get<ModelRenderer>().MaterialOverride = Material.Load( "materials/dev/dev_metal_rough10.vmat" );

		var wallA = Prop( "Wall A", "models/dev/box.vmdl", new Vector3( 60, -20, 50 ), new Color( 0.8f, 0.8f, 0.8f ) );
		wallA.GameObject.WorldScale = new Vector3( 0.2f, 3, 2 );
		var wallB = Prop( "Wall B", "models/dev/box.vmdl", new Vector3( -20, 60, 50 ), new Color( 0.8f, 0.8f, 0.8f ) );
		wallB.GameObject.WorldScale = new Vector3( 3, 0.2f, 2 );

		OnFloor( Prop( "Corner Box", "models/dev/box.vmdl", new Vector3( 30, 30, 0 ), new Color( 0.9f, 0.5f, 0.3f ), scale: 0.5f ) );
		OnFloor( Prop( "Sphere", "models/dev/sphere.vmdl", new Vector3( -20, 10, 0 ), new Color( 0.3f, 0.6f, 1 ), scale: 0.6f ) );
		OnFloor( Prop( "Small Box", "models/dev/box.vmdl", new Vector3( 10, -40, 0 ), new Color( 0.4f, 0.9f, 0.4f ), scale: 0.3f ) );

		if ( ao ) camera.GameObject.Components.Create<Sandbox.AmbientOcclusion>();
		if ( ssr ) camera.GameObject.Components.Create<Sandbox.ScreenSpaceReflections>();
		return scene;
	}

	/// <summary>
	/// The sphere moved up into the corner.
	/// </summary>
	public static void ChangeOcclusionStage( Scene scene )
	{
		using var _ = scene.Push();
		scene.Directory.FindByName( "Sphere" ).First().WorldPosition = new Vector3( 25, -5, 30 );
	}

	/// <summary>
	/// The box moved and its outline recoloured, and the citizen's outline off.
	/// </summary>
	public static void ChangeHighlightOutlines( Scene scene )
	{
		using var _ = scene.Push();

		var box = scene.Directory.FindByName( "Outlined Box" ).First();
		box.WorldPosition += new Vector3( -30, -20, 0 );
		box.Components.Get<HighlightOutline>().Color = new Color( 1, 0.2f, 0.6f );
		scene.Directory.FindByName( "Outlined Citizen" ).First().Components.Get<HighlightOutline>().Enabled = false;
	}

	/// <summary>
	/// The stack changed: AgX tonemapping, no bloom, the colour adjusted the other way, and pixelation.
	/// </summary>
	public static void ChangePostProcessing( Scene scene )
	{
		using var _ = scene.Push();
		var go = scene.Camera.GameObject;

		go.Components.Get<Tonemapping>().Mode = Tonemapping.TonemappingMode.AgX;
		go.Components.Get<Bloom>().Enabled = false;
		var color = go.Components.Get<ColorAdjustments>();
		color.Saturation = 0.6f;
		color.HueRotate = 40;
		go.Components.Create<Pixelate>();
	}

	/// <summary>
	/// Depth of field and a blur: effects that read the frame's depth, and grab the frame before tonemapping.
	/// </summary>
	public static Scene DepthOfField()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var go = camera.GameObject;
		go.Components.Create<Tonemapping>().AutoExposureEnabled = false;
		var dof = go.Components.Create<Sandbox.DepthOfField>();
		dof.FocalDistance = 300;

		return scene;
	}

	const string UIStyles = """
		.lab { position: absolute; left: 0; top: 0; right: 0; bottom: 0; font-family: Poppins; color: white; }
		.card { position: absolute; left: 40px; top: 40px; width: 420px; padding: 20px; flex-direction: column; gap: 10px;
			background-color: rgba( 10, 14, 24, 0.6 ); border: 2px solid rgba( 255, 255, 255, 0.35 ); border-radius: 16px; }
		.title { font-size: 34px; font-weight: 700; }
		.body { font-size: 18px; color: rgba( 255, 255, 255, 0.8 ); }
		.bar { height: 16px; border-radius: 8px; background: linear-gradient( to right, #ff4060, #40c0ff ); }
		.tint { position: absolute; right: 60px; bottom: 60px; width: 300px; height: 180px; border-radius: 24px;
			background-color: rgba( 255, 120, 20, 0.35 ); border: 4px solid rgba( 255, 255, 255, 0.8 ); }
		.glass { position: absolute; left: 520px; top: 60px; width: 220px; height: 220px; border-radius: 110px;
			background-color: rgba( 60, 255, 140, 0.25 ); }
		.cover { position: absolute; left: 280px; top: 140px; width: 720px; height: 420px; border-radius: 20px;
			background-color: rgba( 10, 14, 24, 0.85 ); }
		""";

	/// <summary>
	/// A screen panel to put UI on - its root, with the lab's styles.
	/// </summary>
	static Sandbox.UI.Panel ScreenUI( string name, ScreenPanel.RenderTiming timing = ScreenPanel.RenderTiming.AfterPostProcess )
	{
		var go = new GameObject( true, name );
		var screen = go.Components.Create<ScreenPanel>();
		screen.Timing = timing;

		var root = screen.GetPanel();
		root.StyleSheet.Add( Sandbox.UI.StyleSheet.FromString( UIStyles, "/scenelab/ui.scss" ) );
		return root.Add.Panel( "lab" );
	}

	/// <summary>
	/// Screen UI over the scene: a translucent card with a border, rounded corners, text and a gradient, a translucent
	/// tinted box and a disc over the 3D objects - where blending in gamma space rather than linear shows - drawn after
	/// post processing, as ScreenPanel's default timing is. And a HUD, drawn through the camera's painter every frame.
	/// </summary>
	public static Scene ScreenUI()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();
		camera.GameObject.Components.Create<Tonemapping>().AutoExposureEnabled = false;

		var ui = ScreenUI( "Screen UI" );
		var card = ui.Add.Panel( "card" );
		card.Add.Label( "Scene Lab", "title" );
		card.Add.Label( "Screen UI through the managed renderer's GameObject bridge, blended in gamma space.", "body" );
		card.Add.Panel( "bar" );
		ui.Add.Panel( "tint" );
		ui.Add.Panel( "glass" );

		return scene;
	}

	/// <summary>
	/// The HUD: rectangles, a rounded bordered one, a line and text, drawn by <c>CameraComponent.BeginHud</c> after post
	/// processing.
	/// </summary>
	public static void DrawHud( Scene scene )
	{
		if ( scene.Camera is not { } camera ) return;

		using var hud = camera.BeginHud();
		hud.Fill = new Color( 0, 0, 0, 0.5f );
		hud.Stroke = new Stroke( Color.White.WithAlpha( 0.6f ), 2 );
		hud.Rect( new Rect( 40, 600, 260, 60 ), new Painter.CornerRadii( 12 ) );
		hud.TextStyle = hud.TextStyle with { FontSize = 28, Color = Color.White };
		hud.Text( "HUD 100", new Rect( 60, 612, 240, 40 ) );
		hud.Stroke = new Stroke( new Color( 1, 0.8f, 0.2f, 0.8f ), 6 );
		hud.Line( new Vector2( 330, 630 ), new Vector2( 600, 600 ) );
	}

	/// <summary>
	/// The card changed and the tint moved: the mirror isn't involved in UI, so this proves the panels' own updates reach
	/// the managed frame.
	/// </summary>
	public static void ChangeScreenUI( Scene scene )
	{
		using var _ = scene.Push();
		var root = scene.Directory.FindByName( "Screen UI" ).First().Components.Get<ScreenPanel>().GetPanel();
		var tint = root.Descendants.First( x => x.HasClass( "tint" ) );
		tint.Style.Left = 60;
		tint.Style.Right = null;
		tint.Style.BackgroundColor = new Color( 0.2f, 0.4f, 1, 0.5f );
		root.Descendants.OfType<Sandbox.UI.Label>().First().Text = "Changed";
	}

	/// <summary>
	/// UI drawn before post processing (<c>ScreenPanel.RenderTiming.BeforePostProcess</c>): into the HDR frame, so the
	/// tonemapping and bloom after it apply to it too. Native blends it in gamma space through a float scratch target
	/// seeded from the frame (<c>CManagedRenderPipeline::OnUIHook</c>).
	/// </summary>
	public static Scene EarlyUI()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var go = camera.GameObject;
		var tonemapping = go.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;
		go.Components.Create<Bloom>();

		var ui = ScreenUI( "Early UI", ScreenPanel.RenderTiming.BeforePostProcess );
		var card = ui.Add.Panel( "card" );
		card.Add.Label( "Before post", "title" );
		card.Add.Label( "Tonemapped and bloomed with the scene.", "body" );
		card.Add.Panel( "bar" );
		ui.Add.Panel( "tint" );

		return scene;
	}

	/// <summary>
	/// A world panel: UI in the scene, drawn by its scene object's own code (<c>ScenePanelObject</c>), sorted with the
	/// translucent objects around it.
	/// </summary>
	public static Scene WorldUI()
	{
		var scene = Stage( out var _camera );
		using var _ = scene.Push();
		PostProps();

		var go = new GameObject( true, "World Panel" );
		go.WorldPosition = new Vector3( -40, -30, 110 );
		go.WorldScale = 3;
		go.WorldRotation = Rotation.FromYaw( 215 );
		var panel = go.Components.Create<WorldPanel>();
		panel.PanelSize = new Vector2( 900, 500 );

		var root = panel.GetPanel();
		root.StyleSheet.Add( Sandbox.UI.StyleSheet.FromString( UIStyles, "/scenelab/ui.scss" ) );
		var card = root.Add.Panel( "card" );
		card.Add.Label( "World Panel", "title" );
		card.Add.Label( "UI in the scene, drawn by its own scene object.", "body" );
		card.Add.Panel( "bar" );

		Prop( "Glass", "models/dev/box.vmdl", new Vector3( 0, 20, 40 ), null, "materials/dev/primary_white_trans.vmat", 0.7f );
		return scene;
	}

	/// <summary>
	/// Custom objects - things that draw themselves (<c>SceneCustomObject</c>): billboard sprites, translucent, additive and
	/// opaque; a burst of particles around a translucent box, so they sort with it back to front as native's layers do;
	/// and a line.
	/// </summary>
	public static Scene SpritesAndParticles()
	{
		var scene = Stage( out var _camera );
		using var _ = scene.Push();

		var texture = Texture.Load( "textures/particles/base_sprite.vtex" );
		var sprite = Sprite.FromTexture( texture );

		for ( int i = 0; i < 3; i++ )
		{
			var go = new GameObject( true, $"Sprite {i}" );
			go.WorldPosition = new Vector3( -60 + i * 70, -90, 50 );
			var renderer = go.Components.Create<SpriteRenderer>();
			renderer.Sprite = sprite;
			renderer.Size = 60;
			renderer.Color = new ColorHsv( i * 120, 0.7f, 1 ).ToColor();
			renderer.Additive = i == 1;
			renderer.Opaque = i == 2;
		}

		Prop( "Glass", "models/dev/box.vmdl", new Vector3( 0, 40, 40 ), new Color( 0.6f, 0.8f, 1 ), "materials/dev/primary_white_trans.vmat", 0.8f );
		Prop( "Pillar", "models/dev/box.vmdl", new Vector3( 90, 80, 30 ), new Color( 1, 0.7f, 0.4f ), scale: 0.6f );

		var fx = new GameObject( true, "Particles" );
		fx.WorldPosition = new Vector3( 0, 40, 60 );
		var effect = fx.Components.Create<ParticleEffect>();
		effect.MaxParticles = 300;
		effect.Lifetime = 30;
		var emitter = fx.Components.Create<ParticleSphereEmitter>();
		emitter.Burst = 200;
		emitter.Rate = 0;
		emitter.Radius = 80;
		emitter.Velocity = 4;
		var particles = fx.Components.Create<ParticleSpriteRenderer>();
		particles.Sprite = sprite;

		var line = new GameObject( true, "Line" ).Components.Create<LineRenderer>();
		line.UseVectorPoints = true;
		line.VectorPoints = [new Vector3( -120, 120, 10 ), new Vector3( -40, 150, 80 ), new Vector3( 60, 140, 20 ), new Vector3( 140, 110, 90 )];
		line.Width = 6;
		line.Color = new Color( 1, 0.3f, 0.6f );

		// Custom objects are drawn into shadow maps, but the line's shadow doesn't show yet - an open issue - so it's off
		// here, as the bridge scene's contact shadows are
		line.CastShadows = false;

		return scene;
	}

	/// <summary>
	/// The debug overlay (<c>DebugOverlaySystem</c>), redrawn every frame around the Anchor as a game draws it: lines and
	/// wire boxes (<c>SceneDynamicObject</c>s, whose vertices only live natively), a sphere, and text (a custom object), each
	/// with depth and without (native's <c>OverlayWithDepth</c> and <c>OverlayWithoutDepth</c> layers), part hidden behind a
	/// box. And a line renderer casting a shadow - a custom object in the sun's shadow views.
	/// </summary>
	public static Scene DebugOverlay()
	{
		var scene = Stage( out var _camera, new Vector3( -220, -170, 150 ), new Vector3( 0, 0, 30 ) );
		using var _ = scene.Push();

		Prop( "Blocker", "models/dev/box.vmdl", new Vector3( -20, 0, 30 ), new Color( 0.7f, 0.7f, 0.75f ), scale: 0.8f );
		new GameObject( true, "Anchor" );

		var line = new GameObject( true, "Shadow Line" ).Components.Create<LineRenderer>();
		line.UseVectorPoints = true;
		line.VectorPoints = [new Vector3( -80, 90, 60 ), new Vector3( 0, 110, 90 ), new Vector3( 80, 80, 70 )];
		line.Width = 8;
		line.Color = new Color( 0.3f, 0.9f, 0.5f );
		line.CastShadows = true;

		return scene;
	}

	/// <summary>
	/// The overlay's frame: what a game draws each tick, from the Anchor.
	/// </summary>
	public static void DrawDebugOverlay( Scene scene )
	{
		var overlay = scene.DebugOverlay;
		var at = scene.Directory.FindByName( "Anchor" ).First().WorldPosition;

		overlay.Line( at + new Vector3( 40, -80, 5 ), at + new Vector3( 40, 80, 70 ), Color.Red );
		overlay.Line( at + new Vector3( 50, -80, 5 ), at + new Vector3( 50, 80, 70 ), Color.Yellow, overlay: true );
		overlay.Box( new BBox( at + new Vector3( -60, -40, 0 ), at + new Vector3( 20, 40, 70 ) ), Color.Cyan );
		overlay.Box( new BBox( at + new Vector3( -70, -50, 0 ), at + new Vector3( 30, 50, 80 ) ), Color.Magenta, overlay: true );
		overlay.Line( [at + new Vector3( -100, -100, 2 ), at + new Vector3( -60, -120, 30 ), at + new Vector3( -20, -100, 2 ), at + new Vector3( 20, -120, 30 )], Color.Orange );
		overlay.Sphere( new Sphere( at + new Vector3( 60, 60, 40 ), 25 ), Color.Green );
		overlay.Text( at + new Vector3( 0, 0, 110 ), "Debug Overlay", 24, color: Color.White );
		overlay.Text( at + new Vector3( -40, 0, 40 ), "behind", 20, color: Color.Yellow );
		overlay.Text( at + new Vector3( -40, 0, 20 ), "in front", 20, color: Color.Cyan, overlay: true );
	}

	/// <summary>
	/// The overlay moved with its Anchor, and the shadow line moved and recoloured.
	/// </summary>
	public static void ChangeDebugOverlay( Scene scene )
	{
		using var _ = scene.Push();

		scene.Directory.FindByName( "Anchor" ).First().WorldPosition = new Vector3( 30, -30, 0 );
		var line = scene.Directory.FindByName( "Shadow Line" ).First();
		line.WorldPosition += new Vector3( 0, -40, 20 );
		line.Components.Get<LineRenderer>().Color = new Color( 0.9f, 0.4f, 0.2f );
	}

	/// <summary>
	/// The sprites recoloured and swapped between blends, the line moved, the particles made additive.
	/// </summary>
	public static void ChangeSpritesAndParticles( Scene scene )
	{
		using var _ = scene.Push();

		for ( int i = 0; i < 3; i++ )
		{
			var renderer = scene.Directory.FindByName( $"Sprite {i}" ).First().Components.Get<SpriteRenderer>();
			renderer.Color = new ColorHsv( 60 + i * 120, 0.9f, 1 ).ToColor();
			renderer.Additive = i == 0;
		}

		scene.Directory.FindByName( "Line" ).First().WorldPosition += new Vector3( 0, -60, 20 );
		scene.Directory.FindByName( "Particles" ).First().Components.Get<ParticleSpriteRenderer>().Additive = true;
	}

	/// <summary>
	/// Soft particles: sprites, particles and a line that cut into the floor and a box, faded by how near the scene's depth
	/// is behind them (<c>DepthFeather</c>). They read that depth from the depth chain (<c>Depth::Get</c>), so without it
	/// their edges come out hard, or they vanish.
	/// </summary>
	public static Scene SoftParticles()
	{
		var scene = Stage( out var _camera );
		using var _ = scene.Push();

		var sprite = Sprite.FromTexture( Texture.Load( "textures/particles/base_sprite.vtex" ) );

		Prop( "Box", "models/dev/box.vmdl", new Vector3( 20, 20, 25 ), new Color( 0.7f, 0.75f, 0.8f ), scale: 0.8f );

		for ( int i = 0; i < 3; i++ )
		{
			var go = new GameObject( true, $"Soft Sprite {i}" );
			go.WorldPosition = new Vector3( -70 + i * 70, -30 + i * 30, 15 );
			var renderer = go.Components.Create<SpriteRenderer>();
			renderer.Sprite = sprite;
			renderer.Size = 110;
			renderer.Color = new ColorHsv( i * 120 + 20, 0.6f, 1 ).ToColor();
			renderer.DepthFeather = 40;
		}

		var fx = new GameObject( true, "Soft Particles" );
		fx.WorldPosition = new Vector3( 20, 20, 30 );
		var effect = fx.Components.Create<ParticleEffect>();
		effect.MaxParticles = 200;
		effect.Lifetime = 30;
		var emitter = fx.Components.Create<ParticleSphereEmitter>();
		emitter.Burst = 120;
		emitter.Rate = 0;
		emitter.Radius = 70;
		emitter.Velocity = 2;
		var particles = fx.Components.Create<ParticleSpriteRenderer>();
		particles.Sprite = sprite;
		particles.DepthFeather = 30;

		var line = new GameObject( true, "Soft Line" ).Components.Create<LineRenderer>();
		line.UseVectorPoints = true;
		line.VectorPoints = [new Vector3( -140, 90, -10 ), new Vector3( -40, 110, 20 ), new Vector3( 60, 100, -5 ), new Vector3( 150, 80, 15 )];
		line.Width = 30;
		line.Color = new Color( 0.4f, 1, 0.6f );
		line.DepthFeather = 20;
		line.CastShadows = false;

		return scene;
	}

	/// <summary>
	/// The game overlay layers, as a viewmodel uses them (<c>RenderOptions.Overlay</c>): a sphere partly behind a wall near
	/// the camera, drawn over it anyway, and a translucent box over everything too. A sphere out of the game layers
	/// (<c>RenderOptions.Game</c> off) isn't drawn, but its shadow is.
	/// </summary>
	public static Scene Viewmodel()
	{
		var scene = Stage( out var _camera );
		using var _ = scene.Push();

		// Along the camera's line of sight (Stage's camera looks from -260, -200, 170 at 0, 0, 30), and across it
		var camera = new Vector3( -260, -200, 170 );
		var forward = (new Vector3( 0, 0, 30 ) - camera).Normal;
		var across = new Vector3( -forward.y, forward.x, 0 ).Normal;

		Prop( "Wall", "models/dev/box.vmdl", camera + forward * 70 + across * 12, new Color( 0.5f, 0.55f, 0.6f ), scale: 0.4f );
		Prop( "Viewmodel", "models/dev/sphere.vmdl", camera + forward * 100, new Color( 1, 0.6f, 0.2f ), scale: 0.5f ).RenderOptions.Overlay = true;
		Prop( "Overlay Glass", "models/dev/box.vmdl", camera + forward * 110 - across * 20, new Color( 0.5f, 0.8f, 1 ), "materials/dev/primary_white_trans.vmat", 0.3f ).RenderOptions.Overlay = true;
		Prop( "Not Game", "models/dev/sphere.vmdl", new Vector3( -120, 60, 30 ), new Color( 0.3f, 1, 0.3f ), scale: 0.6f ).RenderOptions.Game = false;
		PostProps();

		return scene;
	}

	/// <summary>
	/// The Viewmodel scene with its depth chain drawn over the frame before post processing, as bands of linear depth
	/// (<c>scene_renderer_depth_chain.shader</c>, through a camera command list both renderers run): what effects reading scene
	/// depth see. Where the viewmodel is inside the wall, native's prepass keeps the viewmodel's depth (the overlay stencil), so
	/// the bands there are the sphere's, not the wall's.
	/// </summary>
	public static Scene ViewmodelDepth()
	{
		var scene = Viewmodel();
		using var _ = scene.Push();

		var commands = new Rendering.CommandList( "Scene Lab depth chain" );
		commands.Blit( Material.FromShader( "shaders/scene_renderer_depth_chain.shader" ) );
		scene.Camera.AddCommandList( commands, Rendering.Stage.BeforePostProcess );

		return scene;
	}

	/// <summary>
	/// The overlay flags changed: the sphere back in the world only, the hidden one drawn.
	/// </summary>
	public static void ChangeViewmodel( Scene scene )
	{
		using var _ = scene.Push();

		scene.Directory.FindByName( "Viewmodel" ).First().Components.Get<ModelRenderer>().RenderOptions.Overlay = false;
		scene.Directory.FindByName( "Not Game" ).First().Components.Get<ModelRenderer>().RenderOptions.Game = true;
	}

	/// <summary>
	/// Bloom objects (<c>RenderOptions.Bloom</c>), which native draws into a quarter resolution target the camera's bloom
	/// adds unthresholded: a sphere out of the game layers that only glows, half behind a box that hides its glow there, a box
	/// drawn both ways, and a translucent box that only glows. Under ACES tonemapping and bloom.
	/// </summary>
	public static Scene BloomObjects()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		Prop( "Hider", "models/dev/box.vmdl", new Vector3( -60, -40, 20 ), new Color( 0.4f, 0.45f, 0.5f ), scale: 0.5f );

		var glow = Prop( "Glow Only", "models/dev/sphere.vmdl", new Vector3( -30, -10, 30 ), new Color( 1, 0.4f, 0.1f ), scale: 0.6f ).RenderOptions;
		glow.Game = false;
		glow.Bloom = true;

		Prop( "Both", "models/dev/box.vmdl", new Vector3( 80, 60, 20 ), new Color( 0.2f, 0.6f, 1 ), scale: 0.4f ).RenderOptions.Bloom = true;

		var glass = Prop( "Glow Glass", "models/dev/box.vmdl", new Vector3( 40, -110, 30 ), new Color( 0.3f, 1, 0.4f ), "materials/dev/primary_white_trans.vmat", 0.4f ).RenderOptions;
		glass.Game = false;
		glass.Bloom = true;

		var go = camera.GameObject;
		var tonemapping = go.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;
		go.Components.Create<Bloom>();

		return scene;
	}

	/// <summary>
	/// The bloom flags changed: the sphere drawn in the world as well, the box no longer glowing, a prop glowing instead.
	/// </summary>
	public static void ChangeBloomObjects( Scene scene )
	{
		using var _ = scene.Push();

		scene.Directory.FindByName( "Glow Only" ).First().Components.Get<ModelRenderer>().RenderOptions.Game = true;
		scene.Directory.FindByName( "Both" ).First().Components.Get<ModelRenderer>().RenderOptions.Bloom = false;
		scene.Directory.FindByName( "Prop 3" ).First().Components.Get<ModelRenderer>().RenderOptions.Bloom = true;
	}

	/// <summary>
	/// The layers that draw into the frame's output after post processing: an opaque sphere and a translucent box drawn over
	/// the screen UI (<c>RenderOptions.AfterUI</c>) - the sphere nowhere else, the box in the world as well - and two objects
	/// matched to the overlay layers before the UI (<c>SceneObject.RenderLayer</c>, as debug overlays are): a box depth tested
	/// against the frame, half behind a prop, and a sphere drawn over the wall in front of it. A dark panel covers the middle of
	/// the screen, so what's drawn after the UI shows over it and what isn't doesn't.
	/// </summary>
	public static Scene OutputOverlays()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;

		var after = Prop( "After UI", "models/dev/sphere.vmdl", new Vector3( -40, 30, 40 ), new Color( 1, 0.5f, 0.2f ), scale: 0.5f ).RenderOptions;
		after.Game = false;
		after.AfterUI = true;
		Prop( "After UI Glass", "models/dev/box.vmdl", new Vector3( 30, -20, 50 ), new Color( 0.4f, 0.8f, 1 ), "materials/dev/primary_white_trans.vmat", 0.4f ).RenderOptions.AfterUI = true;

		Prop( "Wall", "models/dev/box.vmdl", new Vector3( -60, -46, 55 ), new Color( 0.5f, 0.55f, 0.6f ), scale: 0.5f );
		SetRenderLayer( Prop( "Overlay Without Depth", "models/dev/sphere.vmdl", new Vector3( 60, -70, 40 ), new Color( 0.3f, 1, 0.4f ), scale: 1.1f ), SceneRenderLayer.OverlayWithoutDepth );
		SetRenderLayer( Prop( "Overlay With Depth", "models/dev/box.vmdl", new Vector3( 185, 30, 25 ), new Color( 1, 0.3f, 0.8f ), scale: 1.0f ), SceneRenderLayer.OverlayWithDepth );

		var ui = ScreenUI( "Screen UI" );
		ui.Add.Panel( "cover" );

		return scene;
	}

	/// <summary>
	/// Put a renderer's scene object in one of native's named layers, as debug overlays do.
	/// </summary>
	static void SetRenderLayer( ModelRenderer renderer, SceneRenderLayer layer )
	{
		if ( renderer.SceneObject is { } sceneObject ) sceneObject.RenderLayer = layer;
		else Log.Warning( $"Scene Lab: {renderer.GameObject.Name} has no scene object to put in {layer}" );
	}

	/// <summary>
	/// The output layers changed: the sphere no longer after the UI but in the world, the box back in the world's layers, and
	/// the green sphere depth tested.
	/// </summary>
	public static void ChangeOutputOverlays( Scene scene )
	{
		using var _ = scene.Push();

		var after = scene.Directory.FindByName( "After UI" ).First().Components.Get<ModelRenderer>().RenderOptions;
		after.AfterUI = false;
		after.Game = true;
		SetRenderLayer( scene.Directory.FindByName( "Overlay With Depth" ).First().Components.Get<ModelRenderer>(), SceneRenderLayer.Default );
		SetRenderLayer( scene.Directory.FindByName( "Overlay Without Depth" ).First().Components.Get<ModelRenderer>(), SceneRenderLayer.OverlayWithDepth );
	}

	/// <summary>
	/// Glass that refracts and blurs what's behind it (<c>glass.shader</c>, which wants the frame buffer copy): a sphere in front
	/// of the props, blurring, a box that bends the floor, and a translucent box behind the sphere, in the copy it reads. The
	/// copy comes from native's <c>MakeFrameBufferCopy</c> and its depth aware mask, which reads the refraction stencil.
	/// </summary>
	public static Scene Refraction()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;

		Prop( "Behind Glass", "models/dev/box.vmdl", new Vector3( 40, 30, 30 ), new Color( 1, 0.5f, 0.2f ), "materials/dev/primary_white_trans.vmat", 0.4f );
		// No shadows: glass's dithered shadow doesn't match native's yet (AGENTS.md, Open work), and isn't what this checks
		var sphere = Prop( "Glass Sphere", "models/dev/sphere.vmdl", new Vector3( -30, -20, 40 ), null, scale: 1.2f );
		sphere.MaterialOverride = Glass( "scenelab_glass_blur", 1.02f, 0.6f );
		sphere.RenderType = ModelRenderer.ShadowRenderType.Off;
		var box = Prop( "Glass Box", "models/dev/box.vmdl", new Vector3( 60, -70, 20 ), null, scale: 0.8f );
		box.MaterialOverride = Glass( "scenelab_glass_bend", 1.08f, 0 );
		box.RenderType = ModelRenderer.ShadowRenderType.Off;

		return scene;
	}

	/// <summary>
	/// A refractive glass material: the citizen gas mask's glass, which core's shader has no textures to make from, copied with
	/// its refraction and blur changed.
	/// </summary>
	static Material Glass( string name, float refraction, float blur )
	{
		var material = Material.Load( "models/citizen_clothes/hat/gasmask/textures/gasmask_glass.vmat" ).CreateCopy( name );
		material.Set( "g_flRefractionStrength", refraction );
		material.Set( "g_flBlurAmount", blur );
		return material;
	}

	/// <summary>
	/// The sphere no longer glass, and the box moved: what reads the copy, and where, changed.
	/// </summary>
	public static void ChangeRefraction( Scene scene )
	{
		using var _ = scene.Push();

		scene.Directory.FindByName( "Glass Sphere" ).First().Components.Get<ModelRenderer>().MaterialOverride = Material.Load( "materials/dev/primary_white_trans.vmat" );
		scene.Directory.FindByName( "Glass Box" ).First().WorldPosition = new Vector3( 10, -40, 40 );
	}

	/// <summary>
	/// A tools view (<c>SceneCamera.ToolsView</c>, native's <c>SVF_TOOL_VIEW</c>), with props in tools materials, which have
	/// only a <c>ToolsUtil</c> mode, or no <c>Forward</c> one: drawn only in a tools view's ToolsUtil layers - a black box
	/// (<c>tools_generic</c>), a solid sphere (<c>tools_solid</c>) and a faint translucent ghost box.
	/// </summary>
	public static Scene ToolsView()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		camera.SceneCamera.ToolsView = true;
		Prop( "Tools Black", "models/dev/box.vmdl", new Vector3( -40, 30, 25 ), null, "materials/tools/toolsblack.vmat", 0.5f );
		Prop( "Tools Solid", "models/dev/sphere.vmdl", new Vector3( 30, -50, 25 ), null, "materials/dev/debug_solid.vmat", 0.5f );
		Prop( "Tools Ghost", "models/dev/box.vmdl", new Vector3( 10, 20, 40 ), null, "materials/dev/debug_ghost_translucent.vmat", 0.8f );

		return scene;
	}

	/// <summary>
	/// No longer a tools view: the tools materials draw nowhere.
	/// </summary>
	public static void ChangeToolsView( Scene scene )
	{
		using var _ = scene.Push();
		scene.Camera.SceneCamera.ToolsView = false;
	}

	/// <summary>
	/// The camera's debug visualisation (<c>CameraComponent.DebugMode</c>, the shaders' <c>ToolsVisMode</c>): albedo, then world
	/// space normals.
	/// </summary>
	public static Scene DebugViews()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		camera.DebugMode = SceneCameraDebugMode.Albedo;
		return scene;
	}

	/// <summary>
	/// World space normals instead.
	/// </summary>
	public static void ChangeDebugViews( Scene scene )
	{
		using var _ = scene.Push();
		scene.Camera.DebugMode = SceneCameraDebugMode.NormalMap;
	}

	/// <summary>
	/// Volumetric fog: a fog volume over the middle of the floor, lit by the sun and a shadowed spot light through it. The camera
	/// fogs its view when its scene has a volume (<c>CameraComponent</c>). Its camera renders natively every frame alongside
	/// (<c>SceneLabScene.NativeAlongside</c>), since each frame's fog is blended with the frames before it.
	/// </summary>
	public static Scene VolumetricFog()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var volume = new GameObject( true, "Fog Volume" );
		volume.WorldPosition = new Vector3( 0, 0, 60 );
		var fog = volume.Components.Create<VolumetricFogVolume>();
		fog.Bounds = BBox.FromPositionAndSize( 0, new Vector3( 360, 360, 160 ) );
		fog.Strength = 1.0f;
		fog.FalloffExponent = 0.5f;

		var spot = new GameObject( true, "Fog Spot" );
		spot.WorldPosition = new Vector3( 40, 60, 200 );
		spot.WorldRotation = Rotation.LookAt( new Vector3( -20, -20, 0 ) - spot.WorldPosition );
		var spotLight = spot.Components.Create<SpotLight>();
		spotLight.LightColor = new Color( 1.0f, 0.7f, 0.4f ) * 6;
		spotLight.Radius = 400;
		spotLight.ConeOuter = 30;
		spotLight.ConeInner = 20;
		spotLight.Shadows = true;

		return scene;
	}

	/// <summary>
	/// The volume moved, tinted and thinned, through its component.
	/// </summary>
	public static void ChangeVolumetricFog( Scene scene )
	{
		using var _ = scene.Push();

		var volume = scene.Directory.FindByName( "Fog Volume" ).First();
		volume.WorldPosition = new Vector3( 60, -40, 60 );
		var fog = volume.Components.Get<VolumetricFogVolume>();
		fog.Color = new Color( 0.5f, 0.8f, 1.0f );
		fog.Strength = 0.6f;
	}

	/// <summary>
	/// Contact shadows: the sun's short screen space shadows from the depth chain, over its cascades. Two of the props cast no
	/// shadow maps, so theirs are all the shadow they cast - as a static prop's is under a baked sun, whose cascades leave it
	/// out - and one casts both.
	/// </summary>
	public static Scene ContactShadows()
	{
		var scene = Stage( out var camera, new Vector3( -150, -110, 80 ), new Vector3( 0, 0, 10 ) );
		using var _ = scene.Push();

		scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>().ContactShadows = true;

		OnFloor( Prop( "Contact Box", "models/dev/box.vmdl", new Vector3( -10, -20, 0 ), new Color( 0.9f, 0.85f, 0.8f ), scale: 0.5f ) ).RenderType = ModelRenderer.ShadowRenderType.Off;
		OnFloor( Prop( "Contact Sphere", "models/dev/sphere.vmdl", new Vector3( 30, 30, 0 ), new Color( 0.5f, 0.7f, 1 ), scale: 0.6f ) ).RenderType = ModelRenderer.ShadowRenderType.Off;
		OnFloor( Prop( "Shadowed Box", "models/dev/box.vmdl", new Vector3( -50, 40, 0 ), new Color( 1, 0.6f, 0.4f ), scale: 0.4f ) );

		return scene;
	}

	/// <summary>
	/// A prop moved down onto the floor, by its model's bounds.
	/// </summary>
	internal static ModelRenderer OnFloor( ModelRenderer renderer )
	{
		var go = renderer.GameObject;
		go.WorldPosition = go.WorldPosition.WithZ( -renderer.Model.Bounds.Mins.z * go.WorldScale.z );
		return renderer;
	}

	/// <summary>
	/// The box moved, and the sun's contact shadows switched off - through the component, so the change has to reach the
	/// managed renderer's mirror.
	/// </summary>
	public static void ChangeContactShadows( Scene scene )
	{
		using var _ = scene.Push();

		var box = scene.Directory.FindByName( "Contact Box" ).First();
		box.WorldPosition = box.WorldPosition.WithX( 40 ).WithY( -40 );
		scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>().ContactShadows = false;
	}

	/// <summary>
	/// Model deformers (<c>ModelDeformer</c>): deformation volumes applied as a skinned model is skinned, by compute into the
	/// vertex cache. Three citizens in one pose share a mesh - one with its head inflated, one squashed through the middle,
	/// and one left alone - so deformed and plain instances of the same mesh have to be skinned apart. A deformed model's
	/// bounds grow too.
	/// </summary>
	public static Scene ModelDeformers()
	{
		var scene = Stage( out var camera, new Vector3( -170, -60, 90 ), new Vector3( 0, 0, 40 ) );
		using var _ = scene.Push();

		var inflated = Citizen( "Inflated Citizen", new Vector3( 0, -45, 0 ) );
		Deformer( inflated, "Head Deformer", new Vector3( 0, 0, 64 ), 14, ModelDeformer.OperationType.Inflate ).Inflation = 4;

		var squashed = Citizen( "Squashed Citizen", new Vector3( 0, 0, 0 ) );
		Deformer( squashed, "Body Deformer", new Vector3( 0, 0, 38 ), 22, ModelDeformer.OperationType.SquashStretch ).StretchRatio = 0.6f;

		Citizen( "Plain Citizen", new Vector3( 0, 45, 0 ) );
		return scene;
	}

	/// <summary>
	/// The inflated head moved down to the chest, and the squash turned into a twist and lift.
	/// </summary>
	public static void ChangeModelDeformers( Scene scene )
	{
		using var _ = scene.Push();

		scene.Directory.FindByName( "Head Deformer" ).First().LocalPosition = new Vector3( 0, 0, 48 );

		var body = scene.Directory.FindByName( "Body Deformer" ).First().Components.Get<ModelDeformer>();
		body.Operation = ModelDeformer.OperationType.Transform;
		body.RotationOffset = new Angles( 0, 35, 0 );
		body.Translation = new Vector3( 0, 0, 6 );
	}

	/// <summary>
	/// Model deformers on bone-merged clothing: three citizens in one pose, dressed alike. The first two have a head and a
	/// belly deformer each, inflating and deflating on their own loops (<see cref="AnimateBoneMergedDeformers"/>). The
	/// first's have <c>ApplyToBoneMergedChildren</c> on, so its hoodie and cap swell with it. The second's have it off, so
	/// its body swells through clothes left as they were. The third has a deformer on its cap alone. Each item of clothing is
	/// its own skinned object, posed by the body's bones but deformed by its own volumes, which come from the body only when
	/// the deformer asks. The three hoodies share a mesh, deformed and plain.
	/// </summary>
	public static Scene BoneMergedDeformers()
	{
		var scene = Stage( out var camera, new Vector3( -170, -60, 90 ), new Vector3( 0, 0, 40 ) );
		using var _ = scene.Push();

		var merged = DressedCitizen( "Merged Citizen", new Vector3( 0, -45, 0 ) );
		Deformer( merged, "Merged Head", HeadCenter, 16, ModelDeformer.OperationType.Inflate ).ApplyToBoneMergedChildren = true;
		Deformer( merged, "Merged Belly", BellyCenter, 18, ModelDeformer.OperationType.Inflate ).ApplyToBoneMergedChildren = true;

		var bodyOnly = DressedCitizen( "Body Only Citizen", new Vector3( 0, 0, 0 ) );
		Deformer( bodyOnly, "Body Only Head", HeadCenter, 16, ModelDeformer.OperationType.Inflate );
		Deformer( bodyOnly, "Body Only Belly", BellyCenter, 18, ModelDeformer.OperationType.Inflate );

		var capped = DressedCitizen( "Capped Citizen", new Vector3( 0, 45, 0 ) );
		Deformer( Cap( capped ), "Cap Deformer", new Vector3( 0, 0, 70 ), 10, ModelDeformer.OperationType.Inflate ).Inflation = 3;

		deformerFrame = 0;
		AnimateBoneMergedDeformers( scene );
		return scene;
	}

	static readonly Vector3 HeadCenter = new( 0, 0, 66 );
	static readonly Vector3 BellyCenter = new( 0, 0, 38 );
	static int deformerFrame;

	/// <summary>
	/// A frame of the head and belly loops. They're on different periods, so each inflates and deflates on its own. The
	/// loops count frames, not time, so every run captures the same pose.
	/// </summary>
	public static void AnimateBoneMergedDeformers( Scene scene )
	{
		var t = deformerFrame++ / 60.0f;
		var head = 0.75f + 1.5f * MathF.Sin( t * MathF.Tau / 3.0f );
		var belly = 1.5f + 2.5f * MathF.Sin( t * MathF.Tau / 2.0f + 1.5f );

		foreach ( var name in new[] { "Merged", "Body Only" } )
		{
			FindDeformer( scene, $"{name} Head" ).Inflation = head;
			FindDeformer( scene, $"{name} Belly" ).Inflation = belly;
		}
	}

	/// <summary>
	/// The first two swap: the merged citizen's clothes go back while its body keeps swelling, and the other's clothes
	/// take the inflation. The cap's inflation turns into a squash that its body still doesn't get.
	/// </summary>
	public static void ChangeBoneMergedDeformers( Scene scene )
	{
		using var _ = scene.Push();

		foreach ( var part in new[] { "Head", "Belly" } )
		{
			FindDeformer( scene, $"Merged {part}" ).ApplyToBoneMergedChildren = false;
			FindDeformer( scene, $"Body Only {part}" ).ApplyToBoneMergedChildren = true;
		}

		var cap = FindDeformer( scene, "Cap Deformer" );
		cap.Operation = ModelDeformer.OperationType.SquashStretch;
		cap.StretchRatio = 0.5f;
	}

	static ModelDeformer FindDeformer( Scene scene, string name ) => scene.Directory.FindByName( name ).First().Components.Get<ModelDeformer>();

	/// <summary>
	/// A citizen frozen in a pose and dressed in the bridge scene's outfit, clothing bone merged onto it.
	/// </summary>
	internal static SkinnedModelRenderer DressedCitizen( string name, Vector3 position )
	{
		var body = Citizen( name, position );
		BridgeScene.Dress( BridgeScene.Outfit( BridgeScene.Outfits ), body );
		return body;
	}

	/// <summary>
	/// The cap a dressed citizen wears: its bone-merged clothing object for the baseball cap.
	/// </summary>
	static SkinnedModelRenderer Cap( SkinnedModelRenderer body )
	{
		return body.GameObject.Children.Select( x => x.Components.Get<SkinnedModelRenderer>() ).First( x => x is not null && x.Model.Name.Contains( "baseball_cap", StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Morphs (flexes): three citizens' faces up close, one plain and two with different sets of the model's morphs at full
	/// weight. Native runs a morphing object's flex rules as it draws it and composites its morphs into a shared atlas that
	/// compute skinning reads; the managed renderer queues them through native the same way before it skins.
	/// </summary>
	public static Scene Morphs()
	{
		// Sunk through the floor so their faces are at the height SceneLab's camera orbits
		var scene = Stage( out var camera, new Vector3( -55, 0, 31 ), new Vector3( 0, 0, 30 ) );
		using var _ = scene.Push();

		Citizen( "Plain Face", new Vector3( 0, -22, -33 ) );
		SetMorphs( Citizen( "Morphed Face", new Vector3( 0, 0, -33 ) ), 0, 1 );
		SetMorphs( Citizen( "Other Face", new Vector3( 0, 22, -33 ) ), 1, 1 );
		return scene;
	}

	/// <summary>
	/// The morphed faces swap their sets, and the plain one takes half of the first.
	/// </summary>
	public static void ChangeMorphs( Scene scene )
	{
		using var _ = scene.Push();

		SetMorphs( scene.Directory.FindByName( "Morphed Face" ).First().Components.Get<SkinnedModelRenderer>(), 1, 1 );
		SetMorphs( scene.Directory.FindByName( "Other Face" ).First().Components.Get<SkinnedModelRenderer>(), 0, 1 );
		SetMorphs( scene.Directory.FindByName( "Plain Face" ).First().Components.Get<SkinnedModelRenderer>(), 0, 0.5f );
	}

	/// <summary>
	/// Every other one of a renderer's model's morphs, from <paramref name="first"/>, at <paramref name="weight"/> - the rest
	/// cleared - with no fade.
	/// </summary>
	static void SetMorphs( SkinnedModelRenderer renderer, int first, float weight )
	{
		var names = renderer.Model.Morphs.Names;
		for ( int i = 0; i < names.Length; i++ )
			renderer.Morphs.Set( names[i], i % 2 == first ? weight : 0, 0 );
	}

	/// <summary>
	/// A citizen frozen in a pose, facing the camera.
	/// </summary>
	internal static SkinnedModelRenderer Citizen( string name, Vector3 position )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;
		go.WorldRotation = Rotation.FromYaw( 180 );

		var renderer = go.Components.Create<SkinnedModelRenderer>();
		renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );
		renderer.UseAnimGraph = false;
		renderer.Sequence.Name = "AvatarMenu_Entry_Jump";
		renderer.Sequence.TimeNormalized = 0.4f;
		renderer.PlaybackRate = 0;
		return renderer;
	}

	/// <summary>
	/// A sphere deformer on a model, <paramref name="position"/> from its origin.
	/// </summary>
	static ModelDeformer Deformer( ModelRenderer target, string name, Vector3 position, float radius, ModelDeformer.OperationType operation )
	{
		var go = new GameObject( target.GameObject, true, name );
		go.LocalPosition = position;

		var deformer = go.Components.Create<ModelDeformer>();
		deformer.SceneVolume = new Sandbox.Volumes.SceneVolume { Type = Sandbox.Volumes.SceneVolume.VolumeTypes.Sphere, Sphere = new Sphere( 0, radius ) };
		deformer.Operation = operation;
		deformer.Falloff = 0.4f;
		return deformer;
	}

	/// <summary>
	/// Decal geometry: meshes flagged the way a map's decal meshes are (<c>SCENEOBJECTFLAG_IS_DECAL</c>, neither opaque nor
	/// translucent), which native draws only in its decal layer, with no depth test. Flatgrass has none, so the flags are set
	/// here on props: a tinted translucent plane on the floor, and a box half behind a wall, drawn over it.
	/// </summary>
	public static Scene DecalGeometry()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var plane = Prop( "Decal Plane", "models/dev/plane.vmdl", new Vector3( -40, -60, 0.5f ), new Color( 1, 0.3f, 0.1f, 0.8f ), "materials/dev/primary_white_trans.vmat", 1.2f );
		MakeDecal( plane );

		Prop( "Decal Wall", "models/dev/box.vmdl", new Vector3( 20, -20, 30 ), new Color( 0.5f, 0.55f, 0.6f ), scale: 0.6f );
		MakeDecal( Prop( "Decal Box", "models/dev/box.vmdl", new Vector3( 60, 10, 30 ), new Color( 0.2f, 0.9f, 0.4f ), scale: 0.5f ) );

		return scene;
	}

	/// <summary>
	/// Flag a renderer's scene object as a map's decal meshes are flagged (<c>CWorldNode</c>, <c>OBJECT_TYPE_DECAL</c>): a decal,
	/// neither opaque nor translucent, casting no shadow.
	/// </summary>
	static void MakeDecal( ModelRenderer renderer )
	{
		if ( renderer.SceneObject is not { } sceneObject )
		{
			Log.Warning( $"Scene Lab: {renderer.GameObject.Name} has no scene object to flag as a decal" );
			return;
		}

		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.IsDecal, true );
		sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.IsOpaque, false );
		sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.IsTranslucent, false );
	}

	/// <summary>
	/// The plane moved and recoloured, the box no longer a decal.
	/// </summary>
	public static void ChangeDecalGeometry( Scene scene )
	{
		using var _ = scene.Push();

		var plane = scene.Directory.FindByName( "Decal Plane" ).First();
		plane.WorldPosition = new Vector3( 40, 60, 0.5f );
		plane.Components.Get<ModelRenderer>().Tint = new Color( 0.2f, 0.4f, 1, 0.8f );

		var box = scene.Directory.FindByName( "Decal Box" ).First().Components.Get<ModelRenderer>().SceneObject;
		box.Flags.SetFlag( Rendering.SceneObjectFlags.IsDecal, false );
		box.Flags.SetFlag( Rendering.SceneObjectFlags.IsOpaque, true );
	}

	/// <summary>
	/// A decal of a texture, projected along its forward axis, <paramref name="size"/> times a definition's 16 units.
	/// </summary>
	static Decal AddDecal( string name, string texture, Vector3 position, Rotation rotation, Vector2 size, Color? tint = null, float depth = 16 )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;
		go.WorldRotation = rotation;

		// A definition made here from one of core's textures: core's own decal definitions use textures generated from SVGs,
		// which a source checkout doesn't have
		var decal = go.Components.Create<Decal>();
		decal.Decals = [new DecalDefinition { ColorTexture = Texture.Load( texture ) }];
		decal.Size = size;
		decal.Rotation = 0;
		decal.Depth = depth;
		if ( tint is { } color ) decal.ColorTint = color;
		return decal;
	}

	/// <summary>
	/// Decals: shapes on the floor, overlapping, one wrapping the edge of a box, one tinted and a checkerboard only
	/// partly mixed with the surface - packed by the light binner and applied by the forward shaders, as native does.
	/// </summary>
	public static Scene Decals()
	{
		var scene = Stage( out var _camera );
		using var _ = scene.Push();

		Prop( "Box", "models/dev/box.vmdl", new Vector3( 40, 40, 25 ), new Color( 0.8f, 0.8f, 0.85f ), scale: 0.5f );
		Prop( "Sphere", "models/dev/sphere.vmdl", new Vector3( -80, 60, 25 ), new Color( 0.7f, 0.9f, 0.7f ), scale: 0.5f );

		var down = Rotation.LookAt( Vector3.Down );
		AddDecal( "Logo", "textures/shapes/heart1.vtex", new Vector3( -40, -40, 0 ), down, 5 );
		AddDecal( "Circle", "textures/shapes/circle1.vtex", new Vector3( 0, -60, 0 ), down, 4, new Color( 0.3f, 0.7f, 1 ) );
		AddDecal( "Splatter 1", "textures/shapes/arrow1.vtex", new Vector3( -100, -10, 0 ), down, 5 );
		AddDecal( "Splatter 2", "textures/dev/checkerboard.vtex", new Vector3( 60, -40, 0 ), down, 6 ).ColorMix = 0.5f;

		// Across the box's top edge and down its side
		AddDecal( "Wrap", "textures/shapes/circle1.vtex", new Vector3( 40, 27.5f, 37.5f ), Rotation.LookAt( new Vector3( 0, 1, -1 ) ), 1.5f, new Color( 0.9f, 0.2f, 0.1f ), depth: 40 );

		return scene;
	}

	/// <summary>
	/// A decal moved, one retinted, one switched off, and a new one added.
	/// </summary>
	public static void ChangeDecals( Scene scene )
	{
		using var _ = scene.Push();

		scene.Directory.FindByName( "Logo" ).First().WorldPosition += new Vector3( 30, 20, 0 );
		scene.Directory.FindByName( "Circle" ).First().Components.Get<Decal>().ColorTint = new Color( 1, 0.3f, 0.2f );
		scene.Directory.FindByName( "Splatter 1" ).First().Enabled = false;
		AddDecal( "Added", "textures/shapes/bean1.vtex", new Vector3( 100, 60, 0 ), Rotation.LookAt( Vector3.Down ), 4 );
	}

	/// <summary>
	/// Auto exposure: a dim scene with one bright light, which the camera adapts to over its first frames. Both
	/// renderers read the same exposure in a compare, after native's frame update.
	/// </summary>
	public static Scene AutoExposure()
	{
		var scene = Stage( out var camera );
		using var _ = scene.Push();
		PostProps();

		var sun = scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>();
		sun.LightColor *= 0.2f;

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.AutoExposureEnabled = true;
		tonemapping.Rate = 10;

		return scene;
	}
}
