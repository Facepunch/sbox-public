using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading;
using Sandbox.Engine;

namespace Editor.Mcp;

internal static class McpProtocol
{
	internal static async Task<JsonRpcResponse> HandleLine( string line )
	{
		try
		{
			var message = JsonSerializer.Deserialize<JsonRpcMessage>( line );
			if ( message is null || message.JsonRpc != "2.0" || string.IsNullOrWhiteSpace( message.Method ) )
				return JsonRpcResponse.Failure( null, JsonRpcError.InvalidRequest, "Expected a JSON-RPC 2.0 request or notification." );

			return await Handle( message );
		}
		catch ( JsonException )
		{
			return JsonRpcResponse.Failure( null, line.TrimStart().StartsWith( '[' ) ? JsonRpcError.InvalidRequest : JsonRpcError.ParseError, "Invalid MCP message." );
		}
	}

	static Task<JsonNode> OnMainThread( Func<JsonNode> action ) => OnMainThreadAsync( () => Task.FromResult( action() ) );

	static async Task<JsonNode> OnMainThreadAsync( Func<Task<JsonNode>> action )
	{
		var session = McpSession.Current;
		var cancellation = session?.Cancellation ?? default;
		var completion = new TaskCompletionSource<Task<JsonNode>>( TaskCreationOptions.RunContinuationsAsynchronously );
		var state = 0;

		MainThread.Queue( () =>
		{
			if ( Interlocked.CompareExchange( ref state, 1, 0 ) != 0 || cancellation.IsCancellationRequested )
				return;

			using var scope = GlobalContext.GameScope();
			using var connection = McpSession.Enter( session );
			try
			{
				completion.TrySetResult( action() );
			}
			catch ( Exception e )
			{
				completion.TrySetException( e );
			}
		} );

		try
		{
			var operation = await completion.Task.WaitAsync( TimeSpan.FromSeconds( 30 ), cancellation );
			return await operation;
		}
		finally
		{
			Interlocked.CompareExchange( ref state, 2, 0 );
		}
	}
	/// <summary>
	/// Protocol revisions we can speak, newest first. If the client asks for something
	/// we don't know we answer with the newest and let it decide.
	/// </summary>
	static readonly string[] SupportedProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

	/// <summary>
	/// Sent to the client at initialize, which injects it into the agent's context. This is the
	/// home for rules that apply across the whole registry - conventions, formats, workflow hints.
	/// Grow it here rather than repeating rules in every tool description.
	/// </summary>
	static readonly string Instructions = """
		This server is embedded in one s&box game or editor process. Start with instance_status (and editor_status in an editor). Send tools/list to discover entry points, then tools/call with name and arguments. Your tool list is just the entry points - the real tools live in editor and addon code and come and go as code hotloads. Find them with search_tools, invoke them with call_tool, or batch several invocations with call_tools. After doing anything, read_console shows what the editor logged - errors and exceptions land there. Tool failures come back as results with isError rather than protocol errors, so read them and adapt.

		Conventions, everywhere in the registry:
		- Paging is 'limit' and 'offset'. Defaults and ranges are part of each parameter's schema, and out of range values clamp rather than error.
		- Vectors and angles are comma strings, not arrays or objects - a position is 'x,y,z', view angles are 'pitch,yaw,roll'.
		- The coordinate system is Source engine convention, like Half-Life 2: one unit is one inch, +x is forward, +y is left, +z is up. Angles are degrees.
		- Game objects and components are identified by guid. Assets are identified by the relative path asset_search returns.
		- Editor scene tools document their undo behavior. Runtime tools and scripts may change live state without undo.
		- Hammer maps (.vmap) aren't scenes - the scene tools don't see them. Work on the map open in Hammer with the hammer_ tools, which identify map nodes by integer node id.
		- Performance numbers (frame times, CPU scopes, GPU passes, memory, rendering counters) come from the perf_ tools, starting with perf_overview. They're what the performance hub (perf_hub) shows, as data - don't open the hub and screenshot it.
		""";

	/// <summary>
	/// Handle a single protocol message, returning null when it doesn't warrant a response
	/// (notifications). Runs on the thread pool - tool calls marshal themselves onto the
	/// main thread inside <see cref="ToolRegistry"/>.
	/// </summary>
	internal static async Task<JsonRpcResponse> Handle( JsonRpcMessage message )
	{
		if ( message.JsonRpc != "2.0" || string.IsNullOrWhiteSpace( message.Method )
			|| message.Id is not null && message.Id.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number) )
			return JsonRpcResponse.Failure( null, JsonRpcError.InvalidRequest, "Expected a JSON-RPC 2.0 request with a string or number id." );

		// Notifications never get a response, whether we recognise them or not.
		// No method at all means it's a response to a server initiated request - we never send those.
		if ( message.IsNotification || message.Method is null )
			return null;

		try
		{
			var result = message.Method switch
			{
				"initialize" => Initialize( message.Params ),
				"ping" => new JsonObject(),
				"sbox/instance" => McpSession.Metadata,
				"tools/list" => await OnMainThread( ToolRegistry.ListJson ),
				"tools/call" => await ToolsCall( message.Params ),
				_ => throw new McpException( JsonRpcError.MethodNotFound, $"Method not found: {message.Method}" )
			};

			return JsonRpcResponse.Success( message.Id, result );
		}
		catch ( McpException e )
		{
			return JsonRpcResponse.Failure( message.Id, e.Code, e.Message );
		}
		catch ( Exception e )
		{
			return JsonRpcResponse.Failure( message.Id, JsonRpcError.InternalError, e.Message );
		}
	}

	static JsonNode Initialize( JsonNode param )
	{
		var requested = param?["protocolVersion"]?.ToString();
		var version = SupportedProtocolVersions.Contains( requested ) ? requested : SupportedProtocolVersions[0];

		return new JsonObject
		{
			["protocolVersion"] = version,
			["capabilities"] = new JsonObject
			{
				["tools"] = new JsonObject { ["listChanged"] = false }
			},
			["serverInfo"] = new JsonObject
			{
				["name"] = Application.IsEditor ? "sbox-editor" : "sbox-game",
				["title"] = Application.IsEditor ? "s&box Editor" : "s&box Game",
				["version"] = $"{Sandbox.Application.Version}",
				["description"] = "Live tools for this s&box instance"
			},
			["instructions"] = Instructions + McpSession.Instructions,
			["_meta"] = McpSession.Metadata
		};
	}

	static Task<JsonNode> ToolsCall( JsonNode param )
	{
		var name = param?["name"]?.ToString()?.Trim();

		if ( string.IsNullOrEmpty( name ) )
			throw new McpException( JsonRpcError.InvalidParams, "Missing tool name" );

		return OnMainThreadAsync( () => ToolRegistry.Call( name, ToolRegistry.AsArguments( param?["arguments"], "arguments" ) ) );
	}

}
