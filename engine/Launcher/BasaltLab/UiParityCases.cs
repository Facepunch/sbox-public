using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox.BasaltLab;

/// <summary>
/// The cards <see cref="UiParity"/> paints with the engine's <see cref="Painter"/>, one per thing the painter emits:
/// batches, transforms, clips, opacity, layers, filters, backdrops, blend modes, text, images and CSS boxes.
/// </summary>
internal static class UiParityCases
{
	internal delegate void Paint( Painter painter, Images images );

	/// <summary>A case: its name, what it covers, and how it paints.</summary>
	internal sealed record Case( string Name, string Covers, Paint Draw );

	/// <summary>Images the cases draw, made on Source 2 from known pixels.</summary>
	internal sealed class Images
	{
		/// <summary>8x8 RGBA checker, opaque.</summary>
		public Texture Checker { get; } = Make( 8, 8, ( x, y ) => ((x ^ y) & 2) != 0 ? new Color32( 255, (byte)(x * 32), 60 ) : new Color32( 40, (byte)(x * 32), 220 ) );

		/// <summary>32x32 white disc with an alpha falloff and a red-to-blue ramp, for masks and translucent images.</summary>
		public Texture Disc { get; } = Make( 32, 32, ( x, y ) =>
		{
			var d = MathF.Sqrt( (x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f) ) / 16;
			return new Color32( (byte)(255 - x * 8), 200, (byte)(x * 8), (byte)Math.Clamp( (int)((1 - d) * 400), 0, 255 ) );
		} );

		/// <summary>24x24 frame for nine slicing: 6 px corners and edges in different colours, a grey middle.</summary>
		public Texture Frame { get; } = Make( 24, 24, ( x, y ) =>
		{
			bool left = x < 6, right = x >= 18, top = y < 6, bottom = y >= 18;
			if ( (left || right) && (top || bottom) ) return new Color32( 250, 220, 40 );
			if ( left || right ) return new Color32( 40, 180, 250 );
			if ( top || bottom ) return new Color32( 240, 80, 120 );
			return new Color32( 90, 90, 100, 200 );
		} );

		static Texture Make( int width, int height, Func<int, int, Color32> pixel )
		{
			var bytes = new byte[width * height * 4];
			for ( int y = 0; y < height; y++ )
			{
				for ( int x = 0; x < width; x++ )
				{
					var c = pixel( x, y );
					var i = (y * width + x) * 4;
					bytes[i] = c.r;
					bytes[i + 1] = c.g;
					bytes[i + 2] = c.b;
					bytes[i + 3] = c.a;
				}
			}

			return Texture.Create( width, height ).WithData( bytes ).Finish();
		}
	}

	static readonly Color Background = new( 0.12f, 0.13f, 0.15f );

	public static readonly Case[] All =
	[
		new( "card", "one batch: rounded fills and borders, a gradient, shadows, an outline, a rounded clip, a star, a ring, an image, text", Card ),
		new( "batches", "several batches in one frame (blend-mode changes and a mid-frame clear flush them)", Batches ),
		new( "transforms", "rotate, scale and skew in the transform table; a perspective destination transform", Transforms ),
		new( "clips", "nested rounded clips, clips under transforms, a destination clip", Clips ),
		new( "opacity", "drawing opacity on fills, strokes, shadows, text and images", Opacity ),
		new( "layers", "offscreen layers composited back with opacity, nested layers, a layer under a rotation (TransformMat)", Layers ),
		new( "layer-filters", "a layer per colour filter: brightness, contrast, saturate, sepia, invert, hue-rotate, tint", LayerFilters ),
		new( "layer-blur", "blurred layers: mips generated with downsample_cs (gaussian border)", LayerBlur ),
		new( "layer-mask", "layer masks: alpha and luminance, repeat modes, rotation, point sampling", LayerMask ),
		new( "backdrops", "backdrop filters: frame grab, gaussian mips, blur and colour filters, rounded corners", Backdrops ),
		new( "blend-modes", "normal, multiply, lighten and premultiplied blending, on shapes and layers", BlendModes ),
		new( "text", "text at 10 to 40 px, bold and italic, a shadow, an outline, under a rotation", Text ),
		new( "images", "images: point and bilinear, tinted, translucent, tiled fills (repeat, repeat-x, repeat-y), offsets, rotation", Images_ ),
		new( "css-boxes", "CSS boxes: nine-slice border images (stretch, round, fill), per-side borders, a polygon shape", CssBoxes ),
		new( "panel-layer", "a panel layer: a target under a perspective destination (LayerMat), composited with drop shadows, a border wrap and a filter", PanelLayer ),
		new( "backdrop-lods", "backdrop blurs read between mips (trilinear at fractional levels: 1.1, 1.25, 1.73, 2.5, 2.9)", BackdropLods ),
	];

	static void Card( Painter painter, Images images )
	{
		painter.Clear( Background );

		painter.Fill = new Color( 0.9f, 0.3f, 0.2f );
		painter.Stroke = Stroke.Solid( Color.White, 3 );
		painter.Rect( new Rect( 12, 12, 90, 60 ), new Painter.CornerRadii( 14 ) );

		painter.RectShadow( new Rect( 120, 16, 80, 52 ), new Painter.CornerRadii( 8 ), Color.Black.WithAlpha( 0.8f ), blur: 10, offset: new Vector2( 3, 4 ) );
		painter.Stroke = default;
		painter.Fill = Fill.LinearGradient( new Color( 0.2f, 0.6f, 1 ), new Color( 0.9f, 0.2f, 0.8f ) );
		painter.Rect( new Rect( 120, 16, 80, 52 ), new Painter.CornerRadii( 8 ) );

		painter.Fill = new Color( 0.95f, 0.9f, 0.6f );
		painter.Rect( new Rect( 220, 14, 86, 56 ), new Painter.CornerRadii( 6 ) );
		painter.RectShadow( new Rect( 220, 14, 86, 56 ), new Painter.CornerRadii( 6 ), new Color( 0.5f, 0.2f, 0, 0.9f ), blur: 8, inset: true );
		painter.Outline( new Rect( 220, 14, 86, 56 ), 6, 3 );

		using ( painter.Scope() )
		{
			painter.Clip( new Rect( 12, 90, 120, 90 ), new Painter.CornerRadii( 20 ) );
			painter.Fill = new Color( 0.3f, 0.8f, 0.4f );
			painter.Circle( new Vector2( 30, 100 ), 50 );
			painter.Fill = new Color( 0.1f, 0.2f, 0.7f, 0.7f );
			painter.Rect( new Rect( 60, 120, 100, 80 ) );
		}

		using ( painter.Scope() )
		{
			painter.Translate( 190, 135 );
			painter.Rotate( 20 );
			painter.Fill = new Color( 1, 0.8f, 0.1f );
			painter.Stroke = Stroke.Solid( new Color( 0.2f, 0.1f, 0 ), 2 );
			painter.Star( Vector2.Zero, 18, 16 );
		}

		painter.Fill = new Color( 0.4f, 0.9f, 1 );
		painter.Stroke = default;
		painter.Ring( new Vector2( 268, 140 ), 14, 26 );

		painter.Texture( images.Checker, new Rect( 140, 90, 32, 32 ) );

		painter.TextStyle = painter.TextStyle with { Color = Color.White, FontSize = 18 };
		painter.Text( "Basalt.Gpu UI", new Rect( 150, 170, 170, 26 ) );
	}

	static void Batches( Painter painter, Images images )
	{
		painter.Clear( Background );
		for ( int i = 0; i < 6; i++ )
		{
			painter.BlendMode = i % 2 == 0 ? BlendMode.Normal : BlendMode.Lighten;
			painter.Fill = (Color)new ColorHsv( i * 55, 0.7f, 0.8f, 0.8f );
			painter.Rect( new Rect( 10 + i * 50, 10, 60, 60 ), new Painter.CornerRadii( 10 ) );
			painter.Fill = new Color( 0.2f, 0.2f, 0.3f, 0.6f );
			painter.Circle( new Vector2( 40 + i * 50, 100 ), 22 );
		}

		painter.BlendMode = BlendMode.Normal;
		painter.Texture( images.Checker, new Rect( 10, 130, 48, 48 ), filter: FilterMode.Point );
		painter.TextStyle = painter.TextStyle with { FontSize = 16 };
		painter.Text( "after six batches", new Rect( 70, 140, 240, 24 ) );

		// A clear flushes what's pending and lands between two batches
		painter.Clear( new Color( 0.05f, 0.05f, 0.1f ) );
		painter.Fill = new Color( 0.9f, 0.9f, 0.2f );
		painter.Rect( new Rect( 200, 150, 100, 40 ), new Painter.CornerRadii( 6 ) );
		painter.Text( "after a clear", new Rect( 60, 160, 200, 24 ) );
	}

	static void Transforms( Painter painter, Images images )
	{
		painter.Clear( Background );

		using ( painter.Scope() )
		{
			painter.Translate( 60, 60 );
			painter.Rotate( 30 );
			painter.Scale( 1.4f, 0.7f );
			painter.Fill = Fill.LinearGradient( new Color( 1, 0.5f, 0.1f ), new Color( 0.2f, 0.4f, 1 ) );
			painter.Stroke = Stroke.Solid( Color.White, 2 );
			painter.Rect( new Rect( -30, -25, 60, 50 ), new Painter.CornerRadii( 8 ) );
		}

		using ( painter.Scope() )
		{
			painter.Transform = Matrix.CreateSkewX( 25 ) * Matrix.CreateTranslation( new Vector3( 130, 20, 0 ) );
			painter.Texture( images.Checker, new Rect( 0, 0, 56, 56 ), filter: FilterMode.Point );
			painter.RectShadow( new Rect( 0, 70, 56, 30 ), 6, Color.Black, blur: 6, offset: new Vector2( 2, 2 ) );
			painter.Fill = new Color( 0.3f, 0.9f, 0.5f );
			painter.Rect( new Rect( 0, 70, 56, 30 ), 6 );
		}

		// A perspective destination, as a panel with a 3D CSS transform gets: the matrix lands in the transform table
		var centre = new Vector3( 250, 100, 0 );
		System.Numerics.Matrix4x4 perspective = System.Numerics.Matrix4x4.Identity;
		perspective.M34 = -1f / 300;
		Matrix tilt = System.Numerics.Matrix4x4.CreateTranslation( -centre ) * System.Numerics.Matrix4x4.CreateRotationY( 0.6f ) * System.Numerics.Matrix4x4.CreateRotationX( 0.3f ) * perspective * System.Numerics.Matrix4x4.CreateTranslation( centre );
		using ( var destination = painter.WithDestination( new Rect( 200, 40, 100, 120 ), 1, 1, BlendMode.Normal, tilt ) )
		{
			var p = destination.Painter;
			p.Fill = new Color( 0.8f, 0.2f, 0.6f );
			p.Stroke = Stroke.Solid( new Color( 1, 1, 0.6f ), 3 );
			p.Rect( new Rect( 200, 40, 100, 120 ), new Painter.CornerRadii( 12 ) );
			p.Stroke = default;
			p.TextStyle = p.TextStyle with { FontSize = 20 };
			p.Text( "3D", new Rect( 230, 80, 60, 30 ) );
			p.Texture( images.Disc, new Rect( 215, 115, 32, 32 ) );
		}
	}

	static void Clips( Painter painter, Images images )
	{
		painter.Clear( Background );

		using ( painter.Scope() )
		{
			painter.Clip( new Rect( 10, 10, 140, 120 ), new Painter.CornerRadii( 24 ) );
			painter.Fill = new Color( 0.2f, 0.5f, 0.9f );
			painter.Rect( new Rect( 0, 0, 200, 200 ) );
			using ( painter.Scope() )
			{
				painter.Clip( new Rect( 60, 40, 140, 140 ), new Painter.CornerRadii( new Vector2( 40, 20 ) ) );
				painter.Fill = new Color( 0.9f, 0.6f, 0.2f );
				painter.Rect( new Rect( 0, 0, 300, 200 ) );
				painter.Texture( images.Checker, new Rect( 70, 50, 64, 64 ), filter: FilterMode.Point );
			}
		}

		using ( painter.Scope() )
		{
			painter.Translate( 230, 70 );
			painter.Rotate( 35 );
			painter.Clip( new Rect( -40, -40, 80, 80 ), new Painter.CornerRadii( 12 ) );
			painter.Fill = Fill.RadialGradient( new Color( 1, 1, 0.5f ), new Color( 0.6f, 0.1f, 0.3f ) );
			painter.Rect( new Rect( -80, -80, 160, 160 ) );
		}

		// A destination clip, as a panel's overflow: hidden clips its children
		using ( painter.ClipDestination( new Rect( 170, 130, 140, 60 ), BorderRadii.FromCorners( new Vector4( 16, 16, 4, 4 ) ), Matrix.Identity ) )
		{
			painter.Fill = new Color( 0.4f, 0.9f, 0.6f );
			painter.Circle( new Vector2( 240, 160 ), 60 );
			painter.TextStyle = painter.TextStyle with { FontSize = 22, Color = Color.Black };
			painter.Text( "clipped text", new Rect( 160, 145, 200, 30 ) );
		}
	}

	static void Opacity( Painter painter, Images images )
	{
		painter.Clear( Background );
		for ( int i = 0; i < 5; i++ )
		{
			using ( painter.Scope() )
			{
				painter.Opacity = 0.2f + i * 0.2f;
				var x = 10 + i * 62;
				painter.Fill = new Color( 0.9f, 0.4f, 0.2f );
				painter.Stroke = Stroke.Solid( Color.White, 2 );
				painter.Rect( new Rect( x, 10, 50, 50 ), 8 );
				painter.Stroke = default;
				painter.RectShadow( new Rect( x, 75, 50, 30 ), 6, Color.Black, blur: 8, offset: new Vector2( 3, 3 ) );
				painter.Fill = new Color( 0.3f, 0.7f, 1 );
				painter.Rect( new Rect( x, 75, 50, 30 ), 6 );
				painter.Texture( images.Disc, new Rect( x + 9, 115, 32, 32 ) );
				painter.TextStyle = painter.TextStyle with { FontSize = 18 };
				painter.Text( $"{painter.Opacity:0.0}", new Rect( x + 6, 160, 50, 24 ) );
			}
		}
	}

	static void Layers( Painter painter, Images images )
	{
		painter.Clear( Background );
		painter.Fill = new Color( 0.3f, 0.3f, 0.35f );
		painter.Rect( new Rect( 0, 90, 320, 20 ) );

		// Overlapping shapes faded as one group: the overlap doesn't show through
		using ( painter.BeginLayer( new Rect( 10, 10, 140, 120 ), opacity: 0.6f ) )
		{
			painter.Fill = new Color( 1, 0.3f, 0.3f );
			painter.Circle( new Vector2( 60, 60 ), 45 );
			painter.Fill = new Color( 0.3f, 0.5f, 1 );
			painter.Circle( new Vector2( 100, 80 ), 45 );

			using ( painter.BeginLayer( new Rect( 30, 90, 100, 36 ), opacity: 0.5f ) )
			{
				painter.Fill = new Color( 1, 1, 0.3f );
				painter.Rect( new Rect( 30, 90, 100, 36 ), 8 );
				painter.TextStyle = painter.TextStyle with { FontSize = 18, Color = Color.Black };
				painter.Text( "nested", new Rect( 44, 96, 90, 24 ) );
			}
		}

		// A layer under a rotation: the composite's TransformMat isn't identity
		using ( painter.Scope() )
		{
			painter.Translate( 230, 100 );
			painter.Rotate( -25 );
			using ( painter.BeginLayer( new Rect( -60, -45, 120, 90 ), opacity: 0.85f ) )
			{
				painter.Fill = Fill.LinearGradient( new Color( 0.2f, 0.9f, 0.6f ), new Color( 0.1f, 0.2f, 0.6f ) );
				painter.Rect( new Rect( -60, -45, 120, 90 ), 14 );
				painter.Texture( images.Checker, new Rect( -20, -20, 40, 40 ), filter: FilterMode.Point );
			}
		}
	}

	static void LayerFilters( Painter painter, Images images )
	{
		painter.Clear( Background );
		Painter.Filter[] filters =
		[
			new() { Brightness = 1.6f },
			new() { Contrast = 0.4f },
			new() { Saturation = 0 },
			new() { Sepia = 1 },
			new() { Invert = 1 },
			new() { HueRotation = 120 },
			new() { Tint = new Color( 0.4f, 1, 0.6f, 0.8f ) },
			new() { Brightness = 0.7f, Contrast = 1.5f, Saturation = 2, HueRotation = -60 },
		];

		for ( int i = 0; i < filters.Length; i++ )
		{
			var r = new Rect( 8 + (i % 4) * 78, 8 + (i / 4) * 96, 70, 88 );
			using ( painter.BeginLayer( r, filter: filters[i] ) )
			{
				painter.Fill = Fill.LinearGradient( new Color( 1, 0.2f, 0.1f ), new Color( 0.1f, 0.4f, 1 ), 90 );
				painter.Rect( r, 8 );
				painter.Texture( images.Checker, new Rect( r.Left + 19, r.Top + 10, 32, 32 ), filter: FilterMode.Point );
				painter.Fill = new Color( 0.2f, 0.9f, 0.3f );
				painter.Circle( new Vector2( r.Left + 35, r.Top + 64 ), 16 );
			}
		}
	}

	static void LayerBlur( Painter painter, Images images )
	{
		painter.Clear( Background );
		float[] blurs = [1, 3, 8];
		for ( int i = 0; i < blurs.Length; i++ )
		{
			var r = new Rect( 20 + i * 100, 30, 80, 80 );
			using ( painter.BeginLayer( r, filter: new Painter.Filter { Blur = blurs[i] } ) )
			{
				painter.Fill = new Color( 0.95f, 0.85f, 0.3f );
				painter.Rect( r, 10 );
				painter.Texture( images.Checker, new Rect( r.Left + 16, r.Top + 16, 48, 48 ), filter: FilterMode.Point );
			}
		}

		using ( painter.BeginLayer( new Rect( 40, 130, 240, 50 ), filter: new Painter.Filter { Blur = 2, Tint = new Color( 1, 1, 1, 0.8f ) } ) )
		{
			painter.TextStyle = painter.TextStyle with { FontSize = 32 };
			painter.Text( "blurred text", new Rect( 50, 134, 230, 44 ) );
		}
	}

	static void LayerMask( Painter painter, Images images )
	{
		painter.Clear( Background );
		var masks = new Painter.Mask[]
		{
			new( images.Disc, new Rect( 10, 10, 90, 90 ) ),
			new( images.Disc, new Rect( 115, 10, 90, 90 ), MaskMode.Luminance ),
			new( images.Checker, new Rect( 220, 10, 24, 24 ), Repeat: BackgroundRepeat.Repeat, Sampling: FilterMode.Point ),
			new( images.Disc, new Rect( 30, 120, 40, 40 ), Repeat: BackgroundRepeat.RepeatX, Rotation: 20 ),
			new( images.Disc, new Rect( 150, 115, 60, 60 ), Repeat: BackgroundRepeat.Clamp ),
			new( images.Checker, new Rect( 225, 115, 80, 70 ), MaskMode.Luminance, Sampling: FilterMode.Bilinear ),
		};

		var bounds = new[] { new Rect( 10, 10, 90, 90 ), new Rect( 115, 10, 90, 90 ), new Rect( 220, 10, 90, 90 ), new Rect( 10, 110, 120, 80 ), new Rect( 140, 110, 80, 80 ), new Rect( 225, 110, 85, 80 ) };
		for ( int i = 0; i < masks.Length; i++ )
		{
			using ( painter.BeginLayer( bounds[i], mask: masks[i] ) )
			{
				painter.Fill = Fill.LinearGradient( new Color( 0.3f, 0.9f, 1 ), new Color( 0.9f, 0.3f, 0.6f ), 45 );
				painter.Rect( bounds[i] );
			}
		}
	}

	static void Backdrops( Painter painter, Images images )
	{
		painter.Clear( Background );

		// Something busy behind the backdrops
		for ( int i = 0; i < 8; i++ )
		{
			painter.Fill = (Color)new ColorHsv( i * 45, 0.8f, 0.9f );
			painter.Rect( new Rect( i * 40, 0, 20, 200 ) );
		}

		painter.Texture( images.Checker, new Rect( 0, 140, 320, 60 ), filter: FilterMode.Point );
		painter.TextStyle = painter.TextStyle with { FontSize = 28, Color = Color.Black };
		painter.Text( "BEHIND", new Rect( 100, 60, 200, 40 ) );

		painter.FilterBackdrop( new Rect( 10, 10, 90, 70 ), new Painter.Filter { Blur = 6 }, 12 );
		painter.FilterBackdrop( new Rect( 115, 10, 90, 70 ), new Painter.Filter { Saturation = 0, Brightness = 1.3f }, 4 );
		painter.FilterBackdrop( new Rect( 220, 10, 90, 70 ), new Painter.Filter { Invert = 1, HueRotation = 90, Tint = new Color( 1, 1, 1, 0.7f ) } );
		painter.FilterBackdrop( new Rect( 20, 100, 280, 80 ), new Painter.Filter { Blur = 2, Sepia = 0.8f, Contrast = 1.4f }, 24 );

		// Drawing after a backdrop lands on top of it
		painter.Fill = new Color( 1, 1, 1, 0.9f );
		painter.Rect( new Rect( 140, 130, 40, 20 ), 4 );
	}

	static void BlendModes( Painter painter, Images images )
	{
		painter.Clear( Background );
		painter.Fill = Fill.LinearGradient( new Color( 0.9f, 0.9f, 0.9f ), new Color( 0.1f, 0.5f, 0.9f ), 90 );
		painter.Rect( new Rect( 0, 0, 320, 200 ) );

		BlendMode[] modes = [BlendMode.Normal, BlendMode.Multiply, BlendMode.Lighten, BlendMode.PremultipliedAlpha];
		for ( int i = 0; i < modes.Length; i++ )
		{
			painter.BlendMode = modes[i];
			var x = 10 + i * 78;
			painter.Fill = new Color( 0.9f, 0.4f, 0.1f, 0.7f );
			painter.Rect( new Rect( x, 10, 64, 50 ), 8 );
			painter.Fill = Fill.RadialGradient( new Color( 0.2f, 1, 0.4f ), new Color( 0.2f, 0.2f, 0.8f, 0 ) );
			painter.Circle( new Vector2( x + 32, 95 ), 28 );
			painter.Texture( images.Disc, new Rect( x + 16, 130, 32, 32 ) );
		}

		painter.BlendMode = BlendMode.Normal;

		// A layer composited with a blend mode
		painter.BlendMode = BlendMode.Multiply;
		using ( painter.BeginLayer( new Rect( 100, 165, 120, 30 ) ) )
		{
			painter.Fill = new Color( 1, 0.5f, 0.5f );
			painter.Rect( new Rect( 100, 165, 120, 30 ), 6 );
		}

		painter.BlendMode = BlendMode.Lighten;
		using ( painter.BeginLayer( new Rect( 230, 165, 80, 30 ) ) )
		{
			painter.Fill = new Color( 0.3f, 0.3f, 0.6f );
			painter.Rect( new Rect( 230, 165, 80, 30 ), 6 );
		}
	}

	static void Text( Painter painter, Images images )
	{
		painter.Clear( Background );
		var y = 4f;
		foreach ( var size in (ReadOnlySpan<float>)[10, 13, 16, 24, 40] )
		{
			painter.TextStyle = TextStyle.Default with { FontSize = size, Color = new Color( 0.95f, 0.95f, 0.9f ) };
			painter.Text( $"Basalt {size}px", new Rect( 6, y, 200, size * 1.5f ) );
			y += size * 1.3f;
		}

		painter.TextStyle = TextStyle.Default with { FontSize = 20, FontWeight = 800, Color = new Color( 1, 0.8f, 0.3f ) };
		painter.Text( "Bold", new Rect( 200, 4, 120, 30 ) );
		painter.TextStyle = TextStyle.Default with { FontSize = 20, Italic = true, Color = new Color( 0.5f, 0.9f, 1 ) };
		painter.Text( "Italic", new Rect( 200, 32, 120, 30 ) );
		painter.TextStyle = TextStyle.Default with { FontSize = 22, Shadow = new TextRendering.Shadow { Enabled = true, Size = 4, Color = new Color( 0.9f, 0.2f, 0.3f ), Offset = new Vector2( 2, 3 ) } };
		painter.Text( "Shadow", new Rect( 200, 60, 120, 32 ) );
		painter.TextStyle = TextStyle.Default with { FontSize = 22, Color = Color.Black, Outline = new TextRendering.Outline { Enabled = true, Size = 2, Color = new Color( 1, 0.4f, 0.2f ) } };
		painter.Text( "Outline", new Rect( 200, 92, 120, 32 ) );

		using ( painter.Scope() )
		{
			painter.Translate( 230, 160 );
			painter.Rotate( -15 );
			painter.TextStyle = TextStyle.Default with { FontSize = 24, Color = new Color( 0.6f, 1, 0.6f ) };
			painter.Text( "Rotated", new Rect( -50, -16, 120, 32 ) );
		}
	}

	static void Images_( Painter painter, Images images )
	{
		painter.Clear( Background );
		painter.Texture( images.Checker, new Rect( 10, 10, 64, 64 ), filter: FilterMode.Point );
		painter.Texture( images.Checker, new Rect( 84, 10, 64, 64 ), filter: FilterMode.Bilinear );
		painter.Texture( images.Checker, new Rect( 158, 10, 64, 64 ), new Color( 0.5f, 1, 0.5f, 0.7f ), FilterMode.Point );
		painter.Texture( images.Disc, new Rect( 232, 10, 64, 64 ) );

		painter.Fill = Fill.Image( images.Checker, width: 16, height: 16, repeat: BackgroundRepeat.Repeat, filter: FilterMode.Point );
		painter.Rect( new Rect( 10, 84, 90, 50 ), 10 );
		painter.Fill = Fill.Image( images.Disc, width: 20, height: 20, offsetX: 5, repeat: BackgroundRepeat.RepeatX );
		painter.Rect( new Rect( 110, 84, 90, 50 ) );
		painter.Fill = Fill.Image( images.Disc, width: 24, height: 24, offsetY: 4, repeat: BackgroundRepeat.RepeatY );
		painter.Rect( new Rect( 210, 84, 100, 50 ) );
		painter.Fill = Fill.Image( images.Checker, width: 30, height: 30, offsetX: 10, offsetY: 10, repeat: BackgroundRepeat.NoRepeat, filter: FilterMode.Point );
		painter.Rect( new Rect( 10, 144, 90, 50 ), 6 );
		painter.Fill = Fill.Image( images.Checker, width: Length.Percent( 50 ), repeat: BackgroundRepeat.Repeat, filter: FilterMode.Point ).WithRotation( 30 );
		painter.Rect( new Rect( 110, 144, 90, 50 ) );
		painter.Fill = Fill.Image( images.Disc, tint: new Color( 1, 0.6f, 0.2f ) );
		painter.Circle( new Vector2( 255, 169 ), 25 );
	}

	static void CssBoxes( Painter painter, Images images )
	{
		painter.Clear( Background );
		var boxes = new (Rect Rect, BorderImageRepeat Repeat, BorderImageFill Fill)[]
		{
			(new Rect( 10, 10, 90, 70 ), BorderImageRepeat.Stretch, BorderImageFill.Filled),
			(new Rect( 115, 10, 90, 70 ), BorderImageRepeat.Round, BorderImageFill.Unfilled),
			(new Rect( 220, 10, 90, 70 ), BorderImageRepeat.Stretch, BorderImageFill.Unfilled),
		};

		foreach ( var (rect, repeat, fill) in boxes )
		{
			painter.Rect( new Painter.BoxDescriptor( rect, new Color( 0.2f, 0.3f, 0.5f ) )
			{
				Stroke = Painter.ResolveBoxStroke( new Vector4( 12 ), Color.White, Color.White, Color.White, Color.White, BorderStyle.Solid ),
				BorderImage = new NineSliceImage( images.Frame, new Vector4( 6 ), repeat, fill, Color.White ),
			} );
		}

		// Per-side borders with different widths and colours, rounded
		var sides = new Painter.BoxDescriptor( new Rect( 10, 100, 120, 80 ), new Color( 0.9f, 0.9f, 0.85f ) )
		{
			Stroke = Painter.ResolveBoxStroke( new Vector4( 2, 6, 10, 14 ), Color.Red, Color.Green, Color.Blue, Color.Yellow, BorderStyle.Solid ),
		};
		sides.Radii = BorderRadii.FromCorners( new Vector4( 4, 12, 20, 28 ) );
		painter.Rect( sides );

		// A polygon border shape (clip-path style) with an image background
		var shaped = new Painter.BoxDescriptor( new Rect( 150, 100, 150, 80 ), Color.White )
		{
			BackgroundImage = images.Checker,
			BackgroundRect = new Vector4( 0, 0, 150, 80 ),
			BackgroundTint = Color.White,
			BackgroundRepeat = BackgroundRepeat.Clamp,
			FilterMode = FilterMode.Point,
		};
		shaped.SetPolygon( [new Vector2( 75, 0 ), new Vector2( 150, 40 ), new Vector2( 110, 80 ), new Vector2( 40, 80 ), new Vector2( 0, 40 )] );
		painter.Rect( shaped );
	}

	static void PanelLayer( Painter painter, Images images )
	{
		painter.Clear( Background );

		// A panel with filters renders its subtree to a target under its destination transform (LayerMat), then
		// composites it with drop shadows and a border, as Panel.Render and Panel.DrawLayer do
		var centre = new Vector3( 160, 100, 0 );
		System.Numerics.Matrix4x4 perspective = System.Numerics.Matrix4x4.Identity;
		perspective.M34 = -1f / 400;
		Matrix tilt = System.Numerics.Matrix4x4.CreateTranslation( -centre ) * System.Numerics.Matrix4x4.CreateRotationY( -0.5f ) * perspective * System.Numerics.Matrix4x4.CreateTranslation( centre );

		var bounds = new Rect( 60, 30, 200, 140 );
		var layer = new Rect( 40, 10, 240, 180 );
		using ( var destination = painter.WithDestination( bounds, 1, 1, BlendMode.Normal, tilt ) )
		{
			var p = destination.Painter;
			using ( p.Target( "PanelLayer.Parity", layer, 1 ) )
			{
				p.Fill = Fill.LinearGradient( new Color( 0.95f, 0.5f, 0.2f ), new Color( 0.6f, 0.1f, 0.5f ), 135 );
				p.Rect( bounds, 18 );
				p.Texture( images.Checker, new Rect( 90, 60, 48, 48 ), filter: FilterMode.Point );
				p.TextStyle = p.TextStyle with { FontSize = 22 };
				p.Text( "panel layer", new Rect( 100, 120, 160, 30 ) );
			}

			ReadOnlySpan<Shadow> shadows = [new Shadow { OffsetX = 6, OffsetY = 8, Blur = 6, Color = Color.Black.WithAlpha( 0.7f ) }, new Shadow { OffsetX = -4, OffsetY = -2, Blur = 0, Color = new Color( 0.2f, 0.8f, 1, 0.6f ) }];
			p.Composite( new RenderTargetHandle { Name = "PanelLayer.Parity" }, layer, new Painter.Filter { Saturation = 1.4f, Contrast = 1.1f }, null, MaskScope.Default, shadows, 3, new Color( 1, 1, 0.6f ) );
		}
	}

	/// <summary>
	/// Backdrop blurs sample the grab's mips at sqrt( blur / 2 ). Levels between two mips blend them, which is where
	/// drivers can differ (AMD's application profile for Source 2 does).
	/// </summary>
	static void BackdropLods( Painter painter, Images images )
	{
		painter.Clear( Background );
		for ( int i = 0; i < 8; i++ )
		{
			painter.Fill = (Color)new ColorHsv( i * 45, 0.8f, 0.9f );
			painter.Rect( new Rect( i * 40, 0, 20, 200 ) );
		}

		painter.Texture( images.Checker, new Rect( 0, 100, 320, 100 ), filter: FilterMode.Point );
		float[] blurs = [3.125f, 6, 2.42f, 16.82f, 12.5f];
		for ( int i = 0; i < blurs.Length; i++ )
			painter.FilterBackdrop( new Rect( 4 + i * 63, 20, 56, 160 ), new Painter.Filter { Blur = blurs[i] } );
	}
}
