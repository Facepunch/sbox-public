using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

/// <summary>
/// A bar with times and notches on it
/// </summary>
public class ScrubBar : BackgroundItem, ISnapSource, IMovieItem
{
	[FromTheme]
	public static Color TimeLabelColor { get; set; } = Theme.Green.WithAlpha( 0.25f );

	[FromTheme]
	public static new float Height { get; set; } = 24f;

	public Session Session => Timeline.Session;

	public bool IsTop { get; }

	MovieTimeRange IMovieItem.TimeRange => (MovieTime.Zero, MovieTime.MaxValue);

	bool IMovieItem.OverridesMouseEvents => true;

	public ScrubBar( Timeline timeline, bool isTop )
		: base( timeline )
	{
		IsTop = isTop;

		ZIndex = 450;

		HoverEvents = true;
		Selectable = true;

		base.Height = Height;
	}

	private MovieTime _dragStartTime;
	private float _panSpeed;

	public override void Frame()
	{
		Cursor = (Application.KeyboardModifiers & KeyboardModifiers.Alt) != 0
			? CursorShape.IBeam
			: CursorShape.Finger;

		if ( !_panSpeed.AlmostEqual( 0f ) )
		{
			var delta = -_panSpeed * RealTime.Delta;

			Timeline.PanImmediate( delta );

			_lastScrub.ScenePos -= new Vector2( delta, 0f );

			OnScrubUpdate();
		}

		base.Frame();
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		var time = Timeline.ScenePositionToTime( ToScene( e.LocalPosition ), new SnapOptions( source => source is not TimeCursor ) );

		if ( e.MiddleMouseButton )
		{
			// Panning handled by timeline

			return;
		}

		e.Accepted = true;

		if ( e.RightMouseButton )
		{
			ShowContextMenu( time );
			return;
		}

		StartScrubbing( time, e.KeyboardModifiers );
	}

	public void StartScrubbing( MovieTime time, KeyboardModifiers modifiers )
	{
		if ( (modifiers & KeyboardModifiers.Alt) != 0 )
		{
			// Alt+Click: set loop time range

			_dragStartTime = time;
			Timeline.Session.LoopTimeRange = null;
		}
		else
		{
			// Click: set playhead time

			Timeline.Session.PlayheadTime = time;
		}

		_panSpeed = 0f;

		Update();
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		base.OnMouseReleased(e);

		StopScrubbing();
	}

	public void StopScrubbing()
	{
		_panSpeed = 0f;
	}

	private void ShowContextMenu( MovieTime time )
	{
		var menu = new Menu();

		menu.AddHeading( "Preview Loop" );

		menu.AddOption( "Set Start", "start", () =>
		{
			Session.LoopTimeRange = Session.LoopTimeRange is not { } range || range.End <= time
				? (time, Session.Duration)
				: (time, range.End);
		} ).Enabled = Session.LoopTimeRange is null || Session.LoopTimeRange.Value.End > time;

		menu.AddOption( "Set End", "last_page", () =>
		{
			Session.LoopTimeRange = Session.LoopTimeRange is not { } range || range.Start >= time
				? (0d, time)
				: (range.Start, time);
		} ).Enabled = Session.LoopTimeRange is null || Session.LoopTimeRange.Value.Start < time;

		menu.AddOption( "Clear", "clear", () =>
		{
			Session.LoopTimeRange = null;
		} ).Enabled = Session.LoopTimeRange is not null;

		menu.OpenAtCursor();
	}

	private (KeyboardModifiers KeyboardModifiers, Vector2 ScenePos) _lastScrub;

	protected override void OnMouseMove( GraphicsMouseEvent e )
	{
		if ( !e.LeftMouseButton ) return;

		Scrub( e.KeyboardModifiers, ToScene( e.LocalPosition ) );
	}

	public void Scrub( KeyboardModifiers modifiers, Vector2 scenePos )
	{
		_lastScrub = (modifiers, scenePos);

		OnScrubUpdate();
	}

	private void OnScrubUpdate()
	{
		var (modifiers, scenePos) = _lastScrub;

		var sceneView = Timeline.VisibleRect;

		if ( scenePos.x > sceneView.Right )
		{
			_panSpeed = (scenePos.x - sceneView.Right) * 5f;
			scenePos.x = sceneView.Right;
		}
		else if ( scenePos.x < sceneView.Left )
		{
			_panSpeed = (scenePos.x - sceneView.Left) * 5f;
			scenePos.x = sceneView.Left;
		}
		else
		{
			_panSpeed = 0f;
		}

		var time = Timeline.ScenePositionToTime( scenePos, new SnapOptions( source => source is not TimeCursor ) );

		if ( (modifiers & KeyboardModifiers.Alt) != 0 )
		{
			if ( time != _dragStartTime )
			{
				// Alt+Click+Drag: set loop time range

				Session.LoopTimeRange = new MovieTimeRange(
					MovieTime.Min( time, _dragStartTime ),
					MovieTime.Max( time, _dragStartTime ) );
			}
			else
			{
				// Alt+Click: clear loop time range

				Session.LoopTimeRange = null;
			}
		}
		else
		{
			// Click: set playhead time

			Session.PlayheadTime = time;
		}

		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var duration = Session.Duration;
		var baseColor = Session.EditMode?.ScrubBarOverrideColor ?? Timeline.BackgroundColor;

		Paint.SetBrushAndPen( baseColor );
		Paint.DrawRect( LocalRect );

		// Darker background for the clip duration

		if ( Session.SequenceTimeRange is { } sequenceRange )
		{
			Paint.SetBrushAndPen( baseColor.TintRelative( Timeline.BackgroundColor, Timeline.OuterColor ) );
			DrawTimeRangeRect( (MovieTime.Zero, duration) );

			Paint.SetBrushAndPen( baseColor.TintRelative( Timeline.BackgroundColor, Timeline.InnerColor ) );
			DrawTimeRangeRect( sequenceRange );
		}
		else
		{
			Paint.SetBrushAndPen( baseColor.TintRelative( Timeline.BackgroundColor, Timeline.InnerColor ) );
			DrawTimeRangeRect( (MovieTime.Zero, duration) );
		}

		// Paste time range

		if ( Session.EditMode?.SourceTimeRange is { } pasteRange )
		{
			var startX = FromScene( Timeline.TimeToPixels( pasteRange.Start ) ).x;
			var endX = FromScene( Timeline.TimeToPixels( pasteRange.End ) ).x;

			var rect = new Rect( new Vector2( startX, LocalRect.Top ), new Vector2( endX - startX, LocalRect.Height ) );

			Paint.SetBrushAndPen( Color.White.WithAlpha( 0.2f ) );
			Paint.DrawRect( rect );

			Paint.PenSize = 1;
			Paint.Pen = Color.White.WithAlpha( 0.5f );
			Paint.DrawLine( rect.TopLeft, rect.BottomLeft );
			Paint.DrawLine( rect.TopRight, rect.BottomRight );
			Paint.DrawIcon( rect, "content_paste", 16f );
		}

		// Loop time range

		if ( Session.LoopTimeRange is { } loopRange )
		{
			var startX = FromScene( Timeline.TimeToPixels( loopRange.Start ) ).x;
			var endX = FromScene( Timeline.TimeToPixels( loopRange.End ) ).x;

			var rect = new Rect( new Vector2( startX, LocalRect.Top ), new Vector2( endX - startX, LocalRect.Height ) );

			Paint.SetBrushAndPen( Color.White.WithAlpha( 0.05f ) );
			Paint.DrawRect( rect );

			Paint.ClearBrush();
			Paint.SetPen( Color.White );
			Paint.DrawLine( rect.TopLeft, rect.BottomLeft );
			Paint.DrawLine( rect.TopRight, rect.BottomRight );

			var top = IsTop ? rect.Bottom : rect.Top;
			var up = IsTop ? new Vector2( 0f, -1f ) : new Vector2( 0f, 1f );
			var leftCorner = new Vector2( rect.Left, top );
			var rightCorner = new Vector2( rect.Right, top );

			Paint.SetBrush( Color.White.WithAlpha( 0.5f ) );
			Paint.DrawPolygon( leftCorner, leftCorner + up * 6f, leftCorner + new Vector2( 6f, 0f ) );
			Paint.DrawPolygon( rightCorner, rightCorner + up * 6f, rightCorner - new Vector2( 6f, 0f ) );
		}

		var range = Timeline.VisibleTimeRange;

		Paint.PenSize = 2;
		Paint.Pen = Color.White.WithAlpha( 0.1f );

		if ( IsTop )
		{
			Paint.DrawLine( LocalRect.BottomLeft, LocalRect.BottomRight );
		}
		else
		{
			Paint.DrawLine( LocalRect.TopLeft, LocalRect.TopRight );
		}

		Paint.Antialiasing = true;
		Paint.SetFont( "Roboto", 8, 300 );

		foreach ( var (style, interval) in Timeline.Ticks )
		{
			var height = Height;
			var margin = 2f;

			switch ( style )
			{
				case TickStyle.TimeLabel:
					Paint.SetPen( Theme.Green.WithAlpha( 0.2f ) );
					height -= 12f;
					margin = 10f;
					break;

				default:
					continue;
			}

			var y = IsTop ? Height - height - margin : margin;

			var t0 = MovieTime.Max( (range.Start - interval).Round( interval ), MovieTime.Zero );
			var t1 = t0 + range.Duration + interval * 2;

			for ( var t = t0; t <= t1; t += interval )
			{
				var x = FromScene( Timeline.TimeToPixels( t ) ).x;

				Paint.SetPen( TimeLabelColor );

				var labelPos = new Vector2( x + 6, y );
				var time = Timeline.PixelsToTime( ToScene( x ).x );

				if ( IsTop )
				{
					Paint.DrawText( labelPos, TimeToString( time, interval ) );
				}
				else
				{
					var frame = time.GetFrameIndex( Session.FrameRate );
					Paint.DrawText( labelPos, frame.ToString( "0000" ) );
				}
			}
		}
	}

	private void DrawTimeRangeRect( MovieTimeRange timeRange )
	{
		var startX = FromScene( Timeline.TimeToPixels( timeRange.Start ) ).x;
		var endX = FromScene( Timeline.TimeToPixels( timeRange.End ) ).x;

		Paint.DrawRect( new Rect( new Vector2( startX, LocalRect.Top ), new Vector2( endX - startX, LocalRect.Height ) ) );
	}

	private static string TimeToString( MovieTime time, MovieTime interval )
	{
		return time.ToString();
	}

	public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary )
	{
		if ( Session.EditMode?.SourceTimeRange is { } sourceRange )
		{
			yield return sourceRange.Start;
			yield return sourceRange.End;
		}

		if ( Session.LoopTimeRange is { } loopRange )
		{
			yield return loopRange.Start;
			yield return loopRange.End;
		}

		if ( !isPrimary ) yield break;

		yield return new SnapTarget( sourceTime.Round( Timeline.MinorTick.Interval ), -2 );
		yield return new SnapTarget( sourceTime.Round( Timeline.MajorTick.Interval ), -1 );
	}
}
