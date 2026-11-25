
namespace Sandbox;

public partial class Model
{
	Lazy<BBox> _physicsBounds;

	/// <summary>
	/// Total bounds of all the meshes.
	/// </summary>
	public BBox Bounds => native.GetMeshBounds();

	/// <summary>
	/// Total bounds of all the physics shapes.
	/// </summary>
	public BBox PhysicsBounds => (_physicsBounds ??= new( () => native.GetPhysicsBounds() )).Value;

	/// <summary>
	/// Render view bounds.
	/// </summary>
	public BBox RenderBounds => native.GetModelRenderBounds();
}
