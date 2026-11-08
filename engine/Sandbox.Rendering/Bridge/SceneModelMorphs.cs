using NativeEngine;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A scene model's morphs, evaluated by native from its flex rules each frame (<c>ISceneSystem::QueueExternalMorph</c>).
/// </summary>
internal sealed class SceneModelMorphs( SceneModel source ) : IMorphSource
{
	public SceneModel Source { get; } = source;

	public int Queue( Model model, int modelMesh ) => Source.IsValid() ? RenderTools.QueueModelMeshMorph( Source, model.native, modelMesh ) : -1;
}
