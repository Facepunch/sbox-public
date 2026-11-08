using NativeEngine;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A scene model's morphs, evaluated by native from its flex rules each frame (<c>ISceneSystem::QueueExternalMorph</c>).
/// </summary>
internal sealed class SceneModelMorphs( SceneModel source ) : IMorphSource
{
	public SceneModel Source { get; } = source;

	public int Queue( GpuMesh mesh, int modelMesh ) => Source.IsValid() ? RenderTools.QueueModelMeshMorph( Source, mesh.Native(), modelMesh ) : -1;
}
