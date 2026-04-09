using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class AnglesConverter : JsonConverter<Angles>
	{
		public override Angles Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.Null )
			{
				return default;
			}

			if ( reader.TokenType == JsonTokenType.String )
			{
				return Angles.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Angles v = default;

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.pitch = reader.GetSingle();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.yaw = reader.GetSingle();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.roll = reader.GetSingle();
					reader.Read();
				}

				while ( reader.TokenType != JsonTokenType.EndArray )
				{
					reader.Read();
				}

				return v;
			}

			Log.Warning( $"AnglesConverter - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Angles val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{val.pitch:G9},{val.yaw:G9},{val.roll:G9}" ) );
		}
	}
}
