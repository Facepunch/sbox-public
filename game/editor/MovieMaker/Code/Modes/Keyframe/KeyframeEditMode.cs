using Sandbox.MovieMaker;
using System.Collections;
using System.Collections.Immutable;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

[Title( "Keyframe Editor" ), Icon( "key" ), Order( 0 )]
[Description( "Add or modify keyframes on tracks." )]
public sealed partial class KeyframeEditMode : EditMode
{
	public bool AutoCreateTracks { get; set; }
	public bool CreateKeyframeOnClick { get; set; }

	public KeyframeInterpolation DefaultInterpolation { get; set; } = KeyframeInterpolation.Cubic;

	public IEnumerable<KeyframeHandle> SelectedKeyframes => Timeline.SelectedItems.OfType<KeyframeHandle>();

	private readonly Dictionary<TimelineTrack, TrackKeyframeHandles> _trackKeyframeHandles = new();

	public override Color? ScrubBarOverrideColor => AutoCreateTracks ? Theme.Red.Darken( 0.5f ) : default;

	protected override void OnEnable()
	{
		var changesGroup = ToolBar.AddGroup();

		var button = changesGroup.AddToggle( new( "Automatic Track Creation", "playlist_add",
				"When enabled, tracks will be automatically created when making changes in the scene." ),
			() => AutoCreateTracks,
			value => AutoCreateTracks = value );

		button.BackgroundActive = Theme.Red;

		changesGroup.AddToggle( new( "Create Keyframe on Click", "edit",
			"When enabled, clicking on a track in the timeline will create a keyframe.",
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

	private sealed record KeyframeChangeScope( string Name, TrackView? TrackView, IHistoryScope HistoryScope ) : IDisposable
	{
		public void Dispose() => HistoryScope.Dispose();
	}

	private KeyframeChangeScope? _changeScope;

	private IHistoryScope GetKeyframeChangeScope( string name, TrackView? trackView = null )
	{
		if ( _changeScope is { } scope && scope.TrackView == trackView && scope.Name == name ) return _changeScope.HistoryScope;

		_changeScope = new KeyframeChangeScope( name, trackView,
			Session.History.Push( trackView is null ? $"{name} Keyframes" : $"{name} Keyframes ({trackView.Track.Name})" ) );

		return _changeScope.HistoryScope;
	}

	private void ClearKeyframeChangeScope()
	{
		_changeScope = null;
	}

	protected override bool OnPreChange( TrackView view )
	{
		// Touching a property should create a keyframe

		return CreateOrUpdateKeyframeHandle( view, new Keyframe( Session.PlayheadTime, view.Target.Value, DefaultInterpolation ) );
	}

	protected override bool OnPostChange( TrackView view )
	{
		// We've finished changing a property, update the keyframe we created in OnPreChange

		return CreateOrUpdateKeyframeHandle( view, new Keyframe( Session.PlayheadTime, view.Target.Value, DefaultInterpolation ) );
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
		if ( GetTimelineTrack( view ) is not { } timelineTrack ) return false;
		if ( GetHandles( timelineTrack ) is not { } handles ) return false;

		return handles.AddOrUpdate( keyframe );
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
		base.OnKeyPress(e);

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

	private Vector2 _mouseDownLocalPos;

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress(e);

		_mouseDownLocalPos = e.LocalPosition;
	}

	protected override void OnMouseRelease( MouseEvent e )
	{
		if ( !_mouseDownLocalPos.AlmostEqual( e.LocalPosition ) )
		{
			// Don't show context menu / create keyframe if we click and drag
			return;
		}

		var scenePos = Timeline.ToScene( e.LocalPosition );
		var time = Timeline.ScenePositionToTime( scenePos, showSnap: false );
		var timelineTrack = Timeline.Tracks.FirstOrDefault( x => x.SceneRect.IsInside( scenePos ) );

		if ( !e.LeftMouseButton ) return;
		if ( !CreateKeyframeOnClick && (e.KeyboardModifiers & KeyboardModifiers.Shift) == 0 ) return;
		if ( timelineTrack is null ) return;

		CreateKeyframe( timelineTrack, time );
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

	private void CreateKeyframe( TimelineTrack parentTimelineTrack, MovieTime time )
	{
		var writeableViews = GetWritableDescendantTrackViews( parentTimelineTrack.View ).ToImmutableArray();

		if ( writeableViews.Length == 0 ) return;

		ClearKeyframeChangeScope();

		using var scope = Session.History.Push( $"Add {(writeableViews.Length > 1 ? $"{writeableViews.Length} " : "")}" + $"Keyframe{(writeableViews.Length > 1 ? "s" : "")}" );

		foreach ( var view in writeableViews )
		{
			if ( Timeline.Tracks.FirstOrDefault( x => x.View == view ) is not { } timelineTrack ) continue;
			if ( view.Track is not IProjectPropertyTrack propertyTrack ) continue;
			if ( view.Target is not ITrackProperty { IsBound: true, CanWrite: true } target ) continue;

			if ( GetHandles( timelineTrack ) is not { } handles ) return;
			if ( handles.Any( x => x.Time == time ) ) return;

			var value = propertyTrack.TryGetValue( time, out var val ) ? val : target.Value;

			handles.AddOrUpdate( new Keyframe( time, value, DefaultInterpolation ) );
		}

		Session.PlayheadTime = time;
	}

	private IEnumerable<TrackView> GetWritableDescendantTrackViews( TrackView parentView )
	{
		if ( !parentView.Target.IsBound ) yield break;
		if ( parentView.IsLocked ) yield break;

		if ( parentView is { Track: IProjectPropertyTrack, Target: ITrackProperty { CanWrite: true } } )
		{
			yield return parentView;
			yield break;
		}

		foreach ( var child in parentView.Children )
		{
			foreach ( var childView in GetWritableDescendantTrackViews( child ) )
			{
				yield return childView;
			}
		}
	}

	protected override void OnDragItems( IReadOnlyList<IMovieDraggable> items, MovieTime delta )
	{
		UpdateTracksFromHandles( items.OfType<KeyframeHandle>() );
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
		foreach ( var handle in _trackKeyframeHandles.SelectMany( x => x.Value ) )
		{
			handle.Selected = true;
		}
	}

	protected override void OnDelete()
	{
		var selected = SelectedKeyframes
			.ToImmutableHashSet();

		var tracks = SelectedKeyframes
			.Select( x => x.Parent )
			.Distinct()
			.ToArray();

		foreach ( var timelineTrack in tracks )
		{
			if ( GetHandles( timelineTrack ) is not { } handles ) continue;

			handles.RemoveAll( selected.Contains );
		}

		foreach ( var keyframe in SelectedKeyframes )
		{
			keyframe.Destroy();
		}
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

			if ( Gizmo.HasClicked && Gizmo.Pressed.This )
			{
				Session.PlayheadTime = keyframe.Time;
			}
		}
	}

	/// <summary>
	/// Manages the keyframe handles for a particular <see cref="TimelineTrack"/>.
	/// </summary>
	private sealed class TrackKeyframeHandles : IEnumerable<KeyframeHandle>
	{
		private readonly TimelineTrack _timelineTrack;
		private readonly List<KeyframeHandle> _handles = new();

		private readonly List<IProjectPropertyBlock> _sourceBlocks = new();
		private readonly List<MovieTime> _cutTimes = new();

		public TrackView View => _timelineTrack.View;
		public IProjectPropertyTrack Track => (IProjectPropertyTrack)View.Track;

		public TrackKeyframeHandles( TimelineTrack timelineTrack )
		{
			_timelineTrack = timelineTrack;
		}

		public void AddRange( IEnumerable<IKeyframe> keyframes, MovieTime timeOffset )
		{
			foreach ( var keyframe in keyframes )
			{
				var kf = new Keyframe( keyframe.Time + timeOffset, keyframe.Value, keyframe.Interpolation );

				var handle = new KeyframeHandle( _timelineTrack, kf );

				_handles.Add( handle );

				handle.Selected = true;
			}

			_handles.Sort();

			WriteToTrack();
		}

		public bool AddOrUpdate( Keyframe keyframe )
		{
			if ( _sourceBlocks.FirstOrDefault( x => x.TimeRange.Contains( keyframe.Time ) ) is { } sourceBlock )
			{
				// If keyframe is inside a source block, make its value relative

				if ( Transformer.GetDefault( Track.TargetType ) is { } transformer )
				{
					keyframe = keyframe with
					{
						Value = transformer.Difference( sourceBlock.GetValue( keyframe.Time ), keyframe.Value )
					};
				}
			}

			if ( _handles.FirstOrDefault( x => x.Time == keyframe.Time ) is { } handle )
			{
				if ( handle.Keyframe.Equals( keyframe ) ) return false;

				handle.Keyframe = keyframe;
			}
			else
			{
				_handles.Add( new KeyframeHandle( _timelineTrack, keyframe ) );
				_handles.Sort();
			}

			WriteToTrack();
			return true;
		}

		public bool RemoveAll( Predicate<KeyframeHandle> match )
		{
			if ( _handles.RemoveAll( match ) <= 0 ) return false;

			WriteToTrack();
			return true;
		}

		public void UpdatePositions()
		{
			foreach ( var handle in _handles )
			{
				handle.UpdatePosition();
			}
		}

		/// <summary>
		/// Remove overlapping unselected keyframes.
		/// We keep selected ones in case they're being dragged.
		/// </summary>
		public void CleanUpKeyframes()
		{
			for ( var i = _handles.Count - 1; i >= 1; --i )
			{
				var prev = _handles[i - 1];
				var next = _handles[i];

				if ( prev.Selected || next.Selected ) continue;
				if ( prev.Time != next.Time ) continue;

				_handles.RemoveAt( i );

				next.Destroy();
			}
		}

		public void ReadFromTrack()
		{
			foreach ( var handle in _handles )
			{
				handle.Destroy();
			}

			_handles.Clear();

			foreach ( var keyframe in View.Keyframes )
			{
				_handles.Add( new KeyframeHandle( _timelineTrack, keyframe ) );
			}

			_handles.Sort();

			// Blocks that keyframes could apply a local (additive editing) effect to

			_sourceBlocks.Clear();
			_sourceBlocks.AddRange( Track.Blocks
				.Where( x => x.Signal is not IKeyframeSignal )
				.Select( GetBlockWithoutKeyframes ) );

			// Keyframe blocks must be cut by these times
			// Offset start by epsilon so keyframes at the very start of an additive block won't
			// be included in that block, letting you join non-additive and additive keyframe blocks

			_cutTimes.Clear();
			_cutTimes.AddRange( _sourceBlocks
				.SelectMany( x => new[] { x.TimeRange.Start + MovieTime.Epsilon, x.TimeRange.End } )
				.Distinct() );
		}

		[field: ThreadStatic]
		private static List<Keyframe>? WriteToTrack_Block { get; set; }

		[field: ThreadStatic]
		private static List<IProjectPropertyBlock>? WriteToTrack_Blocks { get; set; }

		public void WriteToTrack()
		{
			// Handles might have moved, re-sort them

			_handles.Sort();

			// Keyframes inside a source block will be an additive operation on that block,
			// otherwise they'll produce a new keyframe-only block

			var block = WriteToTrack_Block ??= new List<Keyframe>();
			var blocks = WriteToTrack_Blocks ??= new List<IProjectPropertyBlock>();

			block.Clear();
			blocks.Clear();

			var prevCutTime = MovieTime.Zero;

			foreach ( var handle in _handles )
			{
				var cutTime = _cutTimes.LastOrDefault( x => x <= handle.Time );

				if ( cutTime != prevCutTime && block.Count > 0 )
				{
					blocks.Add( FinishBlock( block ) );
					block.Clear();

					prevCutTime = cutTime;
				}

				if ( block.Count > 0 && block[^1].Time == handle.Time )
				{
					// Use first when overlapping, which will be a selected keyframe
					continue;
				}

				block.Add( handle.Keyframe );
			}

			if ( block.Count > 0 )
			{
				blocks.Add( FinishBlock( block ) );
			}

			// Re-add any source blocks that don't have keyframes in them

			foreach ( var sourceBlock in _sourceBlocks )
			{
				if ( blocks.Any( x => x.TimeRange == sourceBlock.TimeRange ) ) continue;

				blocks.Add( sourceBlock );
			}

			blocks.Sort( ( a, b ) =>
				a.TimeRange.Start.CompareTo( b.TimeRange.Start ) );

			Track.SetBlocks( blocks );
			View.MarkValueChanged();
		}

		private static IProjectPropertyBlock GetBlockWithoutKeyframes( IProjectPropertyBlock block )
		{
			return block.Signal is IAdditiveSignal { First: { } source, Second: IKeyframeSignal }
				? block.WithSignal( source )
				: block;
		}

		private IProjectPropertyBlock FinishBlock( IReadOnlyList<Keyframe> keyframes )
		{
			var start = keyframes[0].Time;
			var end = keyframes[^1].Time;

			var sourceBlock = _sourceBlocks.FirstOrDefault( x => x.TimeRange.Grow( -MovieTime.Epsilon ).Contains( start ) );
			var propertyType = Track.TargetType;

			return sourceBlock?.WithSignal( PropertySignal.FromKeyframes( propertyType, keyframes, sourceBlock.Signal ) )
				?? PropertyBlock.FromSignal( PropertySignal.FromKeyframes( propertyType, keyframes ), (start, end) );
		}

		public IEnumerator<KeyframeHandle> GetEnumerator() => _handles.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
