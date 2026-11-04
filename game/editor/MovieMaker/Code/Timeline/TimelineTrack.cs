using Editor.MovieMaker.BlockDisplays;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

public partial class TimelineTrack : GraphicsItem
{
	// TODO: should this be in DisplayInfo?
	private static Dictionary<Type, Color> HandleColors { get; } = new()
	{
		{ typeof(Vector3), Theme.Blue },
		{ typeof(Rotation), Theme.Green },
		{ typeof(Color), Theme.Pink },
		{ typeof(float), Theme.Yellow },
	};

	public Timeline Timeline { get; }
	public Session Session { get; }
	public TrackView View { get; }

	private readonly SynchronizedList<ITrackBlock, BlockItem> _blockItems;
	private readonly SynchronizedList<ITrackBlock, BlockItem> _previewBlockItems;

	public Color HandleColor { get; }

	public TimelineTrack( Timeline timeline, TrackView view )
	{
		Timeline = timeline;
		Session = timeline.Session;
		View = view;

		HoverEvents = true;
		ToolTip = view.Description;

		HandleColor = HandleColors.TryGetValue( view.Track.TargetType, out var color ) ? color : Color.Gray;

		_blockItems = new SynchronizedList<ITrackBlock, BlockItem>(
			AddBlockItem, RemoveBlockItem, UpdateBlockItem );

		_previewBlockItems = new SynchronizedList<ITrackBlock, BlockItem>(
			AddPreviewBlockItem, RemoveBlockItem, UpdateBlockItem );

		View.Changed += View_Changed;
		View.ValueChanged += View_ValueChanged;
	}

	protected override void OnDestroy()
	{
		Session.EditMode?.ClearTimelineItems( this );

		View.Changed -= View_Changed;
		View.ValueChanged -= View_ValueChanged;
	}

	private void View_Changed( TrackView view )
	{
		UpdateItems();
	}

	private void View_ValueChanged( TrackView view )
	{
		UpdateItems();
	}

	internal void UpdateLayout()
	{
		PrepareGeometryChange();

		var position = View.Position;

		Position = new Vector2( 0, position );
		Size = new Vector2( 50000, Timeline.TrackHeight );

		UpdateItems();
	}

	internal void OnSelected()
	{
		View.InspectProperty();
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		base.OnMousePressed( e );

		if ( e.LeftMouseButton )
		{
			OnSelected();
		}
	}

	public void UpdateItems()
	{
		_blockItems.Update( View.Blocks );
		_previewBlockItems.Update( View.PreviewBlocks );

		Session.EditMode?.UpdateTimelineItems( this );

		Update();
	}

	private BlockItem AddBlockItem( ITrackBlock src )
	{
		var item = BlockItem.Create( this, src, default );

		return item;
	}

	private BlockItem AddPreviewBlockItem( ITrackBlock src )
	{
		var item = AddBlockItem( src );

		item.IsPreview = true;

		return item;
	}

	private void RemoveBlockItem( BlockItem dst )
	{
		dst.Destroy();
	}

	private bool UpdateBlockItem( ITrackBlock src, ref BlockItem dst )
	{
		if ( dst.Block.GetType() != src.GetType() )
		{
			var isPreview = dst.IsPreview;

			dst.Destroy();

			dst = AddBlockItem( src );
			dst.IsPreview = isPreview;
		}

		dst.Block = src;
		dst.Offset = default;
		dst.Layout();

		return true;
	}

	protected override void OnHoverEnter( GraphicsHoverEvent e )
	{
		base.OnHoverEnter( e );

		View.IsHovered = true;
	}

	protected override void OnHoverLeave( GraphicsHoverEvent e )
	{
		base.OnHoverLeave( e );

		View.IsHovered = false;
	}

	protected override void OnPaint()
	{
		if ( View is { IsHovered: false, IsSelected: false } ) return;

		Paint.SetBrushAndPen( View.BackgroundColor.WithAlphaMultiplied( 0.25f ) );
		Paint.DrawRect( LocalRect );
	}
}
