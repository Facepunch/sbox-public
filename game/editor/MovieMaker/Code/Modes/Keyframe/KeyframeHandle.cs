using Sandbox.MovieMaker;
using System.Collections.Immutable;
using System.Linq;
using Editor.MovieMaker.BlockDisplays;

namespace Editor.MovieMaker;

#nullable enable

public sealed class KeyframeHandle : GraphicsItem, IComparable<KeyframeHandle>, ITrackItem, IMovieDraggable, IMovieContextMenu, ISnapSource
{
	private Keyframe _keyframe;
	private bool _isDragging;

	public new TimelineTrack Parent { get; }
	public Session Session { get; }
	public TrackView View { get; }

	public KeyframeEditMode? EditMode => Session.EditMode as KeyframeEditMode;

	public Keyframe Keyframe
	{
		get => _keyframe;
		set
		{
			if ( _keyframe == value ) return;

			_keyframe = value;
			ToolTip = $"{View.Track.Name} = {Keyframe.Value?.ToString() ?? "null"}";

			UpdatePosition();
		}
	}

	public MovieTime Time
	{
		get => Keyframe.Time;
		set => Keyframe = Keyframe with { Time = value };
	}

	bool IMovieItem.MultiSelectable => true;
	TimelineTrack ITrackItem.Track => Parent;

	public KeyframeHandle( TimelineTrack parent, Keyframe keyframe )
		: base( parent )
	{
		Parent = parent;
		Session = parent.Session;
		View = parent.View;

		HandlePosition = new Vector2( 0.5f, 0f );
		ZIndex = 100;

		HoverEvents = true;

		Focusable = true;
		Selectable = true;

		Cursor = CursorShape.Finger;
		Keyframe = keyframe;
	}

	public void UpdatePosition()
	{
		PrepareGeometryChange();

		Position = new Vector2( Parent.Timeline.TimeToPixels( Time ), 0f );
		Size = new Vector2( 16f, Parent.Height );

		Update();
	}

	protected override void OnSelectionChanged()
	{
		base.OnSelectionChanged();
		UpdatePosition();

		ZIndex = Selected ? 101 : 100;
	}

	protected override void OnPaint()
	{
		if ( View.IsLocked ) return;

		Paint.ClearPen();
		Paint.SetBrushRadial( LocalRect.Center, Width * 0.5f, Timeline.Colors.ChannelBackground, Color.Transparent );
		Paint.DrawRect( LocalRect );

		var c = PaintExtensions.PaintSelectColor( Parent.HandleColor.WithAlpha( 0.5f ), Parent.HandleColor, Timeline.Colors.HandleSelected );

		Paint.SetBrushAndPen( c );

		switch ( Keyframe.Interpolation )
		{
			case KeyframeInterpolation.Step:
				Paint.DrawRect( new Rect( Size * 0.5f, 0f ).Grow( 4f ), 1f );
				break;

			case KeyframeInterpolation.Linear:
				PaintExtensions.PaintTriangle( Size * 0.5f, 10 );
				break;

			case KeyframeInterpolation.Quadratic:
				Paint.DrawCircle( Size * 0.5f, 8f );
				break;

			case KeyframeInterpolation.Cubic:
				Paint.DrawCircle( Size * 0.5f, 6f );
				Paint.ClearBrush();

				Paint.SetPen( c );
				Paint.DrawCircle( Size * 0.5f, 10f );
				break;
		}

		Paint.SetPen( c.WithAlphaMultiplied( 0.3f ) );
		Paint.DrawLine( new Vector2( Width * 0.5f, 0 ), new Vector2( Width * 0.5f, Height ) );
	}

	public void ShowContextMenu( EditMode.ContextMenuEvent ev )
	{
		if ( EditMode is not { } editMode ) return;

		ev.Accepted = true;

		editMode.Session.PlayheadTime = Keyframe.Time;

		var selection = GraphicsView.SelectedItems
			.OfType<KeyframeHandle>()
			.ToImmutableArray();

		ev.Menu.AddHeading( $"Selected Keyframe{(selection.Length > 1 ? "s" : "")}" );

		CreateInterpolationMenu( selection, ev.Menu );

		ev.Menu.AddHeading( "Clipboard" );

		ev.Menu.AddOption( "Copy", "content_copy", () => editMode.Copy() );
		ev.Menu.AddOption( "Cut", "content_cut", () => editMode.Cut() );

		if ( GetOverlappingClipboard( selection ) is { } clipboard )
		{
			ev.Menu.AddOption( "Paste", "content_paste", () => editMode.Paste( clipboard, Time - clipboard.Time ) );
		}

		ev.Menu.AddOption( "Delete", "delete", () => editMode.Delete() );
	}

	private KeyframeEditMode.ClipboardData? GetOverlappingClipboard( IReadOnlyList<KeyframeHandle> selection )
	{
		if ( EditMode?.Clipboard is not { } clipboard ) return null;

		if ( !selection.Any( x => clipboard.Keyframes.Any( y => y.Guid == x.View.Track.Id ) ) )
		{
			return null;
		}

		return clipboard;
	}

	private void CreateInterpolationMenu( IReadOnlyList<KeyframeHandle> selection, Menu parent )
	{
		var menu = parent.AddMenu( "Interpolation Mode", "gradient" );
		var currentMode = selection.All( x => x.Keyframe.Interpolation == selection[0].Keyframe.Interpolation )
			? selection[0].Keyframe.Interpolation
			: KeyframeInterpolation.Unknown;

		foreach ( var value in Enum.GetValues<KeyframeInterpolation>() )
		{
			if ( value < 0 ) continue;

			var option = menu.AddOption( value.ToString().ToTitleCase(), action: () =>
			{
				foreach ( var handle in selection )
				{
					handle.Keyframe = handle.Keyframe with { Interpolation = value };
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );

			option.Checkable = true;
			option.Checked = value == currentMode;
		}
	}

	public int CompareTo( KeyframeHandle? other )
	{
		if ( ReferenceEquals( this, other ) )
		{
			return 0;
		}

		if ( other is null )
		{
			return 1;
		}

		var timeCompare = Time.CompareTo( other.Time );

		if ( timeCompare != 0 )
		{
			return timeCompare;
		}

		// When overlapping, put selected first

		return -Selected.CompareTo( other.Selected );
	}

	MovieTimeRange IMovieItem.TimeRange => Keyframe.Time;
	void IMovieDraggable.StartDrag() => _isDragging = true;

	void IMovieDraggable.Drag( MovieTime delta )
	{
		Time += delta;
	}

	void IMovieDraggable.EndDrag() => _isDragging = false;

	public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) => _isDragging ? [] : [Time];

	public bool SnapFilter( ISnapSource source )
	{
		if ( source == this ) return false;
		if ( source is not BlockItem block ) return true;

		var view = View;

		while ( view is not null )
		{
			if ( view == block.Parent.View ) return false;

			view = view.Parent;
		}

		return true;
	}
}
