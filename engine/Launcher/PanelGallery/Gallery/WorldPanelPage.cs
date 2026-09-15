namespace Sandbox.PanelGallery;

/// <summary>
/// UI features rendered by real world panels, with opaque geometry behind and in front.
/// </summary>
public sealed class WorldPanelPage : GalleryPage
{
	public static readonly GalleryPageInfo[] Pages =
	[
		new( "Backdrops", "blur_on", () => new WorldPanelPage( 0 ), "WorldPanel" ),
		new( "Layers & Clipping", "layers", () => new WorldPanelPage( 1 ), "WorldPanel" ),
		new( "Text & Depth", "text_fields", () => new WorldPanelPage( 2 ), "WorldPanel" ),
		new( "Painter Shapes", "brush", () => new WorldPanelPage( 3 ), "WorldPanel" ),
	];

	readonly int mode;
	readonly SceneWorld world;
	readonly ScenePanel view;
	readonly CameraComponent camera;
	readonly List<Sandbox.UI.WorldPanel> panels = new();
	readonly List<SceneObject> stripes = new();
	readonly SceneObject foreground;
	readonly SceneObject movingSphere;
	bool initialized;
	bool animate = true;
	bool occlude;
	bool msaa = true;
	float elapsed;
	int cameraMode;
	float distance = 150;

	WorldPanelPage( int mode ) : base( Pages[mode].Title,
		"Glass UI displays in a 3D test room. The camera loops around the stands while objects move behind the glass. Watch parallax, world captures and depth occlusion; pause or use the front view to compare filters." )
	{
		this.mode = mode;
		var controls = Case( "Scene controls — mouse wheel over the scene to zoom" );
		ToggleOption( controls, "Animate", animate, value => animate = value );
		Options( controls, ["Orbit", "Front", "Oblique"], i => cameraMode = i );
		ToggleOption( controls, "Occluder", occlude, value => occlude = value );
		ToggleOption( controls, "MSAA (4×)", msaa, value => msaa = value );

		var sceneView = AddChild<WorldSceneView>();
		sceneView.Zoom = delta => SetDistance( distance * MathF.Exp( delta * 0.08f ) );
		view = sceneView;
		view.SetProperty( "style", "width: 100%; height: 620px; flex-shrink: 0; pointer-events: all;" );
		var scene = view.RenderScene;
		world = scene.SceneWorld;
		camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.BackgroundColor = Color.FromBytes( 5, 8, 18 );
		camera.FieldOfView = 55;
		var light = scene.CreateObject();
		light.WorldRotation = Rotation.From( 25, -55, 0 );
		var sunlight = light.Components.Create<DirectionalLight>();
		sunlight.LightColor = Color.FromBytes( 255, 195, 125 );
		sunlight.SkyColor = Color.FromBytes( 35, 50, 90 ) * 0.15f;
		scene.CreateObject().Components.Create<AmbientLight>().Color = Color.FromBytes( 12, 18, 32 );
		CreateLight( new Vector3( -25, -65, 25 ), Color.FromBytes( 30, 150, 255 ) * 2.2f, 180 );
		CreateLight( new Vector3( 30, 65, 15 ), Color.FromBytes( 255, 45, 100 ) * 1.8f, 150 );

		// A tiled floor, low plinths and freestanding columns provide perspective and parallax.
		for ( int x = 0; x < 9; x++ )
			for ( int y = 0; y < 12; y++ )
			{
				CreateBox( new Vector3( -50 + x * 16, (y - 5.5f) * 16, -38 ), new Vector3( 15.6f, 15.6f, 2 ),
					(x + y) % 2 == 0 ? Color.FromBytes( 55, 65, 80 ) : Color.FromBytes( 90, 105, 120 ) );
			}
		CreateBox( new Vector3( 8, 0, -35 ), new Vector3( 30, 112, 4 ), Color.FromBytes( 28, 35, 48 ) );
		CreateBox( new Vector3( 18, 64, -27 ), new Vector3( 18, 18, 18 ), Color.FromBytes( 245, 140, 45 ), Rotation.From( 0, 25, 0 ) );
		CreateBox( new Vector3( -15, -65, -30 ), new Vector3( 14, 14, 14 ), Color.FromBytes( 40, 155, 200 ), Rotation.From( 0, -20, 0 ) );

		for ( int i = 0; i < 21; i++ )
		{
			float height = 65 + i % 4 * 8;
			stripes.Add( CreateBox( new Vector3( 42 + i % 3 * 6, (i - 10) * 8, height * 0.5f - 37 ), new Vector3( 6, 5, height ),
				i % 3 == 0 ? Color.FromBytes( 255, 70, 40 ) : i % 3 == 1 ? Color.FromBytes( 30, 190, 255 ) : Color.White ) );
		}

		movingSphere = new SceneObject( world, Model.Sphere )
		{
			Transform = new Transform( new Vector3( 22, 0, 0 ), Rotation.Identity, 0.3f ),
			ColorTint = Color.FromBytes( 245, 185, 40 ),
		};

		foreground = new SceneObject( world, Model.Cube )
		{
			Transform = new Transform( new Vector3( -14, 0, -200 ), Rotation.Identity, new Vector3( 0.12f, 0.12f, 1.6f ) ),
			ColorTint = Color.FromBytes( 255, 210, 35 ),
		};

		Add.Label( mode switch
		{
			0 => "Blur softens the moving world but leaves labels sharp; grayscale removes world colour; brightness darkens it. Compare CSS and Painter backdrop filters inside the cyan guides: both blur and desaturate the room while the world outside stays sharp and coloured. Use Front and pause animation to compare, or orbit to explore world-space alignment.",
			1 => "Rounded and rotated clips contain the effect; nested layers retain their placement; overlapping cards sample the world. In the rotated card, compare direct drawing with BeginLayer at full opacity and no layer filter: both orange rectangles fill their cyan guides. Use Front and pause animation to compare, or orbit to explore their shared world plane.",
			3 => "Expect: Painter fills, strokes, curves and clips stay on their world planes as the camera orbits. Transparent shapes reveal the room; Painter backdrop filters capture it. Toggle MSAA and the occluder, and zoom to inspect thin edges and depth.",
			_ => "Text, images, gradients and shadows remain attached to the world plane. The yellow foreground bar occludes UI, including filtered layers. Explore sampling and depth from distant and oblique views.",
		}, "page-blurb" );
	}

	void CreateLight( Vector3 position, Color color, float radius )
	{
		var lightObject = view.RenderScene.CreateObject();
		lightObject.WorldPosition = position;
		var light = lightObject.Components.Create<PointLight>();
		light.LightColor = color;
		light.Radius = radius;
		light.Shadows = true;
	}

	SceneObject CreateBox( Vector3 position, Vector3 size, Color color, Rotation? rotation = null ) => new( world, Model.Cube )
	{
		// The built-in cube is 50 units on each side.
		Transform = new Transform( position, rotation ?? Rotation.Identity, size / 50 ),
		ColorTint = color,
	};

	void BuildPanels()
	{
		// Wait until this page has a parent so these roots join the window's UI system.
		if ( mode == 0 )
		{
			Card( "Clear reference", "", 0 );
			Card( "World blur / sharp text", "backdrop-filter: blur( 8px );", 1 );
			Card( "World grayscale", "backdrop-filter: saturate( 0 );", 2 );
			Card( "World brightness", "backdrop-filter: brightness( 0.3 );", 3 );
			Card( "Frosted + rounded", "backdrop-filter: blur( 12px ); background-color: #ffffff33; border-radius: 32px;", 4 );
			var backdrops = Card( "Blur + saturation: CSS / Painter", "", 5, Rotation.From( 0, 168, 6 ) );
			PainterComparison( backdrops, backdrop: true );
		}
		else if ( mode == 1 )
		{
			Card( "Rounded clip", "backdrop-filter: blur( 10px ); border-radius: 45px; overflow: hidden;", 0 );
			var layers = Card( "Rotated capture / Painter layers", "backdrop-filter: blur( 8px ); transform: rotate( 12deg ); border-radius: 24px;", 1, Rotation.From( 0, 168, 6 ) );
			PainterComparison( layers, backdrop: false );
			var nested = Card( "Nested opacity layer", "opacity: 0.7; border-radius: 24px; overflow: hidden;", 2 );
			Sample( nested, "backdrop-filter: blur( 12px ); width: 100%; height: 100px; border-radius: 24px;", "Nested world blur" );
			Card( "Filter on UI", "filter: blur( 2px ); background-color: #273551dd;", 3 );
			var overlap = Card( "Overlapping grabs", "backdrop-filter: blur( 4px );", 4 );
			Sample( overlap, "position: absolute; left: 65px; top: 65px; width: 160px; height: 110px; backdrop-filter: brightness( 0.3 ); border: 2px solid cyan;", "Second grab" );
			Card( "Blend with world", "mix-blend-mode: difference; background-color: #c07030;", 5 );
		}
		else if ( mode == 2 )
		{
			var text = Card( "Text / thin borders", "background-color: #16213cbb;", 0 );
			Sample( text, "font-size: 15px;", "15px: The quick brown fox 0123456789" );
			Sample( text, "font-size: 28px; font-weight: bold; text-shadow: 2px 2px 3px black;", "Sharp at an angle" );
			Card( "Gradient + shadow", "background-image: linear-gradient( 45deg, #e85c35, #346de0 ); box-shadow: 8px 8px 12px #000a; border-radius: 25px;", 1 );
			var image = Card( "Image sampling", "background-color: #16213cbb;", 2 );
			Sample( image, "width: 180px; height: 110px; background-image: url( /ui/input/controllers/controller_icon_xbox_one.png ); background-size: contain; background-repeat: no-repeat;", null );
			Card( "Depth: unfiltered", "background-color: #274e82;", 3 );
			Card( "Depth: backdrop", "backdrop-filter: blur( 10px );", 4 );
			Card( "Depth: UI layer", "filter: brightness( 1.3 ); background-color: #274e82; opacity: 0.8;", 5 );
		}
		else
		{
			string[] titles = ["Fills + gradients", "Strokes + curves", "Animated arcs", "Clips + transforms", "Transparent overlap", "Painter world grab"];
			for ( int i = 0; i < titles.Length; i++ )
			{
				var card = Card( titles[i], "", i );
				var shapes = new WorldPainterShapes( i, () => elapsed );
				shapes.SetProperty( "style", "width: 100%; flex-grow: 1;" );
				card.AddChild( shapes );
			}
		}
	}

	Panel Card( string title, string css, int index, Rotation? rotation = null )
	{
		var root = new Sandbox.UI.WorldPanel( world, UISystem )
		{
			PanelBounds = new Rect( -300, -210, 600, 420 ),
			Transform = new Transform( new Vector3( index % 3 * 5, (1 - index % 3) * 36, (0.5f - index / 3) * 25 ), rotation ?? Rotation.From( 0, 180 + (index % 3 - 1) * 10, 0 ), 1 ),
		};
		panels.Add( root );
		var frameColor = Color.FromBytes( 100, 125, 150 );
		foreach ( var edge in new[] { -1, 1 } )
		{
			CreateBox( root.Transform.PointToWorld( new Vector3( -1, edge * 15.5f, 0 ) ), new Vector3( 2, 1, 22 ), frameColor, root.Rotation );
			CreateBox( root.Transform.PointToWorld( new Vector3( -1, 0, edge * 11 ) ), new Vector3( 2, 32, 1 ), frameColor, root.Rotation );
		}
		if ( index >= 3 )
		{
			CreateBox( root.Position.WithZ( -29 ), new Vector3( 3, 3, 10 ), frameColor );
			CreateBox( root.Position.WithZ( -33 ), new Vector3( 12, 20, 2 ), frameColor );
		}
		root.SetProperty( "style", "font-family: Roboto; font-size: 18px; color: white; pointer-events: none;" );
		var card = Sample( root, "width: 100%; height: 100%; flex-direction: column; padding: 14px; border: 1px solid white; " + css, null );
		Sample( card, "font-size: 20px; font-weight: bold; background-color: #101a2ddd; padding: 5px;", title );
		return card;
	}

	static void PainterComparison( Panel card, bool backdrop )
	{
		var pair = Sample( card, "width: 100%; flex-grow: 1;", null );
		for ( int i = 0; i < 2; i++ )
		{
			var surface = Sample( pair, "position: relative; width: 50%; height: 100%;", null );
			Sample( surface, "position: absolute; left: 8%; top: 5%; padding: 5px; background-color: #101a2ddd;",
				backdrop ? (i == 0 ? "CSS backdrop" : "Painter backdrop") : (i == 0 ? "Direct rectangle" : "BeginLayer") );

			const string area = "position: absolute; left: 8%; top: 28%; width: 84%; height: 60%;";
			if ( backdrop && i == 0 )
			{
				Sample( surface, area + "backdrop-filter: blur( 8px ) saturate( 0 );", null );
			}
			else
			{
				var sample = new WorldPainterComparison( backdrop, layered: i == 1 );
				sample.SetProperty( "style", area );
				surface.AddChild( sample );
			}

			// Keep the CSS guide independent of the Painter layer or backdrop operation.
			Sample( surface, area + "border: 2px solid cyan;", null );
		}
	}

	sealed class WorldPainterComparison( bool backdrop, bool layered ) : Panel
	{
		public override void OnDraw( Painter painter )
		{
			var rect = painter.Bounds;
			if ( rect.Width <= 0 || rect.Height <= 0 ) return;

			if ( backdrop )
			{
				painter.FilterBackdrop( rect, new Painter.Filter { Blur = 8, Saturation = 0 } );
				return;
			}

			painter.Stroke = Stroke.None;
			painter.Fill = Color.FromBytes( 255, 135, 35 );
			if ( layered )
			{
				using ( painter.BeginLayer( rect ) )
				{
					painter.Rect( rect );
				}
			}
			else
			{
				painter.Rect( rect );
			}
		}
	}

	static Panel Sample( Panel parent, string css, string text )
	{
		Panel panel = text is null ? parent.Add.Panel() : parent.Add.Label( text );
		panel.SetProperty( "style", css );
		return panel;
	}

	static void Options( Panel parent, string[] labels, Action<int> changed )
	{
		var group = parent.Add.Panel( "demo-actions" );
		group.Style.MarginRight = 16;
		for ( int i = 0; i < labels.Length; i++ )
		{
			var index = i;
			var button = new Sandbox.UI.Button( labels[i].ToUpperInvariant() );
			SetSelected( button, i == 0 );
			button.AddEventListener( "onclick", () =>
			{
				foreach ( var sibling in group.Children.OfType<Sandbox.UI.Button>() ) SetSelected( sibling, sibling == button );
				changed( index );
			} );
			group.AddChild( button );
		}
	}

	static void SetSelected( Sandbox.UI.Button button, bool selected )
	{
		button.Active = selected;
		button.SetClass( "primarybutton", selected );
		button.SetClass( "flatbutton", !selected );
	}

	static void ToggleOption( Panel parent, string title, bool value, Action<bool> changed )
	{
		var button = new Sandbox.UI.Button( title.ToUpperInvariant() );
		button.Style.MarginRight = 16;
		SetSelected( button, value );
		button.AddEventListener( "onclick", () =>
		{
			value = !value;
			SetSelected( button, value );
			changed( value );
		} );
		parent.AddChild( button );
	}

	void SetDistance( float value )
	{
		distance = Math.Clamp( value, 80, 260 );
	}

	sealed class WorldSceneView : ScenePanel
	{
		public Action<float> Zoom { get; set; }

		// Consume the wheel here so zooming does not also scroll the gallery page.
		public override void OnMouseWheel( Vector2 value ) => Zoom?.Invoke( value.y );
	}

	public override void Tick()
	{
		base.Tick();
		if ( !initialized )
		{
			BuildPanels();
			initialized = true;
		}

		if ( animate ) elapsed += RealTime.Delta;
		view.RenderScene.RenderAttributes.Set( "msaa", (int)(msaa ? NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_4X : NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_NONE) );
		for ( int i = 0; i < stripes.Count; i++ )
		{
			stripes[i].Position = stripes[i].Position.WithY( (i - 10) * 8 + MathF.Sin( elapsed * 0.7f ) * 3 );
			stripes[i].Rotation = Rotation.From( 0, MathF.Sin( elapsed * 0.4f + i ) * 20, 0 );
		}
		movingSphere.Position = new Vector3( 25, MathF.Sin( elapsed * 0.8f ) * 46, MathF.Cos( elapsed ) * 15 );

		foreground.Position = new Vector3( -14, MathF.Sin( elapsed * 0.7f ) * 38, occlude ? 0 : -200 );
		var yaw = cameraMode switch { 0 => MathF.Sin( elapsed * 0.22f + 0.7f ) * 35, 1 => 0, _ => -30 };
		var elevation = cameraMode == 1 ? 0 : 35 + MathF.Sin( elapsed * 0.22f ) * 12;
		camera.WorldPosition = -Rotation.From( 0, yaw, 0 ).Forward * distance + Vector3.Up * elevation;
		camera.WorldRotation = Rotation.LookAt( new Vector3( 8, 0, -5 ) - camera.WorldPosition );
	}

	public override void OnDeleted()
	{
		foreach ( var panel in panels ) panel.Delete( true );
		panels.Clear();
		view.Delete( true );
		base.OnDeleted();
	}
}
