
using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class Color32Converter : JsonConverter<Color32>
	{
		public override Color32 Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String )
			{
				var clr = Color32.Parse( reader.GetString() );
				if ( !clr.HasValue ) return Color32.White;
				return clr.Value;
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Color32 v = Color32.White;

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.r = reader.GetByte();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.g = reader.GetByte();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.b = reader.GetByte();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.a = reader.GetByte();
					reader.Read();
				}

				while ( reader.TokenType != JsonTokenType.EndArray )
				{
					reader.Read();
				}

				return v;
			}

			Log.Warning( $"Color32FromJson - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Color32 val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( FormattableString.Invariant( $"{val.r},{val.g},{val.b},{val.a}" ) );
		}
	}
}
