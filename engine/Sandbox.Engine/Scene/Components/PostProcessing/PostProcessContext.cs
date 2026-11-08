namespace Sandbox;

internal struct PostProcessContext
{
	public CameraComponent Camera;
	/// <summary>
	/// Destination for camera-setting effects; null during ordinary command-list builds.
	/// </summary>
	public SceneCamera TargetCamera;
	public IReadOnlyList<WeightedEffect> Components;
}
