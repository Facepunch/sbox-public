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
/// A scene model's morphs on a Basalt.Gpu device: its flex controllers' weights as native morphs it with them (its
/// animation, then the overrides), read once a frame into <see cref="Basalt.Backend.BackendMorphs"/>, which runs the
/// model's flex rules itself.
/// </summary>
internal sealed class GpuSceneModelMorphs( SceneModel source, Basalt.Backend.BackendDevice device ) : IMorphSource
{
	readonly Basalt.Backend.BackendMorphs morphs = new( device );
	float[] weights = [];
	ulong frame = ulong.MaxValue;

	public SceneModel Source { get; } = source;

	public int Queue( GpuMesh mesh, int modelMesh )
	{
		if ( !Source.IsValid() ) return -1;

		if ( frame != Application.FrameCount )
		{
			frame = Application.FrameCount;
			morphs.Clear();

			var count = Source.GetFlexControllerWeights( weights );
			if ( count > weights.Length )
			{
				weights = new float[count];
				count = Source.GetFlexControllerWeights( weights );
			}

			var names = Source.Model.Morphs;
			count = Math.Min( Math.Min( count, weights.Length ), names.Count );
			for ( int i = 0; i < count; i++ )
			{
				if ( weights[i] != 0 && names.GetName( i ) is { } name ) morphs.Set( name, weights[i] );
			}
		}

		return morphs.Queue( mesh, modelMesh );
	}
}
