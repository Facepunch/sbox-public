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
		var duck = Controller.DuckInputFraction;
		var standingEyes = Controller.BodyHeight - Controller.EyeDistanceFromTop;
		var duckedEyeDistance = Controller.DuckedEyeDistanceFromTop ?? (Controller.EyeDistanceFromTop - (Controller.HideBodyInFirstPerson ? 0f : 8f));
		var crouchedEyes = Controller.DuckedHeight - duckedEyeDistance;
		transform.Position = Controller.WorldPosition + Controller.UpDirection * MathX.Lerp( standingEyes, crouchedEyes, duck );
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
