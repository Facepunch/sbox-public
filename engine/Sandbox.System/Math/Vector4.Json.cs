using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class Vector4Converter : JsonConverter<Vector4>
	{
		public override Vector4 Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String )
			{
				return Vector4.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Vector4 v = default;

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

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.w = reader.GetSingle();
					reader.Read();
				}

				while ( reader.TokenType != JsonTokenType.EndArray )
				{
					reader.Read();
				}

				return v;
			}

			Log.Warning( $"Vector4FromJson - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Vector4 val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{val.x:G9},{val.y:G9},{val.z:G9},{val.w:G9}" ) );
		}

		public override Vector4 ReadAsPropertyName( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			return Vector4.Parse( reader.GetString() );
		}

		public override void WriteAsPropertyName( Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options )
		{
			writer.WritePropertyName( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{value.x:G9},{value.y:G9},{value.z:G9},{value.w:G9}" ) );
		}

		public override bool CanConvert( Type typeToConvert )
		{
			return typeToConvert == typeof( Vector4 ) || typeToConvert == typeof( string );
		}
	}
}
