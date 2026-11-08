namespace Basalt;

/// <summary>
/// Evaluates a mesh's morph targets for this frame, for <see cref="MeshObject.MorphSource"/>.
/// </summary>
public interface IMorphSource
{
	/// <summary>
	/// Evaluate and queue <paramref name="modelMesh"/>'s morphs during main-thread setup. Returns the morph atlas handle,
	/// or -1 when nothing morphs this frame.
	/// </summary>
	int Queue( GpuMesh mesh, int modelMesh );
}
