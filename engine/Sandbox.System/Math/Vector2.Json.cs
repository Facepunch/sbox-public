using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class Vector2Converter : JsonConverter<Vector2>
	{
		public override Vector2 Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String )
			{
				return Vector2.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Vector2 v = default;

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

				while ( reader.TokenType != JsonTokenType.EndArray )
				{
					reader.Read();
				}

				return v;
			}

			if ( reader.TokenType == JsonTokenType.StartObject )
			{
				reader.Read();

				Vector2 v = default;

				while ( reader.TokenType != JsonTokenType.EndObject )
				{
					if ( reader.TokenType == JsonTokenType.PropertyName )
					{
						var name = reader.GetString();
						reader.Read();

						if ( reader.TokenType == JsonTokenType.Number )
						{
							var val = reader.GetSingle();
							if ( name == "x" ) v.x = val;
							if ( name == "y" ) v.y = val;
						}
						reader.Read();
					}
					else
					{
						reader.Read();
					}
				}

				return v;
			}

			Log.Warning( $"Vector2FromJson - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Vector2 val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{val.x:G9},{val.y:G9}" ) );
		}
		public override Vector2 ReadAsPropertyName( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			return Vector2.Parse( reader.GetString() );
		}

		public override void WriteAsPropertyName( Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options )
		{
			writer.WritePropertyName( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{value.x:G9},{value.y:G9}" ) );
		}

		public override bool CanConvert( Type typeToConvert )
		{
			return typeToConvert == typeof( Vector2 ) || typeToConvert == typeof( string );
		}

	}
}
