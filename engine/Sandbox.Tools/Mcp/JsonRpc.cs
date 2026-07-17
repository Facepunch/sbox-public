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
	[JsonPropertyName( "jsonrpc" )]
	public string JsonRpc { get; set; }

	/// <summary>
	/// The spec allows string or number ids, so this stays a node and gets echoed back untouched.
	/// </summary>
	[JsonPropertyName( "id" )]
	public JsonNode Id { get; set; }

	[JsonPropertyName( "method" )]
	public string Method { get; set; }

	[JsonPropertyName( "params" )]
	public JsonNode Params { get; set; }

	[JsonIgnore]
	public bool IsNotification => Method is not null && Id is null;
}

/// <summary>
/// An outgoing JSON-RPC 2.0 response. Carries either a result or an error, never both.
/// </summary>
internal class JsonRpcResponse
{
	[JsonPropertyName( "jsonrpc" )]
	public string JsonRpc { get; set; } = "2.0";

	[JsonPropertyName( "id" )]
	public JsonNode Id { get; set; }

	[JsonPropertyName( "result" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonNode Result { get; set; }

	[JsonPropertyName( "error" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonRpcError Error { get; set; }

	public static JsonRpcResponse Success( JsonNode id, JsonNode result )
	{
		return new JsonRpcResponse { Id = id, Result = result ?? new JsonObject() };
	}

	public static JsonRpcResponse Failure( JsonNode id, int code, string message )
	{
		return new JsonRpcResponse { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
	}
}

internal class JsonRpcError
{
	public const int ParseError = -32700;
	public const int InvalidRequest = -32600;
	public const int MethodNotFound = -32601;
	public const int InvalidParams = -32602;
	public const int InternalError = -32603;

	[JsonPropertyName( "code" )]
	public int Code { get; set; }

	[JsonPropertyName( "message" )]
	public string Message { get; set; }

	[JsonPropertyName( "data" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonNode Data { get; set; }
}

/// <summary>
/// Thrown by protocol handlers to bubble a specific JSON-RPC error code back to the client.
/// </summary>
internal class McpException : Exception
{
	public int Code { get; }

	public McpException( int code, string message ) : base( message )
	{
		Code = code;
	}
}
