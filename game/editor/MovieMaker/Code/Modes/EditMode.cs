using Editor.MovieMaker.BlockDisplays;
using Sandbox.MovieMaker;
using System.Collections.Immutable;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

public record EditModeType( TypeDescription TypeDescription )
{
	public string Name => TypeDescription.Name;
	public string Title => TypeDescription.Title;
	public string Description => TypeDescription.Description;
	public string Icon => TypeDescription.Icon;
	public int Order => TypeDescription.Order;

	public EditMode Create()
	{
		return TypeDescription.Create<EditMode>();
	}

	public bool IsMatchingType( EditMode? editMode )
	{
		return TypeDescription.TargetType == editMode?.GetType();
	}
}

public abstract partial class EditMode
{
	protected static EditMode? Focused
	{
		get
		{
			var widget = Application.FocusWidget;

			while ( widget != null )
			{
				if ( widget is MovieEditor editor ) return editor.Session?.EditMode;
				widget = widget.Parent;
			}

			return null;
		}
	}

	public EditModeType Type { get; }
	public Session Session { get; private set; } = null!;
	public MovieProject Project => Session.Project;
	protected Timeline Timeline { get; private set; } = null!;
	protected ToolBarWidget ToolBar { get; private set; } = null!;

	public MovieTimeRange? SourceTimeRange { get; protected set; }

	/// <summary>
	/// Can we create new tracks when properties are edited in the scene?
	/// </summary>
	public virtual bool AllowTrackCreation => false;

	/// <summary>
	/// Can we start / stop recording?
	/// </summary>
	public virtual bool AllowRecording => false;

	public virtual Color? ScrubBarOverrideColor => null;

	protected EditMode()
	{
		Type = new( EditorTypeLibrary.GetType( GetType() ) );
	}

	internal void Enable( Session session )
	{
		Session = session;
		Timeline = session.Editor.TimelinePanel!.Timeline;
		ToolBar = session.Editor.TimelinePanel!.ToolBar;

		OnEnable();

		foreach ( var track in Timeline.Tracks )
		{
			UpdateTimelineItems( track );
		}
	}

	protected virtual void OnEnable() { }

	internal void Disable()
	{
		foreach ( var track in Timeline.Tracks )
		{
			ClearTimelineItems( track );
		}

		OnDisable();

		Session = null!;
	}

	protected virtual void OnDisable() { }

	internal bool StartRecording() => OnStartRecording();
	protected virtual bool OnStartRecording() => false;

	internal void StopRecording() => OnStopRecording();
	protected virtual void OnStopRecording() { }

	internal void Frame() => OnFrame();
	protected virtual void OnFrame() { }

	internal bool PreChange( TrackView view ) => OnPreChange( view );

	protected virtual bool OnPreChange( TrackView view ) => false;

	internal bool PostChange( TrackView view ) => OnPostChange( view );

	protected virtual bool OnPostChange( TrackView view ) => false;

	internal void UpdateTimelineItems( TimelineTrack timelineTrack )
	{
		if ( timelineTrack.View.IsLocked )
		{
			OnClearTimelineItems( timelineTrack );
		}
		else
		{
			OnUpdateTimelineItems( timelineTrack );
		}
	}

	protected virtual void OnUpdateTimelineItems( TimelineTrack timelineTrack ) { }

	internal void ClearTimelineItems( TimelineTrack timelineTrack ) => OnClearTimelineItems( timelineTrack );
	protected virtual void OnClearTimelineItems( TimelineTrack timelineTrack ) { }

	#region UI Events

	internal void MousePress( MouseEvent e ) => OnMousePress( e );
	protected virtual void OnMousePress( MouseEvent e ) { }
	internal void MouseRelease( MouseEvent e ) => OnMouseRelease( e );
	protected virtual void OnMouseRelease( MouseEvent e ) { }
	internal void MouseMove( MouseEvent e ) => OnMouseMove( e );
	protected virtual void OnMouseMove( MouseEvent e ) { }
	internal void MouseWheel( WheelEvent e ) => OnMouseWheel( e );
	protected virtual void OnMouseWheel( WheelEvent e ) { }

	public record ContextMenuEvent( Vector2 ScenePosition, MovieTime Time, TimelineTrack? TimelineTrack, Menu Menu, Label TitleLabel )
	{
		public string Title
		{
			get => TitleLabel.Text;
			set => TitleLabel.Text = value;
		}

		public bool Accepted { get; set; }
	}

	internal void ContextMenu( ContextMenuEvent e ) => OnContextMenu( e );
	protected virtual void OnContextMenu( ContextMenuEvent e ) { }

	internal void DragItems( IReadOnlyList<IMovieDraggable> items, MovieTime delta ) => OnDragItems( items, delta );
	protected virtual void OnDragItems( IReadOnlyList<IMovieDraggable> items, MovieTime delta ) { }

	internal void KeyPress( KeyEvent e ) => OnKeyPress( e );
	protected virtual void OnKeyPress( KeyEvent e ) { }
	internal void KeyRelease( KeyEvent e ) => OnKeyRelease( e );
	protected virtual void OnKeyRelease( KeyEvent e ) { }
	internal void SelectAll() => OnSelectAll();
	protected virtual void OnSelectAll() { }

	internal void Cut() => OnCut();
	protected virtual void OnCut() { }

	internal void Copy() => OnCopy();
	protected virtual void OnCopy() { }

	internal void Paste() => OnPaste();
	protected virtual void OnPaste() { }

	internal void Backspace() => OnBackspace();
	protected virtual void OnBackspace() { }

	internal void Delete()
	{
		var sequenceBlocks = Timeline.SelectedItems
			.OfType<SequenceBlockItem>()
			.ToImmutableArray();

		if ( sequenceBlocks.Length > 0 )
		{
			using var historyScope = Session.History.Push( $"Delete Sequence{(sequenceBlocks.Length == 1 ? "" : "s")}" );

			foreach ( var blockItem in sequenceBlocks )
			{
				blockItem.Track.RemoveBlock( blockItem.Block );

				if ( blockItem.Track.IsEmpty )
				{
					blockItem.Parent.View.Remove();
				}
				else
				{
					blockItem.Parent.View.MarkValueChanged();
				}
			}

			return;
		}

		OnDelete();
	}

	protected virtual void OnDelete() { }

	internal void Insert() => OnInsert();
	protected virtual void OnInsert() { }

	internal void TrackStateChanged( TrackView view ) => OnTrackStateChanged( view );
	protected virtual void OnTrackStateChanged( TrackView view ) { }

	internal void ViewChanged( Rect viewRect ) => OnViewChanged( viewRect );
	protected virtual void OnViewChanged( Rect viewRect ) { }

	#endregion

	public static IReadOnlyList<EditModeType> AllTypes => EditorTypeLibrary.GetTypes<EditMode>()
		.Where( x => !x.IsAbstract )
		.Select( x => new EditModeType( x ) )
		.OrderBy( x => x.Order )
		.ToArray();

	public static EditModeType Get( string name ) => AllTypes.FirstOrDefault( x => x.TypeDescription.Name == name )
		?? AllTypes.First();

	public void DrawGizmos( TrackView trackView, MovieTimeRange timeRange ) => OnDrawGizmos( trackView, timeRange );

	protected virtual void OnDrawGizmos( TrackView trackView, MovieTimeRange timeRange )
	{
		var interval = MovieTime.FromFrames( 1, 30 );
		var clampedTimeRange = timeRange.Clamp( (0d, Session.Duration) );

		Transform? prevTransform = null;

		var gap = false;

		for ( var t = clampedTimeRange.Start.Floor( interval * 2 ); t <= clampedTimeRange.End; t += interval, gap = !gap )
		{
			if ( !trackView.TransformTrack.TryGetValue( t, out var next ) )
			{
				prevTransform = null;
				continue;
			}

			var alpha = Session.GetGizmoAlpha( t, timeRange );
			var dist = Gizmo.Camera.Ortho ? Gizmo.Camera.OrthoHeight : Gizmo.CameraTransform.Position.Distance( next.Position );
			var scale = dist / 64f;

			if ( trackView.Track is IPropertyTrack<Rotation> rotationTrack && rotationTrack.TryGetValue( t, out var rotation ) )
			{
				Gizmo.Draw.Color = Theme.Red.WithAlpha( alpha );
				Gizmo.Draw.Line( next.Position, next.Position + rotation.Forward * scale );

				Gizmo.Draw.Color = Theme.Green.WithAlpha( alpha );
				Gizmo.Draw.Line( next.Position, next.Position + rotation.Right * scale );

				Gizmo.Draw.Color = Theme.Blue.WithAlpha( alpha );
				Gizmo.Draw.Line( next.Position, next.Position + rotation.Up * scale );
			}
			else if ( !gap && prevTransform is { } prev )
			{
				Gizmo.Draw.Color = GetTrailColor( t ).WithAlpha( alpha );
				Gizmo.Draw.Line( prev.Position, next.Position );
			}

			prevTransform = next;
		}
	}

	protected virtual Color GetTrailColor( MovieTime time ) => Color.White;
}
