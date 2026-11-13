using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	/// <summary>
	/// Provides standalone queries and stateless chat operations using the caller's game session.
	/// </summary>
	public interface IAiApi
	{
		/// <summary>
		/// Returns the current player's remaining credits and next monthly renewal.
		/// </summary>
		[Get( "/ai/credits" )]
		Task<ServiceApi.CreditBalance> GetCredits( CancellationToken cancellationToken = default );

		/// <summary>
		/// Generates a response. No conversation state is retained between calls.
		/// </summary>
		[Post( "/ai/query" )]
		Task<AiQueryResponse> Query( [Body] AiQueryRequest input, CancellationToken cancellationToken = default );

		/// <summary>
		/// Generates one assistant message. The client supplies and retains the complete conversation history.
		/// </summary>
		[Post( "/ai/chat/query" )]
		Task<AiChatResponse> QueryChat( [Body] AiChat input, CancellationToken cancellationToken = default );

		/// <summary>
		/// Executes one server-owned tool. Save its result in history before continuing the conversation.
		/// </summary>
		[Post( "/ai/chat/tool" )]
		Task<AiChatToolResponse> ExecuteChatTool( [Body] AiChatToolInput input, CancellationToken cancellationToken = default );
	}
}
