
namespace Sandbox;

public partial class Model
{
	/// <summary>
	/// Total bounds of all the meshes.
	/// </summary>
	public BBox Bounds => native.GetMeshBounds();

	/// <summary>
	/// Total bounds of all the physics shapes.
	/// </summary>
	public BBox PhysicsBounds => native.GetPhysicsBounds();

	/// <summary>
	/// Render view bounds.
	/// </summary>
	public BBox RenderBounds => native.GetModelRenderBounds();
}
