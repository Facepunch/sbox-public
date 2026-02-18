namespace Sandbox;

public partial class Scene : GameObject
{
	[ActionGraphInclude]
	public CameraComponent Camera { get; private set; }

	internal void UpdateMainCamera()
	{
		// Get main cameras first, then order by priority after that.
		// So that if we don't have a main camera, it'll fallback to a shitter one
		Camera = GetAllComponents<CameraComponent>()
			.Where( x => x.IsSceneEditorCamera == false )
			.OrderBy( x => x.IsMainCamera ? 0 : 1 )
			.ThenBy( x => x.Priority )
			.FirstOrDefault();
	}
}
