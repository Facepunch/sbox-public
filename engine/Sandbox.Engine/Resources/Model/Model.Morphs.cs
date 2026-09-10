namespace Sandbox;

/// <summary>
/// Allows fast lookups of morph variables
/// </summary>
public sealed class ModelMorphs
{
	public Model Model { get; }

	public int Count { get; private set; }
	public string[] Names { get; internal set; }

	Dictionary<string, int> _nameToIndex = new( StringComparer.OrdinalIgnoreCase );
	Dictionary<int, string> _indexToName = new();

	// The model's native viseme table index for each viseme in Visemes.MorphNames
	// order, -1 where the model doesn't have it. Resolved once so viseme weight
	// lookups never pass strings across interop.
	readonly int[] _visemeIndices;

	// The same indices by name, for the string lookup path
	Dictionary<string, int> _visemeNameToIndex = new();

	internal ModelMorphs( Model model )
	{
		Model = model;

		Count = model.native.NumFlexControllers();

		for ( int i = 0; i < Count; i++ )
		{
			var name = model.native.GetFlexControllerName( i );
			_nameToIndex[name] = i;
			_indexToName[i] = name;
		}

		Names = _nameToIndex.Keys.ToArray();

		_visemeIndices = new int[Visemes.Count];
		for ( int i = 0; i < _visemeIndices.Length; i++ )
		{
			_visemeIndices[i] = model.native.GetVisemeIndex( Visemes.MorphNameArray[i] );
			_visemeNameToIndex[Visemes.MorphNameArray[i]] = _visemeIndices[i];
		}
	}

	// Which morphs any viseme drives, and each one's weight per viseme. Most
	// morphs aren't viseme morphs, so lipsync only has to touch the affected
	// ones. Built once from native, then every lookup is a managed array read.
	int[] _visemeMorphIndices;   // morph indices that any viseme drives
	int[] _visemeMorphRows;      // morph index -> row in the weight table, -1 when not driven
	float[] _visemeMorphWeights; // [row * Visemes.Count + viseme]

	void EnsureVisemeMorphs()
	{
		if ( _visemeMorphIndices is not null )
			return;

		var affected = new List<int>();
		var weights = new List<float>();

		_visemeMorphRows = new int[Count];

		Span<float> row = stackalloc float[Visemes.Count];

		for ( var morphIndex = 0; morphIndex < Count; morphIndex++ )
		{
			var any = false;

			for ( var v = 0; v < row.Length; v++ )
			{
				row[v] = Model.native.GetVisemeMorph( _visemeIndices[v], morphIndex );
				any |= row[v] != 0.0f;
			}

			if ( !any )
			{
				_visemeMorphRows[morphIndex] = -1;
				continue;
			}

			_visemeMorphRows[morphIndex] = affected.Count;
			affected.Add( morphIndex );

			foreach ( var weight in row )
				weights.Add( weight );
		}

		_visemeMorphWeights = weights.ToArray();
		_visemeMorphIndices = affected.ToArray();
	}

	/// <summary>
	/// The indices of the morphs that visemes drive on this model - lipsync only
	/// needs to touch these.
	/// </summary>
	internal ReadOnlySpan<int> VisemeMorphIndices
	{
		get
		{
			EnsureVisemeMorphs();
			return _visemeMorphIndices;
		}
	}

	/// <summary>
	/// The weight a viseme drives the nth entry of <see cref="VisemeMorphIndices"/> with.
	/// </summary>
	internal float GetVisemeMorphWeight( int row, int viseme ) => _visemeMorphWeights[row * Visemes.Count + viseme];

	/// <summary>
	/// How strongly a viseme (an index into <see cref="Visemes.MorphNames"/>) drives
	/// a morph on this model. Cheap enough to call in a loop every frame.
	/// </summary>
	public float GetVisemeMorph( int viseme, int morph )
	{
		if ( viseme < 0 || viseme >= Visemes.Count )
			return 0.0f;

		if ( morph < 0 || morph >= Count )
			return 0.0f;

		EnsureVisemeMorphs();

		var row = _visemeMorphRows[morph];
		return row < 0 ? 0.0f : _visemeMorphWeights[row * Visemes.Count + viseme];
	}

	/// <summary>
	/// How strongly a named viseme drives a morph on this model. The name is
	/// resolved once and cached - but prefer the index overload in hot loops.
	/// </summary>
	public float GetVisemeMorph( string viseme, int morph )
	{
		if ( !Model.native.IsValid )
			return 0.0f;

		if ( !_visemeNameToIndex.TryGetValue( viseme, out var index ) )
		{
			index = Model.native.GetVisemeIndex( viseme );
			_visemeNameToIndex[viseme] = index;
		}

		return Model.native.GetVisemeMorph( index, morph );
	}

	/// <summary>
	/// Get the name of a morph by its index.
	/// </summary>
	public string GetName( int i ) => _indexToName.GetValueOrDefault( i );

	/// <summary>
	/// Get the index of a morph by its name
	/// </summary>
	public int GetIndex( string name ) => _nameToIndex.GetValueOrDefault( name );

	/// <summary>
	/// Clear it so it can't be used after disposed
	/// </summary>
	internal void Dispose()
	{
		Count = 0;

		_nameToIndex.Clear();
		_nameToIndex = default;

		_indexToName.Clear();
		_indexToName = default;

		_visemeNameToIndex.Clear();
		_visemeNameToIndex = default;
	}
}

public partial class Model
{
	ModelMorphs _morphs;

	/// <summary>
	/// Access to bones of this model.
	/// </summary>
	public ModelMorphs Morphs
	{
		get
		{
			_morphs ??= new ModelMorphs( this );
			return _morphs;
		}
	}

	/// <summary>
	/// Number of morph controllers this model has.
	/// </summary>
	public int MorphCount => Morphs.Count;


	/// <summary>
	/// Returns name of a morph controller at given index.
	/// </summary>
	/// <param name="morph">Morph controller index to get name of, starting at 0.</param>
	/// <returns>Name of the morph controller at given index.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when given index exceeds range of [0,MorphCount-1]</exception>
	public string GetMorphName( int morph )
	{
		if ( morph < 0 || morph >= MorphCount )
			throw new ArgumentOutOfRangeException( nameof( morph ), $"Tried to access out of range morph index {morph}, range is 0-{MorphCount - 1}" );
		return native.GetFlexControllerName( morph );
	}

	/// <summary>
	/// Get morph weight for viseme.
	/// </summary>
	public float GetVisemeMorph( string viseme, int morph )
	{
		if ( morph < 0 || morph >= MorphCount )
			throw new ArgumentOutOfRangeException( nameof( morph ), $"Tried to access out of range morph index {morph}, range is 0-{MorphCount - 1}" );

		return Morphs.GetVisemeMorph( viseme, morph );
	}
}
