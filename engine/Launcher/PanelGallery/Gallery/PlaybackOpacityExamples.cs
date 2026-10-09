namespace Sandbox.PanelGallery;

/// <summary>
/// Replays one recorded UI root at different opacities to compare box and path drawing.
/// </summary>
sealed class PlaybackOpacityExamples : Panel
{
	const int TargetWidth = 640;
	const int TargetHeight = 128;
	readonly float[] alphas = [1f, 0f, 0.5f];
	readonly Sandbox.UI.Image[] previews = new Sandbox.UI.Image[3];
	readonly Texture[] targets = new Texture[3];
	readonly SceneCamera[] cameras = new SceneCamera[3];
	RootPanel root;
	SceneWorld world;

	public PlaybackOpacityExamples()
	{
		Style.Set( "flex-direction: column; flex-shrink: 0;" );
		Add.Label( "Recorded UI playback opacity", "case-title" );
		Add.Label( "One RootPanel command list, recorded at full opacity and replayed into three textures. RenderManual(alpha) changes the playback opacity without changing the drawing.", "page-blurb" );
		string[] titles =
		[
			"RenderManual(1) - full opacity",
			"RenderManual(0) - invisible",
			"RenderManual(0.5) - half opacity"
		];
		for ( int i = 0; i < alphas.Length; i++ )
		{
			var section = Add.Panel( "case" );
			section.Add.Label( titles[i], "case-title" );
			var row = section.Add.Panel( "row column" );
			row.SetProperty( "style", "width: 100%; max-width: 640px; gap: 0px;" );
			var labels = row.Add.Panel();
			labels.SetProperty( "style", "width: 100%; flex-direction: row; padding-bottom: 6px;" );
			foreach ( var title in new[] { "Rectangle", "Thick line", "Polyline", "12-point polygon" } )
			{
				labels.Add.Label( title ).SetProperty( "style", "width: 25%; text-align: center; font-size: 12px;" );
			}

			previews[i] = row.AddChild<Sandbox.UI.Image>();
			previews[i].SetProperty( "style", "width: 100%; height: 128px; flex-shrink: 0; object-fit: contain; background-color: black; pointer-events: none;" );
		}

		Add.Label( "All shapes should fade together and disappear at zero opacity. Captions are outside the rendered root and remain visible.", "page-blurb" );
	}

	public override void Tick()
	{
		base.Tick();
		if ( root is null )
		{
			// A private UI system keeps the gallery's window layout from resizing this offscreen root.
			root = new RootPanel( new UISystem() )
			{
				RenderedManually = true,
				PanelBounds = new Rect( 0, 0, TargetWidth, TargetHeight )
			};
			root.SetProperty( "style", "background-color: transparent; pointer-events: none;" );
			root.AddChild<Shapes>().SetProperty( "style", "width: 100%; height: 100%;" );
			world = new SceneWorld();

			for ( int i = 0; i < alphas.Length; i++ )
			{
				var alpha = alphas[i];
				targets[i] = Texture.CreateRenderTarget().WithSize( TargetWidth, TargetHeight ).WithFormat( ImageFormat.RGBA8888 ).Create();
				previews[i].Texture = targets[i];
				cameras[i] = new SceneCamera( $"Playback opacity {alpha}" )
				{
					World = world,
					Size = new Vector2( TargetWidth, TargetHeight ),
					BackgroundColor = Color.Black,
					ClearFlags = ClearFlags.All,
					EnablePostProcessing = false,
					AntiAliasing = false,
					OnRenderUI = () => root.RenderManual( alpha )
				};
			}
		}

		root.Layout();
		root.BuildCommandList();
		// Record once, replay three times. The camera supplies the render block, never OnDraw.
		for ( int i = 0; i < cameras.Length; i++ )
		{
			cameras[i].RenderToTexture( targets[i], null, default );
		}
	}

	sealed class Shapes : Panel
	{
		public override void OnDraw( Painter painter )
		{
			var color = Color.FromBytes( 255, 150, 40 );
			painter.Fill = color;
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( 32, 28, 96, 72 ) );

			painter.Fill = Fill.None;
			painter.Stroke = Stroke.Solid( color, 24 );
			painter.Line( new Vector2( 192, 64 ), new Vector2( 288, 64 ) );
			painter.Line( [new Vector2( 344, 94 ), new Vector2( 372, 32 ), new Vector2( 404, 90 ), new Vector2( 448, 36 )] );

			painter.Fill = color;
			painter.Stroke = Stroke.None;
			// More than eight vertices forces PolygonPath instead of the small-polygon box route.
			Span<Vector2> polygon = stackalloc Vector2[12];
			for ( int i = 0; i < polygon.Length; i++ )
			{
				float angle = i * MathF.Tau / polygon.Length;
				polygon[i] = new Vector2( 560, 64 ) + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * 44;
			}
			painter.Polygon( polygon );
		}
	}

	public override void OnDeleted()
	{
		foreach ( var camera in cameras )
		{
			if ( camera is null ) continue;
			camera.OnRenderUI = null;
			camera.Dispose();
		}
		root?.Delete( true );
		foreach ( var target in targets ) target?.Dispose();
		world?.Delete();
		base.OnDeleted();
	}
}
