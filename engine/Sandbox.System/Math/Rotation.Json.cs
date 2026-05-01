using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class RotationConverter : JsonConverter<Rotation>
	{
		public override Rotation Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String )
			{
				return Rotation.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Rotation v = default;

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

			Log.Warning( $"RotationConverter - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Rotation val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{val.x:G9},{val.y:G9},{val.z:G9},{val.w:G9}" ) );
		}
	}
}
