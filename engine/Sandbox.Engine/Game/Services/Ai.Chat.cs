using System.Threading;

namespace Sandbox.Services;

public static partial class Ai
{
	/// <summary>
	/// A conversation with client-owned message history and credit accounting.
	/// Outgoing history retains up to 16 MiB of image content, replacing older images with text placeholders.
	/// </summary>
	public sealed partial class Chat
	{
		readonly ServiceApi.IAiApi api;
		readonly List<Message> messages = [];
		readonly Dictionary<string, AiToolRegistration> tools;
		readonly object context;
		readonly AiChat conversation;

		internal Chat( ServiceApi.IAiApi api, IEnumerable<AiToolRegistration> tools, object context, AiChatOptions options )
		{
			this.api = api;
			this.tools = tools.ToDictionary( x => x.Definition.Name, StringComparer.Ordinal );
			this.context = context;
			conversation = new AiChat { Options = options };
			Messages = messages.AsReadOnly();
		}

		/// <summary>
		/// Client-generated identifier for usage accounting. Reset starts a new conversation with a new identifier.
		/// </summary>
		public Guid Id => conversation.Id;

		/// <summary>
		/// User messages and final assistant replies acknowledged by this instance, in order.
		/// A queued user message appears once the first assistant response is received.
		/// </summary>
		public IReadOnlyList<Message> Messages { get; }

		/// <summary>
		/// Most recent final assistant response, or null before the first completed turn.
		/// </summary>
		public Response LastResponse { get; private set; }

		/// <summary>
		/// Total credits charged for this chat, or null when accounting is unavailable or incomplete.
		/// Refreshed by assistant and server-tool responses.
		/// </summary>
		public decimal? TotalCreditsSpent => conversation.TotalCreditsSpent;

		internal bool HasPendingTurn => pendingTurn is not null;

		async Task<Response> QueryRequest( CancellationToken cancellationToken )
		{
			cancellationToken.ThrowIfCancellationRequested();
			TrimImageHistory();
			var result = await Ai.Send( () => api.QueryChat( conversation, cancellationToken ) );
			conversation.AddResponse( result );
			return new Response( result );
		}
	}
}
