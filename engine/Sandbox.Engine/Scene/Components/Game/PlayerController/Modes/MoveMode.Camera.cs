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
		transform.Position = Controller.WorldPosition + Vector3.Up * (Controller.CurrentHeight - Controller.EyeDistanceFromTop);
		transform.Rotation = Controller.EyeAngles.ToRotation();
		return transform;
	}

	/// <summary>
	/// Called to update the camera each frame
	/// </summary>
	public void UpdateCamera( CameraComponent cam )
	{

	}
}
