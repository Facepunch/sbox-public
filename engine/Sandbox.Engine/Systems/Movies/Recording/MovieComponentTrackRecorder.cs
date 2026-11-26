using Sandbox.MovieMaker.Compiled;

namespace Sandbox.MovieMaker;

#nullable enable

internal interface IMovieComponentTrackRecorder : IMovieTrackRecorderInternal
{
	ITrackReference Target { get; }

	bool CanTarget( Component component );
}

internal sealed class MovieComponentTrackRecorder<T> : MovieTrackRecorder<CompiledReferenceTrack<T>, ITrackReference<T>>, IMovieComponentTrackRecorder
	where T : Component
{
	private readonly IReadOnlyList<IComponentCapturer> _captureHandlers;

	public MovieComponentTrackRecorder( MovieGameObjectTrackRecorder parent, CompiledReferenceTrack<T> track )
		: base( parent.MovieRecorder, parent, track )
	{
		_captureHandlers = MovieRecorder.GetCapturers<T>();
	}

	protected override void OnCapture()
	{
		if ( Target.Value is not { IsValid: true } component ) return;

		// Record containing GameObject's transform

		Parent!.Capture();

		Property( nameof( Component.Enabled ) ).Capture();

		// IComponentCapturer implementations decide which properties get recorded

		foreach ( var recorder in _captureHandlers )
		{
			recorder.Capture( this, component );
		}
	}

	protected override void OnSkip()
	{
		Parent!.Capture();

		Property( nameof( Component.Enabled ) ).Capture();
	}

	ITrackReference IMovieComponentTrackRecorder.Target => Target;

	bool IMovieComponentTrackRecorder.CanTarget( Component component )
	{
		return Target.IsBound ? Target.Value == component : Target.TargetType == component.GetType();
	}
}
