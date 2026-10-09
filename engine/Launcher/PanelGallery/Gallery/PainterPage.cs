namespace Sandbox.PanelGallery;

/// <summary>
/// The same drawing code used by a panel, a texture and a bitmap.
/// </summary>
public class PainterPage : GalleryPage
{
	public PainterPage() : base( "Destinations", "One drawing function, three destinations. The bitmap uses GPU painting and synchronous readback. All three should match, including translucent edges." )
	{
		var row = Case( "Shared paint session", column: true );
		foreach ( var target in Enum.GetValues<Target>() )
		{
			var title = target switch { Target.Panel => "Panel callback", Target.Texture => "Painter.Begin( texture )", _ => "Texture.GetBitmap()" };
			var card = row.Add.Panel( "destination-card" );
			card.Add.Label( title, "reference-title" );
			var preview = new TargetPreview( target );
			preview.Style.Height = 180;
			card.AddChild( preview );
		}
		Case( "Manual root rendering", column: true ).AddChild<PlaybackOpacityExamples>();
	}

	enum Target { Panel, Texture, Bitmap }

	static void PaintSample( Painter painter )
	{
		painter.Fill = Fill.LinearGradient( new Vector2( 10, 0 ), new Vector2( 300, 0 ), GalleryPalette.Cyan, GalleryPalette.Pink );
		painter.Stroke = Stroke.Dotted( Color.White, 3, 5 );
		painter.Rect( new Rect( 16, 16, 300, 120 ), 16 );
		using ( painter.Scope() )
		{
			painter.Clip( new Rect( 350, 16, 200, 120 ), 20 );
			painter.Fill = GalleryPalette.Pink.WithAlpha( 0.6f );
			painter.Stroke = Stroke.None;
			painter.Circle( new Vector2( 400, 76 ), 65 );
			painter.Fill = GalleryPalette.Cyan.WithAlpha( 0.6f );
			painter.Circle( new Vector2( 490, 76 ), 65 );
		}
		painter.TextStyle = TextStyle.Default.WithSize( 22 ).WithBold().WithShadow( Color.Black, blur: 2 ).WithAlignment( TextFlag.Center );
		painter.Text( "Painter / shader rendering", new Rect( 20, 42, 290, 60 ) );
	}

	sealed class TargetPreview : Panel
	{
		readonly Target target;
		Texture texture;
		string error;

		public TargetPreview( Target target )
		{
			this.target = target;
			Style.Width = Length.Percent( 100 );
			Style.Height = Length.Percent( 100 );
			Style.BackgroundColor = "#263044";
			Prepare();
		}

		void Prepare()
		{
			try
			{
				if ( target == Target.Texture )
				{
					texture = Texture.CreateRenderTarget().WithSize( 580, 152 ).WithFormat( ImageFormat.RGBA8888 ).Create();
					using ( var painter = Painter.Begin( texture ) )
					{
						painter.Clear( Color.Transparent );
						PaintSample( painter );
					}
				}
				else if ( target == Target.Bitmap )
				{
					using var target = Texture.CreateRenderTarget().WithSize( 580, 152 ).WithFormat( ImageFormat.RGBA8888 ).Create();
					using ( var painter = Painter.Begin( target ) )
					{
						painter.Clear( Color.Transparent );
						PaintSample( painter );
					}
					using var bitmap = target.GetBitmap();
					texture = bitmap.ToTexture( mips: false );
				}
			}
			catch ( Exception e )
			{
				error = e.Message;
				System.Console.Error.WriteLine( e );
			}
		}

		public override void OnDraw( Painter painter )
		{
			if ( error is not null )
			{
				painter.TextStyle = new TextStyle { FontSize = 14, Color = GalleryPalette.Pink };
				painter.Text( error, painter.Bounds );
			}
			else
			{
				float scale = MathF.Min( painter.Bounds.Width / 580, painter.Bounds.Height / 152 );
				painter.Translate( painter.Bounds.Center - new Vector2( 580, 152 ) * scale * 0.5f );
				painter.Scale( scale );
				if ( target == Target.Panel ) PaintSample( painter );
				else if ( texture is not null ) painter.Texture( texture, new Rect( 0, 0, 580, 152 ) );
			}
		}

		public override void OnDeleted()
		{
			texture?.Dispose();
			base.OnDeleted();
		}
	}
}
