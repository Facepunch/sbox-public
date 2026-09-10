using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public abstract partial class BlockItem : GraphicsItem, ISnapSource
{
	private ITrackBlock? _block;
	private float _prevWidth;

	public new TimelineTrack Parent { get; private set; } = null!;

	public bool IsPreview { get; set; }

	public ITrackBlock Block
	{
		get => _block ?? throw new InvalidOperationException();
		set
		{
			if ( ReferenceEquals( _block, value ) ) return;

			if ( _block is IDynamicBlock oldBlock )
			{
				oldBlock.Changed -= Block_Changed;
			}

			_block = value;

			if ( _block is IDynamicBlock newBlock )
			{
				newBlock.Changed += Block_Changed;
			}

			if ( _block is not null )
			{
				OnBlockChanged( _block.TimeRange );
			}
		}
	}

	public MovieTime Offset { get; set; }

	protected IProjectTrack Track => Parent.View.Track;
	protected MovieTimeRange TimeRange => Block.TimeRange + Offset;

	private void Initialize( TimelineTrack parent, ITrackBlock block, MovieTime offset )
	{
		base.Parent = Parent = parent;

		Block = block;
		Offset = offset;
	}

	private void Block_Changed( MovieTimeRange timeRange )
	{
		OnBlockChanged( timeRange );
	}

	protected virtual void OnBlockChanged( MovieTimeRange timeRange ) { }

	protected override void OnDestroy()
	{
		// To remove Changed event

		Block = null!;
	}

	public void Layout()
	{
		var timeline = Parent.Timeline;
		var width = timeline.TimeToPixels( TimeRange.Duration );

		PrepareGeometryChange();

		Position = new Vector2( timeline.TimeToPixels( TimeRange.Start ), (Timeline.TrackHeight - Timeline.BlockHeight) * 0.5f );
		Size = new Vector2( width, Timeline.BlockHeight );

		if ( MathF.Abs( width - _prevWidth ) > 0.1f )
		{
			_prevWidth = width;
			OnResize();
		}

		Update();
	}

	protected virtual void OnResize()
	{

	}

	protected virtual Color BackgroundColor => Timeline.Colors.ChannelBackground.WithAlpha( 0.75f );

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( BackgroundColor.Lighten( Parent.View.IsLocked ? 0.2f : 0f ) );
		Paint.DrawRect( LocalRect );

		if ( Parent.View.IsLocked ) return;

		Paint.ClearBrush();
		Paint.SetPen( Color.White.WithAlpha( 0.1f ) );
		Paint.DrawLine( LocalRect.BottomLeft, LocalRect.TopLeft );
		Paint.DrawLine( LocalRect.BottomRight, LocalRect.TopRight );
	}

	public Rect GetRect( MovieTimeRange range )
	{
		var timeline = Parent.Timeline;

		var origin = timeline.TimeToPixels( Block.TimeRange.Start );
		var left = timeline.TimeToPixels( range.Start ) - origin;
		var right = timeline.TimeToPixels( range.End ) - origin;

		return new Rect( left, LocalRect.Top, right - left, LocalRect.Height );
	}

	public void PaintText( Rect rect, string value )
	{
		Paint.SetPen( Color.White.WithAlpha( 0.5f ), 12f );
		Paint.DrawText( rect, value );
	}

	public virtual IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) =>
		IsPreview ? [] : [Block.TimeRange.Start, Block.TimeRange.End];
}

public interface IBlockItem
{
	MovieTime Offset { get; }


}

public interface IBlockItem<T> : IBlockItem;

public abstract class BlockItem<T> : BlockItem, IBlockItem<T>
	where T : ITrackBlock
{
	public new T Block => (T)base.Block;
}

public interface IPropertyBlockItem : IBlockItem
{
	IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange );
}

public interface IPropertyBlockItem<T> : IBlockItem<T>, IPropertyBlockItem;

public abstract class PropertyBlockItem<T> : BlockItem<IPropertyBlock<T>>, IPropertyBlockItem<T>
{
	/// <inheritdoc cref="IPaintHintBlock.GetPaintHints"/>
	public IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange )
	{
		var clamped = Block.TimeRange.Clamp( timeRange );

		return Block switch
		{
			IPaintHintBlock paintHintBlock => paintHintBlock.GetPaintHints( clamped ),
			CompiledSampleBlock<T> => [clamped],
			_ => []
		};
	}

	/// <summary>
	/// Gets time regions that should be painted as separate blocks, within <paramref name="timeRange"/>.
	/// </summary>
	public IEnumerable<MovieTimeRange> GetPaintBlocks( MovieTimeRange timeRange )
	{
		var hints = GetPaintHints( timeRange );

		var prev = timeRange.Start;

		foreach ( var hint in hints )
		{
			if ( hint.Start > prev )
			{
				yield return (prev, hint.Start);
				prev = hint.Start;
			}

			if ( hint.End > prev )
			{
				yield return (prev, hint.End);
				prev = hint.End;
			}
		}

		if ( prev < timeRange.End )
		{
			yield return (prev, timeRange.End);
		}
	}
}
