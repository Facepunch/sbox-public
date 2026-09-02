using Sandbox.Utility;

namespace Sandbox.UI;

/// <summary>
/// An independent instance of the panel UI. It owns its own root panels, layout size, input
/// state, focus and renderer, so any number of surfaces can run side by side - one per window,
/// viewport or render target.
/// </summary>
internal sealed class UISurface : IDisposable
{
	internal UISystem System { get; private set; } = new();

	/// <summary>
	/// The root panel everything on this surface hangs off.
	/// </summary>
	public RootPanel Root { get; private set; }

	internal SurfaceInput Input { get; }

	/// <summary>
	/// Called when the hovered panel wants a different mouse cursor. Null means default.
	/// </summary>
	public Action<string> OnCursorChanged { get; set; }

	/// <summary>
	/// Size of the surface in pixels. Root panels are laid out to this.
	/// </summary>
	public Vector2 Size
	{
		get => System.Size;
		set => System.Size = value;
	}

	/// <summary>
	/// Scale everything on this surface by this much, on top of the dpi scale.
	/// </summary>
	public float Scale { get; set; } = 1.0f;

	/// <summary>
	/// Dpi scale of the display we're on.
	/// </summary>
	public float DpiScale
	{
		get => System.DpiScale;
		set => System.DpiScale = value;
	}

	/// <summary>
	/// Create a surface and its root panel.
	/// </summary>
	public UISurface()
	{
		Input = new SurfaceInput( this );
		System.Input = Input;
		System.Size = new Vector2( 1024, 1024 );

		Root = new SurfaceRootPanel( this, System );
	}

	/// <summary>
	/// Cursor position in surface pixels, top left is 0,0.
	/// </summary>
	public Vector2 MousePosition
	{
		get => Input.MousePosition;
		set => Input.MousePosition = value;
	}

	/// <summary>
	/// Is the cursor over this surface? When false we don't hover or click anything.
	/// </summary>
	public bool MouseInside
	{
		get => Input.MouseInside;
		set => Input.MouseInside = value;
	}

	/// <summary>
	/// The panel the cursor is over, if any.
	/// </summary>
	public Panel Hovered => Input.Hovered;

	/// <summary>
	/// The panel that has keyboard focus on this surface, if any.
	/// </summary>
	public Panel Focus => System.CurrentFocus;

	/// <summary>
	/// Tell the surface the cursor moved to this position, in surface pixels.
	/// </summary>
	public void MouseMoved( Vector2 position ) => Input.MouseMoved( position );

	/// <summary>
	/// Tell the surface a mouse button went down or up.
	/// </summary>
	public void SetMouseButton( MouseButtons button, bool down, KeyboardModifiers modifiers = default ) => Input.SetMouseButton( button, down, modifiers );

	internal void SetMouseButton( NativeEngine.ButtonCode button, bool down, KeyboardModifiers modifiers ) => Input.SetMouseButton( button, down, modifiers );

	internal void SetKey( NativeEngine.ButtonCode button, bool down, KeyboardModifiers modifiers ) => Input.SetKey( button, down, modifiers );

	/// <summary>
	/// Tell the surface a mouse button was double clicked.
	/// </summary>
	public void SetDoubleClick( MouseButtons button ) => Input.SetDoubleClick( button );

	/// <summary>
	/// Tell the surface a mouse button was clicked a third time.
	/// </summary>
	public void SetTripleClick( MouseButtons button ) => Input.SetTripleClick( button );

	/// <summary>
	/// Tell the surface the mouse wheel moved.
	/// </summary>
	public void SetMouseWheel( Vector2 delta, KeyboardModifiers modifiers = default ) => Input.SetMouseWheel( delta, modifiers );

	/// <summary>
	/// Tell the surface a key went down or up, by name ("tab", "enter", "a"). Goes to the focused panel.
	/// </summary>
	public void SetKey( string button, bool down, int virtualKey = 0, KeyboardModifiers modifiers = default ) => Input.SetKey( button, down, virtualKey, modifiers );

	/// <summary>
	/// Tell the surface some text was typed. Goes to the focused panel.
	/// </summary>
	public void TypeText( string text ) => Input.TypeText( text );

	/// <summary>
	/// The deepest panel at this position, ignoring pointer-events. Used for window hit testing,
	/// which has to answer for panels that don't take input.
	/// </summary>
	public Panel FindPanelAt( Vector2 position )
	{
		return FindPanelAt( Root, position, null );
	}

	/// <summary>
	/// The deepest panel at this position that <paramref name="match"/> accepts. Panels that don't
	/// match are looked through rather than stopped at, so an overlay can sit on top of something
	/// without hiding it from the search.
	/// </summary>
	public Panel FindPanelAt( Vector2 position, Func<Panel, bool> match )
	{
		return FindPanelAt( Root, position, match );
	}

	internal static Panel FindPanelAt( Panel panel, Vector2 position, Func<Panel, bool> match )
	{
		if ( !panel.IsVisible ) return null;
		if ( panel.ComputedStyle is null ) return null;

		position = panel.GetTransformPosition( position );

		if ( !panel.IsInside( position ) )
			return null;

		// Later children draw on top, so they win
		for ( int i = panel.ChildrenCount - 1; i >= 0; i-- )
		{
			var hit = FindPanelAt( panel.GetChild( i ), position, match );
			if ( hit is not null ) return hit;
		}

		return match is null || match( panel ) ? panel : null;
	}

	// The payload of the OS drag over the surface right now. It arrives once, when the drag
	// comes in, and every hover query reuses it until the drag leaves or lands.
	List<string> _dragFiles;
	string _dragText;
	Panel _dragHoverPanel;

	/// <summary>
	/// An OS drag came in over the surface carrying these files and/or this text.
	/// </summary>
	public void DragEnter( IEnumerable<string> files, string text )
	{
		_dragFiles = files?.ToList();
		_dragText = text;
	}

	/// <summary>
	/// The drag moved. Asks the panel under it - "ondrop" with IsDrop false, bubbling up -
	/// whether it'd take the payload. What comes back is what the drag cursor shows.
	/// </summary>
	public DropAction DragOver( Vector2 position ) => DispatchDrag( position, isDrop: false );

	/// <summary>
	/// The drag left the surface without dropping.
	/// </summary>
	public void DragLeave()
	{
		NotifyDragLeave();
		_dragFiles = null;
		_dragText = null;
	}

	/// <summary>
	/// The payload landed - same event, IsDrop true. Returns what the panel did with it.
	/// </summary>
	public DropAction Drop( Vector2 position )
	{
		var action = DispatchDrag( position, isDrop: true );
		DragLeave();
		return action;
	}

	DropAction DispatchDrag( Vector2 position, bool isDrop )
	{
		if ( (_dragFiles is null || _dragFiles.Count == 0) && string.IsNullOrEmpty( _dragText ) )
			return DropAction.None;

		var panel = FindPanelAt( position ) ?? Root;

		// The drag wandered to a different panel - the old one hears it left, so any
		// hover styling can come off
		if ( panel != _dragHoverPanel ) NotifyDragLeave();
		_dragHoverPanel = panel;

		var e = new DropEvent( panel )
		{
			Files = _dragFiles ?? (IReadOnlyList<string>)Array.Empty<string>(),
			Text = _dragText,
			Position = position,
			IsDrop = isDrop,
		};

		panel.DispatchEventImmediate( e );
		return e.Action;
	}

	/// <summary>
	/// Tell the panel a drag was hovering that it's not any more - it left, landed, or moved
	/// to another panel. Same DropEvent shape, named "ondragleave".
	/// </summary>
	void NotifyDragLeave()
	{
		var panel = _dragHoverPanel;
		_dragHoverPanel = null;

		if ( panel is null || !panel.IsValid() ) return;

		panel.DispatchEventImmediate( new DropEvent( panel, "ondragleave" )
		{
			Files = _dragFiles ?? (IReadOnlyList<string>)Array.Empty<string>(),
			Text = _dragText,
		} );
	}

	//
	// The plain path, for platforms without the live drop target - the payload only turns up
	// as it lands, so there's no hover conversation, just the drop itself.
	//

	/// <summary>
	/// A file from outside the app is being dropped on the surface. Files accumulate until
	/// <see cref="DropComplete"/> delivers them.
	/// </summary>
	public void DropFile( string path )
	{
		_dragFiles ??= new();
		_dragFiles.Add( path );
	}

	/// <summary>
	/// Text from outside the app is being dropped on the surface.
	/// </summary>
	public void DropText( string text ) => _dragText = text;

	/// <summary>
	/// The drop finished - everything gathered lands as one "ondrop" on the deepest panel at
	/// <paramref name="position"/>, bubbling up from there.
	/// </summary>
	public void DropComplete( Vector2 position ) => Drop( position );

	/// <summary>
	/// Tick, handle input, lay out and build command lists. Call once a frame, on the main thread.
	/// </summary>
	public void Simulate( bool acceptInput = true )
	{
		ObjectDisposedException.ThrowIf( IsDisposed, this );

		if ( Size.x < 1 || Size.y < 1 )
			return;

		System.TickPanels();
		System.TickSurfaceInput( acceptInput && MouseInside );
		System.LayoutAndBuild();
	}

	/// <summary>
	/// Draw the surface. Must be called from inside a render block.
	/// </summary>
	public void Render()
	{
		ObjectDisposedException.ThrowIf( IsDisposed, this );

		System.Render();
	}

	/// <summary>
	/// Has this surface been disposed? A disposed surface has no panels and does nothing.
	/// </summary>
	public bool IsDisposed { get; private set; }

	/// <summary>
	/// Delete every panel on this surface. Safe to call twice; anything else after that is a bug
	/// in the caller, and says so instead of null-reffing.
	/// </summary>
	public void Dispose()
	{
		if ( IsDisposed )
			return;

		IsDisposed = true;

		System.Clear();
		Root = null;
	}
}

/// <summary>
/// The root of a <see cref="UISurface"/>. Scales with the surface rather than pretending
/// the window is a 1080p game screen.
/// </summary>
file class SurfaceRootPanel : RootPanel
{
	readonly UISurface Surface;

	public SurfaceRootPanel( UISurface surface, UISystem system ) : base( system )
	{
		Surface = surface;
	}

	protected override void UpdateScale( Rect screenSize )
	{
		Scale = Surface.DpiScale * Surface.Scale;
	}
}
