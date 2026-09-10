namespace Sandbox;

/// <summary>
/// Used to access and manipulate morphs.
/// </summary>
public abstract class MorphCollection
{
	/// <summary>
	/// Reset all morphs to their default values.
	/// </summary>
	public abstract void ResetAll();

	/// <summary>
	/// Reset all morphs to their default values.
	/// </summary>
	public abstract void ResetAll( float fadeTime );

	/// <summary>
	/// Reset morph number i to its default value.
	/// </summary>
	public abstract void Reset( int i );

	/// <summary>
	/// Reset morph number i to its default value.
	/// </summary>
	public abstract void Reset( int i, float fadeTime );

	/// <summary>
	/// Reset named morph to its default value.
	/// </summary>
	public abstract void Reset( string name );

	/// <summary>
	/// Reset named morph to its default value.
	/// </summary>
	public abstract void Reset( string name, float fadeTime );

	/// <summary>
	/// Set indexed morph to this value.
	/// </summary>
	public abstract void Set( int i, float weight );

	/// <summary>
	/// Set named morph to this value.
	/// </summary>
	public abstract void Set( string name, float weight );

	/// <summary>
	/// Set indexed morph to this value.
	/// </summary>
	public abstract void Set( int i, float weight, float fadeTime );

	/// <summary>
	/// Set named morph to this value.
	/// </summary>
	public abstract void Set( string name, float weight, float fadeTime );

	/// <summary>
	/// Get indexed morph value (Note: Currently, this only gets the override morph value)
	/// </summary>
	public abstract float Get( int i );

	/// <summary>
	/// Get named morph value (Note: Currently, this only gets the override morph value)
	/// </summary>
	public abstract float Get( string name );

	/// <summary>
	/// Retrieve name of a morph at given index.
	/// </summary>
	public abstract string GetName( int index );

	/// <summary>
	/// Amount of morphs.
	/// </summary>
	public abstract int Count { get; }

	/// <summary>
	/// The model these morphs belong to, if known.
	/// </summary>
	internal virtual Model Model => null;

	/// <summary>
	/// Drive the model's viseme morphs from a set of viseme weights, one per entry
	/// in <see cref="Visemes.MorphNames"/>. Only the morphs that visemes affect are
	/// touched. This is the one blend everything shares - speaking sounds, voice
	/// chat, the sound editor preview - so the same weights make the same mouth
	/// everywhere. A <paramref name="smoothTime"/> above zero eases each morph
	/// toward its target instead of snapping.
	/// </summary>
	public void ApplyVisemes( ReadOnlySpan<float> visemeWeights, float scale = 1.0f, float smoothTime = 0.0f )
	{
		var model = Model;
		if ( model is null )
			return;

		var modelMorphs = model.Morphs;
		var indices = modelMorphs.VisemeMorphIndices;

		for ( var j = 0; j < indices.Length; j++ )
		{
			// Blend the viseme morph weights by how strong each viseme is right now
			var target = 0.0f;
			for ( var v = 0; v < Visemes.Count && v < visemeWeights.Length; v++ )
			{
				if ( visemeWeights[v] <= 0.0f )
					continue;

				target += (modelMorphs.GetVisemeMorphWeight( j, v ) - target) * visemeWeights[v];
			}

			target = (target * scale).Clamp( 0.0f, 1.0f );

			var morphIndex = indices[j];

			if ( smoothTime > 0.0f )
			{
				var current = Get( morphIndex );
				current = MathX.ExponentialDecay( current, target, smoothTime * 0.17f, Time.Delta );
				target = Math.Max( 0.0f, current );
			}

			Set( morphIndex, target );
		}
	}
}
