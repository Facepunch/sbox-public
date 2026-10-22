namespace Sandbox.Services;

/// <summary>
/// An unsuccessful AI API request. The HTTP status describes the category of failure.
/// </summary>
public class AiError
{
	/// <summary>
	/// Stable machine-readable identifier, such as chat_busy or request_id_conflict.
	/// Callers should handle unknown codes using the HTTP status.
	/// </summary>
	public string Code { get; set; }

	/// <summary>
	/// Readable explanation of the failure. This text may change independently of the code.
	/// </summary>
	public string Error { get; set; }
}
