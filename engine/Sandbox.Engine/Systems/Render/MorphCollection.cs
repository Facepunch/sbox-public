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
}
