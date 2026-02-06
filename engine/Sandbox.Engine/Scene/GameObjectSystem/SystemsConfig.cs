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
	protected Dictionary<string, Dictionary<string, object>> Systems { get; set; } = new();

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

		return property.PropertyType.IsValueType ? Activator.CreateInstance( property.PropertyType ) : null;
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

		if ( !properties.TryGetValue( property.Name, out var rawValue ) )
			return false;

		try
		{
			if ( rawValue is JsonElement je )
			{
				value = je.Deserialize( property.PropertyType, Json.options );
				return true;
			}

			// If the value is already assignable to the target type, use it directly
			if ( rawValue?.GetType().IsAssignableTo( property.PropertyType ) == true )
			{
				value = rawValue;
				return true;
			}

			// Only use Convert.ChangeType for primitive/convertible types
			if ( rawValue is IConvertible && property.PropertyType.IsAssignableTo( typeof( IConvertible ) ) )
			{
				value = Convert.ChangeType( rawValue, property.PropertyType );
				return true;
			}

			// Fall back to JSON serialization for complex types
			var json = JsonSerializer.Serialize( rawValue, Json.options );
			value = JsonSerializer.Deserialize( json, property.PropertyType, Json.options );
			return true;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to deserialize {typeName}.{property.Name}: {ex.Message}" );
			return false;
		}
	}

	/// <summary>
	/// Set property value for a specific system type
	/// </summary>
	public void SetPropertyValue( TypeDescription systemType, PropertyDescription property, object value )
	{
		var typeName = GetTypeName( systemType );

		if ( !Systems.ContainsKey( typeName ) )
		{
			Systems[typeName] = new Dictionary<string, object>();
		}

		Systems[typeName][property.Name] = value;
	}
}
