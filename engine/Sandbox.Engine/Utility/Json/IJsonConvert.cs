using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Allows writing JsonConverter in a more compact way, without having to pre-register them.
/// </summary>
public interface IJsonConvert
{
	public abstract static object JsonRead( ref Utf8JsonReader reader, Type typeToConvert );
	public abstract static void JsonWrite( object value, Utf8JsonWriter writer );
}

public sealed class JsonConvertFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert )
	{
		return typeToConvert.IsAssignableTo( typeof( IJsonConvert ) );
	}

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		Type constructed = typeof( JsonSerializedConvert<> ).MakeGenericType( new Type[] { typeToConvert } );

		return (JsonConverter)Activator.CreateInstance( constructed );
	}
}

file class JsonSerializedConvert<T> : JsonConverter<T> where T : IJsonConvert
{
	public override bool CanConvert( Type typeToConvert )
	{
		return typeToConvert.IsAssignableTo( typeof( IJsonConvert ) );
	}

	public override T Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		return (T)T.JsonRead( ref reader, typeToConvert );
	}

	public override void Write( Utf8JsonWriter writer, T val, JsonSerializerOptions options )
	{
		T.JsonWrite( val, writer );
	}
}

/// <summary>
/// Converts a Unix timestamp (seconds since epoch) to a DateTimeOffset.
/// </summary>
class UnixTimestampConverter : JsonConverter<DateTimeOffset>
{
	public override DateTimeOffset Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType == JsonTokenType.Number )
		{
			long unixTimestamp = reader.GetInt64();
			return DateTimeOffset.FromUnixTimeSeconds( unixTimestamp );
		}

		throw new JsonException( $"Unable to convert {reader.TokenType} to DateTimeOffset" );
	}

	public override void Write( Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options )
	{
		writer.WriteNumberValue( value.ToUnixTimeSeconds() );
	}
}
