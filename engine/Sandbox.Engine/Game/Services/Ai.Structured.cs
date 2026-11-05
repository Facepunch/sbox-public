using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Reflection;

namespace Sandbox.Services;

public static partial class Ai
{
	internal static readonly JsonSerializerOptions StructuredJson = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
		Converters = { new JsonStringEnumConverter() }
	};

	/// <summary>
	/// A structured response with the same completion state and accounting as a text response.
	/// </summary>
	public sealed class Response<T> : Response
	{
		internal Response( AiQueryResponse response ) : base( response ) { }

		/// <summary>
		/// Deserialized final output, or default when no complete structured answer was returned.
		/// </summary>
		public T Data { get; private set; }

		/// <summary>
		/// Whether Data contains a completed structured answer. False for refusals, incomplete replies or invalid JSON.
		/// </summary>
		public bool HasData { get; private set; }

		internal void ReadData()
		{
			if ( !IsSuccess || FinishReason != "Stop" || string.IsNullOrWhiteSpace( Text ) ) return;
			try
			{
				Data = JsonSerializer.Deserialize<T>( Text, StructuredJson );
				HasData = Data is not null;
			}
			catch ( JsonException ) { }
		}
	}

	internal static JsonElement CreateSchema<T>( JsonSerializerOptions options = null )
		=> CreateSchema( typeof( T ), options );

	internal static JsonElement CreateSchema( Type schemaType, JsonSerializerOptions options = null )
	{
		var schema = (options ?? StructuredJson).GetJsonSchemaAsNode( schemaType, new JsonSchemaExporterOptions
		{
			TreatNullObliviousAsNonNullable = true,
			TransformSchemaNode = ( context, node ) =>
			{
				if ( node is not JsonObject obj ) return node;
				var description = GetSchemaDescription( context.PropertyInfo?.AttributeProvider ) ?? GetSchemaDescription( context.TypeInfo.Type );
				if ( description is not null ) obj["description"] = description;
				obj.Remove( "pattern" );
				var type = Nullable.GetUnderlyingType( context.TypeInfo.Type ) ?? context.TypeInfo.Type;
				if ( type.IsEnum && obj["enum"] is JsonArray values && obj["type"] is null )
					obj["type"] = values.Any( value => value is null ) ? new JsonArray( "string", "null" ) : JsonValue.Create( "string" );
				if ( type == typeof( DateTime ) || type == typeof( DateTimeOffset ) ) obj.Remove( "format" );
				if ( obj["properties"] is JsonObject properties )
				{
					obj["additionalProperties"] = false;
					obj["required"] = new JsonArray( properties.Select( property => (JsonNode)JsonValue.Create( property.Key ) ).ToArray() );
				}
				return node;
			}
		} );
		return JsonSerializer.SerializeToElement( schema );
	}

	internal static string GetSchemaDescription( ICustomAttributeProvider provider )
	{
		foreach ( var attribute in provider?.GetCustomAttributes( true ) ?? [] )
		{
			var description = attribute switch
			{
				DescriptionAttribute game => game.Value,
				System.ComponentModel.DescriptionAttribute standard => standard.Description,
				_ => null
			};
			if ( !string.IsNullOrWhiteSpace( description ) ) return description;
		}
		return null;
	}
}
