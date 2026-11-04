namespace Sandbox.PanelGallery;

/// <summary>
/// Drawing tests for borders, saved state and compositing.
/// </summary>
public class PainterCompositionPage : GalleryPage
{
	readonly Texture mask;
	readonly Texture checker;

	public PainterCompositionPage( string feature ) : base( feature, "Inspect the effect on rectangles, overlapping shapes and translucent edges." )
	{
		checker = DisplayPanelsPage.Checkerboard();
		const int size = 64;
		var pixels = new byte[size * size * 4];
		for ( var y = 0; y < size; y++ )
			for ( var x = 0; x < size; x++ )
			{
				var index = (y * size + x) * 4;
				pixels[index] = pixels[index + 1] = pixels[index + 2] = 255;
				pixels[index + 3] = (byte)(255 * Math.Clamp( 1 - new Vector2( x - 31.5f, y - 31.5f ).Length / 30, 0, 1 ));
			}
		mask = Texture.Create( size, size ).WithData( pixels ).Finish();
		var examples = Examples( feature );
		PaintComposition draw = feature switch
		{
			"Borders" => ( painter, rect, a, page ) =>
			{
				painter.Rect( rect, new Vector4( 4, 12, 20, 8 ) * a, GalleryPalette.Pink, GalleryPalette.Cyan, GalleryPalette.Lime, Color.White, 20 );
				painter.Stroke = Stroke.Solid( Fill.Image( page.checker ), 8 ).WithAlignment( Stroke.StrokeAlignment.Outside );
				painter.Rect( new Rect( -75, 70, 150, 30 ), 4 );
			}
			,
			"Corner radii" => ( painter, rect, a, page ) =>
			{
				painter.Rect( rect, new Painter.CornerRadii( new Vector2( 70 * a, 15 ), new Vector2( 10, 60 * a ), new Vector2( 50 * a ), new Vector2( 0 ) ) );
			}
			,
			"Layers" => ( painter, rect, a, page ) =>
			{
				using ( painter.BeginLayer( rect, opacity: a ) )
				{
					painter.Fill = GalleryPalette.Cyan;
					painter.Circle( new Vector2( -25, 0 ), 50 );
					painter.Fill = GalleryPalette.Lime;
					painter.Circle( new Vector2( 25, 0 ), 50 );
				}
			}
			,
			"Layer masks" => ( painter, rect, a, page ) =>
			{
				using ( painter.BeginLayer( rect, opacity: a, mask: new Painter.Mask( page.mask, rect ) ) )
				{
					painter.Fill = GalleryPalette.Cyan;
					painter.Circle( new Vector2( -25, 0 ), 50 );
					painter.Fill = GalleryPalette.Lime;
					painter.Circle( new Vector2( 25, 0 ), 50 );
				}
			}
			,
			"Backdrop" => ( painter, rect, a, page ) =>
			{
				painter.Fill = Fill.Image( page.checker );
				painter.Rect( rect );
				painter.TextStyle = new TextStyle { FontSize = 22, Color = GalleryPalette.Pink, Alignment = TextFlag.Center };
				painter.Text( "BACKDROP", rect );
				painter.FilterBackdrop( rect.Shrink( 20 ), new Painter.Filter { Blur = a * 12, Saturation = 1 - a }, new Painter.CornerRadii( 15 ) );
			}
			,
			_ => throw new ArgumentOutOfRangeException( nameof( feature ) )
		};
		var entry = new GalleryExample( examples, feature, "Compare overlapping shapes and translucent edges.", 260 );
		var preview = new Preview( this, draw );
		preview.Style.Width = Length.Percent( 100 );
		preview.Style.Height = Length.Percent( 100 );
		entry.Result.AddChild( preview );
	}

	public override void OnDeleted()
	{
		mask.Dispose();
		checker.Dispose();
		base.OnDeleted();
	}

	delegate void PaintComposition( Painter painter, Rect rect, float amount, PainterCompositionPage page );

	sealed class Preview( PainterCompositionPage page, PaintComposition draw ) : Panel
	{
		public override void OnDraw( Painter painter )
		{
			const float amount = 0.5f;
			painter.Translate( painter.Bounds.Center );
			var rect = new Rect( -75, -60, 150, 120 );
			painter.Fill = Fill.LinearGradient( GalleryPalette.Cyan, GalleryPalette.Pink );
			draw( painter, rect, amount, page );
		}
	}
}
