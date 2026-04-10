using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class Vector3Converter : JsonConverter<Vector3>
	{
		public override Vector3 Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.Null )
			{
				return default;
			}

			if ( reader.TokenType == JsonTokenType.String )
			{
				return Vector3.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Vector3 v = default;

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.x = reader.GetSingle();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.y = reader.GetSingle();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.z = reader.GetSingle();
					reader.Read();
				}

				while ( reader.TokenType != JsonTokenType.EndArray )
				{
					reader.Read();
				}

				return v;
			}

			Log.Warning( $"Vector3FromJson - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Vector3 val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{val.x:G9},{val.y:G9},{val.z:G9}" ) );
		}

		public override Vector3 ReadAsPropertyName( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			return Vector3.Parse( reader.GetString() );
		}

		public override void WriteAsPropertyName( Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options )
		{
			writer.WritePropertyName( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{value.x:G9},{value.y:G9},{value.z:G9}" ) );
		}

		public override bool CanConvert( Type typeToConvert )
		{
			return typeToConvert == typeof( Vector3 ) || typeToConvert == typeof( string );
		}
	}
}
