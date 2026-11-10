namespace Sandbox.Services;

/// <summary>
/// An unsuccessful AI API request. The HTTP status describes the category of failure.
/// </summary>
public readonly record struct AiError
{
	/// <summary>
	/// Account balance after an authenticated operation failed, including any completed paid rounds.
	/// Null means unavailable or unauthenticated; retain the last known balance.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public ServiceApi.CreditBalance? Credits { get; init; }

	/// <summary>
	/// Stable machine-readable identifier, such as chat_busy or request_id_conflict.
	/// Callers should handle unknown codes using the HTTP status.
	/// </summary>
	public string Code { get; init; }

	/// <summary>
	/// Readable explanation of the failure. This text may change independently of the code.
	/// </summary>
	public string Error { get; init; }

	/// <summary>
	/// Next monthly credit renewal, supplied when a request is blocked by an exhausted allowance.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public DateTimeOffset? RenewsAt { get; init; }
}
