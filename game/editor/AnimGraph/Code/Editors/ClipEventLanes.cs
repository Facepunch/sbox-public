namespace Editor.AnimGraph;

/// <summary>
/// The lanes themselves - a row per track and the events on them. Draws and drags, and says what
/// happened; the document is somebody else's to change.
/// </summary>
internal sealed class ClipEventLanes : Widget
{
	public ClipDocument Document { get; set; }
	public int FrameCount { get; set; }
	public string EmptyHint { get; set; }
	public float Playhead { get; set; }

	public ClipDocument.Track SelectedTrack { get; set; }
	public ClipDocument.Event SelectedEvent { get; set; }

	/// <summary>An event was clicked, or a click landed on nothing.</summary>
	public Action<ClipDocument.Track, ClipDocument.Event> Selected { get; set; }

	/// <summary>A drag finished having actually moved something.</summary>
	public Action Edited { get; set; }

	public Action<ClipDocument.Track, float> AddEvent { get; set; }
	public Action DeleteEvent { get; set; }
	public Action<ClipDocument.Track, float> ContextMenu { get; set; }
	public Action<float> Scrubbed { get; set; }

	private bool _resizing;
	private bool _dragged;
	private bool _grabbed;
	private bool _hoverEdge;
	private float _grabOffset;
	private ClipDocument.Event _hover;

	public ClipEventLanes( Widget parent ) : base( parent )
	{
		FocusMode = FocusMode.Click;

		// Without this the cursor only updates while a button is held, so the resize edge gives
		// no sign it's there until you're already dragging
		MouseTracking = true;
	}

	private ClipEventLayout Geometry => new( Width, FrameCount, Document?.Tracks.Count ?? 0 );

	protected override void OnPaint()
	{
		var layout = Geometry;

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );

		if ( Document is null || Document.Tracks.Count == 0 )
		{
			DockPanel.PaintHint( LocalRect, Document is null
				? EmptyHint ?? "Nothing to mark up"
				: "No event tracks yet - add one to mark up this clip" );

			return;
		}

		for ( var i = 0; i < Document.Tracks.Count; i++ )
		{
			PaintRow( layout, i, Document.Tracks[i] );
		}

		PaintGrid( layout );

		for ( var i = 0; i < Document.Tracks.Count; i++ )
		{
			PaintTrack( layout, i, Document.Tracks[i] );
		}

		PaintPlayhead( layout );
	}

	/// <summary>
	/// The rows behind everything, so the grid and the events aren't painted over by the row
	/// under them.
	/// </summary>
	private void PaintRow( ClipEventLayout layout, int index, ClipDocument.Track track )
	{
		var lane = layout.LaneRect( index );
		var row = new Rect( 0f, lane.Top, Width, lane.Height );

		Paint.ClearPen();

		if ( track == SelectedTrack )
		{
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.07f ) );
			Paint.DrawRect( row );
		}
		else if ( index % 2 == 1 )
		{
			Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.2f ) );
			Paint.DrawRect( row );
		}
	}

	/// <summary>
	/// The ruler's frames carried down the lanes, so an event can be read off against them.
	/// </summary>
	private void PaintGrid( ClipEventLayout layout )
	{
		Paint.SetPen( Theme.WindowBackground.WithAlpha( 0.5f ) );

		foreach ( var frame in layout.RulerFrames() )
		{
			var x = layout.FrameToX( frame );

			Paint.DrawLine( new Vector2( x, 0f ), new Vector2( x, Height ) );
		}

		// Where the gutter ends and the clip begins, so the two columns read apart
		Paint.SetPen( Theme.WindowBackground );
		Paint.DrawLine( new Vector2( layout.Gutter - 8f, 0f ), new Vector2( layout.Gutter - 8f, Height ) );
	}

	private void PaintTrack( ClipEventLayout layout, int index, ClipDocument.Track track )
	{
		var lane = layout.LaneRect( index );
		var color = ClipEventTypes.Color( track.EventClass );
		var dimmed = track.IsDisabled;

		// A chip in the track's own colour, so a bar out on the lane can be traced back to the
		// row it belongs to without reading the name
		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( dimmed ? 0.25f : 0.9f ) );
		Paint.DrawRect( new Rect( 8f, lane.Top + 6f, 3f, lane.Height - 12f ), 1.5f );

		// The icon has its own slot at the end of the gutter, so a track name doesn't shift
		// sideways when it's made the sync track
		var label = new Rect( 18f, lane.Top, lane.Left - 48f, lane.Height );

		Paint.SetDefaultFont();
		Paint.SetPen( (track == SelectedTrack ? Theme.Text : Theme.TextControl).WithAlpha( dimmed ? 0.4f : 0.9f ) );
		Paint.DrawText( label, ClipEventTypes.Title( track.EventClass ), TextFlag.LeftCenter );

		if ( track.IsSyncTrack )
		{
			Paint.SetPen( color.WithAlpha( dimmed ? 0.4f : 1f ) );
			Paint.DrawIcon( new Rect( lane.Left - 28f, lane.Top, 14f, lane.Height ), "sync", 13f, TextFlag.Center );
		}

		foreach ( var item in track.Events )
		{
			PaintEvent( layout, index, item, track, color, dimmed );
		}
	}

	private void PaintEvent( ClipEventLayout layout, int index, ClipDocument.Event item, ClipDocument.Track track, Color color, bool dimmed )
	{
		var rect = layout.EventRect( index, item );
		var selected = item == SelectedEvent;
		var hovered = item == _hover;
		var label = ClipEventTypes.Summary( item, track.EventClass );

		// An event the compiler would refuse, throw away or drop is worth flagging where it is
		var invalid = ClipEventTypes.IsIncomplete( item, track.EventClass )
			|| ClipEventTypes.IsOutsideClip( item, layout.LastFrame )
			|| ClipEventTypes.Clashes( track, item, layout.LastFrame );

		// Flat and solid - selection is the full strength colour, everything else sits back
		Paint.ClearPen();
		Paint.SetBrush( (invalid ? Theme.Red : color).WithAlpha( dimmed ? 0.3f : selected || hovered ? 1f : 0.65f ) );
		Paint.DrawRect( rect, MathF.Min( 3f, MathF.Min( rect.Width, rect.Height ) * 0.5f ) );

		// The grab edge is only worth showing on the bar the pointer is actually over
		if ( hovered && _hoverEdge )
		{
			Paint.SetBrush( Theme.Text );
			Paint.DrawRect( new Rect( rect.Right - 3f, rect.Top + 2f, 2f, rect.Height - 4f ), 1f );
		}

		if ( string.IsNullOrWhiteSpace( label ) )
			label = invalid ? "unnamed" : null;

		if ( string.IsNullOrEmpty( label ) )
			return;

		Paint.SetDefaultFont( 7 );
		Paint.ClearBrush();

		if ( rect.Width >= 34f )
		{
			Paint.SetPen( Theme.WindowBackground.WithAlpha( 0.9f ) );
			Paint.DrawText( rect.Shrink( 5f, 0f ), label, TextFlag.LeftCenter );
			return;
		}

		// Most events fire on a single frame, so their marker is too narrow to write in and the
		// name goes beside it instead - otherwise you're clicking blind. It stops where the next
		// event starts, so two close together can't write over each other
		var limit = layout.LaneArea.Right;

		foreach ( var other in track.Events )
		{
			if ( other != item && other.StartFrame > item.StartFrame )
				limit = MathF.Min( limit, layout.EventRect( index, other ).Left - 3f );
		}

		Paint.SetPen( selected ? Theme.Text : (invalid ? Theme.Red : color).WithAlpha( dimmed ? 0.4f : 0.9f ) );
		Paint.DrawText( new Rect( rect.Right + 4f, rect.Top, MathF.Max( limit - rect.Right - 4f, 0f ), rect.Height ), label, TextFlag.LeftCenter );
	}

	private void PaintPlayhead( ClipEventLayout layout )
	{
		var x = layout.FrameToX( Playhead.Clamp( 0f, 1f ) * layout.LastFrame );

		Paint.SetPen( Theme.Primary.WithAlpha( 0.8f ), 1f );
		Paint.DrawLine( new Vector2( x, 0f ), new Vector2( x, Height ) );
	}

	private ClipDocument.Event HitEvent( ClipEventLayout layout, int lane, Vector2 position )
	{
		if ( lane < 0 )
			return null;

		var events = Document.Tracks[lane].Events;

		// Last drawn is on top, so it's the one picked where blocks overlap
		for ( var i = events.Count - 1; i >= 0; i-- )
		{
			if ( layout.EventRect( lane, events[i] ).IsInside( position ) )
				return events[i];
		}

		return null;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		Focus();

		if ( Document is null )
			return;

		var layout = Geometry;
		var lane = layout.HitLane( e.LocalPosition );

		if ( e.RightMouseButton )
		{
			var track = lane < 0 ? null : Document.Tracks[lane];

			if ( track is not null )
				Selected?.Invoke( track, HitEvent( layout, lane, e.LocalPosition ) );

			ContextMenu?.Invoke( track, layout.XToFrame( e.LocalPosition.x ) );
			e.Accepted = true;
			return;
		}

		if ( !e.LeftMouseButton )
			return;

		_grabbed = false;

		if ( lane < 0 || e.LocalPosition.x < layout.LaneArea.Left )
		{
			Selected?.Invoke( lane < 0 ? null : Document.Tracks[lane], null );
			e.Accepted = true;
			return;
		}

		var hit = HitEvent( layout, lane, e.LocalPosition );
		Selected?.Invoke( Document.Tracks[lane], hit );

		if ( hit is null || ReadOnly )
			return;

		_grabbed = true;
		_dragged = false;
		_resizing = ClipEventLayout.IsResizeEdge( layout.EventRect( lane, hit ), e.LocalPosition.x );

		// Measured from the edge being dragged, so the bar keeps up with the pointer instead of
		// jumping to put that edge under it
		_grabOffset = layout.XToFrame( e.LocalPosition.x ) - (_resizing ? hit.EndFrame : hit.StartFrame);

		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		var layout = Geometry;

		// Only a press that landed on an event drags one - a double click creates its event
		// after a press on empty space, and would otherwise move it by whatever offset the last
		// grab left behind
		if ( ReadOnly || !_grabbed || !e.ButtonState.HasFlag( MouseButtons.Left ) || SelectedEvent is not { } item )
		{
			UpdateCursor( layout, e.LocalPosition );
			return;
		}

		// Where the edge being dragged wants to be, rather than where the pointer is - taken off
		// before snapping, so what lands on a whole frame is the edge and not the pointer
		var frame = layout.XToFrame( e.LocalPosition.x ) - _grabOffset;

		// Whole frames unless it's held down - events are authored against what's on screen
		if ( !e.HasCtrl )
			frame = MathF.Round( frame );

		if ( _resizing )
			item.DurationFrames = (frame - item.StartFrame).Clamp( 0f, layout.LastFrame - item.StartFrame );
		else
			item.StartFrame = frame.Clamp( 0f, MathF.Max( layout.LastFrame - item.DurationFrames, 0f ) );

		_dragged = true;

		Scrubbed?.Invoke( item.StartFrame / layout.LastFrame );
		Update();
		e.Accepted = true;
	}

	/// <summary>
	/// What's under the pointer, so the cursor and the bar being hovered can say what a drag from
	/// here would do before it's started.
	/// </summary>
	private void UpdateCursor( ClipEventLayout layout, Vector2 position )
	{
		var lane = Document is null ? -1 : layout.HitLane( position );
		var hit = HitEvent( layout, lane, position );
		var edge = hit is not null && ClipEventLayout.IsResizeEdge( layout.EventRect( lane, hit ), position.x );

		Cursor = hit is null || ReadOnly ? CursorShape.Arrow
			: edge ? CursorShape.SizeH
			: CursorShape.Finger;

		if ( _hover == hit && _hoverEdge == edge )
			return;

		_hover = hit;
		_hoverEdge = edge;

		Update();
	}

	protected override void OnMouseLeave()
	{
		if ( _hover is null )
			return;

		_hover = null;
		_hoverEdge = false;

		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		_resizing = false;
		_grabbed = false;

		// A click just selects - only a drag that actually moved something is an edit
		if ( !_dragged )
			return;

		_dragged = false;

		// The whole drag is one edit, so it's written once it's over rather than per frame
		Edited?.Invoke();
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( Document is null || ReadOnly || !e.LeftMouseButton )
			return;

		var layout = Geometry;
		var lane = layout.HitLane( e.LocalPosition );

		if ( lane < 0 || e.LocalPosition.x < layout.LaneArea.Left || HitEvent( layout, lane, e.LocalPosition ) is not null )
			return;

		_grabbed = false;
		AddEvent?.Invoke( Document.Tracks[lane], layout.XToFrame( e.LocalPosition.x ) );
		e.Accepted = true;
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( ReadOnly || SelectedEvent is not { } item )
			return;

		if ( e.Key == KeyCode.Delete )
		{
			DeleteEvent?.Invoke();
			e.Accepted = true;
			return;
		}

		if ( e.HasCtrl || e.HasAlt || e.Key is not (KeyCode.Left or KeyCode.Right) )
			return;

		var step = e.HasShift ? 10f : 1f;
		var offset = e.Key == KeyCode.Left ? -step : step;
		var frame = (item.StartFrame + offset).Clamp( 0f, MathF.Max( Geometry.LastFrame - item.DurationFrames, 0f ) );
		if ( frame != item.StartFrame )
		{
			item.StartFrame = frame;
			Scrubbed?.Invoke( frame / Geometry.LastFrame );
			Edited?.Invoke();
			Update();
		}
		e.Accepted = true;
	}
}
