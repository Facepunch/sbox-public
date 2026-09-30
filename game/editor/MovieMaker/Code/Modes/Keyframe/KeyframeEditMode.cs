using Sandbox.MovieMaker;
using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

[Title( "Keyframe Editor" ), Icon( "key" ), Order( 0 )]
[Description( "Add or modify keyframes on tracks." )]
public sealed partial class KeyframeEditMode : EditMode
{
	public bool AutoCreateTracks { get; set; }

	private KeyframeInterpolation _defaultInterpolation;

	public KeyframeInterpolation DefaultInterpolation
	{
		get => _defaultInterpolation;
		set
		{
			_defaultInterpolation = value;
			Session.Cookies.KeyframeInterpolation = value;
		}
	}

	public IEnumerable<KeyframeHandle> SelectedKeyframes => Timeline.SelectedItems.OfType<KeyframeHandle>();

	private readonly Dictionary<TimelineTrack, TrackKeyframeHandles> _trackKeyframeHandles = new();

	public override Color? ScrubBarOverrideColor => AutoCreateTracks ? Theme.Red.Darken( 0.5f ) : default;

	protected override void OnEnable()
	{
		_defaultInterpolation = Session.Cookies.KeyframeInterpolation;

		var changesGroup = ToolBar.AddGroup();

		var button = changesGroup.AddToggle( new( "Automatic Track Creation", "playlist_add",
				"When enabled, tracks will be automatically created when making changes in the scene." ),
			() => AutoCreateTracks,
			value => AutoCreateTracks = value );

		button.BackgroundActive = Theme.Red;

		changesGroup.AddToggle( new( "Create Keyframe on Click", "edit",
			"When enabled, clicking on a track in the timeline will create a keyframe.<br/><br/>Drag to create or remove a block between two new keyframes.",
			ShortcutKeyBind: "Shift" ),
			() => CreateKeyframeOnClick || Application.FocusWidget is not null && (Application.KeyboardModifiers & KeyboardModifiers.Shift) != 0,
			value => CreateKeyframeOnClick = value );

		var selectionGroup = ToolBar.AddGroup();

		selectionGroup.AddInterpolationSelector( () =>
		{
			KeyframeInterpolation? interpolation = null;

			foreach ( var handle in SelectedKeyframes )
			{
				interpolation ??= handle.Keyframe.Interpolation;

				if ( interpolation != handle.Keyframe.Interpolation ) return KeyframeInterpolation.Unknown;
			}

			return interpolation ?? DefaultInterpolation;
		}, value =>
		{
			DefaultInterpolation = value;

			foreach ( var handle in SelectedKeyframes )
			{
				handle.Keyframe = handle.Keyframe with { Interpolation = value };
			}

			UpdateTracksFromHandles( SelectedKeyframes );
		} );

		Timeline.OnSelectionChanged += OnSelectionChanged;
	}

	protected override void OnDisable()
	{
		Timeline.OnSelectionChanged -= OnSelectionChanged;
	}

	public override bool AllowTrackCreation => AutoCreateTracks;

	private IHistoryScope? _changeScope;
	private bool _keyframeChangeInProgress;

	protected override bool OnPreChange( TrackView view )
	{
		if ( !_keyframeChangeInProgress )
		{
			_keyframeChangeInProgress = true;
			_changeScope = Session.History.Push( "Change Keyframe Value" );
		}

		// Touching a property should create a keyframe

		return CreateOrUpdateKeyframeHandle( view, new Keyframe( Session.PlayheadTime, view.Target.Value, DefaultInterpolation, default ) );
	}

	protected override bool OnPostChange( TrackView view )
	{
		_keyframeChangeInProgress = false;

		// We've finished changing a property, update the keyframe we created in OnPreChange

		if ( !CreateOrUpdateKeyframeHandle( view, new Keyframe( Session.PlayheadTime, view.Target.Value, DefaultInterpolation, default ) ) )
		{
			return false;
		}

		if ( _changeScope is { IsClosed: false } scope )
		{
			scope.PostChange();
		}

		return true;
	}

	private void OnSelectionChanged()
	{
		// When deselecting keyframes, get rid of overlapping duplicates

		foreach ( var (_, handles) in _trackKeyframeHandles )
		{
			handles.CleanUpKeyframes();
		}
	}

	private TimelineTrack? GetTimelineTrack( TrackView view )
	{
		if ( view.Track is not IProjectPropertyTrack ) return null;
		if ( view.Target is not ITrackProperty { IsBound: true, CanWrite: true } ) return null;

		return Timeline.Tracks.FirstOrDefault( x => x.View == view );
	}

	private TrackKeyframeHandles? GetHandles( TimelineTrack timelineTrack )
	{
		// Handle list should already exist from OnUpdateTimelineItems

		return _trackKeyframeHandles.GetValueOrDefault( timelineTrack );
	}

	/// <summary>
	/// Creates or updates the <see cref="KeyframeHandle"/> for a given <paramref name="keyframe"/>.
	/// Will update a keyframe that already exists if it has the exact same <see cref="Keyframe.Time"/>.
	/// </summary>
	private bool CreateOrUpdateKeyframeHandle( TrackView view, Keyframe keyframe )
	{
		view.ExpandAncestors();

		if ( GetTimelineTrack( view ) is not { } timelineTrack ) return false;
		if ( GetHandles( timelineTrack ) is not { } handles ) return false;

		return handles.AddOrUpdate( keyframe, out _ );
	}

	internal void SplitKeyframe( KeyframeHandle handle )
	{
		GetHandles( handle.Parent )?.SplitHandle( handle );
	}

	internal void RemoveKeyframes( IEnumerable<KeyframeHandle> handles )
	{
		var set = handles.ToImmutableHashSet();

		var tracks = set
			.Select( x => x.Parent )
			.Distinct()
			.ToArray();

		foreach ( var timelineTrack in tracks )
		{
			if ( GetHandles( timelineTrack ) is not { } trackHandles ) continue;

			trackHandles.RemoveAll( set.Contains );
		}

		foreach ( var handle in set )
		{
			handle.Destroy();
		}
	}

	protected override void OnPreRestore()
	{
		foreach ( var timelineTrack in Timeline.Tracks )
		{
			ClearTimelineItems( timelineTrack );
		}
	}

	protected override void OnUpdateTimelineItems( TimelineTrack timelineTrack )
	{
		if ( _trackKeyframeHandles.TryGetValue( timelineTrack, out var handles ) )
		{
			handles.UpdatePositions();
			return;
		}

		// Only create / remove / modify handles if they don't exist yet, because handles are authoritative

		if ( timelineTrack.View.Track is not IProjectPropertyTrack ) return;

		handles = new TrackKeyframeHandles( timelineTrack );

		_trackKeyframeHandles.Add( timelineTrack, handles );

		handles.ReadFromTrack();
	}

	public void UpdateTracksFromHandles( IEnumerable<KeyframeHandle> handles )
	{
		var tracks = handles
			.Select( x => x.Parent )
			.Distinct();

		foreach ( var timelineTrack in tracks )
		{
			GetHandles( timelineTrack )?.WriteToTrack();
		}
	}

	protected override void OnClearTimelineItems( TimelineTrack timelineTrack )
	{
		if ( !_trackKeyframeHandles.Remove( timelineTrack, out var handles ) ) return;

		foreach ( var handle in handles )
		{
			handle.Destroy();
		}
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		var nudgeDelta = MovieTime.FromFrames( e.HasShift ? 10 : 1, Session.FrameRate );

		switch ( e.Key )
		{
			case KeyCode.Right:
				Nudge( nudgeDelta );
				break;
			case KeyCode.Left:
				Nudge( -nudgeDelta );
				break;
		}
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		base.OnKeyRelease( e );

		if ( e.Key == KeyCode.Escape )
		{
			if ( SelectedKeyframes.Any() )
			{
				Timeline.DeselectAll();
				return;
			}

			AutoCreateTracks = false;
			CreateKeyframeOnClick = false;
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		if ( Clipboard is { } clipboard )
		{
			e.Menu.AddHeading( "Clipboard" );
			e.Menu.AddOption( "Paste Keyframes", "content_paste", () => Paste( clipboard, e.Time - clipboard.Time, e.TimelineTrack?.View ) );
		}

		if ( e.TimelineTrack is { } timelineTrack )
		{
			e.Menu.AddHeading( timelineTrack.View.Track.Name );
			e.Menu.AddOption( "Create Keyframe", "key", () => CreateKeyframe( timelineTrack, e.Time ) );
		}
	}

	private void CreateKeyframe( TimelineTrack parentTimelineTrack, MovieTime time, KeyframeConnection connection = default )
	{
		var writeableViews = GetWritableDescendantTrackViews( parentTimelineTrack.View ).ToImmutableArray();

		if ( writeableViews.Length == 0 ) return;

		using var scope = Session.History.Push( $"Add {(writeableViews.Length > 1 ? $"{writeableViews.Length} " : "")}" + $"Keyframe{(writeableViews.Length > 1 ? "s" : "")}" );

		foreach ( var view in writeableViews )
		{
			if ( Timeline.Tracks.FirstOrDefault( x => x.View == view ) is not { } timelineTrack ) continue;
			if ( view.Track is not IProjectPropertyTrack propertyTrack ) continue;
			if ( view.Target is not ITrackProperty { IsBound: true, CanWrite: true } target ) continue;

			if ( GetHandles( timelineTrack ) is not { } handles ) return;
			if ( handles.Any( x => x.Time == time ) ) return;

			var value = propertyTrack.TryGetValue( time, out var val ) ? val : view.IsEnabledTrack ? true : target.Value;

			handles.AddOrUpdate( new Keyframe( time, value, DefaultInterpolation, connection ), out _ );
		}

		Session.PlayheadTime = time;
	}

	private void SelectKeyframe( TrackView trackView, Keyframe keyframe )
	{
		var timelineTrack = Timeline.Tracks.FirstOrDefault( x => x.View == trackView );

		if ( timelineTrack is null ) return;
		if ( !_trackKeyframeHandles.TryGetValue( timelineTrack, out var handles ) ) return;
		if ( handles.FirstOrDefault( x => x.Time == keyframe.Time ) is not { } handle ) return;

		Timeline.DeselectAll();
		handle.Selected = true;
	}

	private MovieTime ClampKeyframeDelta( MovieTime delta )
	{
		var minDelta = SelectedKeyframes
			.Select( x => -x.Time )
			.DefaultIfEmpty( 0d )
			.Max();

		return MovieTime.Max( delta, minDelta );
	}

	private void Nudge( MovieTime delta )
	{
		delta = ClampKeyframeDelta( delta );

		foreach ( var keyframe in SelectedKeyframes )
		{
			keyframe.Time += delta;
		}

		UpdateTracksFromHandles( SelectedKeyframes );
	}

	protected override void OnSelectAll()
	{
		foreach ( var handle in _trackKeyframeHandles.SelectMany( x => x.Value ).ToArray() )
		{
			handle.Selected = true;
		}
	}

	protected override void OnDelete()
	{
		RemoveKeyframes( SelectedKeyframes );
	}

	protected override void OnDrawGizmos( TrackView trackView, MovieTimeRange timeRange )
	{
		base.OnDrawGizmos( trackView, timeRange );

		var clampedTimeRange = timeRange.Clamp( (0d, Session.Duration) );

		foreach ( var keyframe in trackView.Keyframes )
		{
			if ( keyframe.Time < clampedTimeRange.Start ) continue;
			if ( keyframe.Time > clampedTimeRange.End ) break;

			if ( keyframe.Time == Session.PlayheadTime ) continue;

			if ( !trackView.TransformTrack.TryGetValue( keyframe.Time, out var transform ) ) continue;

			var dist = Gizmo.Camera.Ortho ? Gizmo.Camera.OrthoHeight : Gizmo.CameraTransform.Position.Distance( transform.Position );
			var scale = Session.GetGizmoAlpha( keyframe.Time, timeRange ) * dist / 256f;

			using var scope = Gizmo.Scope( keyframe.Time.ToString(), transform );

			var radius = scale * (Gizmo.IsHovered ? 3f : 2f);

			Gizmo.Hitbox.Sphere( new Sphere( Vector3.Zero, radius ) );
			Gizmo.Draw.Color = Color.White.Darken( Gizmo.IsHovered ? 0f : 0.125f );
			Gizmo.Draw.SolidSphere( Vector3.Zero, radius );

			if ( !Gizmo.HasClicked || !Gizmo.Pressed.This ) continue;

			Session.PlayheadTime = keyframe.Time;
			Timeline.PanToPlayheadTime();

			SelectKeyframe( trackView, keyframe );
			trackView.InspectProperty();
		}
	}
}
