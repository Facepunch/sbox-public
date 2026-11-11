namespace Sandbox.Movement;

partial class MoveMode
{
	/// <summary>
	/// Get the position of the player's eye
	/// </summary>
	/// <returns></returns>
	public virtual Transform CalculateEyeTransform()
	{
		var transform = new Transform();
		// A visible first person body crouches its head and chest up into the ducked camera, so lift the camera
		var duckLift = Controller.IsDucking && !Controller.HideBodyInFirstPerson ? 8f : 0f;
		var eyeDist = Controller.EyeDistanceFromTop - duckLift;
		transform.Position = Controller.WorldPosition + Vector3.Up * (Controller.CurrentHeight - eyeDist);
		transform.Rotation = Controller.EyeAngles.ToRotation();
		return transform;
	}

	/// <summary>
	/// Reshape the player's view while this mode is active - swimming tilt, ladder lean, sitting
	/// offsets. Runs inside the PlayerController's camera stage, right after it writes the base view,
	/// so modes don't need to join the camera's modifier chain themselves. Base does nothing.
	/// </summary>
	public virtual void ModifyCamera( ref CameraView view ) { }

	/// <summary>
	/// Called to update the camera each frame
	/// </summary>
	[Obsolete( "Override ModifyCamera( ref CameraView ) instead - the PlayerController calls it in its camera stage." )]
	public void UpdateCamera( CameraComponent cam )
	{

	}
}
