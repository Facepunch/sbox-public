using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable
public sealed partial class Session
{
	private bool _isRecording;

	public bool IsRecording
	{
		get => EditMode is { AllowRecording: true } && _isRecording;

		set
		{
			if ( value ) StartRecording();
			else StopRecording();
		}
	}

	private void StartRecording()
	{
		// TODO: dedicated recording mode?
		SetEditMode( typeof( MotionEditMode ) );

		if ( EditMode is not { AllowRecording: true } editMode ) return;
		if ( _isRecording ) return;

		_isRecording = editMode.StartRecording();
	}

	private void StopRecording()
	{
		if ( !_isRecording ) return;

		_isRecording = false;
		EditMode?.StopRecording();
	}
}
