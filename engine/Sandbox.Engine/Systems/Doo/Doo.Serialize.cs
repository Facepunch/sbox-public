namespace Sandbox;

using System;
using System.Text.Json;

public partial class Doo : IJsonConvert
{
	/// <summary>
	/// Deserializes a Doo from JSON.
	/// </summary>
	public static object JsonRead( ref Utf8JsonReader reader, Type typeToConvert )
	{
		if ( reader.TokenType != JsonTokenType.StartObject )
			throw new JsonException( "Expected StartObject" );

		var doo = new Doo();

		while ( reader.Read() )
		{
			if ( reader.TokenType == JsonTokenType.EndObject )
				break;

			if ( reader.TokenType != JsonTokenType.PropertyName )
				throw new JsonException( "Expected PropertyName" );

			var propName = reader.GetString();
			reader.Read();

			switch ( propName )
			{
				case "body":
					doo.Body = Json.Deserialize<List<Block>>( ref reader );
					break;
				default:
					reader.Skip();
					break;
			}
		}

		return doo;
	}

	/// <summary>
	/// Serializes a Doo to JSON.
	/// </summary>
	public static void JsonWrite( object value, Utf8JsonWriter writer )
	{
		var doo = (Doo)value;
		writer.WriteStartObject();
		writer.WritePropertyName( "body" );
		Json.Serialize( writer, doo.Body );
		writer.WriteEndObject();
	}
}
