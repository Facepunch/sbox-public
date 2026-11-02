namespace Editor.AnimGraph;

/// <summary>
/// The frame numbers above the lanes, and the playhead's handle. Pinned above the scrolling lanes
/// rather than drawn on them, so it's still there however far down the tracks you are.
/// </summary>
internal sealed class ClipEventRuler : Widget
{
	private readonly Widget _lanes;

	public int FrameCount { get; set; }

	public float Playhead { get; set; }

	/// <summary>Dragging the ruler scrubs the preview, so events can be placed against it.</summary>
	public Action<float> Scrubbed { get; set; }

	private bool _scrubbing;

	/// <summary>
	/// Measured against the lanes rather than itself - a scrollbar takes width off them, and the
	/// two columns have to line up to be worth anything.
	/// </summary>
	public ClipEventRuler( Widget parent, Widget lanes ) : base( parent )
	{
		_lanes = lanes;

		FixedHeight = ClipEventLayout.RulerHeight;
		Cursor = CursorShape.SizeH;
	}

	private ClipEventLayout Geometry => new( _lanes.Width, FrameCount, 0 );

	protected override void OnPaint()
	{
		var layout = Geometry;
		var area = layout.LaneArea;

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( LocalRect );

		if ( _lanes.Width < layout.Gutter )
			return;

		Paint.SetDefaultFont( 7 );

		// The name column gets a heading of its own, so the gutter doesn't read as empty space
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.45f ) );
		Paint.DrawText( new Rect( 18f, 0f, layout.Gutter - 26f, Height ), "TRACK", TextFlag.LeftCenter );

		foreach ( var frame in layout.RulerFrames() )
		{
			var x = layout.FrameToX( frame );

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.25f ) );
			Paint.DrawLine( new Vector2( x, Height - 5f ), new Vector2( x, Height ) );

			// Anything that would run off the end is left out rather than drawn cut in half
			if ( x + 24f > area.Right + 8f )
				continue;

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
			Paint.DrawText( new Rect( x + 4f, 0f, 30f, Height ), $"{frame:0}", TextFlag.LeftCenter );
		}

		Paint.SetPen( Theme.WindowBackground );
		Paint.DrawLine( new Vector2( 0f, Height - 1f ), new Vector2( Width, Height - 1f ) );

		PaintPlayhead( layout );
	}

	private void PaintPlayhead( ClipEventLayout layout )
	{
		var x = layout.FrameToX( Playhead.Clamp( 0f, 1f ) * layout.LastFrame );

		Paint.ClearPen();
		Paint.SetBrush( Theme.Primary );
		Paint.DrawPolygon(
			new Vector2( x - 5f, Height - 9f ),
			new Vector2( x + 5f, Height - 9f ),
			new Vector2( x, Height - 1f ) );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton || e.LocalPosition.x < Geometry.Gutter )
			return;

		_scrubbing = true;

		Scrub( e.LocalPosition );
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !_scrubbing )
			return;

		Scrub( e.LocalPosition );
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e ) => _scrubbing = false;

	private void Scrub( Vector2 position )
	{
		var layout = Geometry;

		Scrubbed?.Invoke( layout.XToFrame( position.x ) / layout.LastFrame );
	}
}
