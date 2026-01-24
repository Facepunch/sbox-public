using Sandbox;
using Sandbox.MovieMaker;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	private abstract class TimeSelectionItem : GraphicsItem, IMovieDraggable, IMovieContextMenu
	{
		/// <summary>
		/// Capture time selection before being dragged so we can revert etc.
		/// </summary>
		protected TimeSelection? OriginalSelection { get; private set; }
		protected IModificationOptions? OriginalModificationOptions { get; private set; }

		public MotionEditMode EditMode { get; }

		protected TimeSelectionItem( MotionEditMode editMode )
		{
			EditMode = editMode;

			HoverEvents = true;
			Focusable = true;
			Selectable = true;
		}

		public abstract void UpdatePosition( TimeSelection value, Rect viewRect );

		public override bool Contains( Vector2 localPos )
		{
			return BoundingRect.Shrink( 0f, 24f, 0f, 24f ).IsInside( localPos );
		}

		public abstract MovieTimeRange TimeRange { get; }

		public virtual (MovieTime? Min, MovieTime? Max) DragLimits => (-TimeRange.Start, null);

		public virtual CursorShape DragCursor => CursorShape.Finger;
		public virtual IEnumerable<MovieTime> SnapSources => [TimeRange.Start, TimeRange.End];

		void IMovieDraggable.StartDrag()
		{
			OriginalSelection = EditMode.TimeSelection;
			OriginalModificationOptions = EditMode.Modification?.Options;

			OnStartDrag();
		}

		protected virtual void OnStartDrag() { }

		void IMovieDraggable.Drag( MovieTime delta ) => OnDrag( delta );

		protected virtual void OnDrag( MovieTime delta ) { }

		void IMovieDraggable.EndDrag()
		{
			try
			{
				OnEndDrag();
			}
			finally
			{
				OriginalSelection = null;
				OriginalModificationOptions = null;
			}
		}

		protected virtual void OnEndDrag() { }

		bool IMovieDraggable.SnapFilter( ISnapSource source ) => OnSnapFilter( source );

		protected virtual bool OnSnapFilter( ISnapSource source ) => source is not TimeSelectionItem;

		public void ShowContextMenu( ContextMenuEvent ev )
		{
			if ( EditMode.TimeSelection is not { } selection )
			{
				return;
			}

			ev.Title = selection.TotalTimeRange.ToString();
			ev.Accepted = true;

			EditMode.AddTimeSelectionContextMenu( ev, selection );

			OnShowContextMenu( ev );

			EditMode.AddClipboardContextMenu( ev, selection.TotalTimeRange );
		}

		protected virtual void OnShowContextMenu( ContextMenuEvent ev )
		{

		}
	}

	/// <summary>
	/// Inner region of the timeline selection. Dragging it moves the whole selection left / right.
	/// </summary>
	private sealed class TimeSelectionPeakItem : TimeSelectionItem
	{
		public override MovieTimeRange TimeRange => EditMode.TimeSelection?.PeakTimeRange ?? default;

		public override (MovieTime? Min, MovieTime? Max) DragLimits => (-EditMode.TimeSelection?.TotalStart, null);

		public override IEnumerable<MovieTime> SnapSources
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection ) return [];

				return [selection.TotalStart, selection.PeakStart, selection.PeakEnd, selection.TotalEnd];
			}
		}

		public TimeSelectionPeakItem( MotionEditMode editMode )
			: base( editMode )
		{
			ZIndex = 10000;
			Cursor = CursorShape.Finger;
		}

		protected override void OnDrag( MovieTime delta )
		{
			if ( EditMode.TimeSelection is not { } current ) return;

			if ( EditMode.Modification?.Options is ITranslatableOptions translatable )
			{
				EditMode.Modification.Options = translatable.WithOffset( translatable.Offset + delta );
			}

			EditMode.TimeSelection = current + delta;
		}

		public override void UpdatePosition( TimeSelection value, Rect viewRect )
		{
			PrepareGeometryChange();

			var timeRange = value.PeakTimeRange;
			var timeline = EditMode.Timeline;

			Position = new Vector2( timeline.TimeToPixels( timeRange.Start ), viewRect.Top );
			Size = new Vector2( timeline.TimeToPixels( timeRange.Duration ), viewRect.Height );

			Update();
		}

		protected override void OnPaint()
		{
			var color = EditMode.SelectionColor;

			if ( Hovered ) color = color.Lighten( 0.1f );

			Paint.Antialiasing = true;

			Paint.SetBrush( color );
			Paint.SetPen( Color.White.WithAlpha( 0.5f ), 0.5f );
			Paint.DrawRect( LocalRect.Grow( 0f, 16f ) );

			if ( EditMode.LastActionIcon is { } icon && EditMode._lastActionTime < 1f )
			{
				var t = 1f - EditMode._lastActionTime;

				Paint.SetPen( Color.White.WithAlpha( t * t * t * t ) );
				Paint.DrawIcon( LocalRect.Grow( 32f, 0f ), icon, 32f );
			}
		}
	}

	private enum FadeKind
	{
		FadeIn,
		FadeOut
	}

	/// <summary>
	/// Fade in / out region of the timeline selection. Dragging it moves the fade left / right.
	/// If the selection has a zero-width peak (it fades out right after fading in), then
	/// you can move the whole selection by starting a drag in the direction of the other
	/// fade item.
	/// </summary>
	private sealed class TimeSelectionFadeItem : TimeSelectionItem
	{
		private bool? _moveWholeSelection;

		public FadeKind Kind { get; }

		public override MovieTimeRange TimeRange
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection )
				{
					return default;
				}

				if ( _moveWholeSelection is not false )
				{
					return selection.TotalTimeRange;
				}

				return Kind == FadeKind.FadeIn
					? selection.FadeInTimeRange
					: selection.FadeOutTimeRange;
			}
		}

		public InterpolationMode? Interpolation
		{
			get => Kind == FadeKind.FadeIn
				? EditMode.TimeSelection?.FadeIn.Interpolation
				: EditMode.TimeSelection?.FadeOut.Interpolation;

			set
			{
				if ( EditMode.TimeSelection is not { } selection ) return;
				if ( value is not { } mode ) return;

				EditMode.TimeSelection = Kind == FadeKind.FadeIn
					? selection with { FadeIn = selection.FadeIn with { Interpolation = mode } }
					: selection with { FadeOut = selection.FadeOut with { Interpolation = mode } };
			}
		}

		public override (MovieTime? Min, MovieTime? Max) DragLimits
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection )
				{
					return default;
				}

				if ( _moveWholeSelection is not false )
				{
					return (-selection.TotalStart, null);
				}

				return Kind == FadeKind.FadeIn
					? (-selection.TotalStart, selection.PeakTimeRange.Duration)
					: (-selection.PeakTimeRange.Duration, null);
			}
		}

		public override IEnumerable<MovieTime> SnapSources
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection )
				{
					return [];
				}

				return _moveWholeSelection is not false
					? [selection.TotalStart, selection.PeakStart, selection.TotalEnd]
					: base.SnapSources;
			}
		}

		public TimeSelectionFadeItem( MotionEditMode editMode, FadeKind kind )
			: base( editMode )
		{
			Kind = kind;
			ZIndex = 10001;
			HandlePosition = kind == FadeKind.FadeIn ? new Vector2( 1f, 0f ) : new Vector2( 0f, 0f );
			Cursor = CursorShape.Finger;
		}

		public override void UpdatePosition( TimeSelection value, Rect viewRect )
		{
			PrepareGeometryChange();

			if ( EditMode.TimeSelection is not { } selection )
			{
				Position = new Vector2( -50000f, 0f );
			}
			else
			{
				var timeRange = Kind == FadeKind.FadeIn
					? selection.FadeInTimeRange
					: selection.FadeOutTimeRange;
				var timeline = EditMode.Timeline;

				Position = new Vector2( timeline.TimeToPixels( Kind == FadeKind.FadeIn ? timeRange.End : timeRange.Start ), viewRect.Top );
				Size = new Vector2( timeline.TimeToPixels( timeRange.Duration ), viewRect.Height );
			}

			Update();
		}

		protected override void OnStartDrag()
		{
			if ( OriginalSelection is not { } selection ) return;

			// We can't move the whole selection if there's a peak range between the fade in / fade out.

			_moveWholeSelection = selection.PeakTimeRange.IsEmpty ? null : false;
		}

		protected override void OnDrag( MovieTime delta )
		{
			if ( EditMode.TimeSelection is not { } selection ) return;

			// We want to move the whole time selection if the fade in and fade out are adjacent,
			// and we start trying to "push" the fade in into the fade out (or vice versa).

			_moveWholeSelection ??= Kind == FadeKind.FadeIn && delta.IsPositive || Kind == FadeKind.FadeOut && delta.IsNegative;

			if ( _moveWholeSelection is true )
			{
				if ( EditMode.Modification?.Options is ITranslatableOptions translatable )
				{
					EditMode.Modification.Options = translatable.WithOffset( translatable.Offset + delta );
				}

				EditMode.TimeSelection = selection + delta;
			}
			else if ( Kind == FadeKind.FadeIn )
			{
				EditMode.TimeSelection = selection.WithTimes( totalStart: selection.TotalStart + delta, peakStart: selection.PeakStart + delta );
			}
			else
			{
				EditMode.TimeSelection = selection.WithTimes( peakEnd: selection.PeakEnd + delta, totalEnd: selection.TotalEnd + delta );
			}
		}

		protected override void OnPaint()
		{
			if ( Width < 1f || Interpolation is not { } interpolation ) return;

			var color = EditMode.SelectionColor;

			if ( Hovered ) color = color.Lighten( 0.1f );

			var fadeColor = color.WithAlpha( 0.02f );

			var (x0, x1) = Kind == FadeKind.FadeIn
				? (0f, Width)
				: (Width, 0f);

			Paint.Antialiasing = true;

			Paint.SetBrushLinear( new Vector2( x0, 0f ), new Vector2( x1, 0f ), fadeColor, color );
			Paint.SetPen( color.WithAlpha( 0.5f ), 0.5f );
			PaintExtensions.PaintMirroredCurve( t => interpolation.Apply( t ), LocalRect, ScrubBar.Height, Kind == FadeKind.FadeOut );
		}
	}

	/// <summary>
	/// One of the boundaries between the 3 parts of the selection (fade in / peak / fade out). Drag it to move
	/// just that boundary. If sections of the selection are zero-width (no peak / no fade in / no fade out),
	/// then we work out which handle you wanted to drag based on the direction the drag starts. You can't move
	/// these handles past one another.
	/// </summary>
	private sealed class TimeSelectionHandleItem : TimeSelectionItem, ISnapSource
	{
		private enum Index
		{
			TotalStart,
			PeakStart,
			PeakEnd,
			TotalEnd
		}

		private Index _minIndex;
		private Index _maxIndex;

		private MovieTime? _dragTime;

		public override CursorShape DragCursor => CursorShape.SizeH;

		public TimeSelectionHandleItem( MotionEditMode editMode )
			: base( editMode )
		{
			HandlePosition = new Vector2( 0.5f, 0f );

			ZIndex = 10002;
		}

		public override MovieTimeRange TimeRange
		{
			get
			{
				if ( EditMode.TimeSelection is not { } current )
				{
					return default;
				}

				return _dragTime ?? GetTime( current, GetIndexRange().Min );
			}
		}

		public override (MovieTime? Min, MovieTime? Max) DragLimits
		{
			get
			{
				if ( OriginalSelection is not { } original || EditMode.TimeSelection is not { } current )
				{
					return base.DragLimits;
				}

				// Limit dragging to neighbouring handles

				var curTime = GetTime( current, _minIndex );
				var minTime = GetTime( original, _minIndex - 1 );
				var maxTime = GetTime( original, _maxIndex + 1 );

				return (minTime - curTime, maxTime - curTime);
			}
		}

		protected override void OnStartDrag()
		{
			(_minIndex, _maxIndex) = GetIndexRange();

			if ( OriginalSelection is not { } original ) return;

			_dragTime = GetTime( original, _minIndex );
		}

		protected override void OnDrag( MovieTime delta )
		{
			if ( OriginalSelection is not { } original ) return;
			if ( EditMode.TimeSelection is not { } current ) return;
			if ( _dragTime is not { } time ) return;

			time += delta;

			_dragTime = time;

			// If it's ambiguous which handle we are, pick a side
			// based on which direction we're dragged

			if ( delta.IsNegative ) _maxIndex = _minIndex;
			if ( delta.IsPositive ) _minIndex = _maxIndex;

			EditMode.TimeSelection = SetTime( original, _minIndex, time );
		}

		protected override void OnEndDrag()
		{
			_dragTime = null;
		}

		private static MovieTime GetTime( TimeSelection value, Index index ) => index switch
		{
			Index.TotalStart => value.TotalStart,
			Index.PeakStart => value.PeakStart,
			Index.PeakEnd => value.PeakEnd,
			Index.TotalEnd => value.TotalEnd,
			< 0 => MovieTime.Zero,
			> Index.TotalEnd => MovieTime.MaxValue
		};

		private static TimeSelection SetTime( TimeSelection value, Index index, MovieTime time ) => index switch
		{
			Index.TotalStart => value.WithTimes( totalStart: time ),
			Index.PeakStart => value.WithTimes( peakStart: time ),
			Index.PeakEnd => value.WithTimes( peakEnd: time ),
			Index.TotalEnd => value.WithTimes( totalEnd: time ),
			_ => value
		};

		public override void UpdatePosition( TimeSelection value, Rect viewRect )
		{
			PrepareGeometryChange();

			var time = GetTime( value, GetIndexRange().Min );
			var margin = _maxIndex == Index.TotalStart || _minIndex == Index.TotalEnd
				? ScrubBar.Height
				: 0f;

			Position = new Vector2( EditMode.Timeline.TimeToPixels( time ), viewRect.Top + margin );
			Size = new Vector2( 8f, viewRect.Height - margin * 2f );
		}

		/// <summary>
		/// Get the possible range of handles this could be, if the time selection has
		/// overlapping control points.
		/// </summary>
		private (Index Min, Index Max) GetIndexRange()
		{
			if ( EditMode.TimeSelection is not { } value ) return default;

			var handles = EditMode.Timeline.Items.OfType<TimeSelectionHandleItem>()
				.OrderBy( x => x.Position.x );

			var index = Index.TotalStart + handles.TakeWhile( x => x != this ).Count();

			var minIndex = index;
			var maxIndex = index;

			var time = GetTime( value, index );

			while ( minIndex > Index.TotalStart && GetTime( value, minIndex - 1 ) == time )
			{
				minIndex -= 1;
			}

			while ( maxIndex < Index.TotalEnd && GetTime( value, maxIndex + 1 ) == time )
			{
				maxIndex += 1;
			}

			return (minIndex, maxIndex);
		}

		public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) => [TimeRange.Start];

		protected override void OnPaint()
		{
			if ( Hovered )
			{
				Paint.SetPen( EditMode.SelectionColor.Lighten( 0.1f ).WithAlpha( 1f ) );
				Paint.DrawLine( new Vector2( LocalRect.Center.x, LocalRect.Top ), new Vector2( LocalRect.Center.x, LocalRect.Bottom ) );
			}
		}
	}
}
