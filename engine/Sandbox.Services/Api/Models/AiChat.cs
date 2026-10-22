using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.Services;

/// <summary>
/// Request to create a chat with instructions, a profile, and caller-defined tools.
/// </summary>
public class AiChatCreate
{
	/// <summary>
	/// Instructions describing the assistant's role and behaviour.
	/// </summary>
	public string Instructions { get; set; }

	/// <summary>
	/// Backend-managed model profile.
	/// </summary>
	[EnumDataType( typeof( AiProfile ) )]
	public AiProfile Profile { get; set; } = AiProfile.Fast;

	/// <summary>
	/// Tools the caller can execute when requested by the assistant.
	/// </summary>
	public List<AiToolDefinition> Tools { get; set; } = [];

	/// <summary>
	/// Optional output-token limit for each model response, including reasoning.
	/// </summary>
	[Range( 1, int.MaxValue )]
	public int? MaxOutputTokens { get; set; }
}

/// <summary>
/// A caller-provided function and its argument schema.
/// </summary>
public class AiToolDefinition
{
	/// <summary>
	/// Unique function name used in tool calls.
	/// </summary>
	[Required]
	public string Name { get; set; }

	/// <summary>
	/// Describes what the function does and when to use it.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// JSON Schema object describing the function's arguments.
	/// </summary>
	public JsonElement Parameters { get; set; }
}

/// <summary>
/// An assistant request to execute one of the caller's functions.
/// </summary>
public class AiToolCall
{
	/// <summary>
	/// Identifier to include with the matching tool result.
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// Function to execute.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Function arguments encoded as JSON.
	/// </summary>
	public string Arguments { get; set; }
}

/// <summary>
/// The caller's result for an outstanding tool call.
/// </summary>
public class AiToolResult
{
	/// <summary>
	/// Identifier of the tool call being answered.
	/// </summary>
	[Required]
	public string ToolCallId { get; set; }

	/// <summary>
	/// Function output or error, as text or serialized JSON.
	/// </summary>
	[Required( AllowEmptyStrings = true )]
	public string Content { get; set; }
}

/// <summary>
/// New input for a conversation, without repeating its previous messages.
/// </summary>
public class AiChatInput
{
	/// <summary>
	/// Identifies this submission. Reuse with identical content to retrieve its completed response.
	/// Omit to disable request replay for older callers.
	/// </summary>
	public Guid RequestId { get; set; }

	/// <summary>
	/// Identifier returned when the chat was created.
	/// </summary>
	public Guid Id { get; set; }

	/// <summary>
	/// New user message. Optional when images are supplied; omit when submitting tool results.
	/// </summary>
	public string Input { get; set; }

	/// <summary>
	/// Image URLs or base64 data URLs to include with this message. Omit when submitting tool results.
	/// </summary>
	public List<string> Images { get; set; } = [];

	/// <summary>
	/// Results for all tool calls requested by the previous assistant response.
	/// </summary>
	public List<AiToolResult> ToolResults { get; set; } = [];

	/// <summary>
	/// Optional JSON Schema for this query's final response. Tool calls remain available before that response.
	/// Omit for plain text.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonElement? JsonSchema { get; set; }
}

/// <summary>
/// A cached conversation belonging to the authenticated user.
/// </summary>
public class AiChat
{
	/// <summary>
	/// Identifier used to read or continue this conversation.
	/// </summary>
	public Guid Id { get; set; }

	/// <summary>
	/// Instructions, profile, and tool definitions supplied when created.
	/// </summary>
	public AiChatCreate Options { get; set; }

	/// <summary>
	/// Complete messages in order, preserving tool calls and provider reasoning metadata.
	/// </summary>
	public List<JsonElement> Messages { get; set; } = [];

	/// <summary>
	/// When the cached conversation expires.
	/// </summary>
	public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// One assistant response, which may request tools before the conversation can continue.
/// </summary>
public class AiChatResponse : AiQueryResponse
{
	/// <summary>
	/// Identifier of the submission acknowledged by this response.
	/// </summary>
	public Guid RequestId { get; set; }

	/// <summary>
	/// Functions the caller must execute and answer before sending another user message.
	/// </summary>
	public List<AiToolCall> ToolCalls { get; set; } = [];
}
