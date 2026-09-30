namespace Sandbox.PanelGallery;

/// <summary>
/// Panel.SetMouseCapture in a panel window: the cursor hides and stays put, and the panel reads
/// Mouse.Delta. Next to it the same drag with the cursor left alone, for comparison.
/// </summary>
public class MouseCapturePage : GalleryPage
{
	public MouseCapturePage() : base( "Mouse Capture", "Call SetMouseCapture( true ) on a panel - from OnMouseDown, usually - and the cursor hides and stays where it is while Mouse.Delta reports how far the mouse moved each frame; read it in Tick. Call SetMouseCapture( false ) to let go and the cursor comes back where it was. HasMouseCapture says whether this panel holds it. Capture is dropped for you if the window loses focus or closes. Hold the left button in the boxes to see it: the top one captures, the bottom one just reads the cursor position, so it walks off." )
	{
		var captured = Case( "Captured - cursor hidden and pinned, movement from Mouse.Delta" );
		captured.AddChild( new Arena( capture: true ) );

		var free = Case( "Free - cursor visible, movement from the cursor position" );
		free.AddChild( new Arena( capture: false ) );
	}

	/// <summary>
	/// A box that moves a dot by however far the mouse travels while the button is held.
	/// </summary>
	class Arena : Panel
	{
		readonly bool capture;
		readonly Panel dot;
		readonly Sandbox.UI.Label readout;

		bool dragging;
		Vector2 dotPosition = new( 200, 120 );
		Vector2 lastCursor;
		Vector2 total;

		public Arena( bool capture )
		{
			this.capture = capture;

			AddClass( "capture-arena" );
			SetClass( "captured", capture );

			dot = Add.Panel( "dot" );
			readout = Add.Label( "hold the left button and move", "readout" );
			PlaceDot();
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			if ( e.MouseButton != MouseButtons.Left ) return;

			dragging = true;
			total = 0;
			lastCursor = MousePosition;

			if ( capture ) SetMouseCapture( true );
			SetClass( "dragging", true );
		}

		protected override void OnMouseUp( MousePanelEvent e )
		{
			if ( !dragging ) return;

			dragging = false;
			if ( capture ) SetMouseCapture( false );
			SetClass( "dragging", false );

			readout.Text = $"released after {total.Length:0} px of travel";
		}

		public override void Tick()
		{
			if ( !dragging ) return;

			Vector2 delta;
			if ( capture )
			{
				delta = Mouse.Delta;
			}
			else
			{
				delta = MousePosition - lastCursor;
				lastCursor = MousePosition;
			}

			if ( delta == Vector2.Zero ) return;

			total += delta;
			dotPosition = new Vector2( (dotPosition.x + delta.x).Clamp( 6, Box.Rect.Width * ScaleFromScreen - 6 ), (dotPosition.y + delta.y).Clamp( 6, Box.Rect.Height * ScaleFromScreen - 6 ) );
			PlaceDot();

			readout.Text = $"delta {delta.x:0},{delta.y:0}   travelled {total.Length:0} px   captured {HasMouseCapture}";
		}

		void PlaceDot()
		{
			dot.Style.Left = dotPosition.x - 6;
			dot.Style.Top = dotPosition.y - 6;
			dot.Style.Dirty();
		}
	}
}
