using System.Text.Json;
using System.Net.Http;

namespace Sandbox.Services;

/// <summary>
/// AI conversation responses and request errors authenticated with the current game session.
/// </summary>
public static partial class Ai
{
	/// <summary>
	/// A user message or final assistant reply in a chat's local history.
	/// </summary>
	public sealed class Message
	{
		/// <summary>
		/// Sender: user or assistant.
		/// </summary>
		public string Role { get; init; }

		/// <summary>
		/// Message text, when present.
		/// </summary>
		public string Text { get; init; }

		/// <summary>
		/// Image URLs or base64 data URLs supplied with a user message.
		/// </summary>
		public IReadOnlyList<string> Images { get; init; } = [];

		/// <summary>
		/// Final assistant response and usage; null for user messages.
		/// </summary>
		public Response Response { get; init; }
	}

	internal static async Task<T> Send<T>( Func<Task<T>> request )
	{
		try
		{
			return await request();
		}
		catch ( Refit.ApiException ex )
		{
			AiError? error = null;
			try
			{
				error = JsonSerializer.Deserialize<AiError?>( ex.Content ?? "null", JsonSerializerOptions.Web );
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
	/// An assistant response and provider-reported usage for one model call.
	/// </summary>
	public class Response
	{
		internal AiQueryResponse Source { get; }

		internal Response( AiQueryResponse response )
		{
			Source = response;
			Text = response.Text;
			IsSuccess = response.IsSuccess;
			FinishReason = response.FinishReason;
			Refusal = response.Refusal;
			CostUsd = response.CostUsd;
			TotalCostUsd = (response as AiChatResponse)?.TotalCostUsd;
			InputTokens = response.InputTokens;
			OutputTokens = response.OutputTokens;
			CachedInputTokens = response.CachedInputTokens;
			CacheWriteTokens = response.CacheWriteTokens;
			ToolCalls = (response as AiChatResponse)?.ToolCalls ?? [];
		}

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
		/// Backend-reported total cost across conversation/tool rounds, or null without a conversation or available accounting.
		/// </summary>
		public decimal? TotalCostUsd { get; init; }

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
		internal IReadOnlyList<AiToolCall> ToolCalls { get; init; }
	}
}
