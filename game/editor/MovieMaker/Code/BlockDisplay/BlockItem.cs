using Sandbox.MovieMaker;

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

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Timeline.Colors.ChannelBackground.Lighten( Parent.View.IsLocked ? 0.2f : 0f ).WithAlpha( 0.75f ) );
		Paint.DrawRect( LocalRect );

		if ( Parent.View.IsLocked ) return;

		Paint.ClearBrush();
		Paint.SetPen( Color.White.WithAlpha( 0.1f ) );
		Paint.DrawLine( LocalRect.BottomLeft, LocalRect.TopLeft );
		Paint.DrawLine( LocalRect.BottomRight, LocalRect.TopRight );
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

public interface IPropertyBlockItem<T> : IBlockItem<T>;

public abstract class PropertyBlockItem<T> : BlockItem<IPropertyBlock<T>>, IPropertyBlockItem<T>;
