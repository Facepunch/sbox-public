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

	// The weights and names taken at the sync point (Take), for a frame drawn on the render thread: the main thread animates,
	// swaps or deletes the model meanwhile, and its native pose may be reallocated under a read
	float[] taken = [];
	string[] takenNames = [];
	int takenCount = -1;

	/// <summary>
	/// On the main thread where the object's pose is taken (the sync point): the weights the frame handed over morphs with.
	/// </summary>
	public void Take()
	{
		if ( !Source.IsValid() ) return;

		var count = Source.GetFlexControllerWeights( taken );
		if ( count > taken.Length )
		{
			taken = new float[count];
			count = Source.GetFlexControllerWeights( taken );
		}

		var names = Source.Model.Morphs;
		count = Math.Min( Math.Min( count, taken.Length ), names.Count );
		if ( takenNames.Length < count ) takenNames = new string[taken.Length];
		for ( int i = 0; i < count; i++ ) takenNames[i] = taken[i] != 0 ? names.GetName( i ) : null;
		takenCount = count;
	}

	public int Queue( GpuMesh mesh, int modelMesh )
	{
		if ( !Source.IsValid() ) return -1;

		if ( frame != Application.FrameCount )
		{
			frame = Application.FrameCount;
			morphs.Clear();

			// The render thread reads nothing of the model's: what the sync point took
			if ( ThreadSafe.IsRenderThread && takenCount >= 0 )
			{
				for ( int i = 0; i < takenCount; i++ )
				{
					if ( taken[i] != 0 && takenNames[i] is { } name ) morphs.Set( name, taken[i] );
				}

				return morphs.Queue( mesh, modelMesh );
			}

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
