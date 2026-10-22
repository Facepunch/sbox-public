using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;

namespace Sandbox.Services;

public static partial class Ai
{
	static readonly JsonSerializerOptions StructuredJson = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
		Converters = { new JsonStringEnumConverter() }
	};

	/// <summary>
	/// Generates a standalone structured response using a JSON Schema generated from T.
	/// </summary>
	public static async Task<Response<T>> Query<T>( string input, string instructions = null, IEnumerable<string> images = null, CancellationToken cancellationToken = default )
	{
		var result = await Send( () => Backend.Ai.Query( new AiQueryRequest
		{
			Input = input,
			Instructions = instructions,
			Images = images?.ToList() ?? [],
			Profile = AiProfile.Capable,
			JsonSchema = CreateSchema<T>()
		}, cancellationToken ) );
		var response = new Response<T>( result );
		response.ReadData();
		return response;
	}

	/// <summary>
	/// A structured response with the same completion state, tool calls and accounting as a text response.
	/// </summary>
	public sealed class Response<T> : Response
	{
		internal Response( AiQueryResponse response ) : base( response ) { }

		/// <summary>
		/// Deserialized final output, or default when no complete structured answer was returned.
		/// </summary>
		public T Data { get; private set; }

		/// <summary>
		/// Whether Data contains a completed structured answer. False for tool calls, refusals and incomplete replies.
		/// </summary>
		public bool HasData { get; private set; }

		internal void ReadData()
		{
			if ( !IsSuccess || FinishReason != "Stop" || ToolCalls.Count > 0 ) return;
			Data = JsonSerializer.Deserialize<T>( Text, StructuredJson );
			HasData = true;
		}
	}

	static JsonElement CreateSchema<T>()
	{
		var schema = StructuredJson.GetJsonSchemaAsNode( typeof( T ), new JsonSchemaExporterOptions
		{
			TreatNullObliviousAsNonNullable = true,
			TransformSchemaNode = ( context, node ) =>
			{
				if ( node is not JsonObject obj ) return node;
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
}
