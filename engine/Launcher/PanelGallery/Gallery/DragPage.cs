namespace Sandbox.PanelGallery;

/// <summary>
/// Drag events - the card should stay glued to the cursor. If it flies off or lags, the drag
/// events are carrying the wrong window's mouse position.
/// </summary>
public class DragPage : GalleryPage
{
	public DragPage() : base( "Dragging", "ondrag panel events. Grab the card and move it - it should stay pinned under the cursor." )
	{
		var row = Case( "Drag the card" );

		var arena = row.Add.Panel( "drag-arena" );
		arena.AddChild( new DragCard() );
	}

	/// <summary>
	/// A panel that opts into dragging and follows the cursor with the drag events it gets.
	/// </summary>
	class DragCard : Panel
	{
		public override bool WantsDrag => true;

		public DragCard()
		{
			AddClass( "drag-card" );

			this.Add.Icon( "drag_indicator", "icon" );
			this.Add.Label( "Drag me" );
		}

		protected override void OnDrag( DragEvent e )
		{
			var local = (e.ScreenPosition - Parent.Box.Rect.Position - e.LocalGrabPosition) * ScaleFromScreen;

			Style.Left = Length.Pixels( local.x );
			Style.Top = Length.Pixels( local.y );
			Style.Dirty();
		}
	}
}
