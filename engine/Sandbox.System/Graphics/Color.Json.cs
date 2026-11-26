
using System.Text.Json.Serialization;

namespace Sandbox.Internal.JsonConvert
{
	internal class ColorConverter : JsonConverter<Color>
	{
		public override Color Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String )
			{
				return Vector4.Parse( reader.GetString() );
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				reader.Read();

				Color v = Color.White;

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.r = reader.GetSingle();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.g = reader.GetSingle();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.b = reader.GetSingle();
					reader.Read();
				}

				if ( reader.TokenType == JsonTokenType.Number )
				{
					v.a = reader.GetSingle();
					reader.Read();
				}

				while ( reader.TokenType != JsonTokenType.EndArray )
				{
					reader.Read();
				}

				return v;
			}

			Log.Warning( $"ColorFromJson - unable to read from {reader.TokenType}" );

			return default;
		}

		public override void Write( Utf8JsonWriter writer, Color val, JsonSerializerOptions options )
		{
			// Read back with the invariant culture, a comma decimal separator would split every component in two
			Span<char> text = stackalloc char[128];
			writer.WriteStringValue( text.TryWrite( System.Globalization.CultureInfo.InvariantCulture, $"{val.r:0.#####},{val.g:0.#####},{val.b:0.#####},{val.a:0.#####}", out var written ) ? text[..written] : string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{val.r:0.#####},{val.g:0.#####},{val.b:0.#####},{val.a:0.#####}" ) );
		}
	}
}
