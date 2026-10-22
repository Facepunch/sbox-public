using System.Text.Json;
using System.Threading;

namespace Sandbox.Services;

public static partial class Ai
{
	/// <summary>
	/// A conversation with local message history and provider-reported costs.
	/// </summary>
	public sealed class Chat
	{
		readonly ServiceApi.IAiApi api;
		readonly List<Message> messages = [];
		readonly HashSet<Guid> acknowledged = [];

		internal Chat( Guid id, ServiceApi.IAiApi api )
		{
			Id = id;
			this.api = api;
			Messages = messages.AsReadOnly();
		}

		/// <summary>
		/// Backend identifier, available for diagnostics.
		/// </summary>
		public Guid Id { get; }

		/// <summary>
		/// User messages, tool results and assistant replies acknowledged by this instance, in order.
		/// Failed HTTP requests are omitted and replayed submissions are included only once.
		/// </summary>
		public IReadOnlyList<Message> Messages { get; }

		/// <summary>
		/// Most recently acknowledged assistant response, or null before the first response.
		/// </summary>
		public Response LastResponse { get; private set; }

		/// <summary>
		/// Total cost of acknowledged responses in US dollars, or null if any cost was unavailable.
		/// Replayed responses are counted only once.
		/// </summary>
		public decimal? TotalCostUsd { get; private set; } = 0m;

		/// <summary>
		/// Sends a message, images, or results for all outstanding tool calls.
		/// Supply the same requestId and content when retrying to retrieve the original response.
		/// </summary>
		public Task<Response> Query( string input = null, IEnumerable<ToolResult> toolResults = null,
			IEnumerable<string> images = null, Guid? requestId = null, CancellationToken cancellationToken = default )
			=> QueryInternal( input, toolResults, images, requestId, null, result => new Response( result ), cancellationToken );

		/// <summary>
		/// Requests a structured final response using a JSON Schema generated from T.
		/// Tool calls, refusals and incomplete replies have no Data. Use this overload again when returning tool results.
		/// </summary>
		public async Task<Response<T>> Query<T>( string input = null, IEnumerable<ToolResult> toolResults = null,
			IEnumerable<string> images = null, Guid? requestId = null, CancellationToken cancellationToken = default )
		{
			var response = await QueryInternal( input, toolResults, images, requestId, CreateSchema<T>(), result => new Response<T>( result ), cancellationToken );
			response.ReadData();
			return response;
		}

		async Task<TResponse> QueryInternal<TResponse>( string input, IEnumerable<ToolResult> toolResults, IEnumerable<string> images,
			Guid? requestId, JsonElement? schema, Func<AiChatResponse, TResponse> createResponse, CancellationToken cancellationToken ) where TResponse : Response
		{
			var request = new AiChatInput
			{
				RequestId = requestId ?? Guid.NewGuid(),
				Id = Id,
				Input = input,
				Images = images?.ToList() ?? [],
				ToolResults = toolResults?.Select( x => new AiToolResult { ToolCallId = x.Id, Content = x.Content } ).ToList() ?? [],
				JsonSchema = schema
			};
			var result = await Send( () => api.QueryChat( request, cancellationToken ) );
			var response = createResponse( result );

			if ( acknowledged.Add( request.RequestId ) )
			{
				if ( input is not null || request.Images.Count > 0 )
					messages.Add( new Message { Role = "user", Text = input, Images = request.Images.AsReadOnly() } );
				foreach ( var tool in request.ToolResults )
					messages.Add( new Message { Role = "tool", Text = tool.Content, ToolCallId = tool.ToolCallId } );
				messages.Add( new Message { Role = "assistant", Text = response.Text, Response = response } );
				LastResponse = response;
				TotalCostUsd += response.CostUsd;
			}
			return response;
		}
	}
}
