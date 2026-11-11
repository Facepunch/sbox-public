using System.Text.Json;
using System.Net.Http;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// AI conversation responses and request errors authenticated with the current game session.
/// </summary>
public static partial class Ai
{
	/// <summary>
	/// Last known account balance and monthly renewal date, refreshed by login and AI responses.
	/// Null means no balance is available yet. Reading this property does not make a network request.
	/// </summary>
	public static CreditBalance? Credits { get; internal set; }

	/// <summary>
	/// Refreshes the cached account balance on demand, for example after a grant or monthly renewal.
	/// Login and AI responses normally keep Credits up to date without polling.
	/// </summary>
	public static async Task<CreditBalance> RefreshCredits( CancellationToken cancellationToken = default )
		=> new( await Send( () => Backend.Ai.GetCredits( cancellationToken ) ) );

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
			var response = await request();
			UpdateCredits( response switch
			{
				AiQueryResponse query => query.Credits,
				AiChatToolResponse tool => tool.Credits,
				ServiceApi.CreditBalance balance => balance,
				_ => null
			} );
			return response;
		}
		catch ( Refit.ApiException ex )
		{
			AiError? error = null;
			try
			{
				error = JsonSerializer.Deserialize<AiError?>( ex.Content ?? "null", JsonSerializerOptions.Web );
			}
			catch ( JsonException ) { }
			UpdateCredits( error?.Credits );
			throw new RequestException( (int)ex.StatusCode, error?.Code ?? "http_error", error?.Error ?? ex.Message, ex )
			{
				Credits = CreditBalance.FromDto( error?.Credits ),
				RenewsAt = error?.RenewsAt ?? error?.Credits?.RenewsAt
			};
		}
		catch ( HttpRequestException ex )
		{
			throw new RequestException( null, "network_error", "The AI server could not be reached. Retry to continue the conversation.", ex );
		}
	}

	static void UpdateCredits( ServiceApi.CreditBalance? balance )
	{
		if ( balance.HasValue ) Credits = CreditBalance.FromDto( balance );
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

		/// <summary>
		/// Account balance reported with this failure, or null when unavailable.
		/// </summary>
		public CreditBalance? Credits { get; internal init; }

		/// <summary>
		/// Next monthly allowance renewal, when supplied by the backend.
		/// </summary>
		public DateTimeOffset? RenewsAt { get; internal init; }
	}

	/// <summary>
	/// An assistant response and credits charged for the request.
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
			CreditsSpent = response.CreditsSpent;
			TotalCreditsSpent = (response as AiChatResponse)?.TotalCreditsSpent;
			Credits = CreditBalance.FromDto( response.Credits );
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
		/// Credits charged for this request, or null when accounting is unavailable.
		/// </summary>
		public decimal? CreditsSpent { get; init; }

		/// <summary>
		/// Total credits charged across the conversation, or null without a conversation or available accounting.
		/// </summary>
		public decimal? TotalCreditsSpent { get; init; }

		/// <summary>
		/// Account balance after the request, or null when unavailable.
		/// </summary>
		public CreditBalance? Credits { get; init; }

		/// <summary>
		/// Functions to execute and answer before sending another user message.
		/// </summary>
		internal IReadOnlyList<AiToolCall> ToolCalls { get; init; }
	}
}
