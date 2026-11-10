using System;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.Mcp;

/// <summary>
/// An incoming JSON-RPC 2.0 message. Could be a request (has an id), or a
/// notification (no id) - MCP dropped batching so we never see arrays.
/// </summary>
internal class JsonRpcMessage
{
	/// <summary>
	/// JSON-RPC protocol version, required to be 2.0.
	/// </summary>
	[JsonPropertyName( "jsonrpc" )]
	public string JsonRpc { get; set; }

	/// <summary>
	/// The spec allows string or number ids, so this stays a node and gets echoed back untouched.
	/// </summary>
	[JsonPropertyName( "id" )]
	public JsonNode Id { get; set; }

	/// <summary>
	/// MCP method to dispatch.
	/// </summary>
	[JsonPropertyName( "method" )]
	public string Method { get; set; }

	/// <summary>
	/// Parameters supplied by the client.
	/// </summary>
	[JsonPropertyName( "params" )]
	public JsonNode Params { get; set; }

	/// <summary>
	/// Notifications have no request ID and never receive responses.
	/// </summary>
	[JsonIgnore]
	public bool IsNotification => Method is not null && Id is null;
}

/// <summary>
/// An outgoing JSON-RPC 2.0 response. Carries either a result or an error, never both.
/// </summary>
internal class JsonRpcResponse
{
	/// <summary>
	/// JSON-RPC protocol version.
	/// </summary>
	[JsonPropertyName( "jsonrpc" )]
	public string JsonRpc { get; set; } = "2.0";

	/// <summary>
	/// Request ID echoed without changing its type.
	/// </summary>
	[JsonPropertyName( "id" )]
	public JsonNode Id { get; set; }

	/// <summary>
	/// Successful result, absent when a protocol error occurred.
	/// </summary>
	[JsonPropertyName( "result" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonNode Result { get; set; }

	/// <summary>
	/// Protocol error, absent for successful responses and in-band tool failures.
	/// </summary>
	[JsonPropertyName( "error" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonRpcError Error { get; set; }

	/// <summary>
	/// Create a successful response.
	/// </summary>
	public static JsonRpcResponse Success( JsonNode id, JsonNode result )
	{
		return new JsonRpcResponse { Id = id, Result = result ?? new JsonObject() };
	}

	/// <summary>
	/// Create a protocol error response.
	/// </summary>
	public static JsonRpcResponse Failure( JsonNode id, int code, string message )
	{
		return new JsonRpcResponse { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
	}
}

internal class JsonRpcError
{
	/// <summary>
	/// The incoming text is not valid JSON.
	/// </summary>
	public const int ParseError = -32700;
	/// <summary>
	/// The JSON is not a valid request.
	/// </summary>
	public const int InvalidRequest = -32600;
	/// <summary>
	/// The requested protocol method is unknown.
	/// </summary>
	public const int MethodNotFound = -32601;
	/// <summary>
	/// The protocol method's parameters are invalid.
	/// </summary>
	public const int InvalidParams = -32602;
	/// <summary>
	/// The protocol handler failed unexpectedly.
	/// </summary>
	public const int InternalError = -32603;

	/// <summary>
	/// Machine-readable error code.
	/// </summary>
	[JsonPropertyName( "code" )]
	public int Code { get; set; }

	/// <summary>
	/// Human-readable explanation of the failure.
	/// </summary>
	[JsonPropertyName( "message" )]
	public string Message { get; set; }

	/// <summary>
	/// Optional diagnostic details.
	/// </summary>
	[JsonPropertyName( "data" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonNode Data { get; set; }
}

/// <summary>
/// Thrown by protocol handlers to bubble a specific JSON-RPC error code back to the client.
/// </summary>
internal class McpException : Exception
{
	/// <summary>
	/// JSON-RPC error code to return to the caller.
	/// </summary>
	public int Code { get; }

	/// <summary>
	/// Create a failure with an explicit protocol code and explanation.
	/// </summary>
	public McpException( int code, string message ) : base( message )
	{
		Code = code;
	}
}
