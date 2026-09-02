namespace Sandbox.UI;

/// <summary>
/// Every open panel window. The windows themselves belong to Sandbox.Tools; this is the engine's
/// end of them - what SDL events are matched against, and what the engine loop draws.
/// </summary>
internal static class PanelWindows
{
	static readonly List<IPanelWindow> all = new();

	internal static IReadOnlyList<IPanelWindow> All => all;

	internal static void Register( IPanelWindow window )
	{
		if ( all.Contains( window ) ) return;

		all.Add( window );
	}

	internal static void Unregister( IPanelWindow window )
	{
		all.Remove( window );
	}

	/// <summary>
	/// Close every popup except <paramref name="except"/>. Popups are transient - a click that
	/// isn't on them, or the focus leaving the app, takes them down the way an OS menu goes.
	/// </summary>
	internal static void DismissPopups( IPanelWindow except = null )
	{
		for ( int i = all.Count - 1; i >= 0; i-- )
		{
			var window = all[i];
			if ( window.IsPopup && window != except ) window.RequestClose();
		}
	}

	/// <summary>
	/// Is the keyboard in one of our popups?
	/// </summary>
	internal static bool AnyPopupFocused()
	{
		for ( int i = 0; i < all.Count; i++ )
		{
			if ( all[i].IsPopup && all[i].IsFocused ) return true;
		}

		return false;
	}

	/// <summary>
	/// The window with this OS handle, if it's one of ours.
	/// </summary>
	internal static IPanelWindow Find( IntPtr handle )
	{
		for ( int i = 0; i < all.Count; i++ )
		{
			if ( all[i].Handle == handle ) return all[i];
		}

		return null;
	}

	/// <summary>
	/// The window the cursor is in. Mouse events belong to it rather than to whatever has keyboard
	/// focus - a popup menu never takes focus off its parent.
	/// </summary>
	internal static IPanelWindow Hovering
	{
		get
		{
			for ( int i = all.Count - 1; i >= 0; i-- )
			{
				if ( all[i].MouseInside ) return all[i];
			}

			return null;
		}
	}

	/// <summary>
	/// The cursor is here, in this window. The cursor is only ever in one window at a time, and a
	/// window it has left gets no further move events to tell it so.
	/// </summary>
	internal static void SetCursorPosition( IPanelWindow window, Vector2 position )
	{
		window.SetCursorPosition( position );

		if ( !window.MouseInside ) return;

		for ( int i = 0; i < all.Count; i++ )
		{
			if ( all[i] != window ) all[i].MouseInside = false;
		}
	}

	/// <summary>
	/// The bookkeeping every presented frame needs: borrowed render targets back in the pool,
	/// queued disposals run. The app loop does this once a frame - but frames drawn from resize
	/// events need it too, because during a drag the app loop is parked in the OS modal loop.
	/// Without it every resize event mints render targets at the new size and nothing ever
	/// recycles them, so a long drag gets slower the longer it goes on.
	/// </summary>
	internal static void FrameEnd()
	{
		RenderTarget.EndOfFrame();
		EngineLoop.DrainFrameEndDisposables();
	}

	/// <summary>
	/// Simulate and draw every open window. Returns whether anything was actually presented -
	/// false when every window is minimized, so the caller knows to sleep instead of spin.
	/// </summary>
	internal static bool FrameAll()
	{
		// Called every frame from the engine loop, in games too - where this list is always
		// empty and this is the whole cost of the feature
		if ( all.Count == 0 )
			return false;

		var presented = false;

		// A window can close itself - or its popups - while it draws, so work from a copy
		foreach ( var window in all.ToArray() )
		{
			if ( !window.IsOpen ) continue;

			try
			{
				presented |= window.Frame( interactiveResize: false );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Exception drawing a panel window" );
			}
		}

		return presented;
	}
}
