using System.Linq;
using Sandbox.MovieMaker;
using Sandbox.UI;

namespace Editor.MovieMaker;

#nullable enable

public partial class Timeline : GraphicsView, ISnapSource
{
	[FromTheme]
	public static Color PlayheadColor { get; set; } = Theme.Yellow;

	[FromTheme]
	public static Color PreviewColor { get; set; } = Theme.Blue;

	/// <summary>
	/// Color outside the current movie's time range.
	/// </summary>
	[FromTheme]
	public static Color BackgroundColor { get; set; } = Theme.ControlBackground;

	/// <summary>
	/// Color outside the current sequence block's time range.
	/// </summary>
	[FromTheme]
	public static Color OuterColor { get; set; } = Theme.ControlBackground.LerpTo( Theme.WidgetBackground, 0.5f );

	/// <summary>
	/// Color inside the current sequence block's time range.
	/// </summary>
	[FromTheme]
	public static Color InnerColor { get; set; } = Theme.WidgetBackground;

	public const int TrackHeight = 32;
	public const int BlockHeight = 30;
	public const int RootTrackSpacing = 8;

	public static class Colors
	{
		public static Color ChannelBackground => Theme.ControlBackground;
		public static Color HandleSelected => Color.White;
	}

	public Session Session { get; }

	private readonly BackgroundItem _backgroundItem;
	private readonly GridItem _gridItem;
	private readonly SynchronizedSet<TrackView, TimelineTrack> _tracks;

	private readonly TimeCursor _playhead;
	private readonly TimeCursor _preview;

	public ScrubBar ScrubBarTop { get; }
	public ScrubBar ScrubBarBottom { get; }

	public IEnumerable<TimelineTrack> Tracks => _tracks;

	private readonly SynchronizedSet<(MovieTime Time, Rect SceneRect), GraphicsItem> _snapTargets;

	public Timeline( Session session )
	{
		Session = session;
		MinimumWidth = 256;

		HorizontalScrollbar = ScrollbarMode.Off;

		_tracks = new SynchronizedSet<TrackView, TimelineTrack>(
			AddTrack, RemoveTrack, UpdateTrack );

		_backgroundItem = new BackgroundItem( this );
		Add( _backgroundItem );

		_gridItem = new GridItem( this );
		Add( _gridItem );

		_playhead = new TimeCursor( this, PlayheadColor, true ) { LabelFadeTime = 1f };
		Add( _playhead );

		_preview = new TimeCursor( this, PreviewColor, false );
		Add( _preview );

		ScrubBarTop = new ScrubBar( this, true ) { Width = Width };
		Add( ScrubBarTop );
		ScrubBarBottom = new ScrubBar( this, false ) { Width = Width };
		Add( ScrubBarBottom );

		_snapTargets = new(
			addFunc: _ =>
			{
				var item = new SnapTargetItem();
				Add( item );
				return item;
			},
			removeAction: item => item.Destroy(),
			updateAction: ( target, item ) =>
			{
				var x = TimeToPixels( target.Time );

				item.Position = new Vector2( x, target.SceneRect.Top );
				item.Height = target.SceneRect.Height;

				return true;
			} );

		Session.PlayheadChanged += UpdatePlayheadTime;
		Session.PreviewChanged += UpdatePreviewTime;

		FocusMode = FocusMode.TabOrClickOrWheel;

		AcceptDrops = true;

		var bg = new Pixmap( 8 );
		bg.Clear( BackgroundColor );

		SetBackgroundImage( bg );

		Antialiasing = true;
	}

	public override void OnDestroyed()
	{
		DeleteAllItems();

		Session.PlayheadChanged -= UpdatePlayheadTime;
		Session.PreviewChanged -= UpdatePreviewTime;
	}

	private int _lastState;

	[EditorEvent.Frame]
	public void Frame()
	{
		UpdateZoom();

		_backgroundItem.Frame();

		ScrubBarTop.Frame();
		ScrubBarBottom.Frame();

		UpdateTracksIfNeeded();

		if ( Session.PreviewTime is not null
			&& (Application.KeyboardModifiers & KeyboardModifiers.Shift) == 0
			&& (Application.MouseButtons & MouseButtons.Left) == 0 )
		{
			Session.PreviewTime = null;
		}
	}

	private void UpdateTracksIfNeeded()
	{
		var state = HashCode.Combine( PixelsPerSecond, VisibleTimeRange, Session.FrameRate, Session.TrackList.StateHash );

		if ( state == _lastState ) return;

		_lastState = state;

		UpdateTracks();
		Update();
	}

	private void UpdatePlayheadTime( MovieTime time )
	{
		_playhead.Value = time;
	}

	private void UpdatePreviewTime( MovieTime? time )
	{
		_preview.PrepareGeometryChange();

		if ( time is { } t )
		{
			_preview.Value = t;
		}
		else
		{
			_preview.PrepareGeometryChange();
			_preview.Position = new Vector2( -50000f, 0f );
		}
	}

	public void UpdateTracks()
	{
		_tracks.Update( Session.TrackList.VisibleTracks );

		Update();
	}

	private TimelineTrack AddTrack( TrackView source )
	{
		var item = new TimelineTrack( this, source );

		Add( item );

		return item;
	}

	private void RemoveTrack( TimelineTrack item ) => item.Destroy();
	private bool UpdateTrack( TrackView source, TimelineTrack item )
	{
		item.UpdateLayout();

		return true;
	}

	protected override void OnWheel( WheelEvent e )
	{
		base.OnWheel( e );

		Session.EditMode?.MouseWheel( e );

		if ( e.Accepted ) return;

		// pan
		if ( e.HasShift )
		{
			PanImmediate( -e.Delta );
			UpdatePreviewTime( ToScene( _lastMouseLocalPos ) );
			e.Accept();
			return;
		}

		// zoom
		if ( e.HasCtrl )
		{
			ZoomSmooth( e.Delta / 240.0f, MouseScenePos.x );
			e.Accept();
			return;
		}

		// scrub
		if ( e.HasAlt )
		{
			var dt = MovieTime.FromFrames( 1, Session.FrameRate );
			var nextTime = Session.PlayheadTime.Round( dt ) + Math.Sign( e.Delta ) * dt;

			Session.PlayheadTime = nextTime;
			PanToPlayheadTime();
			e.Accept();
			return;
		}
	}

	private Vector2 _lastMouseLocalPos;

	public Vector2 MouseScenePos => ToScene( _lastMouseLocalPos );

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		try
		{
			var delta = e.LocalPosition - _lastMouseLocalPos;
			var scenePos = ToScene( e.LocalPosition );

			if ( e.HasShift )
			{
				UpdatePreviewTime( scenePos );
			}

			if ( e.ButtonState == MouseButtons.Left && IsDragging )
			{
				Drag( ToScene( e.LocalPosition ) );
				e.Accepted = true;
				return;
			}

			if ( e.ButtonState == 0 && !e.HasCtrl && GetItemAt( scenePos ) is { Selectable: true } item )
			{
				UpdateCursor( scenePos, item );
				return;
			}

			if ( e.ButtonState == MouseButtons.Middle )
			{
				Translate( delta );
			}

			if ( e.ButtonState == MouseButtons.Right )
			{
				ScrubBarTop.Scrub( e.KeyboardModifiers, scenePos );
			}

			Session.EditMode?.MouseMove( e );
		}
		finally
		{
			_lastMouseLocalPos = e.LocalPosition;
		}
	}

	public void UpdatePreviewTime( Vector2 scenePos )
	{
		Session.PreviewTime = Application.MouseButtons != 0
			? ScenePositionToTime( scenePos )
			: PixelsToTime( scenePos.x );
	}

	public new GraphicsItem? GetItemAt( Vector2 scenePosition )
	{
		// TODO: Is there a nicer way?

		const int zIndexOffset = 100_000;

		var nonSelectables = new HashSet<GraphicsItem>();

		try
		{
			while ( true )
			{
				var item = base.GetItemAt( scenePosition );

				if ( item is { Selectable: true } or null )
				{
					return item;
				}

				// If we found something unselectable, move it backwards

				if ( nonSelectables.Add( item ) )
				{
					item.ZIndex -= zIndexOffset;
				}
				else
				{
					// There was nothing selectable, so we hit something unselectable twice!

					return null;
				}
			}
		}
		finally
		{
			// Move unselectables back to their original z-index.

			foreach ( var item in nonSelectables )
			{
				item.ZIndex += zIndexOffset;
			}
		}
	}

	public void UpdateSnapTargets( IEnumerable<(MovieTime Time, Rect SceneRect)> targets )
	{
		_snapTargets.Update( targets );
	}

	private MovieTime? _contextMenuTime;
	private TimeSince _sinceClick;
	private IMovieItem? _lastClickedItem;

	private const float DoubleClickTime = 0.5f;

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		DragType = DragTypes.None;
		_contextMenuTime = null;

		if ( e.ButtonState == MouseButtons.Middle )
		{
			e.Accepted = true;
			return;
		}

		var scenePos = ToScene( e.LocalPosition );
		var time = ScenePositionToTime( scenePos, new SnapOptions( source => source is not TimeCursor ) );

		var accepted = false;

		// Look for an item we clicked on. If shift is pressed, let us drag a time selection instead

		if ( !e.HasShift && GetItemAt( scenePos ) is { Selectable: true } item and IMovieItem movieItem )
		{
			accepted = true;

			// We're handling selection manually so we can multi-select etc. This means we
			// need to Accept the event so the default selection logic doesn't run.

			// If the item wants to handle the event itself, we don't set Accepted here,
			// and assume it handles the selection logic itself.

			if ( !movieItem.OverridesMouseEvents )
			{
				e.Accepted = true;
			}

			if ( !item.Selected )
			{
				// Ctrl multi-selects, if possible

				foreach ( var selected in SelectedItems.ToArray() )
				{
					if ( selected is not IMovieItem { MultiSelectable: true } )
					{
						selected.Selected = false;
					}
				}

				if ( !movieItem.MultiSelectable || !e.HasCtrl )
				{
					DeselectAll();
				}

				item.Selected = true;

				if ( item is ITrackItem trackItem )
				{
					trackItem.Track.View.Select();
				}
			}

			// Move the playhead to be within whatever we clicked on

			if ( movieItem.MovePlayheadOnSelect || e.RightMouseButton )
			{
				time = time.Clamp( movieItem.TimeRange );
				Session.PlayheadTime = time;
			}

			if ( e.LeftMouseButton )
			{
				// If we've clicked on something draggable, start dragging!

				if ( StartDragging( scenePos, item ) ) return;
			}
		}

		if ( !accepted )
		{
			Session.EditMode?.MousePress( e );
		}

		if ( e.LeftMouseButton )
		{
			if ( accepted ) return;

			DragType = DragTypes.SelectionRect;
			return;
		}

		if ( e.RightMouseButton )
		{
			_contextMenuTime = time;

			if ( accepted ) return;

			ScrubBarTop.StartScrubbing( time, e.KeyboardModifiers );
			Session.PlayheadTime = time;
			return;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		UpdateSnapTargets( [] );

		var scenePos = ToScene( e.LocalPosition );
		var time = ScenePositionToTime( scenePos, new SnapOptions( source => source is not TimeCursor ), showSnap: false );

		// Detect double-click

		if ( e.LeftMouseButton && !e.HasShift && !e.HasCtrl )
		{
			var item = GetItemAt( scenePos ) as IMovieItem;

			if ( item is not null && _sinceClick < DoubleClickTime && item == _lastClickedItem )
			{
				e.Accepted = true;

				item.DoubleClick();

				_sinceClick = float.PositiveInfinity;
				_lastClickedItem = null;
				return;
			}

			_sinceClick = 0f;
			_lastClickedItem = item;
		}

		// Don't open context menu if we right-click + drag

		if ( e.RightMouseButton && time == _contextMenuTime )
		{
			e.Accepted = true;

			OpenContextMenu( scenePos, time );
			return;
		}

		ScrubBarTop.StopScrubbing();

		if ( IsDragging )
		{
			StopDragging();
			return;
		}

		Session.EditMode?.MouseRelease( e );
	}

	public void OpenContextMenu( Vector2 scenePos, MovieTime time )
	{
		var menu = new Menu();
		var timelineTrack = Tracks.FirstOrDefault( x => x.SceneRect.IsInside( scenePos ) );
		var titleLabel = menu.AddHeading( time.ToString() );

		Session.CreateImportMenu( menu, time );

		var ev = new EditMode.ContextMenuEvent( scenePos, time, timelineTrack, menu, titleLabel );

		if ( GetItemAt( scenePos ) is { } item and IMovieContextMenu ctxMenuItem )
		{
			if ( !item.Selected )
			{
				item.Selected = true;
			}

			ctxMenuItem.ShowContextMenu( ev );
		}

		if ( !ev.Accepted )
		{
			Session.EditMode?.ContextMenu( ev );
		}

		menu.OpenAtCursor();
	}

	public void DeselectAll()
	{
		Session.TrackList.DeselectAll();

		foreach ( var item in SelectedItems.ToArray() )
		{
			if ( !item.IsValid() ) continue;

			item.Selected = false;
		}
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		Session.EditMode?.KeyPress( e );

		if ( e.Accepted ) return;

		if ( e.Key == KeyCode.Shift )
		{
			e.Accepted = true;
			Session.PreviewTime = ScenePositionToTime( ToScene( _lastMouseLocalPos ) );
		}
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		base.OnKeyRelease( e );

		Session.EditMode?.KeyRelease( e );
	}

	private MovieResource? GetDraggedClip( DragData data )
	{
		if ( data.Assets.FirstOrDefault( x => x.AssetPath?.EndsWith( ".movie" ) ?? false ) is not { } assetData )
		{
			return null;
		}

		var assetTask = assetData.GetAssetAsync();

		if ( !assetTask.IsCompleted ) return null;
		if ( assetTask.Result?.LoadResource<MovieResource>() is not { } resource ) return null;

		if ( !Session.CanReferenceMovie( resource ) ) return null;

		return resource;
	}

	private ProjectSequenceTrack? _draggedTrack;
	private ProjectSequenceBlock? _draggedBlock;
	private readonly HashSet<ITrackBlock> _draggedBlocks = new();

	public override void OnDragHover( DragEvent ev )
	{
		if ( _draggedBlock is null || _draggedTrack is null )
		{
			if ( GetDraggedClip( ev.Data ) is not { } resource )
			{
				ev.Action = DropAction.Ignore;
				return;
			}

			var clip = resource.GetCompiled();

			_draggedTrack = Session.GetOrCreateTrack( resource );
			_draggedBlock = _draggedTrack.AddBlock( (0d, clip.Duration), default, resource );

			Session.TrackList.Update();
			UpdateTracksIfNeeded();
		}

		_draggedBlocks.Clear();
		_draggedBlocks.Add( _draggedBlock );

		var time = ScenePositionToTime( ToScene( ev.LocalPosition ) );

		_draggedBlock.TimeRange = (time, time + _draggedBlock.TimeRange.Duration);
		_draggedBlock.Transform = new MovieTransform( time );

		Session.TrackList.Find( _draggedTrack )?.MarkValueChanged();

		ev.Action = DropAction.Link;
	}

	public override void OnDragLeave()
	{
		base.OnDragLeave();

		if ( _draggedBlock is { } block && _draggedTrack is { } track )
		{
			track.RemoveBlock( block );

			if ( track.IsEmpty )
			{
				track.Remove();
			}

			_draggedTrack = null;
			_draggedBlock = null;
		}
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( GetDraggedClip( ev.Data ) is not { } movie )
		{
			return;
		}

		_draggedTrack = null;
		_draggedBlock = null;
	}

	Rect ISnapSource.SceneSnapBounds => SceneRect;
	IEnumerable<SnapTarget> ISnapSource.GetSnapTargets( MovieTime sourceTime, bool isPrimary )
	{
		yield return MovieTime.Zero;

		if ( !isPrimary || !Session.FrameSnap ) yield break;

		yield return new SnapTarget( sourceTime.Round( MovieTime.FromFrames( 1, Session.FrameRate ) ), -3, false );
	}

	[Shortcut( "timeline.selectall", "CTRL+A" )]
	public void OnSelectAll()
	{
		Session.EditMode?.SelectAll();
	}

	[Shortcut( "timeline.cut", "CTRL+X" )]
	public void OnCut()
	{
		Session.EditMode?.Cut();
	}

	[Shortcut( "timeline.copy", "CTRL+C" )]
	public void OnCopy()
	{
		Session.EditMode?.Copy();
	}

	[Shortcut( "timeline.paste", "CTRL+V" )]
	public void OnPaste()
	{
		Session.EditMode?.Paste();
	}

	[Shortcut( "timeline.backspace", "BACKSPACE" )]
	public void OnBackspace()
	{
		Session.EditMode?.Backspace();
	}

	[Shortcut( "timeline.delete", "DEL" )]
	public void OnDelete()
	{
		Session.EditMode?.Delete();
	}

	[Shortcut( "timeline.insert", "TAB" )]
	public void OnInsert()
	{
		Session.EditMode?.Insert();
	}
}

file sealed class SnapTargetItem : GraphicsItem
{
	public SnapTargetItem()
	{
		Width = 1f;
		ZIndex = 50_000;
	}

	protected override void OnPaint()
	{
		Paint.SetPen( Color.White.WithAlpha( 0.75f ), style: PenStyle.Dash );
		Paint.DrawLine( LocalRect.TopLeft, LocalRect.BottomLeft );
	}
}
