using Sandbox.MovieMaker;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

public interface IMovieItem
{
	bool MultiSelectable => false;
	bool MovePlayheadOnSelect => TimeRange.Duration.IsZero;
	bool OverridesMouseEvents => false;

	MovieTimeRange TimeRange { get; }

	void DoubleClick() { }
}

public interface ITrackItem : IMovieItem
{
	TimelineTrack Track { get; }
}

public interface IMovieDraggable : IMovieItem
{
	/// <summary>
	/// Times on this object that can snap to other objects while dragging.
	/// </summary>
	IEnumerable<MovieTime> SnapSources => [TimeRange.Start, TimeRange.End];
	CursorShape DragCursor => CursorShape.Finger;

	/// <summary>
	/// How far this item can be dragged in either direction before hitting a limit.
	/// </summary>
	(MovieTime? Min, MovieTime? Max) DragLimits => (-TimeRange.Start, null);

	void StartDrag() { }
	void Drag( MovieTime delta );
	void EndDrag() { }

	bool SnapFilter( ISnapSource source ) => source != this;
}

public enum BlockEdge
{
	Start,
	End
}

public interface IMovieResizable : IMovieItem
{
	/// <summary>
	/// Full start / end limits when resizing. If null, can be resized without limit.
	/// </summary>
	(MovieTime? Min, MovieTime? Max) ResizeLimits => (MovieTime.Zero, null);

	CursorShape ResizeCursor => CursorShape.SizeH;

	void StartResize( BlockEdge edge ) { }
	void Resize( BlockEdge edge, MovieTime delta );
	void EndResize() { }
}

public interface IMovieContextMenu : IMovieItem
{
	void ShowContextMenu( EditMode.ContextMenuEvent ev );
}

public readonly record struct SnapTarget( MovieTime Time, int Priority = 0, bool Show = true )
{
	public static implicit operator SnapTarget( MovieTime time ) => new( time );
}

/// <summary>
/// Interface for <see cref="GraphicsItem"/>s in a <see cref="Timeline"/> that can be snapped to by other objects.
/// </summary>
public interface ISnapSource
{
	/// <summary>
	/// Only snap if the cursor is within these bounds.
	/// </summary>
	Rect SceneSnapBounds
	{
		get
		{
			// We're assuming this interface is only implemented by GraphicsItems.

			return this is GraphicsItem item
				? item.GetRealSceneRect()
				: throw new NotImplementedException();
		}
	}

	/// <summary>
	/// Absolute times that other objects can snap to.
	/// </summary>
	/// <param name="sourceTime">Time on a dragged object that wants to snap to this.</param>
	/// <param name="isPrimary">True for the closest part of the dragged object to the mouse cursor when dragging.</param>
	IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary );
}

public delegate bool SnapFilter( ISnapSource source );

public sealed record SnapOptions(
	SnapFilter? Filter = null,
	MovieTime? Min = null,
	MovieTime? Max = null,
	params MovieTime[] SnapOffsets )
{
	public SnapOptions( params MovieTime[] snapOffsets )
		: this( null, null, null, snapOffsets )
	{

	}
}

partial class Timeline
{
	private readonly List<IMovieDraggable> _draggedItems = new();
	private readonly List<(IMovieResizable Item, BlockEdge Edge)> _resizedItems = new();
	private MovieTime _lastDragTime;
	private SnapOptions? _dragSnapOptions;
	private IHistoryScope? _dragScope;

	public bool IsDragging => _draggedItems.Count > 0 || _resizedItems.Count > 0;

	public Rect GetSceneRect( GraphicsItem item, MovieTimeRange timeRange )
	{
		timeRange = timeRange.ClampStart( 0d );

		var min = TimeToPixels( timeRange.Start );
		var max = TimeToPixels( timeRange.End );

		return item.SceneRect with { Left = min, Right = max };
	}

	private BlockEdge? GetBlockEdge( Vector2 scenePos, GraphicsItem item )
	{
		var leftMax = Math.Min( item.SceneRect.Left + 8f, item.Center.x );
		var rightMin = Math.Max( item.SceneRect.Right - 8f, item.Center.x );

		if ( scenePos.x < leftMax )
		{
			return BlockEdge.Start;
		}

		if ( scenePos.x > rightMin )
		{
			return BlockEdge.End;
		}

		return null;
	}

	private void UpdateCursor( Vector2 scenePos, GraphicsItem item )
	{
		if ( item is IMovieDraggable draggable )
		{
			item.Cursor = draggable.DragCursor;
		}

		if ( item is IMovieResizable resizable && GetBlockEdge( scenePos, item ) is not null )
		{
			item.Cursor = resizable.ResizeCursor;
		}
	}

	private bool StartDragging( Vector2 scenePos, GraphicsItem item )
	{
		_dragScope = null;
		_lastDragTime = ScenePositionToTime( scenePos );

		_draggedItems.Clear();
		_resizedItems.Clear();

		if ( StartResizing( scenePos, item ) ) return true;

		if ( item is not IMovieDraggable draggable )
		{
			return false;
		}

		if ( !item.Selected )
		{
			DeselectAll();
			item.Selected = true;
		}

		_draggedItems.AddRange( SelectedItems.OfType<IMovieDraggable>() );

		_dragSnapOptions = new SnapOptions( draggable.SnapFilter );

		foreach ( var dragged in _draggedItems )
		{
			dragged.StartDrag();
		}

		return _draggedItems.Count > 0;
	}

	private bool StartResizing( Vector2 scenePos, GraphicsItem item )
	{
		if ( item is not IMovieResizable resizable )
		{
			return false;
		}

		if ( GetBlockEdge( scenePos, item ) is not { } edge )
		{
			return false;
		}

		if ( !item.Selected )
		{
			DeselectAll();
			item.Selected = true;
		}

		_lastDragTime = edge == BlockEdge.Start ? resizable.TimeRange.Start : resizable.TimeRange.End;

		_draggedItems.Clear();
		_resizedItems.Clear();

		_resizedItems.AddRange( SelectedItems.OfType<IMovieResizable>()
			.Where( x => x.TimeRange.Start == _lastDragTime || x.TimeRange.End == _lastDragTime )
			.Select( x => (x, x.TimeRange.Start == _lastDragTime ? BlockEdge.Start : BlockEdge.End) ) );

		_dragSnapOptions = new SnapOptions();

		foreach ( var itemEdge in _resizedItems )
		{
			itemEdge.Item.StartResize( itemEdge.Edge );
		}

		return _resizedItems.Count > 0;
	}

	private static (MovieTime? Min, MovieTime? Max) GetDragLimits( IMovieResizable item, BlockEdge edge )
	{
		return edge == BlockEdge.Start
			? (item.ResizeLimits.Min, item.TimeRange.End)
			: (item.TimeRange.Start, item.ResizeLimits.Max);
	}

	private static (MovieTime? Min, MovieTime? Max) GetDragLimits( IEnumerable<(MovieTime? Min, MovieTime? Max)> itemLimits )
	{
		var minDragTime = (MovieTime?)null;
		var maxDragTime = (MovieTime?)null;

		foreach ( var limits in itemLimits )
		{
			if ( limits.Min is { } min )
			{
				minDragTime = MovieTime.Max( minDragTime ?? min, min );
			}

			if ( limits.Max is { } max )
			{
				maxDragTime = MovieTime.Min( maxDragTime ?? max, max );
			}
		}

		return (minDragTime, maxDragTime);
	}

	private void Drag( Vector2 scenePos )
	{
		var isResizing = _resizedItems.Count > 0;
		var itemLimits = isResizing
			? _resizedItems.Select( x => GetDragLimits( x.Item, x.Edge ) )
			: _draggedItems.Select( x => (x.DragLimits.Min + _lastDragTime, x.DragLimits.Max + _lastDragTime) );

		var (minDragTime, maxDragTime) = GetDragLimits( itemLimits );

		var snapOffsets = isResizing
			? _dragSnapOptions!.SnapOffsets
			: _draggedItems
				.SelectMany( x => x.SnapSources )
				.Select( x => x - _lastDragTime )
				.Distinct()
				.Order()
				.ToArray();

		var time = ScenePositionToTime( scenePos, _dragSnapOptions! with { Min = minDragTime, Max = maxDragTime, SnapOffsets = snapOffsets } );
		var delta = time - _lastDragTime;

		if ( delta.IsZero ) return;

		_lastDragTime = time;
		_dragScope ??= Session.History.Push( isResizing ? "Resize Selection" : "Drag Selection" );

		Session.PreviewTime = time + snapOffsets.DefaultIfEmpty().MinBy( x => x.Absolute );

		if ( isResizing )
		{
			foreach ( var (item, edge) in _resizedItems )
			{
				item.Resize( edge, delta );
			}
		}
		else
		{
			foreach ( var item in _draggedItems )
			{
				item.Drag( delta );
			}
		}

		Session.EditMode?.DragItems( _draggedItems, delta );
		Session.RefreshNextFrame();

		_dragScope?.PostChange();
	}

	private void StopDragging()
	{
		foreach ( var item in _draggedItems )
		{
			item.EndDrag();
		}

		foreach ( var (item, _) in _resizedItems )
		{
			item.EndResize();
		}

		_dragScope?.Dispose();
		_draggedItems.Clear();
		_resizedItems.Clear();

		Cursor = CursorShape.Arrow;
	}
}
