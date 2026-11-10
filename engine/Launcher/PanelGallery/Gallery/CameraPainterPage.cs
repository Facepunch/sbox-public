namespace Sandbox.PanelGallery;

/// <summary>
/// Camera drawing recorded by a component during update and pre-render.
/// </summary>
public sealed class CameraPainterPage : GalleryPage
{
	public static readonly GalleryPageInfo[] Pages =
	[
		new( "HUD & Overlay", "videocam", () => new CameraPainterPage(), "Scene" ),
	];

	public CameraPainterPage() : base( "HUD & Overlay",
		"OnUpdate paints the circles; OnPreRender projects a moving object's box and nametags. Cyan is HUD; pink is overlay." )
	{
		var controls = Case( "Camera painters" );
		var view = AddChild<CameraPainterView>();
		var scene = view.RenderScene;
		var cameraObject = scene.CreateObject();
		cameraObject.WorldPosition = new Vector3( -180, 100, 80 );
		cameraObject.WorldRotation = Rotation.LookAt( -cameraObject.WorldPosition );
		var camera = cameraObject.Components.Create<CameraComponent>();
		camera.BackgroundColor = "#101b2b";
		camera.FieldOfView = 55;
		view.Lens = camera;

		var model = scene.CreateObject();
		model.Name = "Moving cube";
		var renderer = model.Components.Create<ModelRenderer>();
		renderer.Model = Model.Cube;
		renderer.Tint = GalleryPalette.Lime;
		var light = scene.CreateObject();
		light.WorldRotation = Rotation.From( 45, 30, 0 );
		light.Components.Create<DirectionalLight>().LightColor = Color.White * 2;

		var demo = cameraObject.Components.Create<CameraPainterDemo>();
		view.Demo = demo;
		demo.Camera = camera;
		demo.Model = model;
		demo.Renderer = renderer;
		Toggle( controls, "HUD", value => demo.ShowHud = value );
		Toggle( controls, "Overlay", value => demo.ShowOverlay = value );
		Toggle( controls, "OnUpdate", value => demo.ShowUpdate = value );
		Toggle( controls, "OnPreRender", value => demo.ShowPreRender = value );
		Toggle( controls, "Animate", value => demo.Animate = value );
		Toggle( controls, "UI overlap strip", value => demo.ShowUi = value );
		Add.Label( "The boxes and nametags should follow the moving cube without lag. The pink box is expanded by 6px so both are visible. The UI strip covers cyan drawing, while pink draws above it. Toggle each callback independently: both should append to the same frame, disappear when disabled, and remain visible when animation is paused.", "page-blurb" );
		UseSceneLayout( view, camera );
	}

	static void Toggle( Panel parent, string label, Action<bool> changed ) =>
		parent.AddChild( new Sandbox.UI.Checkbox { LabelText = label, Checked = true, ValueChanged = changed } );

	sealed class CameraPainterView : ScenePanel
	{
		public CameraComponent Lens { get; set; }
		public CameraPainterDemo Demo { get; set; }

		public override void Tick()
		{
			// Set bounds before ScenePanel ticks the components, including on resize.
			if ( Lens.IsValid() ) Lens.CustomSize = Box.RectInner.Size;
			if ( Demo.IsValid() ) Demo.GalleryUiSystem = UISystem;
			base.Tick();
		}
	}
}

// The gallery assembly doesn't run the game's component code rewriter, which normally adds these interfaces.
public sealed class CameraPainterDemo : Component, Sandbox.Internal.IUpdateSubscriber, Sandbox.Internal.IPreRenderSubscriber
{
	internal UISystem GalleryUiSystem { get; set; }
	public CameraComponent Camera { get; set; }
	public GameObject Model { get; set; }
	public ModelRenderer Renderer { get; set; }
	public bool ShowHud { get; set; } = true;
	public bool ShowOverlay { get; set; } = true;
	public bool ShowUpdate { get; set; } = true;
	public bool ShowPreRender { get; set; } = true;
	public bool Animate { get; set; } = true;
	public bool ShowUi { get; set; } = true;
	ScreenPanel screen;
	Sandbox.UI.Label strip;
	float elapsed;

	protected override void OnStart()
	{
		// Standalone gallery windows own their UI systems; there is no default game UI.
		var context = Sandbox.Engine.GlobalContext.Current;
		var previous = context.UISystem;
		try
		{
			context.UISystem = GalleryUiSystem;
			screen = Components.Create<ScreenPanel>();
		}
		finally
		{
			context.UISystem = previous;
		}
		screen.TargetCamera = Camera;
		screen.AutoScreenScale = false;
		strip = screen.GetPanel().Add.Label( "SCREEN UI — HUD BELOW / OVERLAY ABOVE" );
		strip.SetProperty( "style", "position: absolute; left: 0; background-color: #26394f; color: white; font-family: Roboto; font-size: 16px; text-align: center; align-items: center; justify-content: center;" );
	}

	protected override void OnUpdate()
	{
		if ( !Camera.IsValid() ) return;
		if ( Animate ) elapsed += Time.Delta;
		if ( Model.IsValid() )
		{
			Model.WorldPosition = new Vector3( 0, MathF.Sin( elapsed * 0.8f ) * 35, MathF.Sin( elapsed * 1.1f ) * 12 );
			Model.WorldRotation = Rotation.From( 15, elapsed * 30, 15 );
		}
		if ( screen.IsValid() ) screen.Enabled = ShowUi;
		// This root belongs to the gallery window; place its test strip in camera pixels.
		if ( strip.IsValid() )
		{
			strip.Style.Top = Camera.ScreenRect.Height * 0.45f;
			strip.Style.Width = Camera.ScreenRect.Width;
			strip.Style.Height = Camera.ScreenRect.Height * 0.1f;
		}

		if ( !ShowUpdate ) return;

		if ( ShowHud )
		{
			using var painter = Camera.BeginHud();
			Draw( painter, GalleryPalette.Cyan, false );
		}

		if ( ShowOverlay )
		{
			using var painter = Camera.BeginOverlay();
			Draw( painter, GalleryPalette.Pink, true );
		}
	}

	protected override void OnPreRender()
	{
		if ( !ShowPreRender || !Camera.IsValid() || !Model.IsValid() || !Renderer.IsValid() ) return;

		// Project here, after the scene has settled its transforms and camera for rendering.
		var origin = Camera.PointToScreenPixels( Model.WorldPosition, out var behind );
		if ( behind ) return;
		var min = new Vector2( float.MaxValue, float.MaxValue );
		var max = new Vector2( float.MinValue, float.MinValue );
		foreach ( var corner in Renderer.LocalBounds.Corners )
		{
			var point = Camera.PointToScreenPixels( Renderer.WorldTransform.PointToWorld( corner ), out behind );
			// Skip boxes crossing the camera plane instead of drawing an unbounded rectangle.
			if ( behind ) return;
			min = Vector2.Min( min, point );
			max = Vector2.Max( max, point );
		}
		var bounds = Rect.FromPoints( min, max );
		if ( max.x < 0 || max.y < 0 || min.x > Camera.ScreenRect.Width || min.y > Camera.ScreenRect.Height ) return;

		if ( ShowHud )
		{
			using var painter = Camera.BeginHud();
			DrawTracking( painter, bounds, origin, GalleryPalette.Cyan, false );
		}
		if ( ShowOverlay )
		{
			using var painter = Camera.BeginOverlay();
			DrawTracking( painter, bounds, origin, GalleryPalette.Pink, true );
		}
	}

	void DrawTracking( Painter painter, Rect rect, Vector2 origin, Color color, bool overlay )
	{
		painter.Clip( painter.Bounds );
		if ( overlay ) rect = new Rect( rect.Position - new Vector2( 6, 6 ), rect.Size + new Vector2( 12, 12 ) );
		painter.Fill = Fill.None;
		painter.Stroke = Stroke.Solid( color, 2 );
		painter.Rect( rect );
		painter.Circle( origin, overlay ? 8 : 4 );

		var label = new Rect( origin.x - 120, overlay ? rect.Bottom + 6 : rect.Top - 50, 240, 44 );
		painter.Fill = Color.Black.WithAlpha( 0.75f );
		painter.Rect( label, 4 );
		painter.Stroke = Stroke.None;
		painter.TextStyle = new TextStyle { FontSize = 15, Color = color, Alignment = TextFlag.Center };
		painter.Text( Model.Name, new Rect( label.Left, label.Top + 2, label.Width, 22 ) );
		painter.TextStyle = painter.TextStyle with { FontSize = 11 };
		painter.Text( overlay ? "Overlay / OnPreRender()" : "HUD / OnPreRender()", new Rect( label.Left, label.Top + 23, label.Width, 17 ) );
	}

	void Draw( Painter painter, Color color, bool overlay )
	{
		var bounds = painter.Bounds;
		if ( bounds.Width <= 0 || bounds.Height <= 0 ) return;
		float x = bounds.Width * (overlay ? 0.7f : 0.3f);
		float y = bounds.Height * (0.5f + MathF.Sin( elapsed * 1.4f ) * 0.18f);
		float radius = MathF.Min( bounds.Width * 0.09f, bounds.Height * 0.13f );
		painter.Fill = Fill.LinearGradient( color.WithAlpha( 0.8f ), color.WithAlpha( 0.15f ), elapsed * 25 );
		painter.Stroke = Stroke.Solid( color, 3 );
		painter.Circle( new Vector2( x, y ), radius );
		painter.Fill = Fill.None;
		painter.Arc( new Vector2( x, y ), radius + 10, elapsed * 80, 250 );
		painter.Line( new Vector2( x - 14, y ), new Vector2( x + 14, y ) );
		painter.Line( new Vector2( x, y - 14 ), new Vector2( x, y + 14 ) );

		// Opposite corners expose stale bounds or accidental use of the full window size.
		var marker = overlay ? new Rect( bounds.Width - 32, bounds.Height - 32, 20, 20 ) : new Rect( 12, 12, 20, 20 );
		painter.Rect( marker, 4 );
		painter.TextStyle = new TextStyle { FontSize = 18, Color = color, Alignment = TextFlag.Center, FontName = "Roboto Mono" };
		painter.Text( overlay ? "Overlay / OnUpdate()" : "HUD / OnUpdate()", new Rect( x - 150, bounds.Height - 38, 300, 30 ) );
	}
}
