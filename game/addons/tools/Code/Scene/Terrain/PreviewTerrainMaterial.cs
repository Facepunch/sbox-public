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
				_renderer.Model = Model.Sphere;
				_renderer.MaterialOverride = Sandbox.Material.FromShader( "shaders/terrain_preview.shader" );
				ApplyMaterial();
			}

			SceneSize = PrimaryObject.GetBounds().Size * 0.6f;
			SceneCenter = PrimaryObject.GetBounds().Center;
		}

		await Task.CompletedTask;
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

		// The play / pause button, in the widget's own coordinates
		var local = view.FromScreen( cursor );
		var button = new Rect( view.Width - 36, 8, 28, 28 );
		var overButton = button.IsInside( local );
		var overView = view.ScreenRect.IsInside( cursor );

		// Grab cursor over the sphere, a pointer over the button
		var cursorShape = overButton ? CursorShape.Finger : _dragging ? CursorShape.ClosedHand : CursorShape.OpenHand;
		if ( view.Cursor != cursorShape ) view.Cursor = cursorShape;

		if ( pressed && overButton )
		{
			Paused = !Paused;
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

		// The base turns the sphere by the cycle and frames it: feed it the yaw as a cycle
		base.UpdateScene( _yaw / 360.0f, timeStep );

		DrawPauseButton( view, button, overButton );
	}

	/// <summary>
	/// Drawn into the preview itself as a screen-space gizmo: the render surface is native, so a Qt widget
	/// placed over it would be hidden underneath.
	/// </summary>
	void DrawPauseButton( Widget view, Rect button, bool hovered )
	{
		if ( Gizmo.Settings is null )
			return;

		// Gizmo screen space is in device pixels
		var scale = view.DpiScale;
		var rect = new Rect( button.Position * scale, button.Size * scale );

		Gizmo.Draw.ScreenRect( rect, Color.Black.WithAlpha( hovered ? 0.7f : 0.45f ), new Vector4( 4 * scale ) );
		Gizmo.Draw.Color = hovered ? Color.White : Color.White.WithAlpha( 0.8f );
		Gizmo.Draw.ScreenText( Paused ? "play_arrow" : "pause", rect.Center, "Material Icons", 18 * scale, TextFlag.Center );
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
