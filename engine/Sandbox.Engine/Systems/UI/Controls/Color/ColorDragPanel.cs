namespace Sandbox.UI;

/// <summary>
/// A panel you pick a value from by dragging inside it. Subclasses get the pointer as a fraction
/// of their box, and a drag that starts inside stays theirs until the button comes up.
/// </summary>
internal abstract class ColorDragPanel : Panel
{
	/// <summary>
	/// Called when the button comes up after a drag, for things that only want the settled value.
	/// </summary>
	public Action DragEnded { get; set; }

	bool _dragging;

	/// <summary>
	/// The pointer, 0..1 across and 0..1 down the panel.
	/// </summary>
	protected abstract void OnDrag( Vector2 fraction );

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.Button != "mouseleft" ) return;

		UISystem.CurrentFocus?.Blur();
		_dragging = true;
		Drag( e );

		// This press belongs to us - without this a scrolling parent drags the page around
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !_dragging || !PseudoClass.HasFlag( PseudoClass.Active ) ) return;

		Drag( e );
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( !_dragging ) return;

		_dragging = false;
		DragEnded?.Invoke();
		e.StopPropagation();
	}

	void Drag( MousePanelEvent e )
	{
		var rect = Box.Rect;
		if ( rect.Width <= 0 || rect.Height <= 0 ) return;

		var fraction = new Vector2( e.LocalPosition.x / rect.Width, e.LocalPosition.y / rect.Height );
		OnDrag( fraction.Clamp( 0.0f, 1.0f ) );
	}
}
