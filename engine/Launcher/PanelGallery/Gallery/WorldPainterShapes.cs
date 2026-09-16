namespace Sandbox.PanelGallery;

/// <summary>
/// Painter coverage on transparent world panels, driven by the scene's shared animation clock.
/// </summary>
internal sealed class WorldPainterShapes( int example, Func<float> getTime ) : AnimatedDrawPanel( getTime )
{
	protected override void DrawFrame( Painter painter, float time )
	{
		var bounds = painter.Bounds;
		var scale = MathF.Min( bounds.Width / 250, bounds.Height / 140 );
		if ( scale <= 0 ) return;
		painter.Translate( bounds.Center );
		painter.Scale( scale );
		var rect = new Rect( -115, -60, 230, 120 );
		painter.Stroke = Stroke.None;
		painter.Fill = Fill.LinearGradient( Color.Cyan, Color.Magenta, time * 25 );

		switch ( example )
		{
			case 0:
				painter.Rect( new Rect( -110, -45, 65, 90 ), 12 );
				painter.Circle( new Vector2( 0, 0 ), 32 );
				painter.Star( new Vector2( 77, 0 ), 19, 20, 5, time * 30 );
				break;
			case 1:
				for ( int i = 0; i < 4; i++ )
				{
					painter.Stroke = Stroke.Solid( Color.Cyan, 0.5f + i );
					painter.Line( new Vector2( -110, -50 + i * 15 ), new Vector2( 110, -40 + i * 15 ) );
				}
				painter.Stroke = Stroke.Dashed( Color.Yellow, 3, 8, 5 ) with { Offset = time * 20 };
				painter.Bezier( new Vector2( -110, 40 ), new Vector2( -40, -35 ), new Vector2( 40, 100 ), new Vector2( 110, 25 ) );
				break;
			case 2:
				painter.Ring( new Vector2( -55, 0 ), 30, 43, time * 50, 270 );
				painter.Stroke = Stroke.Solid( Color.Yellow, 5 ) with { Cap = Stroke.LineCap.Round };
				painter.Arc( new Vector2( 55, 0 ), 40, -90, 180 + MathF.Sin( time ) * 150 );
				break;
			case 3:
				painter.Clip( rect, 24 );
				painter.Rotate( time * 25 );
				painter.Stroke = Stroke.Solid( Color.White, 2 );
				painter.Star( Vector2.Zero, 45, 65, 7 );
				break;
			case 4:
				painter.Fill = Color.Cyan.WithAlpha( 0.55f );
				painter.Circle( new Vector2( -30 + MathF.Sin( time ) * 15, 0 ), 48 );
				painter.Fill = Color.Magenta.WithAlpha( 0.55f );
				painter.Heart( new Vector2( 35, 0 ), 100 );
				break;
			case 5:
				painter.FilterBackdrop( rect, new Painter.Filter { Blur = 6 + MathF.Sin( time ) * 4, Saturation = 0.2f }, new Painter.CornerRadii( 20 ) );
				painter.Stroke = Stroke.Solid( Color.Cyan, 2 );
				painter.Ring( Vector2.Zero, 30, 42, time * 45, 300 );
				break;
		}
	}
}
