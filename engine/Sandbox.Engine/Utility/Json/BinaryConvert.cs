using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// JSON converter that handles BinaryBlob serialization
/// </summary>
internal class BinaryConvert : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert )
	{
		return typeof( BlobData ).IsAssignableFrom( typeToConvert );
	}

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		return (JsonConverter)Activator.CreateInstance(
			typeof( BinaryBlobConverter<> ).MakeGenericType( typeToConvert ) );
	}

	private class BinaryBlobConverter<T> : JsonConverter<T> where T : BlobData
	{
		public override T Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			// Read the $blob reference
			if ( reader.TokenType != JsonTokenType.StartObject )
				return null;

			Guid? blobGuid = null;

			while ( reader.Read() )
			{
				if ( reader.TokenType == JsonTokenType.EndObject )
					break;

				if ( reader.TokenType == JsonTokenType.PropertyName )
				{
					var propertyName = reader.GetString();
					reader.Read();

					if ( propertyName == "$blob" && reader.TokenType == JsonTokenType.String )
					{
						if ( Guid.TryParse( reader.GetString(), out var guid ) )
						{
							blobGuid = guid;
						}
					}
				}
			}

			if ( blobGuid.HasValue )
			{
				return BlobDataSerializer.ReadBlob( blobGuid.Value, typeToConvert ) as T;
			}

			return null;
		}

		public override void Write( Utf8JsonWriter writer, T value, JsonSerializerOptions options )
		{
			// Register blob and write $blob reference
			var guid = BlobDataSerializer.RegisterBlob( value );

			writer.WriteStartObject();
			writer.WriteString( "$blob", guid.ToString() );
			writer.WriteEndObject();
		}
	}
}
