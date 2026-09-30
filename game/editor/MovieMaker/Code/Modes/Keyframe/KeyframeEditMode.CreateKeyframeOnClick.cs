using Sandbox.MovieMaker;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Editor.MovieMaker;

#nullable enable

partial class KeyframeEditMode
{
	public bool CreateKeyframeOnClick { get; set; }

	private MovieTime _createKeyframeStartTime;
	private IReadOnlyList<CreateKeyframeTask>? _createKeyframeTasks;

	/// <summary>
	/// How to connect a new click-dragged keyframe block.
	/// </summary>
	private enum CreateKeyframeMode
	{
		/// <summary>
		/// Create a block between two new keyframes.
		/// </summary>
		Default,

		/// <summary>
		/// Remove a block between two new keyframes.
		/// </summary>
		Inverted,

		/// <summary>
		/// Connect two new keyframes to their neighbouring blocks.
		/// </summary>
		Connected
	}

	private sealed class CreateKeyframeTask
	{
		public required TrackKeyframeHandles Handles { get; init; }
		public object? Value { get; init; }

		public CreateKeyframeMode Mode { get; set; }
		public KeyframeHandle? FixedHandle { get; set; }
		public KeyframeHandle? DraggedHandle { get; set; }
	}

	/// <summary>
	/// Can this mouse event trigger a keyframe creation?
	/// </summary>
	private bool CanCreateKeyframe( MouseEvent e )
	{
		if ( !e.LeftMouseButton ) return false;
		if ( !CreateKeyframeOnClick && (e.KeyboardModifiers & KeyboardModifiers.Shift) == 0 ) return false;

		return true;
	}

	private static IEnumerable<TrackView> GetWritableDescendantTrackViews( TrackView parentView )
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

	private IReadOnlyList<CreateKeyframeTask> GetCreateKeyframeTasks( TrackView parentView, MovieTime time )
	{
		var tasks = new List<CreateKeyframeTask>();
		var allInverted = true;

		foreach ( var view in GetWritableDescendantTrackViews( parentView ) )
		{
			if ( Timeline.Tracks.FirstOrDefault( x => x.View == view ) is not { } timelineTrack ) continue;
			if ( view.Track is not IProjectPropertyTrack propertyTrack ) continue;
			if ( view.Target is not ITrackProperty { IsBound: true, CanWrite: true } target ) continue;
			if ( GetHandles( timelineTrack ) is not { } handles ) continue;

			var value = propertyTrack.TryGetValue( time, out var val ) ? val : view.IsEnabledTrack ? true : target.Value;
			var task = new CreateKeyframeTask
			{
				Handles = handles,
				Value = value,

				// If we're click-dragging inside an existing block, create an empty space instead
				Mode = propertyTrack.Blocks.Any( x => x.TimeRange.Start < time && x.TimeRange.End > time )
					? CreateKeyframeMode.Inverted
					: CreateKeyframeMode.Default
			};

			tasks.Add( task );

			allInverted &= task.Mode == CreateKeyframeMode.Inverted;
		}

		if ( allInverted ) return tasks;

		// If only some tasks are inverted, make them connected instead

		foreach ( var task in tasks )
		{
			if ( task.Mode == CreateKeyframeMode.Inverted )
			{
				task.Mode = CreateKeyframeMode.Connected;
			}
		}

		return tasks;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		_createKeyframeTasks = null;

		if ( !CanCreateKeyframe( e ) ) return;

		Timeline.DeselectAll();

		// Figure out which tracks we can create keyframes for from this click

		var scenePos = Timeline.ToScene( e.LocalPosition );

		_createKeyframeStartTime = Timeline.ScenePositionToTime( scenePos, showSnap: false );

		var parentTrack = Timeline.Tracks.FirstOrDefault( x => x.SceneRect.IsInside( scenePos ) );
		if ( parentTrack is null ) return;

		var tasks = GetCreateKeyframeTasks( parentTrack.View, _createKeyframeStartTime ).ToImmutableArray();
		if ( tasks.Length == 0 ) return;

		_createKeyframeTasks = tasks;
		Session.PlayheadTime = _createKeyframeStartTime;

		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _createKeyframeTasks is not { } tasks ) return;

		var scenePos = Timeline.ToScene( e.LocalPosition );
		var time = Timeline.ScenePositionToTime( scenePos, showSnap: false );

		if ( _createKeyframeStartTime == time ) return;

		// We've moved the mouse after clicking with CreateKeyframeOnClick enabled,
		// start creating a block between a pair of new keyframes

		var historyScope = Session.History.Push( $"Add Keyframe Block{(tasks.Count > 1 ? "s" : "")}" );
		var draggedHandles = new List<KeyframeHandle>();

		foreach ( var task in tasks )
		{
			task.FixedHandle = task.Handles.Add( new Keyframe( _createKeyframeStartTime, task.Value, DefaultInterpolation, default ) );
			task.DraggedHandle = task.Handles.Add( new Keyframe( time, task.Value, DefaultInterpolation, default ) );

			draggedHandles.Add( task.DraggedHandle );

			task.DraggedHandle.Selected = true;
		}

		Timeline.StartDragging( scenePos, draggedHandles, historyScope );
	}

	protected override void OnDragItems( IReadOnlyList<IMovieDraggable> items, MovieTime delta )
	{
		if ( _createKeyframeTasks is { } tasks )
		{
			// We're dragging keyframes created during CreateKeyframeOnClick,
			// update their connection modes based on their time ordering

			foreach ( var task in tasks )
			{
				if ( task.FixedHandle is null || task.DraggedHandle is null ) continue;
				if ( task.Mode == CreateKeyframeMode.Connected ) continue;

				var inverted = task.Mode == CreateKeyframeMode.Inverted;

				if ( (task.FixedHandle.Time < task.DraggedHandle.Time) != inverted )
				{
					task.FixedHandle.Connection = KeyframeConnection.StartBlock;
					task.DraggedHandle.Connection = KeyframeConnection.EndBlock;
				}
				else
				{
					task.DraggedHandle.Connection = KeyframeConnection.StartBlock;
					task.FixedHandle.Connection = KeyframeConnection.EndBlock;
				}
			}
		}

		UpdateTracksFromHandles( items.OfType<KeyframeHandle>() );
	}

	protected override void OnEndDragItems( IReadOnlyList<IMovieDraggable> items )
	{
		_createKeyframeTasks = null;
	}

	protected override void OnMouseRelease( MouseEvent e )
	{
		if ( _createKeyframeTasks is not { } tasks ) return;

		_createKeyframeTasks = null;

		using var _ = Session.History.Push( $"Add Keyframe{(tasks.Count > 1 ? "s" : "")}" );

		foreach ( var task in tasks )
		{
			task.Handles.AddOrUpdate( new Keyframe( _createKeyframeStartTime, task.Value, DefaultInterpolation, default ), out var handle );

			handle.Selected = true;
		}

		e.Accepted = true;
	}
}
