using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	/// <summary>
	/// Provides standalone queries and cached, tool-enabled conversations using the caller's game session.
	/// </summary>
	public interface IAiApi
	{
		/// <summary>
		/// Generates a response. No conversation state is retained between calls.
		/// </summary>
		[Post( "/ai/query" )]
		Task<AiQueryResponse> Query( [Body] AiQueryRequest input, CancellationToken cancellationToken = default );

		/// <summary>
		/// Creates a cached conversation with instructions and caller-defined tools.
		/// </summary>
		[Post( "/ai/chat/create" )]
		Task<AiChat> CreateChat( [Body] AiChatCreate options, CancellationToken cancellationToken = default );

		/// <summary>
		/// Sends a new message or tool results to an existing chat and generates the next assistant response.
		/// </summary>
		[Post( "/ai/chat/query" )]
		Task<AiChatResponse> QueryChat( [Body] AiChatInput input, CancellationToken cancellationToken = default );
	}
}
