using Editor.MovieMaker.BlockDisplays;
using Sandbox.MovieMaker;
using System.Collections.Immutable;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

public sealed class KeyframeHandle : GraphicsItem, IComparable<KeyframeHandle>, ITrackItem, IMovieDraggable, IMovieContextMenu, ISnapSource
{
	public new TimelineTrack Parent { get; }
	public Session Session { get; }
	public TrackView View { get; }

	public KeyframeEditMode? EditMode => Session.EditMode as KeyframeEditMode;

	public Keyframe Keyframe
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;
			ToolTip = $"{View.Track.Name} = {Keyframe.Value?.ToString() ?? "null"}";

			UpdatePosition();
		}
	}

	public MovieTime Time
	{
		get => Keyframe.Time;
		set => Keyframe = Keyframe with { Time = value };
	}

	public bool IsDragging { get; private set; }

	/// <summary>
	/// True when this keyframe is the end of one block, and overlaps
	/// another keyframe that starts a new block.
	/// </summary>
	internal bool IsOverlappingNextBlock { get; set; }

	bool IMovieItem.MultiSelectable => true;
	MovieTime? IMovieItem.SelectionTime => IsOverlappingNextBlock ? Time - MovieTime.Epsilon : Time;
	TimelineTrack ITrackItem.Track => Parent;

	public KeyframeHandle( TimelineTrack parent, Keyframe keyframe )
		: base( parent )
	{
		Parent = parent;
		Session = parent.Session;
		View = parent.View;

		ZIndex = 100;

		HoverEvents = true;

		Focusable = true;
		Selectable = true;

		Cursor = CursorShape.Finger;
		Keyframe = keyframe;
	}

	public override bool Contains( Vector2 localPos )
	{
		if ( !base.Contains( localPos ) ) return false;

		return Keyframe.Connection switch
		{
			KeyframeConnection.EndBlock => localPos.x <= 0f,
			KeyframeConnection.StartBlock => localPos.x >= 0f,
			_ => true
		};
	}

	public void UpdatePosition()
	{
		PrepareGeometryChange();

		var width = Keyframe.Connection switch
		{
			KeyframeConnection.EndBlock or KeyframeConnection.StartBlock => 8f,
			_ => 16f
		};

		var bias = Keyframe.Connection switch
		{
			KeyframeConnection.EndBlock => 1f,
			KeyframeConnection.StartBlock => 0f,
			_ => 0.5f
		};

		HandlePosition = new Vector2( bias, 0f );
		Position = new Vector2( Parent.Timeline.TimeToPixels( Time ), 0f );
		Size = new Vector2( width, Parent.Height );

		Update();
	}

	void IMovieItem.SingleSelected()
	{
		EditMode?.DefaultInterpolation = Keyframe.Interpolation;
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

		var isStartEnd = Keyframe.Connection is KeyframeConnection.StartBlock or KeyframeConnection.EndBlock;
		var center = LocalRect.Center with { x = MathX.Lerp( LocalRect.Left, LocalRect.Right, HandlePosition.x ) };

		Paint.ClearPen();
		Paint.SetBrushRadial( center, Width * 0.5f, Timeline.Colors.ChannelBackground, Color.Transparent );
		DrawRect( LocalRect );

		var c = PaintExtensions.PaintSelectColor( Parent.HandleColor.WithAlpha( 0.5f ), Parent.HandleColor, Timeline.Colors.HandleSelected );

		Paint.SetBrushAndPen( c );

		switch ( Keyframe.Interpolation )
		{
			case KeyframeInterpolation.Step:
				DrawRect( new Rect( center, 0f ).Grow( 4f ), 1f );
				break;

			case KeyframeInterpolation.Linear:
				DrawDiamond( center, 10 );
				break;

			case KeyframeInterpolation.Quadratic:
				DrawCircle( center, 8f );
				break;

			case KeyframeInterpolation.Cubic:
				DrawCircle( center, 6f );
				Paint.ClearBrush();

				Paint.SetPen( c );
				DrawCircle( center, 10f );
				break;
		}

		Paint.SetPen( c.WithAlphaMultiplied( isStartEnd ? 1f : 0.3f ) );
		Paint.DrawLine( new Vector2( center.x, 0 ), new Vector2( center.x, Height - 1f ) );
	}

	/// <summary>
	/// Draw either a full or half rect based on <see cref="KeyframeConnection"/>.
	/// </summary>
	private void DrawRect( Rect rect, float cornerRadius = 0f )
	{
		switch ( Keyframe.Connection )
		{
			case KeyframeConnection.StartBlock:
				Paint.DrawRect( rect with { Left = rect.Center.x }, cornerRadius );
				return;

			case KeyframeConnection.EndBlock:
				Paint.DrawRect( rect with { Right = rect.Center.x }, cornerRadius );
				return;

			default:
				Paint.DrawRect( rect, cornerRadius );
				return;
		}
	}

	/// <summary>
	/// Draw either a full or half circle based on <see cref="KeyframeConnection"/>.
	/// </summary>
	private void DrawCircle( Vector2 position, float diameter )
	{
		switch ( Keyframe.Connection )
		{
			case KeyframeConnection.StartBlock:
				Paint.DrawPie( position, diameter * 0.5f, 90f, 180f );
				return;

			case KeyframeConnection.EndBlock:
				Paint.DrawPie( position, diameter * 0.5f, -90f, 180f );
				return;

			default:
				Paint.DrawCircle( position, diameter );
				return;
		}
	}

	/// <summary>
	/// Draw either a full or half diamond (otherwise known as a "triangle") based on <see cref="KeyframeConnection"/>.
	/// </summary>
	private void DrawDiamond( Vector2 center, Vector2 size )
	{
		var x = new Vector2( size.x * 0.5f, 0 );
		var y = new Vector2( 0, size.y * 0.5f );

		switch ( Keyframe.Connection )
		{
			case KeyframeConnection.StartBlock:
				Paint.DrawPolygon( center - y, center + x, center + y );
				return;

			case KeyframeConnection.EndBlock:
				Paint.DrawPolygon( center - x, center - y, center + y );
				return;

			default:
				Paint.DrawPolygon( center - x, center - y, center + x, center + y );
				return;
		}
	}

	public void ShowContextMenu( EditMode.ContextMenuEvent ev )
	{
		if ( EditMode is not { } editMode ) return;

		ev.Accepted = true;

		editMode.Session.PlayheadTime = Keyframe.Time;

		var selection = GraphicsView.SelectedItems
			.OfType<KeyframeHandle>()
			.OrderBy( x => x.View )
			.ThenBy( x => x.Keyframe )
			.ToImmutableArray();

		ev.Menu.AddHeading( $"Selected Keyframe{(selection.Length > 1 ? "s" : "")}" );

		CreateInterpolationMenu( selection, ev.Menu );
		CreateConnectionMenu( selection, ev.Menu );

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
				using var _ = Session.History.Push( "Change Keyframe Interpolation" );

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

	private void CreateConnectionMenu( IReadOnlyList<KeyframeHandle> selection, Menu parent )
	{
		// General menu for changing connection mode of all selected keyframes

		var menu = parent.AddMenu( "Connection Mode", "linear_scale" );
		var currentMode = selection.All( x => x.Keyframe.Connection == selection[0].Keyframe.Connection )
			? selection[0].Keyframe.Connection
			: KeyframeConnection.Unknown;

		foreach ( var value in Enum.GetValues<KeyframeConnection>() )
		{
			if ( value < 0 ) continue;

			var icon = TypeLibrary.GetEnumDescription( value ).Icon;

			var option = menu.AddOption( value.ToString().ToTitleCase(), icon, action: () =>
			{
				using var _ = Session.History.Push( "Change Keyframe Connections" );

				foreach ( var handle in selection )
				{
					handle.Keyframe = handle.Keyframe with { Connection = value };
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );

			option.Checkable = true;
			option.Checked = value == currentMode;
		}

		var trackGroups = selection
			.GroupBy( x => x.View )
			.Select( x => (
				All: x.First().Parent.Children
					.OfType<KeyframeHandle>()
					.OrderBy( y => y.Keyframe )
					.ToImmutableArray(),
				Selected: x.ToImmutableHashSet()) )
			.ToArray();

		var canSplitRanges = trackGroups
			.SelectMany( x => x.All.GetSelectedRanges( x.Selected ) )
			.Where( CanSplit )
			.ToArray();

		var canJoinPairs = trackGroups
			.SelectMany( x => x.All.GetSelectedPairs( x.Selected ) )
			.Where( x => CanJoin( x.Prev, x.Next ) )
			.ToArray();

		if ( canSplitRanges.Length > 0 )
		{
			parent.AddOption( "Split", "join_inner", () =>
			{
				using var _ = Session.History.Push( "Split Keyframes" );

				foreach ( var range in canSplitRanges )
				{
					Split( range );
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );
		}

		if ( canJoinPairs.Length > 0 )
		{
			parent.AddOption( "Join", "join_full", () =>
			{
				using var _ = Session.History.Push( "Join Keyframes" );

				foreach ( var pair in canJoinPairs )
				{
					Join( pair.Prev, pair.Next );
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );
		}
	}

	/// <summary>
	/// Split a selected range of keyframes.
	/// </summary>
	private void Split( IReadOnlyList<KeyframeHandle> keyframes )
	{
		var editMode = EditMode!;

		if ( keyframes.Count == 1 )
		{
			editMode.SplitKeyframe( keyframes[0] );
			return;
		}

		keyframes[0].Keyframe = keyframes[0].Keyframe with { Connection = KeyframeConnection.EndBlock };
		keyframes[^1].Keyframe = keyframes[^1].Keyframe with { Connection = KeyframeConnection.StartBlock };

		editMode.RemoveKeyframes( keyframes.Skip( 1 ).Take( keyframes.Count - 2 ) );
	}

	/// <summary>
	/// Join a disconnected pair of keyframes.
	/// </summary>
	private static void Join( KeyframeHandle prev, KeyframeHandle next )
	{
		if ( !CanJoin( prev, next ) ) return;

		if ( prev.Keyframe.Connection is KeyframeConnection.EndBlock )
		{
			prev.Keyframe = prev.Keyframe with { Connection = KeyframeConnection.Connect };
		}

		if ( next.Keyframe.Connection is KeyframeConnection.StartBlock )
		{
			next.Keyframe = next.Keyframe with { Connection = KeyframeConnection.Connect };
		}
	}

	private static bool CanSplit( IReadOnlyList<KeyframeHandle> handles ) =>
		handles.Any( x => x.Keyframe.Connection is KeyframeConnection.Connect )
		|| handles.Count > 2
		|| handles is [{ Keyframe.Connection: not KeyframeConnection.EndBlock }, { Keyframe.Connection: not KeyframeConnection.StartBlock }];

	private static bool CanJoin( KeyframeHandle prev, KeyframeHandle next ) =>
		prev.Keyframe.Connection is KeyframeConnection.EndBlock ||
		next.Keyframe.Connection is KeyframeConnection.StartBlock;

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

		var keyframeCompare = Keyframe.CompareTo( other.Keyframe );

		if ( keyframeCompare != 0 )
		{
			return keyframeCompare;
		}

		// When overlapping, put selected first

		return -Selected.CompareTo( other.Selected );
	}

	MovieTimeRange IMovieItem.TimeRange => Keyframe.Time;
	void IMovieDraggable.StartDrag() => IsDragging = true;

	void IMovieDraggable.Drag( MovieTime delta )
	{
		Time += delta;
	}

	void IMovieDraggable.EndDrag() => IsDragging = false;

	public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) => IsDragging ? [] : [Time];

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
