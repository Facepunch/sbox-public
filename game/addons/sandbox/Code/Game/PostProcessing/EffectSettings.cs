/// <summary>
/// A camera's live effect and the serialized defaults it started with.
/// </summary>
internal sealed class EffectSettings
{
	internal GameObject GameObject { get; }
	internal Component Component { get; }
	readonly Dictionary<string, string> _defaults;

	internal EffectSettings( GameObject go )
	{
		GameObject = go;
		Component = go.GetComponent<BasePostProcess>( true );
		_defaults = ReadValues();
	}

	IEnumerable<PropertyDescription> Settings() => TypeLibrary.GetType( Component.GetType() ).Properties
		.Where( p => p.HasAttribute<PropertyAttribute>() && !p.HasAttribute<HideAttribute>()
			&& p.CanWrite && p.Name != nameof( Component.Enabled )
			&& p.PropertyType is not null && !p.PropertyType.IsAssignableTo( typeof( Delegate ) ) );

	string Key( PropertyDescription property ) => $"{Component.GetType().Name}.{property.Name}";

	Dictionary<string, string> ReadValues()
	{
		var values = new Dictionary<string, string>();

		foreach ( var property in Settings() )
		{
			try
			{
				values[Key( property )] = Sandbox.Json.Serialize( property.GetValue( Component ) );
			}
			catch ( Exception )
			{
				// Some editable component properties have no JSON representation.
			}
		}

		return values;
	}

	internal void Apply( Func<string, string> storedJson )
	{
		foreach ( var property in Settings() )
		{
			var key = Key( property );
			var raw = storedJson( key ) ?? _defaults.GetValueOrDefault( key );
			if ( raw is null ) continue;

			try
			{
				property.SetValue( Component, Sandbox.Json.Deserialize( raw, property.PropertyType ) );
			}
			catch ( Exception )
			{
				// A saved camera may contain a value from an older version of the effect.
			}
		}
	}
}
