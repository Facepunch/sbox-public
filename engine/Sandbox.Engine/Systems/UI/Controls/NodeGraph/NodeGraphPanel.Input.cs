namespace Sandbox.UI.NodeGraph;

//
// Keys and the wheel. The mouse is handled next to what it drives: Wires, PlugDrag and NodePanel.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// A text field in the graph, such as an inline editor, has the keyboard.
	/// </summary>
	bool TextEntryFocused => Descendants.OfType<TextEntry>().Any( x => x.HasFocus );

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button == "delete" && !IsBoxSelecting )
		{
			DeleteSelection();
			e.StopPropagation = true;
			return;
		}

		// Ctrl+C, X and V come as copy, cut and paste events; D is ours, unless a text field wants it
		if ( e.HasCtrl && e.Button == "d" && !IsBoxSelecting && !TextEntryFocused )
		{
			DuplicateSelection();
			e.StopPropagation = true;
			return;
		}

		// Space opens the node menu at the mouse, like right-clicking empty space
		if ( e.Button == "space" && Graph is not null && !IsBoxSelecting && !TextEntryFocused )
		{
			OpenContextMenu( MouseTarget, null );
			e.StopPropagation = true;
			return;
		}

		if ( e.HasCtrl && e.Button == "f" && Graph is not null && !IsBoxSelecting && !TextEntryFocused )
		{
			OpenFindMenu();
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}

	/// <summary>
	/// Holding R makes a wire dropped on empty space end in a reroute instead of opening the menu.
	/// </summary>
	bool _createReroute;

	public override void OnButtonEvent( ButtonEvent e )
	{
		if ( e.Button == "r" && !TextEntryFocused ) _createReroute = e.Pressed;

		base.OnButtonEvent( e );
	}

	/// <summary>
	/// Blurs arrive late and bubble up from whatever lost focus - a press on a plug focuses its
	/// node and then the graph - so look at where the focus is now. R's release won't reach the
	/// graph once a text field or something outside has the keyboard.
	/// </summary>
	protected override void OnBlur( PanelEvent e )
	{
		base.OnBlur( e );

		var focus = UISystem?.CurrentFocus;
		if ( focus is null || focus is TextEntry || !focus.AncestorsAndSelf.Contains( this ) ) _createReroute = false;
	}

	public override void OnMouseWheel( Vector2 value )
	{
		var delta = value.y != 0 ? value.y : value.x;
		if ( delta == 0 || !CanNavigate || IsPanning || IsBoxSelecting || !Viewport.IsInside( MousePosition ) ) return;

		// ZoomAt scales the visible span, so the zoom changes by its inverse. Stop on the limit
		// rather than short of it.
		var zoom = Zoom;
		var target = (zoom * (delta < 0 ? 1.1f : 0.9f)).Clamp( MinZoom, MaxZoom );
		if ( target.AlmostEqual( zoom ) ) return;

		ZoomAt( MousePosition, zoom / target );
	}
}
