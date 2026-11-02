namespace Editor.AnimGraph;

/// <summary>
/// Base for pannable, zoomable graph-space canvases. Owns the view transform,
/// smooth cursor-anchored zooming, panning and fit framing so every canvas
/// looks and feels the same. Derived classes implement <see cref="Fit"/> and
/// <see cref="PaintCanvas"/> and work in graph units.
/// </summary>
internal abstract class CanvasWidget : Widget
{
	/// <summary>Selection pink used across all node editors.</summary>
	protected static readonly Color SelectionColor = Color.Parse( "#ff99c8" ) ?? default;

	protected readonly Window Editor;

	/// <summary>View centre in graph units.</summary>
	protected internal Vector2 ViewCenter { get; private set; }

	/// <summary>Pixels per graph unit. Zero until the first fit.</summary>
	protected internal float Zoom { get; private set; }

	private Vector2 _centerTarget;
	private float _zoomTarget;
	private Vector2? _zoomAnchor;
	private Vector2 _zoomAnchorGraph;
	private bool _panning;
	private Vector2 _lastMouse;

	/// <summary>Graph-space y grows up the screen instead of down.</summary>
	protected virtual bool FlipY => false;

	/// <summary>Locks the view to the x axis for one-dimensional canvases.</summary>
	protected virtual bool LockY => false;

	protected virtual float MinZoom => 0.2f;
	protected virtual float MaxZoom => 2.5f;

	// Limits may adapt to content, so never let a shifting floor shove the
	// current view — it only stops you zooming further out from where you are.
	private float ClampZoom( float zoom ) => Math.Clamp( zoom, MathF.Min( MinZoom, _zoomTarget ), MaxZoom );

	protected CanvasWidget( Widget parent, Window window ) : base( parent )
	{
		Editor = window;
		MouseTracking = true;
		FocusMode = FocusMode.Click;
	}

	protected Vector2 ToCanvas( Vector2 p )
	{
		var d = (p - ViewCenter) * Zoom;
		return LocalRect.Size * 0.5f + (FlipY ? new Vector2( d.x, -d.y ) : d);
	}

	protected Vector2 FromCanvas( Vector2 s )
	{
		var d = (s - LocalRect.Size * 0.5f) / Zoom;
		return ViewCenter + (FlipY ? new Vector2( d.x, -d.y ) : d);
	}

	protected Rect ToCanvas( Rect r ) => new( ToCanvas( r.TopLeft ), r.Size * Zoom );

	/// <summary>Frame everything - called on F, Fit View and before the first paint.</summary>
	protected abstract void Fit();

	internal void FitView() => Fit();

	protected abstract void PaintCanvas();

	/// <summary>Smoothly bring the view to the given centre and zoom.</summary>
	protected void FrameView( Vector2 center, float zoom )
	{
		_centerTarget = LockY ? center.WithY( 0 ) : center;
		_zoomTarget = ClampZoom( zoom );
		_zoomAnchor = null;

		if ( Zoom <= 0 )
		{
			ViewCenter = _centerTarget;
			Zoom = _zoomTarget;
		}

		Update();
	}

	/// <summary>Snap straight to a remembered view - used when a page reopens.</summary>
	internal void RestoreView( Vector2 center, float zoom ) => FrameView( center, zoom );

	[EditorEvent.Frame]
	private void SmoothView()
	{
		if ( Zoom <= 0 )
			return;

		if ( ViewCenter.Distance( _centerTarget ) * Zoom < 0.5f && MathF.Abs( MathF.Log( _zoomTarget / Zoom ) ) < 0.002f )
		{
			_zoomAnchor = null;
			return;
		}

		var t = 1f - MathF.Exp( -RealTime.Delta * 20f );
		Zoom = MathF.Exp( MathX.Lerp( MathF.Log( Zoom ), MathF.Log( _zoomTarget ), t ) );

		if ( _zoomAnchor is { } anchor )
		{
			// Keep the graph point under the cursor pinned while the zoom eases in
			var d = (anchor - LocalRect.Size * 0.5f) / Zoom;
			ViewCenter = _zoomAnchorGraph - (FlipY ? new Vector2( d.x, -d.y ) : d);
			if ( LockY ) ViewCenter = ViewCenter.WithY( 0 );
			_centerTarget = ViewCenter;
		}
		else
		{
			ViewCenter = Vector2.Lerp( ViewCenter, _centerTarget, t );
		}

		Update();
	}

	protected bool IsPanning => _panning;

	protected void StartPan( Vector2 position )
	{
		_panning = true;
		_lastMouse = position;
		Cursor = CursorShape.ClosedHand;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.MiddleMouseButton )
		{
			StartPan( e.LocalPosition );
			e.Accepted = true;
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !_panning )
			return;

		var delta = (e.LocalPosition - _lastMouse) / Zoom;
		_lastMouse = e.LocalPosition;

		if ( FlipY ) delta.y = -delta.y;
		if ( LockY ) delta.y = 0;

		ViewCenter -= delta;
		_centerTarget = ViewCenter;
		_zoomTarget = Zoom;
		_zoomAnchor = null;
		Update();
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( _panning )
		{
			_panning = false;
			Cursor = CursorShape.Arrow;
		}
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		_zoomTarget = ClampZoom( _zoomTarget * (e.Delta > 0 ? 1.15f : 1 / 1.15f) );
		_zoomAnchor = e.Position;
		_zoomAnchorGraph = FromCanvas( e.Position );
		Update();
		e.Accept();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( e.Key == KeyCode.F )
			Fit();
	}

	protected sealed override void OnPaint()
	{
		if ( Zoom <= 0 )
			Fit();

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		PaintCanvas();
	}

	internal Pixmap Capture()
	{
		if ( Zoom <= 0 )
			Fit();

		var pixmap = new Pixmap( (int)Width, (int)Height );
		using ( Paint.ToPixmap( pixmap ) )
		{
			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;
			PaintCanvas();
		}

		return pixmap;
	}
}
