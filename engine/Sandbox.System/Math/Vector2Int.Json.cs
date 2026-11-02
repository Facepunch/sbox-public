using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class Vector2IntConverter : JsonConverter<Vector2Int>
	{
		public override Vector2Int Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String )
			{
				return Vector2Int.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Vector2Int v = default;

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.x = reader.GetInt32();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.y = reader.GetInt32();
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

				Vector2Int v = default;

				while ( reader.TokenType != JsonTokenType.EndObject )
				{
					if ( reader.TokenType == JsonTokenType.PropertyName )
					{
						var name = reader.GetString();
						reader.Read();

						if ( reader.TokenType == JsonTokenType.Number )
						{
							var val = reader.GetInt32();
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

			Log.Warning( $"Vector2IntFromJson - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Vector2Int val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( FormattableString.Invariant( $"{val.x},{val.y}" ) );
		}

		public override Vector2Int ReadAsPropertyName( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			return Vector2Int.Parse( reader.GetString() );
		}

		public override void WriteAsPropertyName( Utf8JsonWriter writer, Vector2Int value, JsonSerializerOptions options )
		{
			writer.WritePropertyName( value.ToString() );
		}

		public override bool CanConvert( Type typeToConvert )
		{
			return typeToConvert == typeof( Vector2Int ) || typeToConvert == typeof( string );
		}
	}
}
