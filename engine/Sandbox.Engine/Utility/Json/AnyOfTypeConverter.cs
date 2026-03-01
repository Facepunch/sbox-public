using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

internal sealed class AnyOfTypeConverterFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert )
	{
		return typeToConvert.IsGenericType
			&& typeToConvert.GetGenericTypeDefinition() == typeof( AnyOfType<> );
	}

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		var baseType = typeToConvert.GetGenericArguments()[0];
		var converterType = typeof( AnyOfTypeConverter<> ).MakeGenericType( baseType );
		return (JsonConverter)System.Activator.CreateInstance( converterType );
	}
}

internal sealed class AnyOfTypeConverter<T> : JsonConverter<AnyOfType<T>> where T : class
{
	public override AnyOfType<T> Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType is JsonTokenType.Null or not JsonTokenType.StartObject )
			return default;

		using var doc = JsonDocument.ParseValue( ref reader );
		var root = doc.RootElement;

		if ( !root.TryGetProperty( "Type", out var typeProp ) )
			return default;

		var typeName = typeProp.GetString();
		var instance = Game.TypeLibrary.GetType<T>( typeName )?.Create<T>();
		if ( instance is null )
			return default;

		var instanceType = Game.TypeLibrary.GetType( instance.GetType() );
		if ( instanceType is not null )
		{
			DeserializeProperties( instance, instanceType, root, options );
		}

		return new AnyOfType<T>( instance, typeName );
	}

	static void DeserializeProperties( T instance, TypeDescription instanceType, JsonElement root, JsonSerializerOptions options )
	{
		foreach ( var prop in instanceType.Properties )
		{
			if ( !prop.CanWrite || !prop.IsPublic ) continue;
			if ( prop.HasAttribute<JsonIgnoreAttribute>() ) continue;
			if ( !root.TryGetProperty( prop.Name, out var propElement ) ) continue;

			try
			{
				var value = JsonSerializer.Deserialize( propElement.GetRawText(), prop.PropertyType, options );
				prop.SetValue( instance, value );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( ex, $"Failed to deserialize property {prop.Name} on {instanceType.Name}" );
			}
		}
	}

	public override void Write( Utf8JsonWriter writer, AnyOfType<T> value, JsonSerializerOptions options )
	{
		if ( !value.HasValue )
		{
			writer.WriteNullValue();
			return;
		}

		var concreteType = value.Value.GetType();
		var typeDesc = Game.TypeLibrary.GetType( concreteType );

		writer.WriteStartObject();
		writer.WriteString( "Type", typeDesc?.ClassName ?? concreteType.Name );

		if ( typeDesc is not null )
		{
			foreach ( var prop in typeDesc.Properties )
			{
				if ( !prop.IsPublic ) continue;
				if ( prop.HasAttribute<JsonIgnoreAttribute>() ) continue;

				try
				{
					writer.WritePropertyName( prop.Name );
					JsonSerializer.Serialize( writer, prop.GetValue( value.Value ), prop.PropertyType, options );
				}
				catch ( System.Exception ex )
				{
					Log.Warning( ex, $"Failed to serialize property {prop.Name} on {concreteType.Name}" );
				}
			}
		}

		writer.WriteEndObject();
	}
}
