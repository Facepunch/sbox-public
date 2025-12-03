using Sandbox.MovieMaker;
using System.Linq;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

[Title( "Motion Editor" ), Icon( "brush" ), Order( 1 )]
[Description( "Sculpt changes on selected time ranges. Ideal for tweaking recordings." )]
public sealed partial class MotionEditMode : EditMode
{
	private TimeSelection? _timeSelection;
	private bool _newTimeSelection;
	private bool _isAdditive;

	public TimeSelection? TimeSelection
	{
		get => _timeSelection;
		set
		{
			_timeSelection = value;
			SelectionChanged();
		}
	}

	public InterpolationMode DefaultInterpolation { get; private set; } = InterpolationMode.QuadraticInOut;

	public bool DefaultIsAdditive
	{
		get => _isAdditive;
		set
		{
			_isAdditive = value;
			Session.Cookies.IsAdditive = value;
		}
	}

	private MovieTime? _selectionStartTime;

	protected override void OnEnable()
	{
		_isAdditive = Session.Cookies.IsAdditive;

		var selectionGroup = ToolBar.AddGroup();

		selectionGroup.AddInterpolationSelector( () => DefaultInterpolation, value =>
		{
			DefaultInterpolation = value;

			if ( TimeSelection is { } timeSelection )
			{
				TimeSelection = timeSelection.WithInterpolation( value );
			}
		} );

		SelectionChanged();
	}

	protected override void OnDisable()
	{
		ClearChanges();

		TimeSelection = null;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		var scenePos = Timeline.ToScene( e.LocalPosition );
		var time = Session.ScenePositionToTime( scenePos );

		if ( e.RightMouseButton )
		{
			if ( TimeSelection is { } selection && selection.TotalTimeRange.Contains( time ) )
			{
				e.Accepted = true;
			}
		}

		if ( !e.LeftMouseButton ) return;

		if ( Timeline.GetItemAt( scenePos ) is TimeSelectionItem && !e.HasShift ) return;

		_selectionStartTime = Session.ScenePositionToTime( scenePos );
		_newTimeSelection = false;

		Session.PlayheadTime = time;

		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( (e.ButtonState & MouseButtons.Left) == 0 ) return;
		if ( _selectionStartTime is not { } dragStartTime ) return;

		e.Accepted = true;

		var time = Session.ScenePositionToTime( Timeline.ToScene( e.LocalPosition ), new SnapOptions( x => x is not TimeSelectionItem ) );

		// Only create a time selection when mouse has moved enough

		if ( time == dragStartTime && TimeSelection is null ) return;

		var (minTime, maxTime) = Session.VisibleTimeRange;

		if ( time < minTime ) time = MovieTime.Zero;
		if ( time > maxTime ) time = Session.Project!.Duration;

		ClearChanges();

		TimeSelection = new TimeSelection( (MovieTime.Min( time, dragStartTime ), MovieTime.Max( time, dragStartTime )), DefaultInterpolation );
		_newTimeSelection = true;

		Session.PreviewTime = time;
	}

	protected override void OnMouseRelease( MouseEvent e )
	{
		var scenePos = Timeline.ToScene( e.LocalPosition );
		var time = Session.ScenePositionToTime( scenePos, showSnap: false );

		if ( e.RightMouseButton )
		{
			if ( TimeSelection?.TotalTimeRange.Contains( time ) is true )
			{
				e.Accepted = true;
			}
		}

		if ( !e.LeftMouseButton ) return;

		if ( _selectionStartTime is null ) return;
		_selectionStartTime = null;

		if ( !_newTimeSelection ) return;
		_newTimeSelection = false;

		if ( TimeSelection is not { } selection ) return;

		var timeRange = selection.PeakTimeRange.Clamp( Session.VisibleTimeRange );

		Session.PlayheadTime = timeRange.Start;
		Session.PreviewTime = null;
	}

	protected override void OnContextMenu( ContextMenuEvent ev )
	{
		AddClipboardContextMenu( ev, ev.Time );
	}

	internal void AddTimeSelectionContextMenu( ContextMenuEvent ev, TimeSelection selection )
	{
		ev.Menu.AddHeading( "Time Selection" );
		ev.Menu.AddOption( "Insert", "keyboard_tab", Insert );
		ev.Menu.AddOption( "Remove", "backspace", () => Delete( true ) );
		ev.Menu.AddOption( "Clear", "delete", () => Delete( false ) );
		ev.Menu.AddOption( "Save As Sequence..", "theaters",
			() => Session.Editor.SaveAsDialog( "Save As Sequence..",
				() => CreateSequence( TimeSelection!.Value.TotalTimeRange ) ) );

		AddCustomModifications( ev.Menu, selection );

		if ( GetSkinnedModelRendererTrack( ev.TimelineTrack?.View ) is (_, var rTrack, { Model.AnimationCount: > 0 } renderer ) )
		{
			ev.Menu.AddHeading( "Skinned Model Renderer" );

			var animSequenceMenu = ev.Menu.AddMenu( "Import Anim Sequence", "local_movies" );

			animSequenceMenu.AddOptions( renderer.Model.AnimationNames,
				x => string.Join( "/", x.Split( '_', '.' ) ),
				name => ImportAnimationSequence( renderer, rTrack, selection, renderer.Model, name ) );
		}
	}

	internal void AddClipboardContextMenu( ContextMenuEvent ev, MovieTimeRange timeRange )
	{
		if ( !timeRange.Duration.IsPositive && Clipboard is null )
		{
			return;
		}

		ev.Menu.AddHeading( "Clipboard" );

		if ( timeRange.Duration.IsPositive )
		{
			ev.Menu.AddOption( "Cut", "content_cut", Cut );
			ev.Menu.AddOption( "Copy", "content_copy", Copy );
		}

		if ( Clipboard is not null )
		{
			ev.Menu.AddOption( "Paste", "content_paste", () => Paste( timeRange.Start ) );
		}
	}

	private (TrackView TrackView, ProjectReferenceTrack<SkinnedModelRenderer> Track, SkinnedModelRenderer? BoundRenderer)? GetSkinnedModelRendererTrack( TrackView? trackView )
	{
		while ( true )
		{
			if ( trackView?.Track is ProjectReferenceTrack<GameObject> )
			{
				trackView = trackView.Children.FirstOrDefault( x => x.Track is IReferenceTrack<SkinnedModelRenderer> );
			}

			if ( trackView is null ) return null;

			if ( trackView is { Track: ProjectReferenceTrack<SkinnedModelRenderer> track, Target: ITrackReference<SkinnedModelRenderer> reference } )
			{
				return reference.IsBound ? (trackView, track, reference.Value) : (trackView, track, null);
			}

			trackView = trackView.Parent;
		}
	}

	private void AddCustomModifications( Menu menu, TimeSelection selection )
	{
		var modificationTypes = EditorTypeLibrary
			.GetTypesWithAttribute<MovieModificationAttribute>()
			.OrderBy( x => x.Attribute.Order )
			.GroupBy( x => x.Attribute.Group );

		foreach ( var group in modificationTypes )
		{
			var firstInGroup = true;

			foreach ( var (type, attribute) in group )
			{
				if ( type.IsAbstract || type.IsGenericType ) continue;

				var dummy = (IMovieModification)Activator.CreateInstance( type.TargetType )!;
				var canStart = dummy.CanStart( Session.TrackList, selection );

				if ( !canStart ) continue;

				if ( firstInGroup && group.Key is { } heading )
				{
					firstInGroup = false;

					menu.AddHeading( heading );
				}

				menu.AddOption( attribute.Title, attribute.Icon,
					() =>
					{
						var modification = SetModification( type.TargetType, selection );

						modification.Start( Session.TrackList, selection );
					} );
			}
		}
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		if ( !e.HasShift ) return;

		if ( TimeSelection is not { } selection || !selection.PeakTimeRange.Contains( Session.PlayheadTime ) )
		{
			selection = new TimeSelection( Session.PlayheadTime, DefaultInterpolation );
		}

		var delta = Math.Sign( e.Delta ) * Session.MinorTick.Interval;

		TimeSelection = selection.WithFadeDurationDelta( delta );

		e.Accept();
	}

	private void SetInterpolation( InterpolationMode mode )
	{
		DefaultInterpolation = mode;

		if ( Timeline.GetItemAt( Timeline.ToScene( Timeline.FromScreen( Application.CursorPosition ) ) ) is TimeSelectionFadeItem fade )
		{
			fade.Interpolation = mode;
		}
	}

	protected override Color GetTrailColor( MovieTime time )
	{
		if ( TimeSelection is not { } selection ) return base.GetTrailColor( time );

		return Color.Gray.LerpTo( SelectionColor, selection.GetFadeValue( time ) );
	}
}
