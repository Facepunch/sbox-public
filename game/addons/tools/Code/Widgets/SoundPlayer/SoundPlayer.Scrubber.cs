namespace Editor;

public partial class SoundPlayer
{
	public class Scrubber : GraphicsItem
	{
		private readonly TimelineView TimelineView;

		public Scrubber( TimelineView view )
		{
			TimelineView = view;
			ZIndex = -1;
			HoverEvents = true;
			Cursor = CursorShape.SizeH;
			Movable = true;
			Selectable = true;
			HandlePosition = new( 0.5f, 0f );
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			Paint.Antialiasing = false;
			Paint.ClearPen();
			Paint.SetBrush( Theme.Green.WithAlpha( 0.7f ) );

			var boxRect = new Rect( new Vector2( 0, (Theme.RowHeight / 2) + 1 ), new Vector2( LocalRect.Width, Theme.RowHeight / 2 ) );
			Paint.DrawPolygon( boxRect.TopLeft, boxRect.TopRight, boxRect.BottomRight - new Vector2( 0, 4 ), boxRect.Center.WithY( boxRect.Bottom ), boxRect.BottomLeft - new Vector2( 0, 4 ) );

			Paint.SetPen( Theme.Green.WithAlpha( 0.7f ) );
			Paint.DrawLine( new Vector2( 4, Theme.RowHeight + 1 ), new Vector2( 4, LocalRect.Bottom ) );

		}

		protected override void OnMousePressed( GraphicsMouseEvent e )
		{
			base.OnMousePressed( e );
			TimelineView.Scrubbing = true;
		}

		protected override void OnMouseReleased( GraphicsMouseEvent e )
		{
			base.OnMouseReleased( e );
			TimelineView.MoveScrubber( Position.x );
			TimelineView.Scrubbing = false;
		}

		protected override void OnMoved()
		{
			base.OnMoved();

			TimelineView.Scrubbing = true;
			TimelineView.MoveScrubber( Position.x + 0.5f, false );
		}
	}

}
