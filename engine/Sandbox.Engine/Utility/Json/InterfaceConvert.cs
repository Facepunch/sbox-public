using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

internal sealed class InterfaceConverterFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert )
	{
		//
		// Apply only to interfaces, no generic args
		// Otherwise we pick up stuff like IList<T>
		//

		if ( !typeToConvert.IsInterface ) return false;
		if ( typeToConvert.IsGenericType ) return false;

		//
		// If the interface has its own converter, don't try
		// to handle it with this one.
		//

		var converterType = typeToConvert.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;

		return converterType is null || converterType == typeof( InterfaceConverterFactory );
	}

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		var converterType = typeof( InterfaceConverter<> ).MakeGenericType( typeToConvert );
		return (JsonConverter)Activator.CreateInstance( converterType )!;
	}
}

internal sealed class InterfaceConverter<T> : JsonConverter<T> where T : class
{
	/// <summary>
	/// A simple type value wrapper that indicates what type the value is.
	/// </summary>
	/// <param name="Type"></param>
	/// <param name="Value"></param>
	private record struct Wrapper( string Type, JsonElement Value );

	public override T Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		//
		// No token to read
		//
		if ( reader.TokenType == JsonTokenType.Null )
		{
			return null;
		}

		var data = JsonSerializer.Deserialize<Wrapper>( ref reader );

		//
		// Invalid data
		//
		if ( string.IsNullOrEmpty( data.Type ) || data.Value.ValueKind == JsonValueKind.Undefined )
		{
			return null;
		}

		//
		// Component types
		//
		if ( data.Type == "Component" )
		{
			var rawText = data.Value.GetRawText();
			var bytes = Encoding.UTF8.GetBytes( rawText );
			var r = new Utf8JsonReader( bytes.AsSpan() );
			r.Read(); // Advance to the first token
			var obj = Component.JsonRead( ref r, null );
			return (T)obj;
		}

		//
		// Resource types
		//
		if ( data.Type == "Resource" )
		{
			if ( data.Value.ValueKind == JsonValueKind.String )
			{
				var path = data.Value.GetString();
				var res = Resource.LoadFromPath( typeof( GameResource ), path );
				return res as T;
			}
		}

		return null;
	}

	public override void Write( Utf8JsonWriter writer, T value, JsonSerializerOptions options )
	{
		writer.WriteStartObject();

		if ( value is null )
		{
			writer.WriteNull( "Type" );
			writer.WriteNull( "Value" );
			writer.WriteEndObject();
			return;
		}

		if ( value is Resource resource )
		{
			writer.WriteString( "Type", "Resource" );
			writer.WritePropertyName( "Value" );
			writer.WriteStringValue( resource.ResourcePath );
			writer.WriteEndObject();
			return;
		}

		if ( value is Component component )
		{
			writer.WriteString( "Type", "Component" );
			writer.WritePropertyName( "Value" );
			Component.JsonWrite( component, writer );
			writer.WriteEndObject();
			return;
		}

		//
		// Unhandled type - write as null
		//
		writer.WriteNull( "Type" );
		writer.WriteNull( "Value" );
		writer.WriteEndObject();
	}
}
