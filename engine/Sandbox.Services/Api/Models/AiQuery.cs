using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Sandbox.Services;

/// <summary>
/// A standalone AI request. Callers supply all context needed for the response.
/// </summary>
public class AiQueryRequest
{
	/// <summary>
	/// Input to answer, including any context needed for this request. Optional when images are supplied.
	/// </summary>
	public string Input { get; set; }

	/// <summary>
	/// Image URLs or base64 data URLs to include with the input.
	/// </summary>
	public List<string> Images { get; set; } = [];

	/// <summary>
	/// Optional instructions describing how the AI should respond.
	/// </summary>
	public string Instructions { get; set; }

	/// <summary>
	/// Service profile to use. Defaults to Fast; arbitrary model names are not accepted.
	/// </summary>
	[EnumDataType( typeof( AiProfile ) )]
	public AiProfile Profile { get; set; } = AiProfile.Fast;

	/// <summary>
	/// Optional maximum output tokens, including reasoning. Omit to use the preset or provider default.
	/// </summary>
	[Range( 1, int.MaxValue )]
	public int? MaxOutputTokens { get; set; }

	/// <summary>
	/// Optional JSON Schema object for strict structured output. Omit for plain text.
	/// </summary>
	public JsonElement? JsonSchema { get; set; }
}

/// <summary>
/// The generated text or JSON, completion state, and provider-reported token usage.
/// </summary>
public class AiQueryResponse
{
	/// <summary>
	/// Generated text, or serialized JSON when a schema was requested. May be partial on failure.
	/// </summary>
	public string Text { get; set; }

	/// <summary>
	/// Whether generation completed normally without a refusal.
	/// </summary>
	public bool IsSuccess { get; set; }

	/// <summary>
	/// Why generation ended, such as Stop, Length, or ContentFilter.
	/// </summary>
	public string FinishReason { get; set; }

	/// <summary>
	/// Provider refusal message, when present.
	/// </summary>
	public string Refusal { get; set; }

	/// <summary>
	/// Input tokens reported by the provider, or null when unavailable.
	/// </summary>
	public int? InputTokens { get; set; }

	/// <summary>
	/// Output tokens reported by the provider, including reasoning, or null when unavailable.
	/// </summary>
	public int? OutputTokens { get; set; }

	/// <summary>
	/// Amount charged for this model response in US dollars, or null when unavailable.
	/// </summary>
	public decimal? CostUsd { get; set; }

	/// <summary>
	/// Input tokens read from the provider's prompt cache, or null when unavailable.
	/// </summary>
	public int? CachedInputTokens { get; set; }

	/// <summary>
	/// Input tokens written to the provider's prompt cache, or null when unavailable.
	/// </summary>
	public int? CacheWriteTokens { get; set; }
}
