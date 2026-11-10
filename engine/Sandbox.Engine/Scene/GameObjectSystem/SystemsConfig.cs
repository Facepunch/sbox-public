using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Configuration for GameObjectSystem properties at a project level. 
/// Specific scenes may override this as well - but will be serialized directly in the scene.
/// </summary>
public class SystemsConfig : ConfigData
{
	/// <summary>
	/// Stores GameObjectSystems to property names to property values
	/// </summary>
	[JsonInclude]
	private Dictionary<string, Dictionary<string, object>> Systems { get; set; } = new();

	/// <summary>
	/// Quick utility method to get the type name from a TypeDescription
	/// </summary>
	/// <param name="type"></param>
	/// <returns></returns>
	private static string GetTypeName( TypeDescription type ) => type.FullName;

	/// <summary>
	/// Get property value for a specific system type.
	/// Returns the configured value, or a default value for the type if not found.
	/// </summary>
	public object GetPropertyValue( TypeDescription systemType, PropertyDescription property )
	{
		if ( TryGetPropertyValue( systemType, property, out var value ) )
			return value;

		return GetDefaultValue( property );
	}

	/// <summary>
	/// Get the default value for a GameObjectSystem property.
	/// </summary>
	public static object GetDefaultValue( PropertyDescription property )
	{
		if ( property.GetCustomAttribute<DefaultValueAttribute>() is { } defaultValue )
			return defaultValue.Value;

		var type = property.PropertyType;

		if ( Nullable.GetUnderlyingType( type ) is { } nullableType )
			type = nullableType;

		if ( type.IsValueType )
			return Activator.CreateInstance( type );

		return null;
	}

	/// <summary>
	/// Try to get property value for a specific system type.
	/// Returns true if the property was found in the config.
	/// </summary>
	public bool TryGetPropertyValue( TypeDescription systemType, PropertyDescription property, out object value )
	{
		value = null;

		var typeName = GetTypeName( systemType );

		if ( !Systems.TryGetValue( typeName, out var properties ) )
			return false;

		if ( !properties.TryGetValue( property.Name, out var rawValue ) || rawValue is not JsonElement je )
			return false;

		try
		{
			value = je.Deserialize( property.PropertyType, Json.options );
			return true;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Removing stale GameObjectSystem config value {typeName}.{property.Name}: {ex.Message}" );
			properties.Remove( property.Name );

			if ( properties.Count == 0 )
			{
				Systems.Remove( typeName );
			}

			return false;
		}
	}

	/// <summary>
	/// Set property value for a specific system type
	/// </summary>
	public void SetPropertyValue( TypeDescription systemType, PropertyDescription property, object value )
	{
		var typeName = GetTypeName( systemType );
		var snapshot = JsonSerializer.SerializeToElement( value, value?.GetType() ?? typeof( object ), Json.options );

		if ( !Systems.TryGetValue( typeName, out var properties ) )
			Systems[typeName] = properties = new();

		properties[property.Name] = snapshot;
	}
}
