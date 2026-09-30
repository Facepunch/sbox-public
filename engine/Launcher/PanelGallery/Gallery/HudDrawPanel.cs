namespace Sandbox.PanelGallery;

/// <summary>
/// Animated HUD with a radar, gauges, a chart and progress indicators.
/// </summary>
internal sealed class HudDrawPanel : AnimatedDrawPanel
{
	static readonly Color Cyan = GalleryPalette.CyanHex;
	static readonly Color Teal = GalleryPalette.CyanHex;
	static readonly Color Amber = GalleryPalette.LimeHex;
	static readonly Color White = "#dfebef";
	static readonly Color Muted = GalleryPalette.CyanHex;
	static readonly Fill Background = Fill.LinearGradient( "#101f31", "#060e1b", 65 );
	static readonly Fill ScanBackground = Fill.LinearGradient( "#0a1928", "#102e38", 20 );
	static readonly Fill Scanner = Fill.LinearGradient( Cyan.WithAlpha( 0 ), Cyan.WithAlpha( 0.12f ) );
	static readonly Fill Glow = Fill.RadialGradient( Cyan.WithAlpha( 0.14f ), Cyan.WithAlpha( 0 ) );
	static readonly Fill GraphFill = Fill.LinearGradient( Teal.WithAlpha( 0.4f ), Teal.WithAlpha( 0 ), 90 );
	static readonly string[] Stages = ["START", "LOAD", "PROCESS", "FINISH"];
	readonly Vector2[] _graph = new Vector2[81];
	readonly Vector2[] _graphArea = new Vector2[83];

	public HudDrawPanel( Func<float> getTime ) : base( getTime )
	{
		AddClass( "draw-hud" );
	}

	protected override void DrawFrame( Painter painter, float time )
	{
		var bounds = Box.RectInner;
		bounds.Position -= Box.Rect.Position;
		float scale = MathF.Min( bounds.Width / 1000, bounds.Height / 560 );
		if ( scale <= 0 ) return;
		var origin = new Vector2( bounds.Center.x - 500 * scale, bounds.Top + 20 );
		Vector2 Point( float x, float y ) => origin + new Vector2( x, y ) * scale;
		Stroke ScaledStroke( Color color, float width = 1 ) => new( color, width * scale ) { Cap = Stroke.LineCap.Round };
		void Label( Painter painter, string text, float x, float y, float size, Color color, float width = 120 )
		{
			var extent = new Vector2( width, size * 1.6f ) * scale;
			painter.TextStyle = new TextStyle { FontSize = size * scale / ScaleToScreen, Color = color, Alignment = TextFlag.Center, FontName = "Roboto Mono" };
			painter.Text( text, new Rect( Point( x, y ) - extent * 0.5f, extent ) );
		}
		Vector2 Route( float t )
		{
			float v = 1 - t;
			return Point( 94, 346 ) * (v * v * v) + Point( 230, 345 ) * (3 * v * v * t)
				+ Point( 250, 158 ) * (3 * v * t * t) + Point( 490, 204 ) * (t * t * t);
		}

		float cycle = time % 24 / 24;
		int stage = (int)(cycle * 4);
		float pulse = 0.5f + 0.5f * MathF.Sin( time * 3 );
		painter.Fill = Background;
		painter.Rect( bounds );
		painter.Fill = Cyan;
		painter.Quad( Point( 26, 20 ), Point( 31, 20 ), Point( 31, 61 ), Point( 26, 61 ) );
		Label( painter, "PAINTER HUD", 225, 34, 25, White, 360 );
		Label( painter, "RADAR / GAUGES / PROGRESS", 206, 58, 11, Muted, 324 );
		Label( painter, "ANIMATED DRAWING EXAMPLE", 652, 31, 11, Muted, 300 );
		Label( painter, "SHAPES / GRADIENTS / TEXT", 661, 53, 11, White, 330 );
		int elapsed = (int)time % 3600;
		Label( painter, $"T+ {elapsed / 60:00}:{elapsed % 60:00}", 922, 31, 14, White, 110 );
		painter.Fill = Cyan.WithAlpha( 0.5f + pulse * 0.5f );
		painter.Circle( Point( 864, 53 ), 2.5f * scale );
		Label( painter, "ANIMATING", 924, 53, 10, Cyan, 100 );

		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.7f ) );
		painter.Fill = ScanBackground;
		painter.Polygon( [Point( 42, 82 ), Point( 600, 82 ), Point( 616, 98 ), Point( 616, 398 ),
			Point( 600, 414 ), Point( 26, 414 ), Point( 26, 98 )] );
		Label( painter, "01 / RADAR", 132, 101, 11, Cyan, 185 );
		Label( painter, "RANGE / 2.4 km", 493, 101, 10, Muted, 220 );
		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.4f ) );
		painter.Line( Point( 44, 114 ), Point( 598, 114 ) );
		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.14f ) );
		for ( int i = 0; i < 15; i++ )
		{
			float x = 48 + i * 38 + MathF.Sin( time * 0.13f ) * 5;
			painter.Line( Point( x, 120 ), Point( x, 378 ) );
		}
		for ( int i = 0; i < 8; i++ )
		{
			float y = 121 + i * 36 + MathF.Cos( time * 0.13f ) * 4;
			painter.Line( Point( 44, y ), Point( 598, y ) );
		}
		Span<Vector2> rock = stackalloc Vector2[7];
		for ( int i = 0; i < 13; i++ )
		{
			float x = 79 + i * 137 % 478 + MathF.Sin( time * 0.12f + i ) * 7;
			float y = 153 + i * 83 % 193 + MathF.Cos( time * 0.1f + i ) * 5;
			float radius = 10 + i * 7 % 20;
			for ( int j = 0; j < rock.Length; j++ )
				rock[j] = CirclePoint( Point( x, y ), radius * (0.72f + 0.28f * MathF.Sin( i * 4 + j * 7 )) * scale, j * 360f / 7 + i * 19 );
			painter.Stroke = ScaledStroke( Muted.WithAlpha( 0.19f ) );
			painter.Fill = Muted.WithAlpha( 0.045f );
			painter.Polygon( rock );
			painter.Stroke = ScaledStroke( Muted.WithAlpha( 0.08f ) );
			painter.Line( rock[0], rock[3] );
		}
		float scan = 44 + (0.5f - 0.5f * MathF.Cos( time * 0.45f )) * 520;
		painter.Stroke = Stroke.None;
		painter.Fill = Scanner;
		painter.Quad( Point( scan, 120 ), Point( scan + 32, 120 ), Point( scan + 32, 378 ), Point( scan, 378 ) );
		painter.Stroke = ScaledStroke( Cyan.WithAlpha( 0.25f ) );
		painter.Line( Point( scan + 32, 120 ), Point( scan + 32, 378 ) );
		painter.Stroke = ScaledStroke( Cyan.WithAlpha( 0.4f ), 1.5f ) with { Style = BorderStyle.Dashed, DashLength = 6 * scale, Gap = 7 * scale, Offset = -time * 18 * scale };
		painter.Bezier( Point( 94, 346 ), Point( 230, 345 ), Point( 250, 158 ), Point( 490, 204 ) );
		painter.Stroke = ScaledStroke( Cyan, 2 );
		painter.Line( [Point( 61, 331 ), Point( 61, 361 ), Point( 89, 361 )] );
		Label( painter, "ORIGIN", 108, 374, 10, Muted, 90 );
		for ( int drone = 0; drone < 2; drone++ )
		{
			float flight = (time / 13 + drone * 0.48f) % 1;
			float alpha = Math.Clamp( MathF.Min( flight, 1 - flight ) * 12, 0, 1 );
			for ( int trail = 1; trail <= 16; trail++ )
			{
				float t = MathF.Max( 0, flight - trail * 0.009f );
				painter.Stroke = ScaledStroke( Cyan.WithAlpha( alpha * (1 - trail / 17f) * 0.55f ), 2 );
				painter.Line( Route( t ), Route( MathF.Max( 0, t - 0.009f ) ) );
			}
			var ship = Route( flight );
			var direction = (Route( flight + 0.001f ) - ship).Normal;
			var side = new Vector2( -direction.y, direction.x );
			painter.Stroke = ScaledStroke( Cyan.WithAlpha( alpha ) );
			painter.Fill = White.WithAlpha( alpha );
			painter.Polygon( [ship + direction * 11 * scale, ship - direction * 7 * scale + side * 5 * scale,
				ship - direction * 4 * scale, ship - direction * 7 * scale - side * 5 * scale] );
		}
		var beacon = Point( 490, 204 );
		painter.Stroke = Stroke.None;
		painter.Fill = Glow;
		painter.Circle( beacon, 57 * scale );
		for ( int i = 0; i < 2; i++ )
		{
			float age = (time * 0.32f + i * 0.5f) % 1;
			painter.Stroke = ScaledStroke( Cyan.WithAlpha( (1 - age) * 0.3f ) );
			painter.Arc( beacon, (18 + age * 44) * scale, 0, 360 );
		}
		painter.Stroke = ScaledStroke( Cyan, 2 );
		painter.Fill = Cyan.WithAlpha( 0.12f );
		painter.Quad( Point( 501, 204 ), Point( 490, 215 ), Point( 479, 204 ), Point( 490, 193 ) );
		painter.Stroke = ScaledStroke( White );
		painter.Line( [Point( 472, 195 ), Point( 472, 186 ), Point( 481, 186 )] );
		painter.Line( [Point( 499, 222 ), Point( 508, 222 ), Point( 508, 213 )] );
		painter.Stroke = ScaledStroke( Cyan.WithAlpha( 0.5f ) );
		painter.Line( [Point( 508, 192 ), Point( 529, 165 ), Point( 592, 165 )] );
		Label( painter, "TARGET", 552, 150, 10, White, 90 );
		Label( painter, "TRACKING", 544, 238, 10, Cyan, 110 );
		for ( int i = 0; i < 3; i++ )
		{
			float x = 175 + i * 161;
			float y = 174 + i * 69;
			painter.Stroke = ScaledStroke( Amber.WithAlpha( 0.5f + 0.2f * MathF.Sin( time * 2 - i ) ) );
			painter.Fill = Fill.None;
			painter.Quad( Point( x + 4, y ), Point( x, y + 4 ), Point( x - 4, y ), Point( x, y - 4 ) );
			painter.Stroke = ScaledStroke( Amber.WithAlpha( 0.3f ) );
			painter.Line( Point( x + 8, y ), Point( x + 25, y ) );
		}
		painter.Stroke = ScaledStroke( Muted.WithAlpha( 0.6f ) );
		painter.Line( [Point( 514, 362 ), Point( 514, 368 ), Point( 586, 368 ), Point( 586, 362 )] );
		Label( painter, "1 km", 550, 354, 9, Muted, 60 );
		Label( painter, "03 CONTACTS", 181, 397, 10, Amber, 280 );
		Label( painter, "ANIMATED BEZIER PATH", 483, 397, 10, Muted, 235 );

		painter.Stroke = ScaledStroke( Teal );
		painter.Line( Point( 646, 82 ), Point( 974, 82 ) );
		Label( painter, "02 / GAUGES", 756, 101, 11, Cyan, 220 );
		var reactor = Point( 742, 210 );
		float load = 64 + 9 * MathF.Sin( time * 0.7f ) + 3 * MathF.Sin( time * 1.6f );
		painter.Stroke = Stroke.None;
		painter.Fill = Glow;
		painter.Circle( reactor, 89 * scale );
		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.65f ) );
		painter.Fill = Fill.None;
		float hexWidth = 43 * MathF.Sqrt( 3 );
		painter.Polygon( [Point( 742 + hexWidth, 253 ), Point( 742, 296 ), Point( 742 - hexWidth, 253 ),
			Point( 742 - hexWidth, 167 ), Point( 742, 124 ), Point( 742 + hexWidth, 167 )] );
		for ( int i = 0; i < 8; i++ )
		{
			float angle = -110 + i * 45;
			painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.24f ), 5 );
			painter.Arc( reactor, 72 * scale, angle, 32 );
			painter.Stroke = ScaledStroke( i >= 6 ? Amber : Cyan, 5 );
			painter.Arc( reactor, 72 * scale, angle, 32 * Math.Clamp( load / 12.5f - i, 0, 1 ) );
			float spin = i * 45 + time * 16;
			painter.Stroke = ScaledStroke( Cyan.WithAlpha( 0.45f ), 1.5f );
			painter.Line( CirclePoint( reactor, 54 * scale, spin ), CirclePoint( reactor, 62 * scale, spin ) );
		}
		Label( painter, "LOAD", 742, 186, 9, Muted, 104 );
		Label( painter, $"{load:0}%", 742, 212, 29, White, 104 );
		Label( painter, "STABLE", 742, 240, 10, Cyan, 104 );
		Label( painter, "FORCE", 910, 155, 10, Muted, 128 );
		Label( painter, $"{18 + 2 * MathF.Sin( time * 0.7f ):0.0} kN", 910, 178, 19, White, 128 );
		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.45f ) );
		painter.Line( Point( 852, 200 ), Point( 968, 200 ) );
		Label( painter, "THERMAL MARGIN", 910, 220, 10, Muted, 128 );
		Label( painter, "12% RESERVE", 910, 242, 13, Amber, 128 );
		Label( painter, "LIMIT / 24 kN", 910, 269, 10, Muted, 128 );
		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.6f ) );
		painter.Line( Point( 646, 300 ), Point( 974, 300 ) );
		Label( painter, "POWER / kW", 720, 317, 10, Cyan, 148 );
		Label( painter, "240 NOMINAL", 918, 317, 10, Muted, 110 );
		for ( int i = 0; i < 3; i++ )
		{
			painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.22f ) );
			painter.Line( Point( 664, 339 + i * 21 ), Point( 958, 339 + i * 21 ) );
		}
		for ( int i = 0; i < _graph.Length; i++ )
		{
			float t = i / (float)(_graph.Length - 1);
			float wave = MathF.Sin( t * 22 - time * 2.8f ) * 9 + MathF.Sin( t * 49 - time * 4 ) * 3;
			_graphArea[i] = _graph[i] = Point( 664 + t * 294, 356 - wave );
		}
		_graphArea[_graph.Length] = Point( 958, 381 );
		_graphArea[_graph.Length + 1] = Point( 664, 381 );
		painter.Stroke = Stroke.None;
		painter.Fill = GraphFill;
		painter.Polygon( _graphArea );
		painter.Stroke = ScaledStroke( Cyan, 1.5f );
		painter.Line( _graph );
		painter.Stroke = Stroke.None;
		painter.Fill = White;
		painter.Circle( _graph[^1], 3 * scale );
		Label( painter, "-12 s", 687, 398, 10, Muted, 46 );
		Label( painter, "SCROLLING LINE CHART", 863, 398, 10, Muted, 190 );

		painter.Stroke = ScaledStroke( Teal );
		painter.Line( Point( 26, 438 ), Point( 974, 438 ) );
		Label( painter, "03 / PROGRESS", 145, 455, 11, Cyan, 238 );
		Label( painter, "24 s LOOP", 461, 455, 10, Muted, 120 );
		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.5f ), 2 );
		painter.Line( Point( 66, 489 ), Point( 456, 489 ) );
		painter.Stroke = ScaledStroke( Cyan, 2 );
		painter.Line( Point( 66, 489 ), Point( 66 + MathF.Min( cycle * 4 / 3, 1 ) * 390, 489 ) );
		for ( int i = 0; i < 4; i++ )
		{
			float x = 66 + i * 130;
			painter.Stroke = ScaledStroke( i <= stage ? Cyan : Teal, 2 );
			painter.Fill = Background;
			painter.Quad( Point( x + 7, 489 ), Point( x, 496 ), Point( x - 7, 489 ), Point( x, 482 ) );
			if ( i <= stage )
			{
				painter.Stroke = Stroke.None;
				painter.Fill = Cyan;
				painter.Circle( Point( x, 489 ), 2 * scale );
			}
			if ( i == stage )
			{
				painter.Stroke = ScaledStroke( Cyan.WithAlpha( 0.5f - pulse * 0.3f ) );
				painter.Arc( Point( x, 489 ), (11 + pulse * 5) * scale, 0, 360 );
			}
			Label( painter, Stages[i], x, 514, 10, White.WithAlpha( i <= stage ? 1 : 0.45f ), 80 );
		}
		painter.Stroke = ScaledStroke( Teal.WithAlpha( 0.5f ) );
		painter.Line( Point( 550, 451 ), Point( 550, 527 ) );
		int cargo = (3 + (int)(time / 24)) % 9;
		float loading = Math.Clamp( (cycle - 0.75f) * 4, 0, 1 );
		Label( painter, "SEGMENTED PROGRESS", 720, 455, 10, Cyan, 250 );
		Label( painter, $"{cargo:00} / 08", 935, 455, 10, White, 78 );
		for ( int i = 0; i < 8; i++ )
		{
			float x = 600 + i * 46;
			painter.Stroke = ScaledStroke( Teal );
			painter.Line( [Point( x, 478 ), Point( x, 516 ), Point( x + 36, 516 ), Point( x + 36, 478 )] );
			float fill = i < cargo ? 1 : i == cargo ? loading : 0;
			if ( fill <= 0 ) continue;
			float y = 480 + (1 - fill) * 11;
			painter.Stroke = ScaledStroke( Cyan.WithAlpha( fill ) );
			painter.Fill = Cyan.WithAlpha( fill * 0.14f );
			painter.Quad( Point( x + 4, y ), Point( x + 32, y ), Point( x + 32, 512 ), Point( x + 4, 512 ) );
			painter.Stroke = ScaledStroke( Cyan.WithAlpha( fill * 0.5f ) );
			painter.Line( Point( x + 8, y + 4 ), Point( x + 28, 508 ) );
			painter.Line( Point( x + 28, y + 4 ), Point( x + 8, 508 ) );
		}
		Label( painter, cargo == 8 ? "COMPLETE" : stage == 3 ? "LOADING" : "WAITING", 780, 531, 9, Muted, 270 );
	}

	static Vector2 CirclePoint( Vector2 center, float radius, float degrees )
	{
		float radians = degrees * MathF.PI / 180;
		return center + new Vector2( MathF.Cos( radians ), MathF.Sin( radians ) ) * radius;
	}
}
