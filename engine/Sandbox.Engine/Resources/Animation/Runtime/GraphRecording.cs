namespace Sandbox.Animation;

/// <summary>
/// Recording and playback for a graph instance. Recordings belong to the native controller and do not survive its replacement.
/// </summary>
public sealed class GraphRecording
{
	public enum UpdateKind
	{
		Unknown,
		Reset,
		FullState,
		TimeStep,
		ExternalGraphChanged
	}

	public enum SeekResult
	{
		Success,
		ExternalGraphsChanged,
		Failure
	}

	public readonly record struct UpdateInfo(
		UpdateKind Kind,
		float DeltaTime,
		int StartEvent,
		float StartPercentage,
		int EndEvent,
		float EndPercentage )
	{
		public bool AdvancesTime => Kind is UpdateKind.FullState or UpdateKind.TimeStep or UpdateKind.ExternalGraphChanged;
	}

	private readonly AnimationAccessor _graph;
	private NativeEngine.AnimationGraph2Controller Controller => _graph.Controller;

	internal GraphRecording( AnimationAccessor graph ) => _graph = graph;

	public bool IsRecording => Controller is { IsNull: false } controller && controller.IsRecording();
	public bool HasData => Controller is { IsNull: false } controller && controller.HasRecordedData();
	public bool IsPlaybackActive => Controller is { IsNull: false } controller && controller.IsRecordingPlaybackActive();
	public int Count => Controller is { IsNull: false } controller ? controller.GetNumRecordedUpdates() : 0;
	public int CurrentIndex => Controller is { IsNull: false } controller ? controller.GetCurrentRecordedUpdateIndex() : -1;
	public Transform CharacterTransform => Controller is { IsNull: false } controller ? controller.GetRecordedCharacterTransformS2() : Transform.Zero;

	/// <summary>Starts a new recording, unless already recording or reviewing one.</summary>
	public bool Start()
	{
		var controller = Controller;
		if ( controller.IsNull || controller.IsRecording() || controller.IsRecordingPlaybackActive() )
			return false;

		controller.StartRecording();
		return controller.IsRecording();
	}

	public void Stop()
	{
		var controller = Controller;
		if ( !controller.IsNull )
			controller.StopRecording();
	}

	public bool Clear()
	{
		var controller = Controller;
		if ( controller.IsNull || controller.IsRecording() || controller.IsRecordingPlaybackActive() )
			return false;

		controller.ClearRecording();
		return !controller.HasRecordedData();
	}

	public bool StartPlayback()
	{
		var controller = Controller;
		return !controller.IsNull && controller.StartRecordingPlayback();
	}

	public void StopPlayback()
	{
		var controller = Controller;
		if ( !controller.IsNull )
			controller.StopRecordingPlayback();
	}

	/// <summary>Both Success and ExternalGraphsChanged indicate a successful seek.</summary>
	public SeekResult Seek( int updateIndex )
	{
		var controller = Controller;
		return controller.IsNull ? SeekResult.Failure : controller.GoToRecordedUpdate( updateIndex );
	}

	public bool TryGetCurrentUpdate( out UpdateInfo info )
	{
		info = default;
		var controller = Controller;
		if ( controller.IsNull || !controller.GetCurrentRecordedUpdateInfo(
			out var kind, out var deltaTime, out var startEvent, out var startPercentage, out var endEvent, out var endPercentage ) )
			return false;

		info = new( (UpdateKind)kind, deltaTime, startEvent, startPercentage, endEvent, endPercentage );
		return true;
	}

	public bool TryGetUpdate( int updateIndex, out UpdateInfo info )
	{
		info = default;
		var controller = Controller;
		if ( controller.IsNull || !controller.GetRecordedUpdateInfo(
			updateIndex, out var kind, out var deltaTime, out var startEvent, out var startPercentage, out var endEvent, out var endPercentage ) )
			return false;

		info = new( (UpdateKind)kind, deltaTime, startEvent, startPercentage, endEvent, endPercentage );
		return true;
	}
}
