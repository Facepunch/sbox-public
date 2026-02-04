using System.Collections;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// This isn't ideal, but it does what we want it to do. Kind of.
/// </summary>
internal static class Translation
{
	internal static JsonSerializerOptions options;

	static Translation()
	{
		options = new JsonSerializerOptions( JsonSerializerOptions.Default );
		options.PropertyNameCaseInsensitive = true;
		options.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals;
		options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;

		options.Converters.Add( new JsonStringEnumConverter( null, true ) );
	}

	internal static bool TryConvert( ref object value, Type targetType )
	{
		if ( TryConvert( value, targetType, out var converted ) )
		{
			value = converted;
			return true;
		}

		return false;
	}

	internal static bool TryConvert( object from, System.Type targetType, out object convertedValue )
	{
		convertedValue = null;

		//
		// Old value is null
		//
		if ( from == null )
		{
			return true;
		}

		var fromT = from.GetType();

		//
		// Targetting Nullable? Work with the underlying type
		//
		if ( targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof( Nullable<> ) )
		{
			targetType = Nullable.GetUnderlyingType( targetType ) ?? targetType;
		}

		//
		// Type compatible matches
		//
		if ( fromT == targetType || fromT.IsAssignableTo( targetType ) )
		{
			convertedValue = from;
			return true;
		}

		//
		// String
		//
		if ( targetType == typeof( string ) )
		{
			convertedValue = FormattableString.Invariant( $"{from}" );
			return true;
		}

		//
		// JsonElement
		//
		if ( from is JsonElement jse )
		{
			convertedValue = jse.Deserialize( targetType, options );
			return true;
		}

		//
		// Bool
		//
		if ( targetType == typeof( bool ) )
		{
			convertedValue = $"{from}".ToBool();
			return true;
		}

		//
		// Bool
		//
		if ( targetType.IsEnum )
		{
			convertedValue = System.Enum.Parse( targetType, from.ToString() );
			return true;
		}

		//
		// Float
		//
		if ( targetType == typeof( float ) )
		{
			if ( float.TryParse( from.ToString(), CultureInfo.InvariantCulture, out var f ) )
			{
				convertedValue = f;
				return true;
			}

			return false;
		}

		//
		// Int
		//
		if ( targetType == typeof( int ) )
		{
			if ( from is string str )
			{
				convertedValue = str.ToInt();
				return true;
			}

			convertedValue = Convert.ToInt32( from );
			return true;
		}

		//
		// List
		//
		if ( targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof( List<string> ).GetGenericTypeDefinition() )
		{
			if ( fromT.IsArray && fromT.GetElementType() == targetType.GenericTypeArguments[0] )
			{
				convertedValue = System.Activator.CreateInstance( targetType, new[] { from } );
				return true;
			}
		}

		//
		// Array
		//
		if ( targetType.IsArray )
		{
			if ( from is IList list )
			{
				var a = Array.CreateInstance( fromT.GenericTypeArguments[0], list.Count );
				list.CopyTo( a, 0 );
				convertedValue = a;
				return true;
			}
		}

		//
		// Implicit conversion
		//
		var op_Implicit = targetType.GetMethod( "op_Implicit", new[] { from.GetType() } );
		if ( op_Implicit != null )
		{
			convertedValue = op_Implicit.Invoke( null, new[] { from } );
			return true;
		}

		//
		// convertable
		//
		if ( from is IConvertible )
		{
			try
			{
				var changedValue = System.Convert.ChangeType( from, targetType );
				if ( changedValue is not null )
				{
					convertedValue = changedValue;
					return true;
				}
			}
			catch
			{
				Log.Trace( $"Couldn't convert {from} from its type to {targetType}" );
			}
		}

		// TODO - serialize to json, deserialize from json

		return false;
	}
}
