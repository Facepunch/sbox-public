namespace Sandbox.PanelGallery;

/// <summary>
/// Visual tests for Painter, separate from CSS decoration.
/// </summary>
public partial class DrawPage : GalleryPage
{
	// Captures can freeze the shared animation clock with -pause-animation.
	readonly bool _animate = !Environment.GetCommandLineArgs().Any( x => x.Equals( "-pause-animation", StringComparison.OrdinalIgnoreCase ) );
	float _animationTime = ShapeMovie.InitialTime();
	readonly Texture _checker;

	/// <summary>
	/// Builds samples of every drawing primitive and their interaction with panel styling.
	/// </summary>
	public DrawPage( string feature = "Rectangle" ) : base( feature switch { "Rectangle" => "Rect", "Quadratic Bezier" => "Bezier (quadratic)", "Cubic Bezier" => "Bezier (cubic)", _ => feature }, "Painter inside OnDraw(), redrawing geometry dynamically. Frames, captions and controls stay fixed in CSS." )
	{
		Color cyan = GalleryPalette.CyanHex;
		Color lime = GalleryPalette.LimeHex;
		Color pink = GalleryPalette.PinkHex;
		var checker = _checker = DisplayPanelsPage.Checkerboard();
		if ( feature == "Painter Draw" )
		{
			DeleteChildren( true );
			AddClass( "full-page hud-demo" );
			Add.Label( "This HUD is drawn entirely using the Painter class, combining simple shapes, gradients and text to create the animated radar, gauges and graphs.", "demo-info" );
			AddChild( new HudDrawPanel( () => _animationTime ) );
			return;
		}

		if ( feature == "Bezier" )
		{
			Add.Label( "Quadratic · one control point", "page-title" );
			AddChild( new ShapeMovie( "Quadratic Bezier", () => _animationTime, time => _animationTime = time ) );
			Add.Label( "Cubic · two control points", "page-title" );
			AddChild( new ShapeMovie( "Cubic Bezier", () => _animationTime, time => _animationTime = time ) );
			ShapeSpecificCases( "Quadratic Bezier" );
			ShapeSpecificCases( "Cubic Bezier" );
			return;
		}

		if ( ShapeExamples.ContainsKey( feature ) )
		{
			AddChild( new ShapeMovie( feature, () => _animationTime, time => _animationTime = time ) );
			if ( feature == "Line" ) PolylineExamples();
			ShapeSpecificCases( feature );
			return;
		}


		if ( feature == "Text" ) TextStyleHelpers( cyan, lime, pink );
		if ( feature == "Gradients" ) LengthGradients( cyan, lime, pink );
		if ( feature == "Gradients" ) PositionedGradients( cyan, lime, pink );
		if ( feature == "Text measurement" ) TextMeasurementSamples( cyan, lime );
		if ( feature == "Text" ) TextStyleSamples( cyan, lime, pink );
		if ( feature == "Opacity" ) OpacitySamples( checker, cyan, lime );
		if ( feature == "Clipping" ) ClippedShapes( checker, cyan, lime );
		if ( feature == "Transforms" ) TransformedShapes( checker, cyan, lime, pink );
		StyledShapes( feature, checker, cyan, lime, pink );

		if ( feature == "Triangle" )
		{
			var row = Examples( "Triangle" );
			Sample( row, "Triangle", "Three layout-coordinate vertices, solid fill.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Triangle( r.Position + new Vector2( r.Width * 0.5f + Wave( 24 ), 8 + Wave( 8, 1.3f ) ),
					r.Position + new Vector2( r.Width, r.Height ), r.Position + new Vector2( 0, r.Height ) );
			} );
		}

		if ( feature == "Quad" )
		{
			var row = Examples( "Quad" );
			Sample( row, "Quad stroke", "Transparent fill with a centered 5px stroke.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( lime, 5 );
				painter.Quad( r.Position + new Vector2( r.Width * 0.2f + Wave( 12 ), 0 ),
					r.Position + new Vector2( r.Width, r.Height * 0.2f + Wave( 10, 1, 1.5f ) ),
					r.Position + new Vector2( r.Width * 0.8f - Wave( 12 ), r.Height ),
					r.Position + new Vector2( 0, r.Height * 0.8f - Wave( 10, 1, 1.5f ) ) );
			} );
		}

		if ( feature == "Polygon" )
		{
			var row = Examples( "Polygon" );
			Sample( row, "Concave polygon", "Six vertices; translucent fill and centered stroke.", ( painter, r ) =>
			{
				var notch = new Vector2( r.Width * (0.5f + Wave( 0.15f )), r.Height * (0.5f + Wave( 0.15f, 1, 1.5f )) );
				painter.Stroke = Stroke.Solid( pink.WithAlpha( 0.7f ), 5 );
				painter.Fill = pink.WithAlpha( 0.3f );
				painter.Polygon( stackalloc Vector2[]
				{
					r.Position, r.Position + new Vector2( r.Width, 0 ),
					r.Position + new Vector2( r.Width, notch.y ),
					r.Position + notch,
					r.Position + new Vector2( notch.x, r.Height ), r.Position + new Vector2( 0, r.Height )
				} );
			} );
		}

		if ( feature == "Line" )
		{
			var row = Examples( "Line" );
			Sample( row, "Lines", "A 1px lime stroke above a diagonal 14px cyan stroke.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( lime, 1 );
				painter.Line( r.Position, r.Position + new Vector2( r.Width, 0 ) );
				painter.Stroke = Stroke.Solid( cyan, 14 );
				painter.Line( r.Position + new Vector2( 8, r.Height - 20 + Wave( 12 ) ),
					r.Position + new Vector2( r.Width - 8, 24 - Wave( 12 ) ) );
			} );
		}

		if ( feature == "Arrow" )
		{
			var row = Examples( "Arrow" );
			Sample( row, "Arrow", "One flexing shafted polygon, 10px shaft and 26-46px head.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( lime, 3 );
				painter.Fill = cyan;
				painter.Arrow( r.Position + new Vector2( 8, r.Height - 20 + Wave( 10 ) ),
					r.Position + new Vector2( r.Width - 8, 20 - Wave( 10 ) ), 10, 36 + Wave( 10 ) );
			} );
			Sample( row, "Arrow stroke", "Transparent fill; centered stroke follows shaft and head.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( pink, 3 );
				painter.Arrow( r.Center - new Vector2( r.Width * 0.4f, Wave( 12 ) ),
					r.Center + new Vector2( r.Width * 0.4f, Wave( 12 ) ), width: 18, headSize: 48 + Wave( 10 ) );
			} );
			Sample( row, "Short / wide arrows", "Head length clamps to the segment; head width is at least the shaft.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Arrow( r.Center + new Vector2( -16 - Wave( 8 ), -24 ), r.Center + new Vector2( 16 + Wave( 8 ), -24 ), 8, 48 );
				painter.Fill = lime;
				painter.Arrow( r.Center + new Vector2( -48 + Wave( 12 ), 24 ), r.Center + new Vector2( 48 - Wave( 12 ), 24 ), 24, 12 );
			} );
		}

		if ( feature == "Ellipse" )
		{
			var row = Examples( "Ellipse" );
			Sample( row, "Ellipse", "An oval breathing along each axis, unlike Circle.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Circle( r.Shrink( 10 + Wave( 10 ), 8 - Wave( 8 ) ) );
			} );
			Sample( row, "Ellipse stroke", "Transparent fill; 6px stroke centered on the ellipse.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( lime, 6 );
				painter.Circle( r.Shrink( 10 + Wave( 10 ), 8 - Wave( 8 ) ) );
			} );
			Sample( row, "Translucent ellipse", "Translucent fill and centered stroke over a solid triangle.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Triangle( r.Position, r.Position + new Vector2( r.Width, 0 ),
					r.Position + new Vector2( r.Width * 0.5f, r.Height ) );
				painter.Stroke = Stroke.Solid( pink.WithAlpha( 0.7f ), 8 );
				painter.Fill = pink.WithAlpha( 0.3f );
				painter.Circle( r.Shrink( 10 + Wave( 10 ), 8 - Wave( 8 ) ) );
			} );
		}

		if ( feature == "Opacity" )
		{
			var row = Examples( "Opacity" );
			foreach ( var opacity in new[] { 1.0f, 0.35f } )
			{
				var sample = Sample( row, $"Shape opacity: {opacity:0.##}", "CSS opacity applies to both shape fills and strokes.", ( painter, r ) =>
				{
					painter.Stroke = Stroke.Solid( lime, 4 );
					painter.Fill = cyan;
					painter.Circle( r );
					painter.Stroke = Stroke.Solid( Color.White, 2 );
					painter.Fill = pink;
					painter.Arrow( r.Center - new Vector2( r.Width * 0.35f, Wave( 14 ) ),
						r.Center + new Vector2( r.Width * 0.35f, Wave( 14 ) ), 10, 32 );
				} );
				sample.Style.Opacity = opacity;
			}
		}

		if ( feature == "Rectangle" )
		{
			var row = Examples( "Rectangle" );
			Sample( row, "Rectangle", "Breathing dimensions, no corner radius.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Rect( r.Shrink( 12 + Wave( 12 ), 6 - Wave( 6 ) ) );
			} );
		}

		if ( feature == "Rectangle" )
		{
			var row = Examples( "Rounded rectangle" );
			Sample( row, "Rounded rectangle", "Uniform radius flows from 8 to 32px.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Rect( r, 20 + Wave( 12 ) );
			} );
			Sample( row, "Per-corner radii", "BR 0; TR 4-20, BL 16-32, TL 28-52px.", ( painter, r ) =>
			{
				painter.Fill = lime;
				painter.Rect( r, new Painter.CornerRadii( new( 40 + Wave( 12 ) ), new( 12 + Wave( 8 ) ), new( 0 ), new( 24 - Wave( 8 ) ) ) );
			} );
		}

		if ( feature == "Rectangle" )
		{
			var row = Examples( "Pill" );
			Sample( row, "Pill", "Breathing length; radius is half the height.", ( painter, r ) =>
			{
				painter.Fill = lime;
				painter.Rect( r.Shrink( 16 + Wave( 16 ), 0 ), r.Height * 0.5f );
			} );
		}

		if ( feature == "Circle" )
		{
			var row = Examples( "Circle" );
			Sample( row, "Circle", "A breathing filled circle, not an ellipse.", ( painter, r ) =>
			{
				painter.Fill = pink;
				painter.Circle( r.Center, r.Height * (0.4f + Wave( 0.08f )) );
			} );
			Sample( row, "Alpha / draw order", "Three orbiting translucent circles; cyan, lime, then pink.", ( painter, r ) =>
			{
				var radius = r.Height * 0.3f;
				painter.Fill = cyan.WithAlpha( 0.65f );
				painter.Circle( r.Center + Orbit( r.Height * 0.2f ), radius );
				painter.Fill = lime.WithAlpha( 0.65f );
				painter.Circle( r.Center + Orbit( r.Height * 0.2f, phase: MathF.Tau / 3 ), radius );
				painter.Fill = pink.WithAlpha( 0.65f );
				painter.Circle( r.Center + Orbit( r.Height * 0.2f, phase: MathF.Tau * 2 / 3 ), radius );
			} );
		}

		if ( feature == "Scopes" )
		{
			var row = Examples( "Scopes" );
			Sample( row, "Saved drawing state", "Blue, lime, pink, lime, cyan. Nested scopes restore both fill and stroke.", ( painter, r ) =>
			{
				void Bar( Painter painter, int index ) => painter.Rect( new Rect( r.Left + index * r.Width / 5 + 3, r.Top + 8, r.Width / 5 - 6, r.Height - 16 ), 4 );
				painter.Fill = cyan;
				painter.Stroke = Stroke.Solid( Color.White, 2 );
				Bar( painter, 0 );
				using ( painter.Scope() )
				{
					painter.Fill = lime;
					painter.Stroke = Stroke.Dashed( Color.White, 2, 4, 3 );
					Bar( painter, 1 );
					using ( painter.Scope() )
					{
						painter.Fill = pink;
						painter.Stroke = Stroke.None;
						Bar( painter, 2 );
					}
					Bar( painter, 3 );
				}
				Bar( painter, 4 );
			} );
		}

		if ( feature == "Images" )
		{
			var row = Examples( "Texture" );
			Sample( row, "Square", "Generated 8x8 checkerboard, smoothly zooming.", ( painter, r ) =>
				painter.Texture( checker, new Rect( r.Center - new Vector2( r.Height * 0.5f ), new Vector2( r.Height ) ).Shrink( 8 + Wave( 8 ) ) ) );
			Sample( row, "Stretched", "The same texture stretches and relaxes horizontally.", ( painter, r ) => painter.Texture( checker, r.Shrink( 16 + Wave( 16 ), 0 ) ) );
			Sample( row, "Tint", "Blue tint with 50% alpha stretches over an pink fill.", ( painter, r ) =>
			{
				painter.Fill = pink;
				painter.Rect( r );
				painter.Texture( checker, r.Shrink( 12 + Wave( 12 ), 6 - Wave( 6 ) ), cyan.WithAlpha( 0.5f ) );
			} );
		}

		if ( feature == "Text" )
		{
			var row = Examples( "Text" );
			Sample( row, "Alignment", "LeftTop, Center and RightBottom travel in a fixed-size box.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( cyan.WithAlpha( 0.4f ), 1 );
				painter.Outline( r );
				painter.TextStyle = new TextStyle { FontSize = 14, Color = Color.White };
				painter.Text( "Left / top", r );
				painter.TextStyle = new TextStyle { FontSize = 18, Color = lime, Alignment = TextFlag.Center };
				painter.Text( "Center", r );
				painter.TextStyle = new TextStyle { FontSize = 14, Color = pink, Alignment = TextFlag.RightBottom };
				painter.Text( "Right / bottom", r );
			} );
			Sample( row, "Font / size", "Roboto Mono, fixed 24px, floating in fixed-size bounds.", ( painter, r ) =>
			{
				painter.TextStyle = new TextStyle { FontSize = 24, Color = Color.White, Alignment = TextFlag.Center, FontName = "Roboto Mono" };
				painter.Text( "Aa 0123", r );
			} );
			Sample( row, "Word wrap", "TextFlag.WordWrap; moving bounds keep a fixed wrapping width.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( cyan.WithAlpha( 0.4f ), 1 );
				painter.Outline( r );
				painter.TextStyle = new TextStyle { FontSize = 16, Color = Color.White, Alignment = TextFlag.LeftTop | TextFlag.WordWrap };
				painter.Text( "Custom text can wrap across several lines inside its bounds.", r );
			} );
		}

		if ( feature == "Shadows" )
		{
			var row = Examples( "Shadow" );
			Sample( row, "Hard shadow", "No blur; a 10px orbiting offset. Shadow before fill.", ( painter, r ) =>
			{
				painter.RectShadow( r, 12, color: Color.Black.WithAlpha( 0.8f ), offset: Orbit( 10 ) );
				painter.Fill = cyan;
				painter.Rect( r, 12 );
			} );
			Sample( row, "Soft shadow / spread", "Blur 8-16, spread 2-6; an 8px orbiting offset.", ( painter, r ) =>
			{
				painter.RectShadow( r, 12, color: lime.WithAlpha( 0.7f ), blur: 12 + Wave( 4 ), spread: 4 + Wave( 2 ), offset: Orbit( 8 ) );
				painter.Fill = cyan;
				painter.Rect( r, 12 );
			} );
			Sample( row, "Inset shadow", "Fill first, then an inset shadow with blur 12.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Rect( r, 12 );
				painter.RectShadow( r, 12, color: Color.Black.WithAlpha( 0.8f ), blur: 12, spread: 3, offset: Orbit( 10 ), inset: true );
			} );
		}

		if ( feature == "Outlines" )
		{
			var row = Examples( "Outline" );
			foreach ( var offset in new[] { -6, 0, 6 } )
			{
				Sample( row, $"Offset {offset}", "4px outline; corner radius breathes 4-24px. Faint fill marks the source box.", ( painter, r ) =>
				{
					painter.Fill = cyan.WithAlpha( 0.2f );
					painter.Rect( r, 14 + Wave( 10 ) );
					painter.Stroke = Stroke.Solid( cyan, 4 );
					painter.Outline( r, cornerRadius: 14 + Wave( 10 ), offset: offset );
				} );
			}
		}

		if ( feature == "Panel integration" )
		{
			var row = Examples( "Panel integration" );
			var clipped = Sample( row, "CSS clipping", "Oversized drawing should stop at the rounded frame.", ( painter, r ) =>
			{
				painter.Fill = cyan;
				painter.Rect( r.Grow( r.Height ) );
				painter.Fill = lime;
				painter.Circle( r.Position + new Vector2( r.Width * (0.3f + Wave( 0.3f )), 0 ), r.Width * 0.65f );
				painter.TextStyle = new TextStyle { FontSize = 18, Color = Color.Black, Alignment = TextFlag.Center };
				painter.Text( "Clipped by CSS", r );
			} );
			clipped.Parent.AddClass( "draw-clipped" );

			foreach ( var opacity in new[] { 1.0f, 0.35f } )
			{
				var sample = Sample( row, $"CSS opacity: {opacity:0.##}", "Opacity check: compare drawn fill/text with the CSS label.", ( painter, r ) =>
				{
					painter.Fill = cyan;
					painter.Rect( r, 20 + Wave( 12 ) );
					painter.TextStyle = new TextStyle { FontSize = 20, Color = Color.White, Alignment = TextFlag.Center };
					painter.Text( "painter.Text", r );
				} );
				sample.Style.Opacity = opacity;
				sample.Add.Label( "CSS label", "draw-overlay" );
			}
		}

		if ( feature == "Invalidation" )
		{
			var row = Examples( "Shadow invalidation" );
			float blur = 8;
			var preview = Sample( row, "Shadow invalidation", "Click the preview to change the blur.", ( painter, r ) =>
			{
				painter.RectShadow( r, 16, color: lime.WithAlpha( 0.8f ), blur: blur, offset: Orbit( 8 ) );
				painter.Fill = cyan;
				painter.Rect( r, 16 );
				painter.TextStyle = new TextStyle { FontSize = 20, Color = Color.White, Alignment = TextFlag.Center };
				painter.Text( $"Blur {blur:0}", r );
			} );
			preview.Style.PointerEvents = Sandbox.UI.PointerEvents.All;
			preview.AddEventListener( "onclick", () =>
			{
				blur = (blur + 8) % 32;
			} );
		}

	}

	/// <summary>
	/// Advances the shared drawing clock unless animation is paused.
	/// </summary>
	public override void Tick()
	{
		base.Tick();
		if ( _animate ) _animationTime += RealTime.Delta;
	}

	public override void OnDeleted()
	{
		_checker.Dispose();
		base.OnDeleted();
	}

	float Wave( float amplitude, float speed = 1, float phase = 0 ) => amplitude * MathF.Sin( _animationTime * speed + phase );

	Vector2 Orbit( float radius, float speed = 1, float phase = 0 ) => new( Wave( radius, speed, phase + MathF.PI * 0.5f ), Wave( radius, speed, phase ) );

	void StyledShapes( string feature, Texture checker, Color cyan, Color lime, Color pink )
	{
		var linear = Fill.LinearGradient( [new( 0, cyan ), new( 0.45f, lime ), new( 1, pink )], 25 );
		Fill.GradientStop[] linearStops = [new( 0, cyan ), new( 0.35f, lime ), new( 0.7f, pink ), new( 1, cyan )];
		Fill.GradientStop[] conicStops = [new( 0, cyan ), new( 0.33f, lime ), new( 0.66f, pink ), new( 1, cyan )];
		var radial = Fill.RadialGradient( Color.White, cyan.WithAlpha( 0.15f ) );
		Fill conic = default;
		float conicTime = float.NaN;
		Fill FlowingConic()
		{
			if ( conicTime == _animationTime ) return conic;
			conicTime = _animationTime;
			return conic = Fill.ConicGradient( conicStops, -90 + _animationTime * 45 );
		}
		Fill FlowingImage() => Fill.Image( checker, cyan, width: Length.Percent( 50 ), height: Length.Percent( 50 ),
			offsetX: Length.Percent( -((_animationTime * 0.12f) % 1) * 50 ), offsetY: Length.Percent( -((_animationTime * 0.06f) % 1) * 50 ),
			repeat: BackgroundRepeat.Repeat, filter: Sandbox.Rendering.FilterMode.Point );
		var image = Fill.Image( checker, cyan, width: Length.Percent( 50 ), height: Length.Percent( 50 ),
			repeat: BackgroundRepeat.Repeat, filter: Sandbox.Rendering.FilterMode.Point );
		var outline = new Stroke( Color.White.WithAlpha( 0.7f ), 2 );

		if ( feature == "Stroke" )
		{
			var row = Examples( "Styled shapes / End caps" );
			foreach ( var cap in new[] { Stroke.LineCap.Butt, Stroke.LineCap.Square, Stroke.LineCap.Round, Stroke.LineCap.Triangle, Stroke.LineCap.Arrow } )
			{
				var note = cap switch
				{
					Stroke.LineCap.Triangle => " Triangle tip extends half the stroke width.",
					Stroke.LineCap.Arrow => " Arrow base is twice the stroke width; tip extends one full width.",
					_ => ""
				};
				Sample( row, cap.ToString(), "16-20px stroke sways +/-10 degrees. Guides follow the exact endpoints." + note, ( painter, r ) =>
				{
					float angle = Wave( MathF.PI / 18 );
					var direction = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) );
					var extent = direction * (r.Width * (0.3f + Wave( 0.04f )));
					var from = r.Center - extent;
					var to = r.Center + extent;
					var normal = new Vector2( -direction.y, direction.x ) * 25;
					painter.Stroke = Stroke.Solid( Color.White.WithAlpha( 0.2f ) );
					painter.Line( from - normal, from + normal );
					painter.Line( to - normal, to + normal );
					painter.Stroke = new Stroke( linear, 18 + 2 * MathF.Sin( _animationTime * 0.8f ) ) { Cap = cap };
					painter.Line( from, to );
				}, animateBounds: false );
			}
		}

		if ( feature == "Stroke" )
		{
			var row = Examples( "Styled shapes / Joins and miter limits" );
			foreach ( var join in new[] { Stroke.LineJoin.Miter, Stroke.LineJoin.Bevel, Stroke.LineJoin.Round } )
			{
				Sample( row, join.ToString(), "14px joined polyline; miter limit 4. Identical traveling corner in every sample.", ( painter, r ) =>
				{
					painter.Stroke = new Stroke( lime, 14 ) { Join = join };
					painter.Line( [r.BottomLeft, r.Center + new Vector2( Wave( 20 ), -r.Height * 0.35f ), r.BottomRight] );
				} );
			}
			foreach ( var limit in new[] { 2f, 8f } )
			{
				Sample( row, $"Miter limit {limit}", "Same sharp corner; a short limit falls back to bevel.", ( painter, r ) =>
				{
					painter.Stroke = new Stroke( pink, 10 ) { Join = Stroke.LineJoin.Miter, MiterLimit = limit };
					painter.Line( [r.Center + new Vector2( -18 + Wave( 20 ), 35 ), r.Center + new Vector2( Wave( 20 ), -20 ), r.Center + new Vector2( 18 + Wave( 20 ), 35 )] );
				} );
			}
		}

		if ( feature == "Stroke" )
		{
			var row = Examples( "Styled shapes / Dashes, dots and pixel phase" );
			foreach ( var pattern in new[] { BorderStyle.Dashed, BorderStyle.Dotted } )
			{
				foreach ( var phase in new[] { 0f, 8f } )
				{
					var note = pattern == BorderStyle.Dotted
						? "6px dots fit evenly around the closed loop. Phase flows at 32px/s without a disappearing seam dot."
						: "6px border; dash 12px, gap 8px. Phase flows at 32px/s, keeping the 0/8px difference.";
					Sample( row, $"{pattern}, phase +{phase}px", note, ( painter, r ) =>
					{
						painter.Stroke = new Stroke( lime, 6 ) { Style = pattern, DashLength = 12, Gap = 8, Offset = phase + _animationTime * 32, Cap = Stroke.LineCap.Round };
						painter.Fill = cyan.WithAlpha( 0.08f );
						painter.Rect( r.Shrink( 8 ), 16 );
					} );
				}
			}
			var graphPoints = new Vector2[2000];
			Sample( row, "2,000-point graph", "Traveling waveform with a breathing envelope; one continuous 0.75px polyline.", ( painter, r ) =>
			{
				float envelope = 0.23f + Wave( 0.08f, 1.2f );
				for ( int i = 0; i < graphPoints.Length; i++ )
				{
					float t = i / (float)(graphPoints.Length - 1);
					float phase = t * 35 - _animationTime * MathF.Tau * 0.65f;
					graphPoints[i] = r.Position + new Vector2( t * r.Width, r.Height * (0.5f + envelope * MathF.Sin( phase ) + 0.06f * MathF.Sin( t * 180 - _animationTime * MathF.Tau )) );
				}
				painter.Stroke = new Stroke( linear, 0.75f );
				painter.Line( graphPoints );
			} );
		}

		if ( feature == "Fill" )
		{
			var row = Examples( "Styled shapes / Gradient and image fills" );
			Sample( row, "Four-stop triangle", "Linear gradient rotates at 40 degrees/s; the tip flows across a centered white stroke.", ( painter, r ) =>
			{
				painter.Stroke = outline;
				painter.Fill = Fill.LinearGradient( linearStops, 25 + _animationTime * 40 );
				painter.Triangle( r.TopLeft, r.TopRight, new Vector2( r.Center.x + Wave( 24 ), r.Bottom ) );
			} );
			Sample( row, "20-point concave polygon", "Alternating inner and outer radii; even-odd fill and a centered round join.", ( painter, r ) =>
			{
				Span<Vector2> points = stackalloc Vector2[20];
				for ( int i = 0; i < points.Length; i++ )
				{
					float angle = i * MathF.PI / 10 - MathF.PI * 0.5f + _animationTime * 0.6f;
					float radius = r.Height * (i % 2 == 0 ? 0.48f : 0.25f);
					points[i] = r.Center + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * radius;
				}
				painter.Stroke = outline;
				painter.Fill = linear;
				painter.Polygon( points );
			} );
			Sample( row, "Radial ellipse", "Premultiplied-alpha interpolation, white center to translucent cyan edge.", ( painter, r ) =>
			{
				painter.Stroke = outline;
				painter.Fill = radial;
				painter.Circle( r.Shrink( 10 + Wave( 10 ), 8 - Wave( 8 ) ) );
			} );
			Sample( row, "Conic disc", "Four stops rotate at 45 degrees/s; matching colors close the seam.", ( painter, r ) =>
			{
				painter.Stroke = outline;
				painter.Fill = FlowingConic();
				painter.Circle( r.Center, r.Height * 0.5f );
			} );
			Sample( row, "100px image tiles", "100px-wide tiles preserve their aspect ratio. Offset starts at 50px and moves right at 12px/s.", ( painter, r ) =>
			{
				painter.Stroke = outline;
				painter.Fill = Fill.Image( checker, cyan, width: 100, offsetX: 50 + (_animationTime * 12) % 100,
					repeat: BackgroundRepeat.Repeat, filter: Sandbox.Rendering.FilterMode.Point );
				painter.Quad( r.TopLeft + new Vector2( 24 + Wave( 12 ), 0 ), r.TopRight, r.BottomRight - new Vector2( 24 + Wave( 12 ), 0 ), r.BottomLeft );
			} );
			Sample( row, "Rounded fill + stroke", "Independent gradient fill and image stroke; per-corner radii.", ( painter, r ) =>
			{
				painter.Stroke = new Stroke( FlowingImage(), 10 );
				painter.Fill = linear;
				painter.Rect( r.Shrink( 6 ), new Painter.CornerRadii( new( 40 ), new( 12 ), new( 0 ), new( 24 ) ) );
			} );
			var faded = Sample( row, "CSS opacity 0.35", "Panel opacity applies once to both the image and the gradient stroke.", ( painter, r ) =>
			{
				painter.Stroke = new Stroke( FlowingConic(), 8 );
				painter.Fill = FlowingImage();
				painter.Circle( r.Shrink( 6 ) );
			} );
			faded.Style.Opacity = 0.35f;
		}

		if ( feature == "Ring" )
		{
			var row = Examples( "Ring" );
			Sample( row, "Gradient ring", "A gradient fills the band between the inner and outer radii.", ( painter, r ) =>
			{
				painter.Fill = FlowingConic();
				var radius = r.Height * (0.35f + Wave( 0.05f ));
				painter.Ring( r.Center, radius - 6, radius + 6 );
			} );
		}

		if ( feature == "Arc" )
		{
			var row = Examples( "Arc" );
			Sample( row, "Dashed arc", "Rotating start, sweep 120-320 degrees. Round dash caps; phase flows at 32px/s.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Dashed( lime, 8, 10, 8, 5 + _animationTime * 32 ) with { Cap = Stroke.LineCap.Round };
				painter.Arc( r.Center, r.Height * 0.4f, -90 + _animationTime * 30, 220 + Wave( 100 ) );
			} );
			Sample( row, "Negative sweep", "Counterclockwise: rotating start, sweep -320 to -120 degrees, square caps.", ( painter, r ) =>
			{
				painter.Stroke = new Stroke( pink, 10 ) { Cap = Stroke.LineCap.Square };
				painter.Arc( r.Center, r.Height * 0.4f, -_animationTime * 30, -220 - Wave( 100 ) );
			} );
		}

		if ( feature == "Pie" )
		{
			var row = Examples( "Pie" );
			Sample( row, "Pie", "Rotating clockwise 120-320-degree sector with a centered outline.", ( painter, r ) =>
			{
				painter.Stroke = outline;
				painter.Fill = linear;
				painter.Pie( r.Center, r.Height * 0.45f, -90 + _animationTime * 30, 220 + Wave( 100 ) );
			} );
		}

		if ( feature == "Coverage" )
		{
			var row = Examples( "Styled shapes / Subpixel coverage" );
			foreach ( var width in new[] { 0.1f, 0.25f, 0.5f, 1f, 2f } )
			{
				Sample( row, $"{width:0.##} layout px", "Top: solid. Middle: dashed diagonal. Bottom: dotted diagonal. Thin strokes fade by coverage.", ( painter, r ) =>
				{
					var stroke = new Stroke( Color.White, width ) { Cap = Stroke.LineCap.Round, DashLength = 8, Gap = 4, Offset = _animationTime * 32 };
					painter.Stroke = stroke;
					painter.Line( r.TopLeft + new Vector2( 0, 8 ), r.TopRight + new Vector2( 0, 8 ) );
					painter.Stroke = stroke with { Style = BorderStyle.Dashed, Fill = cyan };
					painter.Line( r.Position + new Vector2( 0, r.Height * 0.55f ), r.Position + new Vector2( r.Width, r.Height * 0.25f ) );
					painter.Stroke = stroke with { Style = BorderStyle.Dotted, Fill = lime };
					painter.Line( r.BottomLeft, r.Position + new Vector2( r.Width, r.Height * 0.7f ) );
				} );
			}
		}

		if ( feature == "Quadratic Bezier" )
		{
			var row = Examples( "Quadratic Bezier" );
			Sample( row, "Quadratic", "One moving control point, round endpoints; flattening targets 0.25 layout pixels.", ( painter, r ) =>
			{
				painter.Stroke = new Stroke( linear, 8 ) { Cap = Stroke.LineCap.Round };
				painter.Bezier( r.BottomLeft, r.Center + new Vector2( Wave( r.Width * 0.35f ), -r.Height * (0.65f + Wave( 0.35f, 1, 1.5f )) ), r.BottomRight );
			} );
		}

		if ( feature == "Cubic Bezier" )
		{
			var row = Examples( "Cubic Bezier" );
			Sample( row, "Cubic dashes", "Two moving control points; one continuous dash phase along the flattened curve.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Dashed( pink, 5, 10, 6, _animationTime * 32 ) with { Cap = Stroke.LineCap.Round };
				painter.Bezier( r.BottomLeft, r.TopLeft + new Vector2( r.Width * (0.3f + Wave( 0.3f )), 12 + Wave( 12, 1, 1.5f ) ),
					r.BottomRight - new Vector2( r.Width * (0.3f + Wave( 0.3f )), 12 + Wave( 12, 1, 1.5f ) ), r.TopRight );
			} );
		}

	}

	void LengthGradients( Color cyan, Color lime, Color pink )
	{
		var row = Examples( "Drawing gradient lengths" );
		Sample( row, "Linear / mixed units", "Start at 20px, end at 80% of each shape's width. One fill is reused for both shapes.", ( painter, r ) =>
		{
			var half = Length.Percent( 50 );
			painter.Fill = Fill.LinearGradient( 20, half, Length.Percent( 80 ), half, cyan, pink );
			painter.Translate( r.Position );
			painter.Rect( new Rect( 0, 0, r.Width * 0.7f, r.Height * 0.4f ), 6 );
			painter.Rect( new Rect( 0, r.Height * 0.6f, r.Width, r.Height * 0.4f ), 6 );
		}, animateBounds: false );
		Sample( row, "Radial / calc radius", "Center at 50%, 50%; the radius stays 25px in both shapes.", ( painter, r ) =>
		{
			var half = Length.Percent( 50 );
			painter.Fill = Fill.RadialGradient( half, half, Length.Calc( "calc(50% + 25px)" ), half,
				[new( 0, Color.White ), new( 0.7f, lime ), new( 1, cyan )] );
			painter.Translate( r.Position );
			painter.Rect( new Rect( 0, 0, r.Width * 0.7f, r.Height * 0.4f ), 6 );
			painter.Rect( new Rect( 0, r.Height * 0.6f, r.Width, r.Height * 0.4f ), 6 );
		}, animateBounds: false );
		Sample( row, "Conic / percentage center", "Percentage coordinates keep the sweep centered in each shape.", ( painter, r ) =>
		{
			var half = Length.Percent( 50 );
			painter.Fill = Fill.ConicGradient( half, half, Length.Percent( 100 ), half,
				[new( 0, cyan ), new( 0.25f, pink ), new( 0.6f, lime ), new( 1, cyan )] );
			painter.Translate( r.Position );
			painter.Rect( new Rect( 0, 0, r.Width * 0.7f, r.Height * 0.4f ), 6 );
			painter.Rect( new Rect( 0, r.Height * 0.6f, r.Width, r.Height * 0.4f ), 6 );
		}, animateBounds: false );
	}

	void PositionedGradients( Color cyan, Color lime, Color pink )
	{
		var row = Examples( "Drawing gradient coordinates" );
		var center = new Vector2( 60, 45 );
		var edge = center + new Vector2( 55, 0 );
		var radial = Fill.RadialGradient( center, edge, [new( 0, Color.White ), new( 0.5f, lime ), new( 1, cyan )] );
		Sample( row, "Pixel position and radius", "Center at (60, 45), edge at (115, 45): a 55px radius, independent of the rectangle.", ( painter, r ) =>
		{
			painter.Translate( r.Position );
			painter.Fill = radial;
			painter.Rect( new Rect( Vector2.Zero, r.Size ), 8 );
			painter.Stroke = Stroke.Solid( Color.Black.WithAlpha( 0.5f ), 1 );
			painter.Line( center, edge );
			painter.Stroke = Stroke.None;
			painter.Fill = Color.White;
			painter.Circle( center, 2 );
			painter.Circle( edge, 2 );
		}, animateBounds: false );
		var shared = Fill.RadialGradient( Vector2.Zero, new Vector2( 70, 0 ),
			[new( 0, pink ), new( 0.5f, pink ), new( 0.5f, cyan ), new( 1, lime )] );
		Sample( row, "Shared across shapes", "One gradient spans two shapes. The hard color transition stays 35px from the center.", ( painter, r ) =>
		{
			painter.Translate( r.Center );
			painter.Fill = shared;
			painter.Rect( new Rect( -r.Width * 0.5f, -r.Height * 0.5f, r.Width * 0.5f - 4, r.Height ), 6 );
			painter.Rect( new Rect( 4, -r.Height * 0.5f, r.Width * 0.5f - 4, r.Height ), 6 );
		}, animateBounds: false );
		var transformed = Fill.RadialGradient( new Vector2( -20, 0 ), new Vector2( 30, 0 ), Color.White, cyan );
		Sample( row, "Transformed gradient", "painter.Transform rotates and scales the gradient together with the shape.", ( painter, r ) =>
		{
			painter.Translate( r.Center );
			painter.Rotate( -20 );
			painter.Scale( 1, 0.7f );
			painter.Fill = transformed;
			painter.Rect( new Rect( -75, -45, 150, 90 ), 8 );
		}, animateBounds: false );
	}

	void TextMeasurementSamples( Color cyan, Color lime )
	{
		var row = Examples( "Drawing text measurement" );
		Sample( row, "Measured badge", "The background fits the measured text, with 8px padding on each side.", ( painter, r ) =>
		{
			painter.TextStyle = new TextStyle { FontSize = 18, FontWeight = 700, Alignment = TextFlag.Center };
			const string text = "Measured badge";
			var size = painter.MeasureText( text );
			var bounds = new Rect( r.Center - size * 0.5f, size );
			painter.Fill = cyan;
			painter.Rect( bounds.Grow( 8 ), 6 );
			painter.Text( text, bounds );
		}, animateBounds: false );
		Sample( row, "Aligned text bounds", "The lime outline is the measured rectangle, including wrapping and italic overhang.", ( painter, r ) =>
		{
			painter.TextStyle = new TextStyle { FontSize = 16, Italic = true, Alignment = TextFlag.RightCenter, Color = lime };
			const string text = "Measured text wraps inside these bounds.";
			var bounds = painter.MeasureText( text, r );
			painter.Stroke = Stroke.Solid( lime.WithAlpha( 0.5f ), 1 );
			painter.Rect( bounds );
			painter.Text( text, r );
		}, animateBounds: false );
	}

	void TextStyleHelpers( Color cyan, Color lime, Color pink )
	{
		var row = Examples( "Drawing text style helpers" );
		var heading = TextStyle.Default.WithFont( "Roboto", 26 ).WithBold().WithAlignment( TextFlag.Center );
		Sample( row, "Fluent typography", "A shared heading style, with size, color, italic and spacing helpers.", ( painter, r ) =>
		{
			painter.TextStyle = heading.WithColor( cyan );
			painter.Text( "Heading", new Rect( r.Left, r.Top, r.Width, 45 ) );
			painter.TextStyle = heading.WithSize( 16 ).WithBold( false ).WithItalic().WithLetterSpacing( 1 ).WithColor( lime );
			painter.Text( "Small details", new Rect( r.Left, r.Top + 50, r.Width, 45 ) );
		}, animateBounds: false );
		Sample( row, "Shadow and outline", "WithShadow and WithOutline combine. MeasureText includes both effects.", ( painter, r ) =>
		{
			painter.Fill = cyan.WithAlpha( 0.3f );
			painter.Rect( r, 8 );
			painter.TextStyle = heading.WithColor( pink ).WithOutline( Color.Black, 2 ).WithShadow( Color.Black, 3, 4, blur: 4 );
			var bounds = painter.MeasureText( "Effects", r );
			painter.Stroke = Stroke.Solid( Color.White.WithAlpha( 0.2f ), 1 );
			painter.Fill = Fill.None;
			painter.Rect( bounds );
			painter.Text( "Effects", r );
		}, animateBounds: false );
		Sample( row, "Crisp shadow / scoped style", "Zero blur keeps a sharp offset shadow. Scope restores the heading style after the second line.", ( painter, r ) =>
		{
			painter.TextStyle = heading.WithSize( 22 ).WithColor( Color.White ).WithShadow( cyan, 3, 3, blur: 0 );
			painter.Text( "Sharp shadow", new Rect( r.Left, r.Top, r.Width, 45 ) );
			using ( painter.Scope() )
			{
				painter.TextStyle = painter.TextStyle.WithoutShadow().WithColor( lime ).WithSize( 16 ).WithBold( false );
				painter.Text( "Without shadow", new Rect( r.Left, r.Top + 50, r.Width, 45 ) );
			}
		}, animateBounds: false );
	}

	void TextStyleSamples( Color cyan, Color lime, Color pink )
	{
		var row = Examples( "Drawing text styles" );
		Sample( row, "Scoped typography", "Scope restores the cyan text style after the lime line.", ( painter, r ) =>
		{
			painter.TextStyle = new TextStyle { FontSize = 20, FontWeight = 700, Color = cyan, Alignment = TextFlag.Center };
			painter.Text( "painter.TextStyle", new Rect( r.Left, r.Top, r.Width, 30 ) );
			using ( painter.Scope() )
			{
				painter.TextStyle = painter.TextStyle with { FontSize = 18, FontWeight = 400, Italic = true, Color = lime, LetterSpacing = 1 };
				painter.Text( "Scoped style", new Rect( r.Left, r.Top + 35, r.Width, 30 ) );
			}
			painter.Text( "Restored", new Rect( r.Left, r.Top + 70, r.Width, 30 ) );
		}, animateBounds: false );
		Sample( row, "Font and layout", "Monospaced text, extra line height and right alignment, using the same Text call.", ( painter, r ) =>
		{
			painter.TextStyle = new TextStyle { FontName = "Roboto Mono", FontSize = 15, Color = pink, LineHeight = 1.4f, Alignment = TextFlag.RightCenter };
			painter.Text( "ALT  1048\nVEL   240\nFUEL   87", r );
		}, animateBounds: false );
	}

	void OpacitySamples( Texture checker, Color cyan, Color lime )
	{
		var row = Examples( "Drawing opacity" );
		foreach ( var opacity in new[] { 1f, 0.5f, 0.2f } )
		{
			Sample( row, $"Opacity {opacity:0.0}", "The image, fill, stroke, text and shadow share one opacity setting.", ( painter, r ) =>
			{
				using var scope = painter.Scope();
				painter.Opacity = opacity;
				painter.RectShadow( r, 8, color: Color.Black, blur: 8, offset: new Vector2( 4, 6 ) );
				painter.Texture( checker, r );
				painter.Fill = cyan;
				painter.Stroke = Stroke.Solid( lime, 4 );
				painter.Rect( r.Shrink( 15 ), 8 );
				painter.TextStyle = new TextStyle { FontSize = 20, Color = Color.White, Alignment = TextFlag.Center };
				painter.Text( "Opacity", r );
			}, animateBounds: false );
		}
	}

	void ClippedShapes( Texture checker, Color cyan, Color lime )
	{
		var row = Examples( "Drawing clips" );
		Sample( row, "Fixed rounded clip", "The rounded clip stays fixed while its image, text and shadow rotate.", ( painter, r ) =>
		{
			using var scope = painter.Scope();
			painter.Clip( r, 20 );
			painter.Translate( r.Center );
			painter.Rotate( 25 + Wave( 30 ) );
			var box = new Rect( -100, -40, 200, 80 );
			painter.RectShadow( box, color: Color.Black, blur: 8, offset: new Vector2( 5, 8 ) );
			painter.Texture( checker, box );
			painter.TextStyle = new TextStyle { FontSize = 18, Color = Color.White, Alignment = TextFlag.Center };
			painter.Text( "Clipped drawing", box );
		}, animateBounds: false );
		Sample( row, "Nested transformed clips", "Intersect a rounded rectangle with a rotated rectangle. The cyan dot is drawn after Scope restores the clip.", ( painter, r ) =>
		{
			using ( painter.Scope() )
			{
				painter.Clip( r, 18 );
				painter.Translate( r.Center );
				painter.Rotate( 30 + Wave( 20 ) );
				painter.Clip( new Rect( -65, -65, 130, 130 ), 12 );
				painter.Transform = Matrix.Identity;
				painter.Fill = lime;
				painter.Rect( r.Grow( 80 ) );
			}
			painter.Fill = cyan;
			painter.Circle( r.TopLeft, 9 );
		}, animateBounds: false );
		PerspectiveClipExamples();
	}

	void TransformedShapes( Texture checker, Color cyan, Color lime, Color pink )
	{
		var row = Examples( "Drawing transforms / local coordinates" );
		var gradient = Fill.LinearGradient( cyan, lime );
		Sample( row, "Rotate a drawing", "Shape, stroke, text and shadow rotate together around a translated origin.", ( painter, r ) =>
		{
			using var scope = painter.Scope();
			painter.Translate( r.Center );
			painter.Rotate( 20 + Wave( 25 ) );
			var box = new Rect( -55, -25, 110, 50 );
			painter.RectShadow( box, 8, color: Color.Black.WithAlpha( 0.8f ), blur: 8, offset: new Vector2( 4, 6 ) );
			painter.Fill = gradient;
			painter.Stroke = Stroke.Dashed( Color.White, 2, 6, 3 );
			painter.Rect( box, 8 );
			painter.TextStyle = new TextStyle { FontSize = 14, Color = Color.White, Alignment = TextFlag.Center };
			painter.Text( "LOCAL", box );
		}, animateBounds: false, previewSize: new Vector2( 180 ) );
		Sample( row, "Nested transforms", "The pink arm rotates independently; leaving its scope restores the lime drawing axes.", ( painter, r ) =>
		{
			using var scope = painter.Scope();
			painter.Translate( r.Center );
			painter.Rotate( -15 + Wave( 15 ) );
			painter.Fill = cyan;
			painter.Rect( new Rect( -65, -10, 130, 20 ), 6 );
			using ( painter.Scope() )
			{
				painter.Translate( 45, 0 );
				painter.Rotate( 35 + Wave( 40 ) );
				painter.Scale( 0.8f );
				painter.Fill = pink;
				painter.Rect( new Rect( -8, -8, 55, 16 ), 4 );
			}
			painter.Fill = lime;
			painter.Circle( new Vector2( -45, 0 ), 14 );
		}, animateBounds: false, previewSize: new Vector2( 180 ) );
		Sample( row, "Scale an image", "Nonuniform scale applies equally to the checkerboard, outline and inset shadow.", ( painter, r ) =>
		{
			using var scope = painter.Scope();
			painter.Translate( r.Center );
			painter.Rotate( -15 );
			painter.Scale( 1.2f + Wave( 0.2f ), 0.8f + Wave( 0.2f, 1, 1 ) );
			var box = new Rect( -50, -30, 100, 60 );
			painter.Texture( checker, box );
			painter.RectShadow( box, color: Color.Black.WithAlpha( 0.8f ), blur: 8, inset: true );
			painter.Stroke = Stroke.Solid( lime, 3 );
			painter.Outline( box );
		}, animateBounds: false, previewSize: new Vector2( 180 ) );
		Sample( row, "Mirrored geometry", "Negative X scale mirrors the pink arrow. Both use the same local vertices.", ( painter, r ) =>
		{
			using var scope = painter.Scope();
			painter.Translate( r.Center );
			painter.Fill = cyan;
			painter.Translate( 0, -20 );
			painter.Arrow( new Vector2( -60, 0 ), new Vector2( 60, 0 ), 8, 24 );
			painter.Translate( 0, 40 );
			painter.Scale( -1, 1 );
			painter.Fill = pink;
			painter.Arrow( new Vector2( -60, 0 ), new Vector2( 60, 0 ), 8, 24 );
		}, animateBounds: false, previewSize: new Vector2( 180 ) );
	}

	DrawSample Sample( Panel row, string title, string note, PaintSample draw, bool animateBounds = true, Vector2? previewSize = null )
	{
		var example = new GalleryExample( row, title, note );
		if ( previewSize.HasValue ) ((GalleryScaledPreview)example.Result).PreviewSize = previewSize.Value;
		var sample = new DrawSample( draw, () => _animationTime, animateBounds );
		sample.AddClass( "draw-sample" );
		example.Result.AddChild( sample );
		return sample;
	}

	/// <summary>
	/// Draws a sample with synchronized geometry motion.
	/// </summary>
	delegate void PaintSample( Painter painter, Rect bounds );

	class DrawSample( PaintSample draw, Func<float> getTime, bool animateBounds ) : AnimatedDrawPanel( getTime )
	{
		/// <summary>
		/// Draws the sample within its layout bounds.
		/// </summary>
		protected override void DrawFrame( Painter painter, float time )
		{
			// The callback uses local coordinates; Box still reports layout coordinates.
			var r = Box.RectInner.Shrink( 24 * ScaleToScreen );
			r.Position -= Box.Rect.Position;
			if ( animateBounds )
			{
				// Translate without resizing text layout bounds.
				r.Position += new Vector2( MathF.Sin( time ), MathF.Sin( time * 0.8f ) ) * (8 * ScaleToScreen);
			}
			draw( painter, r );
		}
	}
}
