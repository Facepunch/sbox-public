using System.Text.Json;
using System.Net.Http;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// Cached AI chats authenticated with the current game session.
/// </summary>
public static partial class Ai
{
	/// <summary>
	/// Creates a chat using the backend's tool-capable model profile.
	/// The returned object retains its history and continues without resending context or tools.
	/// </summary>
	public static async Task<Chat> CreateChat( string instructions, IEnumerable<Tool> tools = null, CancellationToken cancellationToken = default )
	{
		var api = Backend.Ai;
		var chat = await Send( () => api.CreateChat( new AiChatCreate
		{
			Instructions = instructions,
			Profile = AiProfile.Agent,
			Tools = tools?.Select( x => new AiToolDefinition
			{
				Name = x.Name,
				Description = x.Description,
				Parameters = x.Parameters
			} ).ToList() ?? []
		}, cancellationToken ) );
		return new Chat( chat.Id, api );
	}

	/// <summary>
	/// A user message, tool result or assistant reply in a chat's local history.
	/// </summary>
	public sealed class Message
	{
		/// <summary>
		/// Sender: user, tool or assistant.
		/// </summary>
		public string Role { get; init; }

		/// <summary>
		/// Message text or tool output, when present.
		/// </summary>
		public string Text { get; init; }

		/// <summary>
		/// Image URLs supplied with a user message.
		/// </summary>
		public IReadOnlyList<string> Images { get; init; } = [];

		/// <summary>
		/// Invocation answered by a tool result; null for other messages.
		/// </summary>
		public string ToolCallId { get; init; }

		/// <summary>
		/// Assistant response, including tool calls and usage; null for other messages.
		/// </summary>
		public Response Response { get; init; }
	}

	static async Task<T> Send<T>( Func<Task<T>> request )
	{
		try
		{
			return await request();
		}
		catch ( Refit.ApiException ex )
		{
			AiError error = null;
			try
			{
				error = JsonSerializer.Deserialize<AiError>( ex.Content ?? "null", JsonSerializerOptions.Web );
			}
			catch ( JsonException ) { }
			throw new RequestException( (int)ex.StatusCode, error?.Code ?? "http_error", error?.Error ?? ex.Message, ex );
		}
		catch ( HttpRequestException ex )
		{
			throw new RequestException( null, "network_error", "The AI server could not be reached. Retry to resend the same submission.", ex );
		}
	}

	/// <summary>
	/// An AI request failure with a code callers can handle without matching message text.
	/// </summary>
	public sealed class RequestException : Exception
	{
		internal RequestException( int? statusCode, string code, string message, Exception innerException ) : base( message, innerException )
		{
			StatusCode = statusCode;
			Code = code;
		}

		/// <summary>
		/// Backend HTTP status, or null when no response was received.
		/// </summary>
		public int? StatusCode { get; }

		/// <summary>
		/// Backend error code, or network_error when the server could not be reached.
		/// </summary>
		public string Code { get; }
	}

	/// <summary>
	/// A caller-owned function described by a JSON Schema for its arguments.
	/// </summary>
	public record Tool( string Name, string Description, JsonElement Parameters );

	/// <summary>
	/// A requested function invocation. Arguments contains serialized JSON.
	/// </summary>
	public record ToolCall( string Id, string Name, string Arguments );

	/// <summary>
	/// The output or error for a requested function invocation.
	/// </summary>
	public record ToolResult( string Id, string Content );

	/// <summary>
	/// An assistant response and provider-reported usage for one model call.
	/// </summary>
	public class Response
	{
		internal Response( AiQueryResponse response )
		{
			RequestId = (response as AiChatResponse)?.RequestId ?? Guid.Empty;
			Text = response.Text;
			IsSuccess = response.IsSuccess;
			FinishReason = response.FinishReason;
			Refusal = response.Refusal;
			CostUsd = response.CostUsd;
			InputTokens = response.InputTokens;
			OutputTokens = response.OutputTokens;
			CachedInputTokens = response.CachedInputTokens;
			CacheWriteTokens = response.CacheWriteTokens;
			ToolCalls = Array.AsReadOnly( (response as AiChatResponse)?.ToolCalls.Select( x => new ToolCall( x.Id, x.Name, x.Arguments ) ).ToArray() ?? [] );
		}

		/// <summary>
		/// Identifier of the acknowledged submission, including when its response was replayed.
		/// </summary>
		public Guid RequestId { get; init; }

		/// <summary>
		/// Generated text, which can accompany tool calls.
		/// </summary>
		public string Text { get; init; }

		/// <summary>
		/// Whether the model completed normally.
		/// </summary>
		public bool IsSuccess { get; init; }

		/// <summary>
		/// Why generation ended.
		/// </summary>
		public string FinishReason { get; init; }

		/// <summary>
		/// Provider refusal text, when present.
		/// </summary>
		public string Refusal { get; init; }

		/// <summary>
		/// Amount charged for this response in US dollars, when reported.
		/// </summary>
		public decimal? CostUsd { get; init; }

		/// <summary>
		/// Input token count, when reported.
		/// </summary>
		public int? InputTokens { get; init; }

		/// <summary>
		/// Output token count, including reasoning, when reported.
		/// </summary>
		public int? OutputTokens { get; init; }

		/// <summary>
		/// Input tokens read from the provider's cache, when reported.
		/// </summary>
		public int? CachedInputTokens { get; init; }

		/// <summary>
		/// Input tokens written to the provider's cache, when reported.
		/// </summary>
		public int? CacheWriteTokens { get; init; }

		/// <summary>
		/// Functions to execute and answer before sending another user message.
		/// </summary>
		public IReadOnlyList<ToolCall> ToolCalls { get; init; }
	}
}
