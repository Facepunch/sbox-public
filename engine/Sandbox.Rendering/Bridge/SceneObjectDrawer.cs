namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Implements mid-frame <c>Graphics.Render( SceneObject )</c> from mirrored draw state (<c>RenderTools::DrawSceneObject</c>):
/// the engine's block draws the object (<see cref="RenderOnGpu"/>). Main-thread-only.
/// </summary>
internal sealed class SceneObjectDrawer
{
	readonly RenderSystem system;

	/// <summary>
	/// The renderer of the world being drawn - the camera's this frame.
	/// </summary>
	public WorldRender Render { get; set; }

	/// <summary>
	/// The renderers of the extra worlds the camera draws this frame, in <see cref="Render"/>'s world.
	/// </summary>
	public List<WorldRender> ExtraWorlds { get; } = new();

	RenderObject Find( SceneObject sceneObject )
	{
		if ( Render?.Find( sceneObject ) is { } found ) return found;

		foreach ( var extra in ExtraWorlds )
		{
			if ( extra.Find( sceneObject ) is { } inExtra ) return inExtra;
		}

		return null;
	}

	/// <summary>
	/// What a camera's frame on a Borealis.Gpu device calls (<see cref="BorealisGpuView.RenderSceneObject"/>): the engine's
	/// block draws the object (<see cref="BorealisGpuRenderBackend.DrawObject"/>).
	/// </summary>
	public Action<SceneObject, Transform, Color, Material, RenderAttributes> RenderOnGpu { get; }

	public SceneObjectDrawer( RenderSystem system )
	{
		this.system = system;
		RenderOnGpu = DrawOnGpu;
	}

	void DrawOnGpu( SceneObject sceneObject, Transform transform, Color color, Material material, RenderAttributes attributes )
	{
		if ( Graphics.Backend is not BorealisGpuRenderBackend backend ) return;
		if ( Find( sceneObject ) is not MeshObject obj || obj.Mesh is null ) return;

		// It may not be in any view this frame
		obj.RefreshAttributes();
		backend.DrawObject( obj, system.Transforms, transform, color, material, attributes, sceneObject.Attributes );
	}
}
