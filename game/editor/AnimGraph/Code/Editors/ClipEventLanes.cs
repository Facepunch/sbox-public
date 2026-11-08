namespace Editor.AnimGraph;

internal sealed class ClipEventLanes : Widget
{
	public ClipDocument Document { get; private set; }
	public int FrameCount { get; private set; }
	public float Playhead
	{
		get;
		set
		{
			if ( field != value )
				PlayheadChanged = 0f;
			field = value;
		}
	}
	public RealTimeSince PlayheadChanged { get; private set; }
	public ClipDocument.Track SelectedTrack { get; set; }
	public ClipDocument.Event SelectedEvent { get; set; }
	public Action<ClipDocument.Track, ClipDocument.Event> Selected { get; set; }
	public Action Edited { get; set; }
	public Action<ClipDocument.Track, float> AddEvent { get; set; }
	public Action EditEvent { get; set; }
	public Action DeleteEvent { get; set; }
	public Action<ClipDocument.Track, float> ContextMenu { get; set; }
	public Action Copy { get; set; }
	public Action Paste { get; set; }
	public Action<float> Scrubbed { get; set; }
	public Action HeightChanged { get; set; }
	public Action<float> Scrolled { get; set; }
	public float FrameRate { get; set; } = 30;
	public bool SnapToFrames { get; set; } = true;

	private readonly List<Lane> _lanes = [];
	private readonly List<(ClipDocument.Track Track, Label Title)> _headers = [];
	private float _pixelsPerSecond = 100;
	private float _targetScale = 100;
	private float _zoomVelocity;
	private float _zoomTime;
	private float _offset = 8;
	private float _lastFitScale;
	private bool _fitted = true;
	private bool _panning;
	private bool _scrubbing;
	private Vector2 _lastMouse;
	private string _splitterState;
	public float HeaderWidth => _lanes.FirstOrDefault()?.Position.x ?? ClipEventLayout.HeaderWidth;
	public float LaneWidth => _lanes.FirstOrDefault()?.Width ?? MathF.Max( Width - HeaderWidth, 1 );
	public ClipEventLayout Geometry => new( LaneWidth, FrameCount, FrameRate, _pixelsPerSecond, _offset );
	private float FitScale => MathF.Max( LaneWidth - 16, 1 ) * FrameRate / Geometry.LastFrame;
	private float MaxScale => MathF.Max( 1024, FitScale * Geometry.LastFrame );

	private void ClampPan()
	{
		var minimum = MathF.Min( LaneWidth - 8 - Geometry.LastFrame * Geometry.PixelsPerFrame, 8 );
		_offset = _offset.Clamp( minimum, 8 );
	}

	public void Pan( float pixels )
	{
		_offset += pixels;
		ClampPan();
		UpdateTracks( false );
		HeightChanged?.Invoke();
	}

	public void Fit()
	{
		_fitted = true;
		_pixelsPerSecond = _targetScale = FitScale;
		_lastFitScale = FitScale;
		_zoomVelocity = 0;
		_offset = 8;
		UpdateTracks();
		HeightChanged?.Invoke();
	}

	public void Zoom( float amount, float x )
	{
		_zoomTime = (x.Clamp( 0, LaneWidth ) - _offset) / _pixelsPerSecond;
		_targetScale = (_targetScale * MathF.Pow( 2, amount )).Clamp( FitScale, MaxScale );
		_fitted = _targetScale == FitScale;
	}

	public void RevealPlayhead()
	{
		var x = Geometry.FrameToX( Playhead * Geometry.LastFrame );
		var margin = MathF.Min( 128, LaneWidth * 0.25f );
		if ( x < margin )
			Pan( margin - x );
		else if ( x > LaneWidth - margin )
			Pan( LaneWidth - margin - x );
	}

	public void HandleWheel( WheelEvent e, float x )
	{
		if ( e.HasShift )
			Pan( -e.Delta );
		else if ( e.HasCtrl )
			Zoom( e.Delta / 240, x );
		else if ( e.HasAlt )
		{
			Scrubbed?.Invoke( (MathF.Round( Playhead * Geometry.LastFrame ) + MathF.Sign( e.Delta )).Clamp( 0, Geometry.LastFrame ) / Geometry.LastFrame );
			RevealPlayhead();
		}
		else
		{
			e.Ignore();
			return;
		}
		e.Accept();
	}

	[EditorEvent.Frame]
	private void UpdateZoom()
	{
		if ( Document is null )
			return;
		if ( _lastFitScale != FitScale )
		{
			_lastFitScale = FitScale;
			if ( _fitted )
			{
				Fit();
				return;
			}
			_pixelsPerSecond = _pixelsPerSecond.Clamp( FitScale, MaxScale );
			_targetScale = _targetScale.Clamp( FitScale, MaxScale );
			ClampPan();
			UpdateTracks();
			HeightChanged?.Invoke();
		}
		if ( _pixelsPerSecond == _targetScale )
			return;
		var previous = _pixelsPerSecond;
		_pixelsPerSecond = MathX.SmoothDamp( previous, _targetScale, ref _zoomVelocity, 0.3f, RealTime.Delta );
		if ( MathF.Abs( _pixelsPerSecond - _targetScale ) < 0.001f )
			_pixelsPerSecond = _targetScale;
		_offset += _zoomTime * (previous - _pixelsPerSecond);
		ClampPan();
		foreach ( var lane in _lanes )
			lane.LayoutEvents();
		UpdateTracks( false );
		HeightChanged?.Invoke();
	}

	protected override void OnMouseWheel( WheelEvent e ) => HandleWheel( e, e.Position.x - HeaderWidth );

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LocalPosition.x < HeaderWidth )
			return;
		_panning = e.MiddleMouseButton;
		_scrubbing = e.LeftMouseButton || e.RightMouseButton;
		_lastMouse = e.ScreenPosition;
		Focus();
		if ( _scrubbing )
			ScrubAt( e.LocalPosition.x - HeaderWidth );
		e.Accepted = _panning || _scrubbing;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _panning )
		{
			var delta = e.ScreenPosition - _lastMouse;
			Pan( delta.x );
			Scrolled?.Invoke( -delta.y );
		}
		else if ( _scrubbing )
			ScrubAt( e.LocalPosition.x - HeaderWidth );
		else
			return;
		_lastMouse = e.ScreenPosition;
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e ) => _panning = _scrubbing = false;
	protected override void OnKeyPress( KeyEvent e ) => HandleKey( e );

	public void ScrubAt( float x )
	{
		var frame = Geometry.XToFrame( x );
		Scrubbed?.Invoke( (SnapToFrames ? MathF.Round( frame ) : frame) / Geometry.LastFrame );
	}

	public ClipEventLanes( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Layout.Margin = new Sandbox.UI.Margin( 0, 2 );
		Layout.SizeConstraint = SizeConstraint.SetMinimumSize;
		VerticalSizeMode = SizeMode.CanGrow;
		FocusMode = FocusMode.Click;
	}

	public void SetClip( ClipDocument document, int frameCount, string emptyHint )
	{
		FinishDrag();
		if ( Document != document )
		{
			_fitted = true;
			_lastFitScale = 0;
		}
		Document = document;
		FrameCount = frameCount;
		if ( document is not null && _lanes.Count > 0 && _lanes.Count == document.Tracks.Count )
		{
			for ( var i = 0; i < _lanes.Count; i++ )
			{
				_lanes[i].SetTrack( document.Tracks[i] );
				_headers[i] = (document.Tracks[i], _headers[i].Title);
			}
			UpdateTracks();
			return;
		}

		if ( _lanes.FirstOrDefault()?.Parent is Splitter previousRow )
			_splitterState = previousRow.SaveState();
		Layout.Clear( true );
		_lanes.Clear();
		_headers.Clear();

		if ( document is null || document.Tracks.Count == 0 )
		{
			Layout.Add( new Label( document is null ? emptyHint : "Add an event to start editing.", this )
			{ WordWrap = true, Alignment = TextFlag.Center, MinimumHeight = 80, FixedWidth = HeaderWidth, Color = Theme.TextControl } );
			return;
		}

		LinkableSplitter firstRow = null;
		foreach ( var track in document.Tracks )
		{
			var row = Layout.Add( new LinkableSplitter( Orientation.Horizontal, this ) { FixedHeight = ClipEventLayout.LaneHeight } );
			row.SetStyles( $"QSplitter::handle:horizontal {{ background: {Theme.SurfaceBackground.Hex}; margin: 0px; }}" );
			var title = new Label( row )
			{
				Size = new Vector2( ClipEventLayout.HeaderWidth - 2, ClipEventLayout.LaneHeight ),
				MinimumWidth = 120,
				Margin = 0,
				Alignment = TextFlag.LeftCenter,
				ToolTip = "Right-click for track settings"
			};
			row.AddWidget( title );
			_headers.Add( (track, title) );
			var lane = new Lane( this, track );
			row.AddWidget( lane );
			row.SetStretch( 0, 0 );
			row.SetStretch( 1, 1 );
			row.SetCollapsible( 0, false );
			row.SetCollapsible( 1, false );
			if ( _splitterState is not null )
				row.RestoreState( _splitterState );
			row.HandleWidth = 2;
			if ( firstRow is not null )
				row.LinkWith( firstRow );
			firstRow ??= row;
			_lanes.Add( lane );
			BindHeader( title, lane );
		}
		Layout.AddStretchCell( 1 );
		UpdateTracks();
	}

	private void BindHeader( Label title, Lane lane )
	{
		title.OnPaintOverride = () =>
		{
			var track = lane.Track;
			var alpha = track.IsDisabled ? 0.4f : 1f;
			Paint.Antialiasing = true;
			var background = Theme.SurfaceBackground.LerpTo( Theme.ControlBackground, track.IsDisabled ? 0.5f : 0 );
			if ( track == SelectedTrack )
				background = background.LerpTo( Theme.Primary, track.IsDisabled ? 0.2f : 0.5f );
			if ( Paint.HasMouseOver )
				background = background.Lighten( 0.25f );
			Paint.ClearPen();
			Paint.SetBrush( background );
			Paint.DrawRect( new Rect( 2, 0, title.Width - 4, title.Height ), 4 );
			Paint.SetPen( ClipEventTypes.TrackColor( track.EventClass ).WithAlpha( alpha ) );
			Paint.DrawIcon( new Rect( 4, 2, 16, title.Height - 4 ), ClipEventTypes.Icon( track.EventClass ), 16 );
			Paint.SetDefaultFont();
			Paint.SetPen( Theme.TextControl.WithAlpha( alpha * 0.65f ) );
			var badge = new Rect( title.Width - 28, 2, 24, title.Height - 4 );
			Paint.DrawText( badge, $"{track.Events.Count}", TextFlag.Center );
			var textRight = badge.Left - 2;
			if ( track.IsDisabled )
			{
				Paint.DrawIcon( new Rect( textRight - 16, 2, 16, title.Height - 4 ), "visibility_off", 14 );
				textRight -= 18;
			}
			if ( track.IsSyncTrack )
			{
				Paint.DrawIcon( new Rect( textRight - 16, 2, 16, title.Height - 4 ), "sync", 14 );
				textRight -= 18;
			}
			Paint.SetDefaultFont();
			Paint.SetPen( title.Color );
			var textRect = new Rect( 22, 2, textRight - 22, title.Height - 4 );
			Paint.DrawText( textRect, Paint.GetElidedText( title.Text, textRect.Width ), TextFlag.LeftCenter );
			return true;
		};
		title.MouseLeftPress = () => Selected?.Invoke( lane.Track, null );
		title.MouseRightPress = () =>
		{
			Selected?.Invoke( lane.Track, null );
			ContextMenu?.Invoke( lane.Track, Playhead * MathF.Max( FrameCount - 1, 1 ) );
		};
	}

	public void UpdateTracks( bool layout = true )
	{
		if ( layout )
		{
			foreach ( var (track, title) in _headers )
			{
				var matching = Document.Tracks.Where( other => other.EventClass == track.EventClass ).ToList();
				title.Text = track.EventClass == ClipEventTypes.Generic ? "Events" : $"{ClipEventTypes.Title( track.EventClass )} Events";
				if ( matching.Count > 1 )
					title.Text += $" {matching.IndexOf( track ) + 1}";
				title.Color = (track == SelectedTrack ? Theme.Text : Theme.TextControl).WithAlpha( track.IsDisabled ? 0.35f : 1 );
				title.ToolTip = $"{(track.IsDisabled ? "Disabled track. " : "")}{(track.IsSyncTrack ? "Sync track. " : "")}Right-click for track settings";
				title.Update();
			}
		}
		foreach ( var lane in _lanes )
		{
			if ( layout )
				lane.LayoutEvents();
			lane.Update();
		}
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = false;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );
		if ( Document is null )
			return;

		var layout = Geometry;
		Paint.SetBrush( ClipEventLayout.RangeBackground );
		Paint.DrawRect( new Rect( HeaderWidth + layout.VisibleLeft, 0, layout.VisibleRight - layout.VisibleLeft, Height ) );
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.02f ) );
		foreach ( var frame in layout.RulerFrames( true ) )
		{
			var grid = HeaderWidth + MathF.Round( layout.FrameToX( frame ) ) + 0.5f;
			if ( grid < HeaderWidth ) continue;
			Paint.DrawLine( new Vector2( grid, 0 ), new Vector2( grid, Height ) );
		}
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.02f ), 2 );
		foreach ( var frame in layout.RulerFrames() )
		{
			var grid = HeaderWidth + MathF.Round( layout.FrameToX( frame ) ) + 0.5f;
			if ( grid < HeaderWidth ) continue;
			Paint.DrawLine( new Vector2( grid, 0 ), new Vector2( grid, Height ) );
		}
		var x = HeaderWidth + MathF.Round( layout.FrameToX( Playhead * layout.LastFrame ) ) + 0.5f;
		Paint.SetPen( Theme.Yellow.WithAlpha( 0.5f ) );
		if ( x >= HeaderWidth )
			Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Height ) );
		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( new Rect( HeaderWidth - 2, 0, 2, Height ) );
	}

	public void FinishDrag()
	{
		foreach ( var lane in _lanes )
			lane.FinishDrag();
	}

	public void HandleKey( KeyEvent e )
	{
		if ( !e.HasCtrl && !e.HasAlt && e.Key == KeyCode.F )
		{
			Fit();
		}
		else if ( e.HasCtrl && e.Key is KeyCode.C or KeyCode.V )
		{
			if ( e.Key == KeyCode.C )
				Copy?.Invoke();
			else if ( !ReadOnly )
				Paste?.Invoke();
		}
		else if ( SelectedEvent is { } item )
		{
			if ( e.Key is KeyCode.Enter or KeyCode.Return )
				EditEvent?.Invoke();
			else if ( e.Key == KeyCode.Delete && !ReadOnly )
				DeleteEvent?.Invoke();
			else if ( !ReadOnly && !e.HasCtrl && !e.HasAlt && e.Key is KeyCode.Left or KeyCode.Right )
			{
				var last = MathF.Max( FrameCount - 1, 1 );
				var offset = (e.Key == KeyCode.Left ? -1 : 1) * (e.HasShift ? 10 : 1);
				var frame = (item.StartFrame + offset).Clamp( 0, MathF.Max( last - item.DurationFrames, 0 ) );
				if ( frame != item.StartFrame )
				{
					item.StartFrame = frame;
					Scrubbed?.Invoke( frame / last );
					Edited?.Invoke();
					UpdateTracks();
				}
			}
			else
				return;
		}
		else
			return;
		e.Accepted = true;
	}

	private sealed class Lane : Widget
	{
		private readonly ClipEventLanes _owner;
		private ClipDocument.Track _track;
		public ClipDocument.Track Track => _track;
		private ClipDocument.Event _hover;
		private ClipDocument.Event _drag;
		private Vector2 _press;
		private float _pressFrame;
		private float _start;
		private float _end;
		private int _edge;
		private bool _moved;
		private bool _panning;
		private bool _rightScrubbing;
		private bool _rightMoved;
		private bool _leftScrubbing;
		private Vector2 _lastMouse;
		private float _layoutWidth = -1;
		private readonly Dictionary<ClipDocument.Event, float> _eventRows = [];
		public float TrackHeight { get; private set; } = ClipEventLayout.LaneHeight;

		public void SetTrack( ClipDocument.Track track )
		{
			_track = track;
			_hover = null;
		}

		private ClipEventLayout Geometry => _owner.Geometry;
		private string EventLabel( ClipDocument.Event item ) => ClipEventTypes.Summary( item, _track.EventClass ) is { Length: > 0 } text ? text : "Event";
		private Rect MarkerRect( ClipDocument.Event item ) => Geometry.EventRect( item, Paint.MeasureText( EventLabel( item ) ).x );

		public void LayoutEvents()
		{
			Paint.SetDefaultFont();
			_layoutWidth = Width;
			_eventRows.Clear();
			var ends = new List<float>();
			var layout = Geometry;
			foreach ( var item in _track.Events.OrderBy( item => item.StartFrame ) )
			{
				var pill = layout.PillRect( item );
				var label = layout.LabelRect( item, MathF.Ceiling( Paint.MeasureText( EventLabel( item ) ).x ) + 1 );
				var right = MathF.Max( pill.Right, label.Right + (label.Right > pill.Right ? 4 : 0) );
				var row = ends.FindIndex( end => end <= pill.Left );
				if ( row < 0 )
				{
					row = ends.Count;
					ends.Add( right );
				}
				else
					ends[row] = right;
				_eventRows[item] = row * ClipEventLayout.LaneHeight;
			}
			var height = MathF.Max( ends.Count * ClipEventLayout.LaneHeight, ClipEventLayout.LaneHeight );
			if ( TrackHeight != height )
			{
				TrackHeight = height;
				Parent.FixedHeight = height;
			}
			_owner.HeightChanged?.Invoke();
		}

		protected override void DoLayout()
		{
			if ( _owner is not null && Parent != _owner && _layoutWidth != Width )
				LayoutEvents();
		}

		private Rect EventRect( ClipDocument.Event item )
		{
			var rect = MarkerRect( item );
			return new Rect( rect.Left, rect.Top + _eventRows.GetValueOrDefault( item ), rect.Width, rect.Height );
		}

		public Lane( ClipEventLanes owner, ClipDocument.Track track ) : base( owner )
		{
			_owner = owner;
			_track = track;
			FocusMode = FocusMode.Click;
			MouseTracking = true;
			MinimumWidth = 80;
		}

		protected override void OnPaint()
		{
			var layout = Geometry;
			Paint.SetDefaultFont();
			Paint.Antialiasing = false;
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( LocalRect );
			Paint.SetBrush( ClipEventLayout.RangeBackground );
			Paint.DrawRect( new Rect( layout.VisibleLeft, 0, layout.VisibleRight - layout.VisibleLeft, Height ) );
			if ( _owner.SelectedTrack == _track )
			{
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.06f ) );
				Paint.DrawRect( LocalRect );
			}
			layout.PaintGrid( Height );
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.02f ) );
			Paint.DrawLine( new Vector2( 0, Height - 0.5f ), new Vector2( Width, Height - 0.5f ) );

			foreach ( var item in _track.Events )
			{
				if ( item != _owner.SelectedEvent )
					PaintEvent( layout, item );
			}
			if ( _owner.SelectedTrack == _track && _owner.SelectedEvent is { } selected )
				PaintEvent( layout, selected );

			var playhead = MathF.Round( layout.FrameToX( _owner.Playhead * layout.LastFrame ) ) + 0.5f;
			Paint.SetPen( Theme.Yellow.WithAlpha( 0.5f ), 1 );
			Paint.DrawLine( new Vector2( playhead, 0 ), new Vector2( playhead, Height ) );
		}

		private void PaintEvent( ClipEventLayout layout, ClipDocument.Event item )
		{
			var pill = layout.PillRect( item );
			var offset = new Vector2( 0, _eventRows.GetValueOrDefault( item ) );
			pill.Position += offset;
			var selected = item == _owner.SelectedEvent;
			var hovered = item == _hover;
			var invalid = ClipEventTypes.IsOutsideClip( item, layout.LastFrame ) || ClipEventTypes.Clashes( _track, item, layout.LastFrame );
			var color = invalid ? Theme.Red : ClipEventTypes.TrackColor( _track.EventClass );
			var alpha = _track.IsDisabled ? 0.35f : 1f;
			var start = layout.FrameToX( item.StartFrame ).Clamp( layout.Left, layout.Right );
			var end = layout.FrameToX( item.EndFrame ).Clamp( start, layout.Right );
			Paint.ClearPen();
			Paint.Antialiasing = true;
			if ( item.DurationFrames > 0 )
			{
				Paint.SetBrush( Theme.ControlBackground.LerpTo( color, selected ? 0.45f : hovered ? 0.35f : 0.22f ).WithAlpha( alpha ) );
				Paint.SetPen( (selected && !invalid ? Theme.Text : color).WithAlpha( alpha * (selected ? 0.9f : hovered ? 0.75f : 0.4f) ) );
				Paint.DrawRect( pill, 3 );
				if ( (selected || hovered) && pill.Width >= 8 )
				{
					Paint.SetPen( Theme.Text.WithAlpha( alpha * 0.6f ), 2 );
					Paint.DrawLine( new Vector2( start + 2, pill.Top + 6 ), new Vector2( start + 2, pill.Bottom - 6 ) );
					Paint.DrawLine( new Vector2( end - 2, pill.Top + 6 ), new Vector2( end - 2, pill.Bottom - 6 ) );
				}
			}
			else
			{
				Paint.SetBrush( (selected && !invalid ? Theme.Text : color).WithAlpha( alpha * (selected || hovered ? 1f : 0.75f) ) );
				Paint.DrawPolygon( new Vector2( start, pill.Center.y - 5 ), new Vector2( start + 5, pill.Center.y ),
					new Vector2( start, pill.Center.y + 5 ), new Vector2( start - 5, pill.Center.y ) );
			}

			Paint.SetDefaultFont();
			var text = EventLabel( item );
			var textRect = layout.LabelRect( item, MathF.Ceiling( Paint.MeasureText( text ).x ) + 1 );
			textRect.Position += offset;
			textRect.Width = MathF.Min( textRect.Width, MathF.Max( Width - textRect.Left, 0 ) );
			if ( textRect.Width <= 0 )
				return;
			Paint.SetPen( Theme.Text.WithAlpha( alpha ) );
			Paint.DrawText( textRect, Paint.GetElidedText( text, textRect.Width ), TextFlag.LeftCenter );
		}

		private ClipDocument.Event HitEvent( Vector2 position ) =>
			_owner.SelectedTrack == _track && _owner.SelectedEvent is { } selected && EventRect( selected ).IsInside( position )
				? selected : _track.Events.LastOrDefault( item => EventRect( item ).IsInside( position ) );

		protected override void OnMousePress( MouseEvent e )
		{
			if ( e.MiddleMouseButton )
			{
				_panning = true;
				_lastMouse = e.ScreenPosition;
				e.Accepted = true;
				return;
			}
			if ( !e.LeftMouseButton && !e.RightMouseButton )
				return;
			Focus();
			e.Accepted = true;
			if ( e.RightMouseButton )
			{
				_rightScrubbing = true;
				_rightMoved = false;
				_press = e.LocalPosition;
				return;
			}
			var item = HitEvent( e.LocalPosition );
			_owner.Selected?.Invoke( _track, item );
			if ( item is null )
			{
				_leftScrubbing = true;
				_owner.ScrubAt( e.LocalPosition.x );
				return;
			}
			if ( ReadOnly )
				return;
			_drag = item;
			_owner._targetScale = _owner._pixelsPerSecond;
			_owner._zoomVelocity = 0;
			_press = e.LocalPosition;
			_pressFrame = (e.LocalPosition.x - Geometry.Left) / Geometry.PixelsPerFrame;
			_start = item.StartFrame;
			_end = item.EndFrame;
			_edge = HitEdge( item, e.LocalPosition.x );
			_moved = false;
		}

		private int HitEdge( ClipDocument.Event item, float x )
		{
			if ( item.DurationFrames <= 0 )
				return 0;
			if ( MathF.Abs( Geometry.FrameToX( item.EndFrame ) - x ) < 5 )
				return 1;
			return MathF.Abs( Geometry.FrameToX( item.StartFrame ) - x ) < 5 ? -1 : 0;
		}

		protected override void OnMouseMove( MouseEvent e )
		{
			if ( _leftScrubbing )
			{
				_owner.ScrubAt( e.LocalPosition.x );
				e.Accepted = true;
				return;
			}
			if ( _panning )
			{
				var movement = e.ScreenPosition - _lastMouse;
				_owner.Pan( movement.x );
				_owner.Scrolled?.Invoke( -movement.y );
				_lastMouse = e.ScreenPosition;
				e.Accepted = true;
				return;
			}
			if ( _rightScrubbing )
			{
				_rightMoved |= MathF.Abs( e.LocalPosition.x - _press.x ) >= 3;
				if ( _rightMoved )
					_owner.ScrubAt( e.LocalPosition.x );
				e.Accepted = true;
				return;
			}
			if ( _drag is null || !e.ButtonState.HasFlag( MouseButtons.Left ) )
			{
				_hover = HitEvent( e.LocalPosition );
				Cursor = _hover is null || ReadOnly ? CursorShape.Arrow : HitEdge( _hover, e.LocalPosition.x ) != 0 ? CursorShape.SizeH : CursorShape.Finger;
				ToolTip = _hover is null ? "Double-click to add an event" : $"{ClipEventTypes.Summary( _hover, _track.EventClass )}\nFrames {_hover.StartFrame:0.##} - {_hover.EndFrame:0.##}\nDouble-click to edit";
				Update();
				return;
			}
			if ( !_moved && MathF.Abs( e.LocalPosition.x - _press.x ) < 3 )
				return;
			_moved = true;
			var layout = Geometry;
			var delta = (e.LocalPosition.x - layout.Left) / layout.PixelsPerFrame - _pressFrame;
			var frame = (_edge > 0 ? _end : _start) + delta;
			if ( _owner.SnapToFrames && !e.HasCtrl )
				frame = MathF.Round( frame );
			if ( _edge > 0 )
				_drag.DurationFrames = frame.Clamp( _start, layout.LastFrame ) - _start;
			else if ( _edge < 0 )
			{
				_drag.StartFrame = frame.Clamp( 0, _end );
				_drag.DurationFrames = _end - _drag.StartFrame;
			}
			else
				_drag.StartFrame = frame.Clamp( 0, MathF.Max( layout.LastFrame - (_end - _start), 0 ) );
			_owner.Scrubbed?.Invoke( _drag.StartFrame / layout.LastFrame );
			Update();
			e.Accepted = true;
		}

		public void FinishDrag()
		{
			var changed = _drag is not null && (_drag.StartFrame != _start || _drag.EndFrame != _end);
			_drag = null;
			_moved = false;
			if ( changed )
				_owner.Edited?.Invoke();
		}

		protected override void OnMouseReleased( MouseEvent e )
		{
			_panning = false;
			_leftScrubbing = false;
			if ( e.RightMouseButton && _rightScrubbing )
			{
				_rightScrubbing = false;
				if ( !_rightMoved )
				{
					_owner.Selected?.Invoke( _track, HitEvent( e.LocalPosition ) );
					_owner.ContextMenu?.Invoke( _track, Geometry.XToFrame( e.LocalPosition.x ) );
				}
				e.Accepted = true;
				return;
			}
			FinishDrag();
		}

		protected override void OnMouseWheel( WheelEvent e ) => _owner.HandleWheel( e, e.Position.x );

		protected override void OnMouseLeave()
		{
			_hover = null;
			Update();
		}

		protected override void OnDoubleClick( MouseEvent e )
		{
			if ( !e.LeftMouseButton )
				return;
			FinishDrag();
			var item = HitEvent( e.LocalPosition );
			_owner.Selected?.Invoke( _track, item );
			if ( item is not null )
				_owner.EditEvent?.Invoke();
			else if ( !ReadOnly )
				_owner.AddEvent?.Invoke( _track, Geometry.XToFrame( e.LocalPosition.x ) );
			e.Accepted = true;
		}

		protected override void OnKeyPress( KeyEvent e )
		{
			if ( e.Key == KeyCode.Escape && _drag is { } item )
			{
				item.StartFrame = _start;
				item.DurationFrames = _end - _start;
				_drag = null;
				Update();
				e.Accepted = true;
				return;
			}
			_owner.HandleKey( e );
		}
	}
}
