namespace Sandbox.UI;

/// <summary>
/// A drag handle for the nearest floating window. Children marked floating-window-nodrag remain clickable.
/// </summary>
internal class FloatingWindowTitleBar : Panel
{
	FloatingWindow window;
	bool leftPressed;

	/// <summary>
	/// Make the titlebar a drag target without enabling drag scrolling.
	/// </summary>
	public FloatingWindowTitleBar()
	{
		AddClass( "floating-window-titlebar" );
		CanDragScroll = false;
	}

	/// <summary>
	/// Titlebars receive drag events even when their contents do not scroll.
	/// </summary>
	public override bool WantsDrag => true;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		window = Ancestors.OfType<FloatingWindow>().FirstOrDefault();
		window?.BringToFront();
		leftPressed = e.Button == "mouseleft";

		for ( var panel = e.Target; panel is not null && panel != this; panel = panel.Parent )
		{
			if ( panel.HasClass( "floating-window-nodrag" ) || panel is Button ) leftPressed = false;
		}

		// The panel input system only starts a drag when the press is allowed to propagate.
		if ( !leftPressed ) e.StopPropagation();
	}

	protected override void OnDragStart( DragEvent e )
	{
		e.StopPropagation();
		if ( leftPressed && window.IsValid() ) window.BeginMove( e.ScreenPosition - e.ScreenGrabPosition );
	}

	protected override void OnDrag( DragEvent e )
	{
		e.StopPropagation();
		if ( window.IsValid() ) window.Move( e.ScreenPosition - e.ScreenGrabPosition );
	}

	protected override void OnDragEnd( DragEvent e )
	{
		e.StopPropagation();
		if ( !window.IsValid() ) return;
		window.Move( e.ScreenPosition - e.ScreenGrabPosition );
		window.EndMove( false );
	}

	protected override void OnDragCancel( DragEvent e )
	{
		e.StopPropagation();
		if ( window.IsValid() ) window.EndMove( true );
	}

	/// <summary>
	/// End any drag if the titlebar is removed while the mouse is held.
	/// </summary>
	public override void OnDeleted()
	{
		if ( window.IsValid() ) window.EndMove( true );
		base.OnDeleted();
	}
}
