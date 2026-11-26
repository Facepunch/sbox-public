namespace Sandbox.Internal;

public partial class TypeLibrary
{
	/// <summary>
	/// Get a list of properties on the target object. To do this we'll just call GetDescription( obj.GetType() ) and return .Properties.
	/// Will return an empty array if we can't access these properties.
	/// </summary>
	public PropertyDescription[] GetPropertyDescriptions( object obj, bool onlyOwn = false )
	{
		var desc = GetType( obj.GetType() );
		if ( desc == null ) return Array.Empty<PropertyDescription>();

		if ( onlyOwn ) return desc.Properties.Where( x => x.MemberInfo.DeclaringType == desc.TargetType ).ToArray();
		return desc.Properties;
	}

	/// <summary>
	/// Set a named property on given object.
	/// Will perform extra magic for string inputs and try to convert to target property type.
	/// </summary>
	/// <param name="target">The target object to set a named property on.</param>
	/// <param name="name">Name of the property to set.</param>
	/// <param name="value">Value for the property.</param>
	/// <returns>Whether the property was set or not.</returns>
	public bool SetProperty( object target, string name, object value )
	{
		if ( !typedata.TryGetValue( target.GetType(), out var typeData ) )
			return false;

		var prop = typeData.GetProperty( name );
		if ( prop == null || !prop.CanWrite )
			return false;

		try
		{
			if ( value.GetType() != prop.PropertyType )
			{
				if ( value is string strValue )
				{
					value = strValue.ToType( prop.PropertyType );
				}
				else if ( prop.PropertyType == typeof( string ) )
				{
					value = $"{value}";
				}
			}

			prop.SetValue( target, value );
			return true;
		}
		catch ( System.Exception e )
		{
			log.Warning( e, $"Couldn't set property {target.GetType()}.{name} ({prop.PropertyType}) to {value} ({value?.GetType()})" );
			return false;
		}
	}

	/// <summary>
	/// Try to get a value from a property on an object
	/// </summary>
	public object GetPropertyValue( object target, string name )
	{
		if ( !typedata.TryGetValue( target.GetType(), out var typeData ) )
			return null;

		var prop = typeData.GetProperty( name );
		if ( prop == null || !prop.CanRead )
			return null;

		return prop.GetValue( target );
	}
}

