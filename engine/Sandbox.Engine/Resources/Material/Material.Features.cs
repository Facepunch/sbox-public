namespace Sandbox;

public partial class Material
{
	/// <summary>
	/// Set a feature flag on the material. This is usually used to enable/disable shader permutations.
	/// This is kind of a define, also known as a combo.
	/// </summary>
	public void SetFeature( string name, int value )
	{
		if ( GetFeature( name ) == value )
			return;

		native.Set( name, new Vector4( value, 0, 0, 0 ) );
		native.ReloadStaticCombos();
		RememberFeature( name, value );
	}

	/// <summary>
	/// Features code set on this material (<see cref="SetFeature"/>) by name, for devices that read the material's file (or
	/// its shader's defaults) and apply these over it, as <see cref="SetInCode"/> has its values. Replaced whole on each set,
	/// which moves <see cref="SetInCodeVersion"/> on. Null until something's set.
	/// </summary>
	internal IReadOnlyDictionary<string, int> FeaturesSetInCode { get; private set; }

	void RememberFeature( string name, int value )
	{
		var features = FeaturesSetInCode is null ? new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase ) : new Dictionary<string, int>( FeaturesSetInCode, StringComparer.OrdinalIgnoreCase );
		features[name] = value;
		FeaturesSetInCode = features;
		NoteSetInCode();
	}

	/// <summary>
	/// Get a feature flag on the material. This is usually used to enable/disable shader permutations.
	/// </summary>
	public int GetFeature( string name )
	{
		var val = native.GetVector4( name );
		return val.x.CeilToInt();
	}
}
