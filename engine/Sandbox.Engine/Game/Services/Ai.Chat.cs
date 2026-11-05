using System.Threading;

namespace Sandbox.Services;

public static partial class Ai
{
	/// <summary>
	/// A conversation with local message history and provider-reported costs.
	/// </summary>
	public sealed partial class Chat
	{
		readonly ServiceApi.IAiApi api;
		readonly List<Message> messages = [];
		readonly Dictionary<string, AiToolRegistration> tools;
		readonly object context;
		readonly AiChatCreate createOptions;

		internal Chat( Guid id, ServiceApi.IAiApi api, decimal? totalCostUsd, IEnumerable<AiToolRegistration> tools, object context, AiChatCreate createOptions )
		{
			Id = id;
			this.api = api;
			this.tools = tools.ToDictionary( x => x.Definition.Name, StringComparer.Ordinal );
			this.context = context;
			this.createOptions = createOptions;
			TotalCostUsd = totalCostUsd;
			Messages = messages.AsReadOnly();
		}

		/// <summary>
		/// Backend identifier, available for diagnostics. Changes after Reset creates a new conversation.
		/// </summary>
		public Guid Id { get; private set; }

		/// <summary>
		/// User messages and final assistant replies acknowledged by this instance, in order.
		/// Failed HTTP requests are omitted and replayed submissions are included only once.
		/// </summary>
		public IReadOnlyList<Message> Messages { get; }

		/// <summary>
		/// Most recent final assistant response, or null before the first completed turn.
		/// </summary>
		public Response LastResponse { get; private set; }

		/// <summary>
		/// Backend-reported total cost of this chat in US dollars, or null when accounting is unavailable or incomplete.
		/// Refreshed on every successful query, including replayed submissions.
		/// </summary>
		public decimal? TotalCostUsd { get; private set; }

		internal bool HasPendingTurn => pendingTurn is not null;

		async Task<Response> QueryRequest( AiChatInput request, CancellationToken cancellationToken )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = await Ai.Send( () => api.QueryChat( request, cancellationToken ) );
			var response = new Response( result );
			TotalCostUsd = result.TotalCostUsd;

			if ( request.Input is not null || request.Images.Count > 0 )
			{
				messages.Add( new Message { Role = "user", Text = request.Input, Images = request.Images.AsReadOnly() } );
			}

			return response;
		}
	}
}
