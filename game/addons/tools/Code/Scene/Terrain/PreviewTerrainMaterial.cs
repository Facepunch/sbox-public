using Editor.Assets;

namespace Editor.TerrainEditor;

[AssetPreview( "tmat" )]
class PreviewTerrainMaterial : AssetPreview
{
	public override float PreviewWidgetCycleSpeed => 0.1f;

	public PreviewTerrainMaterial( Asset asset ) : base( asset ) { }

	// The material shown. Loaded through the resource system, so it's the instance the editor is changing and
	// the preview follows unsaved edits.
	TerrainMaterial Material;

	ModelRenderer _renderer;
	EnvmapProbe _envmap;
	bool? _studioEnvmap;

	/// <summary>
	/// Stops the automatic turn; dragging still turns the sphere. Toggled by the button in the live preview's
	/// top-right corner and kept for every material.
	/// </summary>
	static bool Paused
	{
		get => EditorCookie.Get( "terrain.material.preview.paused", false );
		set => EditorCookie.Set( "terrain.material.preview.paused", value );
	}

	internal enum PreviewShape { Sphere, Cube, Plane }

	/// <summary>
	/// What the live preview shows the material on, picked with the buttons in its top-left corner and kept
	/// for every material. Thumbnails always use the sphere, so the palette stays consistent.
	/// </summary>
	static PreviewShape Shape
	{
		get => EditorCookie.Get( "terrain.material.preview.shape", PreviewShape.Sphere );
		set => EditorCookie.Set( "terrain.material.preview.shape", value );
	}

	// Each shape's button draws the shape itself, as a rounded rect: the icon font in the 3D view lacks some
	// Material Icons glyphs and shows a blank box for them instead. A full radius makes the circle, the plane
	// is a flat slab so it can't be mistaken for the cube's square.
	static readonly (PreviewShape Shape, Vector2 Glyph, float Radius)[] Shapes =
	[
		(PreviewShape.Sphere, new Vector2( 16, 16 ), 8),
		(PreviewShape.Cube, new Vector2( 14, 14 ), 2),
		(PreviewShape.Plane, new Vector2( 20, 6 ), 1),
	];

	PreviewShape _shape;

	float _yaw;
	bool _dragging, _wasDown;
	float _lastCursorX;

	public override async Task InitializeAsset()
	{
		using ( EditorUtility.DisableTextureStreaming() )
		{
			if ( !Asset.TryLoadResource<TerrainMaterial>( out Material ) )
				return;

			UpdateLighting();

			using ( Scene.Push() )
			{
				PrimaryObject = new GameObject();

				_renderer = PrimaryObject.AddComponent<ModelRenderer>();
				_renderer.MaterialOverride = Sandbox.Material.FromShader( "shaders/terrain_preview.shader" );
			}

			// Thumbnails and videos have no live widget and always show the sphere
			SetShape( LiveWidget.IsValid() ? Shape : PreviewShape.Sphere );
		}

		await Task.CompletedTask;
	}

	/// <summary>
	/// Puts the material on <paramref name="shape"/> and frames it. The plane is a flat patch seen from above,
	/// closer to how the material reads on a terrain.
	/// </summary>
	void SetShape( PreviewShape shape )
	{
		_shape = shape;
		if ( !_renderer.IsValid() ) return;

		using ( Scene.Push() )
		{
			_renderer.Model = shape switch
			{
				PreviewShape.Cube => Model.Cube,
				PreviewShape.Plane => Model.Plane,
				_ => Model.Sphere,
			};
			ApplyMaterial();

			// Framing below 1 crops into the bounds: a sphere fills them, a turning cube's corners swing past
			// them, and the plane is meant to fill the view
			var bounds = _renderer.Model.Bounds;
			SceneSize = bounds.Size * (shape == PreviewShape.Cube ? 0.9f : 0.6f);
			SceneCenter = bounds.Center;

			if ( Camera.IsValid() )
				Camera.WorldRotation = new Angles( shape == PreviewShape.Plane ? 45 : 20, 180 + 45, 0 );
		}
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		// Cheap attribute sets: keeps sliders and recompiled textures live without rebuilding the scene
		ApplyMaterial();

		// Thumbnails and videos have no live widget: just turn
		var view = LiveWidget;
		if ( !view.IsValid() )
		{
			base.UpdateScene( cycle, timeStep );
			return;
		}

		// The preview's render widget is native and gets no Qt mouse events, so poll the cursor instead
		var cursor = Application.CursorPosition;
		var down = Application.MouseButtons.HasFlag( MouseButtons.Left );
		var pressed = down && !_wasDown;
		_wasDown = down;

		// The corner buttons, in the widget's own coordinates: play / pause top right, shapes top left
		var local = view.FromScreen( cursor );
		var pauseButton = new Rect( view.Width - 36, 8, 28, 28 );
		var overPause = pauseButton.IsInside( local );
		var overShape = -1;
		for ( int i = 0; i < Shapes.Length; i++ )
			if ( ShapeButton( i ).IsInside( local ) ) overShape = i;
		var overButton = overPause || overShape >= 0;
		var overView = view.ScreenRect.IsInside( cursor );

		// Grab cursor over the material, a pointer over the buttons
		var cursorShape = overButton ? CursorShape.Finger : _dragging ? CursorShape.ClosedHand : CursorShape.OpenHand;
		if ( view.Cursor != cursorShape ) view.Cursor = cursorShape;

		if ( pressed && overPause )
		{
			Paused = !Paused;
		}
		else if ( pressed && overShape >= 0 )
		{
			Shape = Shapes[overShape].Shape;
			SetShape( Shape );
		}
		// Only a press that starts on the preview: a drag from elsewhere passing over it isn't a turn
		else if ( pressed && overView )
		{
			_dragging = true;
			_lastCursorX = cursor.x;
		}
		else if ( !down )
		{
			_dragging = false;
		}

		if ( _dragging )
		{
			_yaw += (cursor.x - _lastCursorX) * 0.5f;
			_lastCursorX = cursor.x;
		}
		else if ( !Paused )
		{
			_yaw += timeStep * PreviewWidgetCycleSpeed * 360.0f;
		}

		// The base turns the object by the cycle and frames it: feed it the yaw as a cycle
		base.UpdateScene( _yaw / 360.0f, timeStep );

		DrawButton( view, pauseButton, "pause", overPause, false, ( rect, scale ) =>
			Gizmo.Draw.ScreenText( Paused ? "play_arrow" : "pause", rect.Center, "Material Icons", 18 * scale, TextFlag.Center ) );

		for ( int i = 0; i < Shapes.Length; i++ )
		{
			var (shape, glyph, radius) = Shapes[i];
			DrawButton( view, ShapeButton( i ), $"shape.{shape}", overShape == i, shape == _shape, ( rect, scale ) =>
				Gizmo.Draw.ScreenRect( new Rect( rect.Center - glyph * scale / 2, glyph * scale ), Gizmo.Draw.Color, new Vector4( radius * scale ) ) );
		}
	}

	static Rect ShapeButton( int index ) => new( 8 + index * 32, 8, 28, 28 );

	/// <summary>
	/// A corner button, drawn into the preview itself as a screen-space gizmo: the render surface is native, so
	/// a Qt widget placed over it would be hidden underneath.
	/// </summary>
	static void DrawButton( Widget view, Rect button, string id, bool hovered, bool active, Action<Rect, float> drawContent )
	{
		if ( Gizmo.Settings is null )
			return;

		// One scope per button: the gizmo reuses a scene object per call site within a scope, so without it
		// every button would draw into the same one and only the last would show
		using var scope = Gizmo.Scope( $"button.{id}" );

		// Gizmo screen space is in device pixels
		var scale = view.DpiScale;
		var rect = new Rect( button.Position * scale, button.Size * scale );

		var background = active ? Theme.Blue.WithAlpha( 0.85f ) : Color.Black.WithAlpha( hovered ? 0.7f : 0.45f );
		Gizmo.Draw.ScreenRect( rect, background, new Vector4( 4 * scale ) );
		Gizmo.Draw.Color = hovered || active ? Color.White : Color.White.WithAlpha( 0.8f );

		// Its own scope too: the content may also be a screen rect, which would replace the background
		using ( Gizmo.Scope( "content" ) )
			drawContent( rect, scale );
	}

	void ApplyMaterial()
	{
		if ( Material is null || !_renderer.IsValid() || _renderer.SceneObject is not { } so )
			return;

		so.Attributes.Set( "BCR", Material.BCRTexture );
		so.Attributes.Set( "NHO", Material.NHOTexture );

		// Same values the terrain feeds its GPU material buffer, so the preview
		// tiles and shades like the material does on a terrain
		so.Attributes.Set( "UVScale", Material.UVScale );
		so.Attributes.Set( "NormalStrength", 1.0f / Material.NormalStrength );
		so.Attributes.Set( "Metalness", Material.Metalness );
		so.Attributes.Set( "Roughness", Material.Roughness );
		so.Attributes.Set( "NoTile", Material.NoTiling );

		// Metals and glossy surfaces are mostly what they reflect. The plain ground cubemap is so even that
		// blurring it more or less (roughness) changes nothing visible, and leaves a metal flat and dark: give
		// them the studio one, whose light sources show both. Rough non-metal ground keeps the plain one, the
		// studio lights speckle through on it. Swapped only when the material crosses that line.
		var studio = Material.Metalness > 0.0f || Material.Roughness < 0.9f;
		if ( _envmap.IsValid() && _studioEnvmap != studio )
		{
			_studioEnvmap = studio;
			_envmap.Texture = Texture.Load( studio ? "textures/cubemaps/default2.vtex" : "textures/cubemaps/default.vtex" );
			_envmap.TintColor = Color.White * (studio ? 1.0f : 2.0f);
		}
	}

	/// <summary>
	/// The default preview lighting suits shiny props. Terrain materials are dark, fully rough
	/// ground, so light them like open ground instead: a warm key, a cool rim for separation, and
	/// the reflection probe carrying the ambient. Levels are kept so that even a bright albedo
	/// stays off the ceiling - a clipped highlight is the one thing that makes a rough material
	/// read as wet.
	/// </summary>
	void UpdateLighting()
	{
		using var _ = Scene.Push();

		// Key light
		{
			var go = Scene.Directory.FindByName( "sun" )?.FirstOrDefault() ?? new GameObject( true, "sun" );
			var light = go.GetOrAddComponent<DirectionalLight>();
			light.WorldRotation = Rotation.From( 45, -180, 0 );
			light.LightColor = new Color( 1.0f, 0.95f, 0.85f ) * 0.7f;
			light.SkyColor = Color.Gray * 0.35f;
		}

		// Rim light for a bit of separation. Kept gentle - it's blue, and it's the thing that
		// pushes a saturated albedo's blue channel over the top.
		{
			var go = Scene.Directory.FindByName( "rim" )?.FirstOrDefault() ?? new GameObject( true, "rim" );
			var light = go.GetOrAddComponent<PointLight>();
			light.WorldPosition = new Vector3( -100, 40, 80 );
			light.LightColor = new Color( 0.4f, 0.6f, 1.0f ) * 0.6f;
			light.Radius = 800;
			light.Shadows = false;
		}

		// Ambient / reflections. default.vtex rather than the default2 studio cubemap: that one
		// has light sources bright enough that, on a fully rough surface, they still burn through
		// as speckles at any tint low enough to be worth having. This one has no such hotspots,
		// so it can be turned up and actually light the material. Metals get default2 (ApplyMaterial).
		{
			var go = Scene.Directory.FindByName( "envmap" )?.FirstOrDefault() ?? new GameObject( true, "envmap" );
			var c = _envmap = go.GetOrAddComponent<EnvmapProbe>();
			c.WorldPosition = Vector3.Zero;
			c.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
			c.Texture = Texture.Load( "textures/cubemaps/default.vtex" );
			c.TintColor = Color.White * 2.0f;
			c.Bounds = BBox.FromPositionAndSize( 0, 100000 );
		}

		// A probe replaces the sky fill rather than adding to it, so top the diffuse back up
		{
			var go = Scene.Directory.FindByName( "ambient" )?.FirstOrDefault() ?? new GameObject( true, "ambient" );
			var light = go.GetOrAddComponent<AmbientLight>();
			light.Color = Color.White * 0.15f;
		}
	}
}
