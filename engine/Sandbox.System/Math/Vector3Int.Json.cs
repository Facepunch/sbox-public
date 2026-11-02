using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class Vector3IntConverter : JsonConverter<Vector3Int>
	{
		public override Vector3Int Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.Null )
			{
				return default;
			}

			if ( reader.TokenType == JsonTokenType.String )
			{
				return Vector3Int.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Vector3Int v = default;

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

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.z = reader.GetInt32();
					reader.Read();
				}

				while ( reader.TokenType != JsonTokenType.EndArray )
				{
					reader.Read();
				}

				return v;
			}

			Log.Warning( $"Vector3IntFromJson - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Vector3Int val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( FormattableString.Invariant( $"{val.x},{val.y},{val.z}" ) );
		}

		public override Vector3Int ReadAsPropertyName( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			return Vector3Int.Parse( reader.GetString() );
		}

		public override void WriteAsPropertyName( Utf8JsonWriter writer, Vector3Int value, JsonSerializerOptions options )
		{
			writer.WritePropertyName( value.ToString() );
		}

		public override bool CanConvert( Type typeToConvert )
		{
			return typeToConvert == typeof( Vector3Int ) || typeToConvert == typeof( string );
		}
	}
}
