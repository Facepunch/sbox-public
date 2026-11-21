using System.Text.Json;

public static class EffectSettings
{
	/// <summary>
	/// The settings of an effect component that can be changed, the same ones the Effects panel shows.
	/// </summary>
	public static IEnumerable<PropertyDescription> Settings( Component component )
	{
		var type = TypeLibrary.GetType( component.GetType() );
		if ( type is null ) return [];

		return type.Properties.Where( p => p.HasAttribute<PropertyAttribute>()
			&& !p.HasAttribute<HideAttribute>()
			&& p.CanWrite
			&& p.Name != nameof( Component.Enabled )
			&& p.PropertyType is not null
			&& !p.PropertyType.IsAssignableTo( typeof( Delegate ) ) );
	}

	static string Key( Component component, PropertyDescription property ) => $"{component.GetType().Name}.{property.Name}";

	// each effect's prefab values, serialized, so changes can be told apart from defaults
	static readonly Dictionary<string, Dictionary<string, string>> _defaults = new( StringComparer.OrdinalIgnoreCase );

	static Dictionary<string, string> Defaults( string path )
	{
		if ( _defaults.TryGetValue( path, out var cached ) ) return cached;

		var defaults = new Dictionary<string, string>();
		var prefab = ResourceLibrary.Get<PostProcessResource>( path )?.Prefab?.GetScene();
		foreach ( var component in prefab?.GetComponentsInChildren<Component>( true ) ?? [] )
		{
			foreach ( var property in Settings( component ) )
			{
				if ( Serialize( property.GetValue( component ) ) is { } value )
					defaults.TryAdd( Key( component, property ), value );
			}
		}

		_defaults[path] = defaults;
		return defaults;
	}

	public static Dictionary<string, JsonElement> Capture( string path, IEnumerable<Component> components )
	{
		var values = new Dictionary<string, JsonElement>();
		foreach ( var (key, json) in CaptureJson( path, components ) )
		{
			using var document = JsonDocument.Parse( json );
			values[key] = document.RootElement.Clone();
		}

		return values;
	}

	public static Dictionary<string, string> CaptureJson( string path, IEnumerable<Component> components )
	{
		var defaults = Defaults( path );
		var values = new Dictionary<string, string>();

		foreach ( var component in components )
		{
			foreach ( var property in Settings( component ) )
			{
				var key = Key( component, property );
				var current = Serialize( property.GetValue( component ) );
				if ( current is not null && defaults.GetValueOrDefault( key ) != current )
					values[key] = current;
			}
		}

		return values;
	}

	public static void Apply( string path, IEnumerable<Component> components, IReadOnlyDictionary<string, JsonElement> values )
	{
		// player effect presets from before keys had the component type saved just the property name
		Apply( path, components, key =>
			values is not null && (values.TryGetValue( key, out var stored ) || values.TryGetValue( key[(key.LastIndexOf( '.' ) + 1)..], out stored ))
				? stored.GetRawText()
				: null );
	}

	public static void Apply( string path, IEnumerable<Component> components, Func<string, string> storedJson )
	{
		var defaults = Defaults( path );

		foreach ( var component in components )
		{
			foreach ( var property in Settings( component ) )
			{
				var key = Key( component, property );
				var raw = storedJson( key ) ?? defaults.GetValueOrDefault( key );
				if ( raw is null ) continue;

				try
				{
					if ( Sandbox.Json.Deserialize( raw, property.PropertyType ) is { } value )
						property.SetValue( component, value );
				}
				catch { }
			}
		}
	}

	static string Serialize( object value )
	{
		if ( value is null ) return null;

		try { return Sandbox.Json.Serialize( value ); }
		catch { return null; }
	}
}
