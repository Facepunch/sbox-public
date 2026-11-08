using Sandbox.MovieMaker;

namespace Editor.AnimGraph;

internal sealed class ClipEventRuler : Widget
{
	private readonly ClipEventLanes _lanes;
	private readonly bool _top;
	private bool _scrubbing;
	private bool _panning;
	private Vector2 _lastMouse;

	public ClipEventRuler( Widget parent, ClipEventLanes lanes, bool top ) : base( parent )
	{
		_lanes = lanes;
		_top = top;
		FixedHeight = ClipEventLayout.RulerHeight;
		MouseTracking = true;
		FocusMode = FocusMode.Click;
		Cursor = CursorShape.Finger;
		ToolTip = "Drag to scrub. Ctrl+wheel to zoom, Shift+wheel to pan, Alt+wheel to step frames.";
	}

	protected override void OnPaint()
	{
		var layout = _lanes.Geometry;
		var header = _lanes.HeaderWidth;
		Paint.Antialiasing = false;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );
		Paint.SetBrush( ClipEventLayout.RangeBackground );
		Paint.DrawRect( new Rect( header + layout.VisibleLeft, 0, layout.VisibleRight - layout.VisibleLeft, Height ) );

		Paint.SetPen( Theme.TextControl.WithAlpha( 0.02f ), 1 );
		foreach ( var frame in layout.RulerFrames( true ) )
		{
			var x = header + MathF.Round( layout.FrameToX( frame ) ) + 0.5f;
			if ( x < header ) continue;
			Paint.DrawLine( new Vector2( x, _top ? 16 : 0 ), new Vector2( x, _top ? Height : Height - 16 ) );
		}
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.02f ), 2 );
		foreach ( var frame in layout.RulerFrames() )
		{
			var x = header + MathF.Round( layout.FrameToX( frame ) ) + 0.5f;
			if ( x < header ) continue;
			Paint.DrawLine( new Vector2( x, _top ? 8 : 0 ), new Vector2( x, _top ? Height : Height - 8 ) );
		}

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( new Rect( header, _top ? Height - 2 : 0, Width - header, 2 ) );
		Paint.DrawRect( new Rect( header - 2, 0, 2, Height ) );
		Paint.Antialiasing = true;
		Paint.SetFont( "Roboto", 8, 300 );
		Paint.SetPen( Theme.Green.WithAlpha( 0.25f ) );
		foreach ( var frame in layout.RulerFrames() )
		{
			var x = header + layout.FrameToX( frame );
			if ( x < header ) continue;
			var text = _top ? MovieTime.FromSeconds( frame / layout.FrameRate ).ToString() : $"{frame:0000}";
			Paint.DrawText( new Vector2( x + 6, _top ? 2 : 10 ), text );
		}

		var playhead = header + layout.FrameToX( _lanes.Playhead * layout.LastFrame );
		if ( playhead < header || playhead > header + _lanes.LaneWidth )
			return;

		var tip = _top ? Height : 0;
		var direction = _top ? -1 : 1;
		Paint.SetBrushAndPen( Theme.Yellow );
		Paint.DrawPolygon( new Vector2( playhead, tip ), new Vector2( playhead + 4, tip + direction * 4 ),
			new Vector2( playhead + 4, tip + direction * 12 ), new Vector2( playhead - 4, tip + direction * 12 ),
			new Vector2( playhead - 4, tip + direction * 4 ) );

		var alpha = 1f - Math.Clamp( _lanes.PlayheadChanged * 4f - 3f, 0f, 1f );
		if ( alpha <= 0f )
			return;

		var currentFrame = _lanes.Playhead * layout.LastFrame;
		var label = _top
			? MovieTime.FromSeconds( currentFrame / layout.FrameRate ).ToString()
			: MathF.Round( currentFrame ).ToString( "0000" );
		Paint.SetDefaultFont();
		var size = Paint.MeasureText( label );
		var rect = new Rect( playhead + 8f, (_top ? 12f : Height - 12f) - 10f, size.x + 16f, 20f );
		Paint.SetBrushAndPen( Theme.ControlBackground.WithAlpha( 0.75f * alpha ) );
		Paint.DrawRect( rect, 3f );
		Paint.ClearBrush();
		Paint.SetPen( Theme.Yellow.WithAlpha( alpha ) );
		Paint.DrawText( rect.Shrink( 8f, 0f, 0f, 0f ), label, TextFlag.LeftCenter );
		Update();
	}

	protected override void OnMouseWheel( WheelEvent e ) => _lanes.HandleWheel( e, e.Position.x - _lanes.HeaderWidth );
	protected override void OnKeyPress( KeyEvent e ) => _lanes.HandleKey( e );

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LocalPosition.x < _lanes.HeaderWidth )
			return;
		Focus();
		_lastMouse = e.LocalPosition;
		_panning = e.MiddleMouseButton;
		_scrubbing = e.LeftMouseButton || e.RightMouseButton;
		if ( _scrubbing )
			Scrub( e );
		else if ( _panning )
			e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _panning )
		{
			_lanes.Pan( e.LocalPosition.x - _lastMouse.x );
			e.Accepted = true;
		}
		else if ( _scrubbing )
		{
			Scrub( e );
		}
		_lastMouse = e.LocalPosition;
	}

	protected override void OnMouseReleased( MouseEvent e ) => _scrubbing = _panning = false;

	[EditorEvent.Frame]
	private void AutoPan()
	{
		if ( !_scrubbing )
			return;
		var x = _lastMouse.x - _lanes.HeaderWidth;
		var edge = x.Clamp( 0, _lanes.LaneWidth );
		if ( x == edge )
			return;
		if ( x > edge && _lanes.Geometry.XToFrame( edge ) >= _lanes.Geometry.LastFrame )
			return;
		_lanes.Pan( (edge - x) * 5 * RealTime.Delta );
		_lanes.ScrubAt( edge );
	}

	private void Scrub( MouseEvent e )
	{
		_lanes.ScrubAt( (e.LocalPosition.x - _lanes.HeaderWidth).Clamp( 0, _lanes.LaneWidth ) );
		e.Accepted = true;
	}
}
