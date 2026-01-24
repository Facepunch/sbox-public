using Sandbox.MovieMaker;
using System.Linq;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public sealed class SequenceBlockItem : BlockItem<ProjectSequenceBlock>, IMovieDraggable, IMovieResizable, IMovieContextMenu
{
	private bool _isDragging;
	private BlockEdge? _resizeEdge;

	public new ProjectSequenceTrack Track => (ProjectSequenceTrack)Parent.View.Track;

	public string BlockTitle => Block.Resource.ResourceName.ToTitleCase();

	public Rect FullSceneRect
	{
		get
		{
			var fullTimeRange = FullTimeRange;
			var timeline = Parent.Timeline;

			var min = timeline.TimeToPixels( fullTimeRange.Start );
			var max = timeline.TimeToPixels( fullTimeRange.End );

			return SceneRect with { Left = min, Right = max };
		}
	}

	private MovieTimeRange FullTimeRange
	{
		get
		{
			var sourceTimeRange = new MovieTimeRange( 0d, Block.Resource.GetCompiled().Duration );
			return new MovieTimeRange( Block.Transform * sourceTimeRange.Start, Block.Transform * sourceTimeRange.End );
		}
	}

	bool IMovieItem.MultiSelectable => true;

	public SequenceBlockItem()
	{
		HoverEvents = true;
		Selectable = true;

		Cursor = CursorShape.Finger;
	}

	private void OnSplit( MovieTime time )
	{
		if ( time <= TimeRange.Start ) return;
		if ( time >= TimeRange.End ) return;

		using ( Parent.Session.History.Push( $"Split Sequence ({BlockTitle})" ) )
		{
			Track.AddBlock( (time, Block.TimeRange.End), Block.Transform, Block.Resource );

			Block.TimeRange = (Block.TimeRange.Start, time);
		}

		Layout();
		Parent.View.MarkValueChanged();
	}

	public void ShowContextMenu( EditMode.ContextMenuEvent ev )
	{
		ev.Accepted = true;
		Selected = true;

		var time = Parent.Session.PlayheadTime;

		ev.Menu.AddHeading( "Sequence Block" );
		ev.Menu.AddOption( "Edit", "edit", OnEdit );

		if ( time > TimeRange.Start && time < TimeRange.End )
		{
			ev.Menu.AddOption( "Split", "carpenter", () => OnSplit( time ) );
		}

		ev.Menu.AddOption( "Delete", "delete", OnDelete );
	}

	private void OnDelete()
	{
		using ( Parent.Session.History.Push( "Sequence Deleted" ) )
		{
			Track.RemoveBlock( Block );

			if ( Track.IsEmpty )
			{
				Parent.View.Remove();
			}
		}

		Parent.View.MarkValueChanged();
	}

	private void OnEdit()
	{
		if ( Block.Resource is { } resource )
		{
			Parent.Session.Editor.EnterSequence( resource, Block.Transform, Block.Transform.Inverse * Block.TimeRange );
		}
	}

	protected override void OnPaint()
	{
		var isLocked = Parent.View.IsLocked;
		var isSelected = !isLocked && Selected;
		var isHovered = !isLocked && Paint.HasMouseOver;

		var color = Theme.Primary.Desaturate( isLocked ? 0.25f : 0f ).Darken( isLocked ? 0.5f : isSelected ? 0f : isHovered ? 0.1f : 0.25f );

		PaintExtensions.PaintFilmStrip( LocalRect.Shrink( 0f, 0f, 1f, 0f ), color );

		var minLoopX = FullSceneRect.Left - SceneRect.Left;
		var loopWidth = FullSceneRect.Width;

		minLoopX -= MathF.Floor( minLoopX / loopWidth ) * loopWidth;

		for ( var x = minLoopX; x < SceneRect.Width; x += loopWidth )
		{
			if ( x.AlmostEqual( LocalRect.Left ) ) continue;

			Paint.SetPen( color.Darken( 0.5f ) );
			Paint.DrawLine( new Vector2( x, LocalRect.Top + 6f ), new Vector2( x, LocalRect.Bottom - 6f ) );
		}

		var minX = LocalRect.Left;
		var maxX = LocalRect.Right;

		Paint.ClearBrush();
		Paint.SetPen( Theme.TextControl.Darken( isLocked ? 0.25f : 0f ) );

		var textRect = new Rect( minX + 8f, LocalRect.Top, maxX - minX - 16f, LocalRect.Height );
		var fullTimeRange = FullTimeRange;

		var duration = Block.Resource.GetDuration();

		if ( _resizeEdge is BlockEdge.Start )
		{
			TryDrawText( ref textRect, $"{WrapTime( Block.TimeRange.End - fullTimeRange.Start, duration, -MovieTime.Epsilon )}", TextFlag.RightCenter );
			TryDrawText( ref textRect, $"{WrapTime( Block.TimeRange.Start - fullTimeRange.Start, duration )}", TextFlag.LeftCenter );
		}
		else if ( _resizeEdge is BlockEdge.End )
		{
			TryDrawText( ref textRect, $"{WrapTime( Block.TimeRange.Start - fullTimeRange.Start, duration )}", TextFlag.LeftCenter );
			TryDrawText( ref textRect, $"{WrapTime( Block.TimeRange.End - fullTimeRange.Start, duration, -MovieTime.Epsilon )}", TextFlag.RightCenter );
		}
		else
		{
			TryDrawText( ref textRect, BlockTitle, icon: "movie", flags: TextFlag.LeftCenter );
		}
	}

	private MovieTime WrapTime( MovieTime time, MovieTime duration, MovieTime bias = default )
	{
		if ( !duration.IsPositive ) return time;

		return time - (time + bias).GetFrameIndex( duration ) * duration;
	}

	private void TryDrawText( ref Rect rect, string text, TextFlag flags = TextFlag.Center, string? icon = null, float iconSize = 16f )
	{
		var originalRect = rect;

		if ( icon != null )
		{
			if ( rect.Width < iconSize ) return;

			rect.Left += iconSize + 4f;
		}

		var textRect = Paint.MeasureText( rect, text, flags );

		if ( textRect.Width > rect.Width )
		{
			if ( icon != null )
			{
				Paint.DrawIcon( originalRect, icon, iconSize, flags );
			}

			rect = default;
			return;
		}

		if ( icon != null )
		{
			Paint.DrawIcon( new Rect( textRect.Left - iconSize - 4f, rect.Top, iconSize, rect.Height ), icon, iconSize, flags );
		}

		Paint.DrawText( rect, text, flags );

		if ( (flags & TextFlag.Left) != 0 )
		{
			rect.Left = textRect.Right;
		}
		else if ( (flags & TextFlag.Right) != 0 )
		{
			rect.Right = textRect.Left;
		}
	}

	MovieTimeRange IMovieItem.TimeRange => Block.TimeRange;

	void IMovieItem.DoubleClick() => OnEdit();

	void IMovieDraggable.StartDrag() => _isDragging = true;

	void IMovieDraggable.Drag( MovieTime delta )
	{
		Block.TimeRange += delta;
		Block.Transform += delta;

		Layout();
	}

	void IMovieDraggable.EndDrag() => _isDragging = false;

	void IMovieResizable.StartResize( BlockEdge edge )
	{
		_resizeEdge = edge;
	}

	void IMovieResizable.Resize( BlockEdge edge, MovieTime delta )
	{
		Block.TimeRange = 
			edge == BlockEdge.Start
				? Block.TimeRange with { Start = Block.TimeRange.Start + delta }
				: Block.TimeRange with { End = Block.TimeRange.End + delta };

		if ( Block.Resource.GetDuration() is { IsPositive: true } duration )
		{
			var localStartTime = Block.Transform.Inverse * Block.TimeRange.Start;

			Block.Transform += localStartTime.GetFrameIndex( duration ) * duration;
		}

		Layout();
	}

	void IMovieResizable.EndResize()
	{
		_resizeEdge = null;
	}

	public override IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary )
	{
		if ( _isDragging ) yield break;

		if ( _resizeEdge is null )
		{
			yield return Block.TimeRange.Start;
			yield return Block.TimeRange.End;
		}

		if ( Block.Resource.GetDuration() is not { IsPositive: true } duration ) yield break;

		var localTime = Block.Transform.Inverse * sourceTime;
		var nearestLoopPoint = Block.Transform * localTime.Round( duration );

		// If nearest loop point is outside the block, only snap if we're resizing this block

		if ( !Block.TimeRange.Contains( nearestLoopPoint ) && _resizeEdge is null ) yield break;

		yield return nearestLoopPoint;
	}

	IEnumerable<MovieTime> IMovieDraggable.SnapSources
	{
		get
		{
			if ( Block.Resource.GetDuration() is not { IsPositive: true } duration )
			{
				return [Block.TimeRange.Start, Block.TimeRange.End];
			}

			// If source clip has a duration, snap on loop points

			return
			[
				Block.TimeRange.Start,
				..(Block.Transform * (0, duration))
					.Repeat( Block.TimeRange )
					.Select( x => x.Range.End )
			];
		}
	}
}

file sealed class FullBlockGhostItem : GraphicsItem
{
	public FullBlockGhostItem()
	{
		ZIndex = -100;
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Timeline.Colors.ChannelBackground );
		Paint.DrawRect( LocalRect, 2 );
	}
}
