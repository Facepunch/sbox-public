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

/// <summary>
/// A scene model's morphs on a Basalt.Gpu device: its flex controllers' values, as the model's morphs have them, read once a
/// frame into <see cref="Basalt.Backend.BackendMorphs"/>, which runs the model's flex rules itself.
/// </summary>
internal sealed class GpuSceneModelMorphs( SceneModel source, Basalt.Backend.BackendDevice device ) : IMorphSource
{
	readonly Basalt.Backend.BackendMorphs morphs = new( device );
	ulong frame = ulong.MaxValue;

	public SceneModel Source { get; } = source;

	public int Queue( GpuMesh mesh, int modelMesh )
	{
		if ( !Source.IsValid() ) return -1;

		if ( frame != Application.FrameCount )
		{
			frame = Application.FrameCount;
			morphs.Clear();
			var values = Source.Morphs;
			for ( int i = 0; i < values.Count; i++ )
			{
				var value = values.Get( i );
				if ( value != 0 ) morphs.Set( values.GetName( i ), value );
			}
		}

		return morphs.Queue( mesh, modelMesh );
	}
}
