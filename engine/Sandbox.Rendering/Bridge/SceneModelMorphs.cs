namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A scene model's morphs on a Borealis.Gpu device: its flex controllers' weights as native morphs it with them (its
/// animation, then the overrides), read once a frame into <see cref="Borealis.Backend.BackendMorphs"/>, which runs the
/// model's flex rules itself.
/// </summary>
internal sealed class GpuSceneModelMorphs( SceneModel source, Borealis.Backend.BackendDevice device ) : IMorphSource
{
	readonly Borealis.Backend.BackendMorphs morphs = new( device );
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
