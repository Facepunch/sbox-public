namespace Sandbox;

public partial class BaseCombatWeapon : ICameraModifier
{
	int ICameraModifier.CameraOrder => 200;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !IsDrivingCamera( camera ) )
			return;

		ModifyCamera( camera, ref view );
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		if ( !IsDrivingCamera( camera ) )
			return;

		PlaceViewModel( camera, view );
	}

	// Only the local player's deployed weapon drives the main camera.
	bool IsDrivingCamera( CameraComponent camera )
	{
		return !IsProxy && IsHeld && IsActive && camera == Scene.Camera;
	}

	/// <summary>
	/// Reshape the holder's view while this weapon is deployed - scope zoom, offsets, lean. Runs in
	/// the camera's modifier chain on the owning client, after the player's camera and vehicles.
	/// Base does nothing.
	/// </summary>
	protected virtual void ModifyCamera( CameraComponent camera, ref CameraView view ) { }

	/// <summary>
	/// Place the view model against the composed view - runs after every modifier, before render
	/// effects. Base pins it to the view exactly; override to add bob, sway and offsets, or to take
	/// over placement completely.
	/// </summary>
	protected virtual void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( !ViewModel.IsValid() )
			return;

		ViewModel.WorldPosition = view.Position;
		ViewModel.WorldRotation = view.Rotation;
	}
}
