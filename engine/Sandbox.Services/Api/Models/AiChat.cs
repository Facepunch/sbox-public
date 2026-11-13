using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.Services;

/// <summary>
/// Conversation settings supplied by the client with each request.
/// </summary>
public class AiChatOptions
{
	/// <summary>
	/// Caller-supplied game package ident, such as "facepunch.sandbox", attributed to every turn.
	/// Optional when the chat has no game context.
	/// </summary>
	public string GamePackageIdent { get; set; }

	/// <summary>
	/// Optional ident of the addon package creating this chat, attributed to every turn.
	/// </summary>
	public string AddonPackageIdent { get; set; }

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
	/// Tools the caller can execute when requested by the assistant, alongside server-provided built-ins.
	/// Names beginning with sb_ are reserved for built-in tools.
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
/// An assistant request to execute a client or server function.
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
	/// Function output or error, as text or serialized JSON. Optional when images are supplied.
	/// </summary>
	public string Content { get; set; }

	/// <summary>
	/// Image URLs or base64 image data URLs returned by this tool.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public List<string> Images { get; set; }
}

/// <summary>
/// Client-owned conversation history. Send it with each query and save it wherever the client needs it.
/// </summary>
public class AiChat
{
	/// <summary>
	/// Client-generated identifier used to correlate usage, not to retrieve server-side state.
	/// </summary>
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>
	/// Instructions, profile, tool definitions, and package attribution for this conversation.
	/// </summary>
	public AiChatOptions Options { get; set; } = new();

	/// <summary>
	/// Complete messages, including original provider reasoning metadata and completed tool results.
	/// </summary>
	public List<JsonElement> Messages { get; set; } = [];

	/// <summary>
	/// Last reported total credits spent, or null when unavailable.
	/// </summary>
	public decimal? TotalCreditsSpent { get; set; } = 0m;

	/// <summary>
	/// Appends a completed response. Failed completions keep their visible text but cannot execute partial tool calls.
	/// Transport and credit failures throw instead, leaving the existing history available for retry.
	/// </summary>
	public void AddResponse( AiChatResponse response )
	{
		Messages.Add( response.IsSuccess ? response.Message : JsonSerializer.SerializeToElement( new
		{
			role = "assistant",
			content = response.Text ?? "",
			refusal = response.Refusal
		} ) );
		TotalCreditsSpent = response.TotalCreditsSpent;
	}

	/// <summary>
	/// Unanswered calls in the latest assistant message, derived from the saved history.
	/// </summary>
	[JsonIgnore]
	public List<AiToolCall> PendingToolCalls
	{
		get
		{
			var answered = new HashSet<string>( StringComparer.Ordinal );
			for ( var index = Messages.Count - 1; index >= 0; index-- )
			{
				var message = Messages[index];
				var role = message.GetProperty( "role" ).GetString();
				if ( role == "tool" )
				{
					answered.Add( message.GetProperty( "tool_call_id" ).GetString() );
				}
				else if ( role == "assistant" )
				{
					return GetToolCalls( message ).Where( call => !answered.Contains( call.Id ) ).ToList();
				}
				else
				{
					break;
				}
			}
			return [];
		}
	}

	/// <summary>
	/// Reads tool calls without discarding any of the original assistant message's metadata.
	/// </summary>
	public static List<AiToolCall> GetToolCalls( JsonElement message )
	{
		if ( !message.TryGetProperty( "tool_calls", out var calls ) || calls.ValueKind != JsonValueKind.Array ) return [];
		return calls.EnumerateArray().Select( call => new AiToolCall
		{
			Id = call.GetProperty( "id" ).GetString(),
			Name = call.GetProperty( "function" ).GetProperty( "name" ).GetString(),
			Arguments = call.GetProperty( "function" ).GetProperty( "arguments" ).GetString()
		} ).ToList();
	}

	/// <summary>
	/// Encodes user text and images in the provider's message format.
	/// </summary>
	public static JsonElement CreateUserMessage( string text, List<string> images = null )
	{
		if ( images is not { Count: > 0 } )
		{
			if ( string.IsNullOrWhiteSpace( text ) ) throw new ValidationException( "A user message or image is required." );
			return JsonSerializer.SerializeToElement( new { role = "user", content = text } );
		}

		var parts = new List<object>();
		if ( !string.IsNullOrWhiteSpace( text ) ) parts.Add( new { type = "text", text } );
		foreach ( var image in images )
		{
			if ( image?.StartsWith( "data:image/", StringComparison.OrdinalIgnoreCase ) == true )
			{
				var separator = image.IndexOf( ";base64,", StringComparison.OrdinalIgnoreCase );
				if ( separator < 0 ) throw new ValidationException( "Image data URLs must contain base64 data." );
				try
				{
					Convert.FromBase64String( image[(separator + 8)..] );
				}
				catch ( FormatException )
				{
					throw new ValidationException( "Image data URLs must contain valid base64 data." );
				}
			}
			else if ( !Uri.TryCreate( image, UriKind.Absolute, out var uri ) || uri.Scheme is not ("http" or "https") )
			{
				throw new ValidationException( "Images must be HTTP URLs or base64 image data URLs." );
			}
			parts.Add( new { type = "image_url", image_url = new { url = image } } );
		}
		return JsonSerializer.SerializeToElement( new { role = "user", content = parts } );
	}

	/// <summary>
	/// Encodes a completed tool result so the client can append and persist it before requesting more work.
	/// </summary>
	public static JsonElement CreateToolMessage( AiToolResult result )
	{
		Validator.ValidateObject( result, new ValidationContext( result ), true );
		if ( result.Images is not { Count: > 0 } )
		{
			if ( result.Content is null ) throw new ValidationException( "Tool results require content or images." );
			return JsonSerializer.SerializeToElement( new { role = "tool", tool_call_id = result.ToolCallId, content = result.Content } );
		}
		var message = CreateUserMessage( result.Content, result.Images );
		return JsonSerializer.SerializeToElement( new { role = "tool", tool_call_id = result.ToolCallId, content = message.GetProperty( "content" ) } );
	}
}

/// <summary>
/// One assistant response. The client decides whether to append it and execute its tools.
/// </summary>
public class AiChatResponse : AiQueryResponse
{
	/// <summary>
	/// Credits charged for this user's chat, or null when accounting is incomplete.
	/// </summary>
	public decimal? TotalCreditsSpent { get; set; }

	/// <summary>
	/// Correlation identifier for this operation, not a server replay key.
	/// </summary>
	public Guid RequestId { get; set; }

	/// <summary>
	/// Original assistant message, including reasoning metadata, to preserve in client history.
	/// </summary>
	public JsonElement Message { get; set; }

	/// <summary>
	/// Functions to execute. Names starting with sb_ use the server tool endpoint.
	/// </summary>
	public List<AiToolCall> ToolCalls { get; set; } = [];
}

/// <summary>
/// One server-owned tool invocation, with the same profile and attribution as its conversation.
/// </summary>
public class AiChatToolInput
{
	/// <summary>
	/// Client-generated conversation identifier used for usage accounting.
	/// </summary>
	public Guid Id { get; set; }

	/// <summary>
	/// Model profile and package attribution for hosted tools.
	/// </summary>
	[Required]
	public AiChatOptions Options { get; set; } = new();

	/// <summary>
	/// Built-in function to execute. Identity and permissions always come from the authenticated caller.
	/// </summary>
	[Required]
	public AiToolCall Call { get; set; }
}

/// <summary>
/// Completed server tool output and the latest conversation accounting total.
/// </summary>
public class AiChatToolResponse
{
	/// <summary>
	/// Account balance after this tool invocation. Null means unavailable; retain the last known balance.
	/// </summary>
	public ServiceApi.CreditBalance? Credits { get; set; }

	/// <summary>
	/// Result to append to the client's conversation history.
	/// </summary>
	public AiToolResult Result { get; set; }

	/// <summary>
	/// Credits charged for this tool invocation. Local tools spend zero credits; null means charging is unavailable.
	/// </summary>
	public decimal? CreditsSpent { get; set; }

	/// <summary>
	/// Credits charged for this user's chat, or null when accounting is incomplete.
	/// </summary>
	public decimal? TotalCreditsSpent { get; set; }
}
