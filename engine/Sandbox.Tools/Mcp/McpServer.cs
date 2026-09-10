using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.Mcp;

/// <summary>
/// A Model Context Protocol server embedded in the editor process, letting AI agents like
/// Claude Code drive the editor. Speaks the streamable HTTP transport (plain JSON responses,
/// no server initiated streams). Binds loopback only and dies with the editor - it must
/// never be reachable from beyond this machine.
/// </summary>
public static class McpServer
{
	static readonly Logger log = new( "MCP" );

	const long MaxRequestSize = 8 * 1024 * 1024;

	static HttpListener listener;

	/// <summary>
	/// The port we're listening on. Meaningless when not running.
	/// </summary>
	public static int Port { get; private set; }

	public static bool IsRunning => listener?.IsListening ?? false;

	/// <summary>
	/// The url MCP clients should connect to, null when not running.
	/// </summary>
	public static string Url => IsRunning ? $"http://127.0.0.1:{Port}/mcp" : null;

	internal static void Start()
	{
		if ( IsRunning ) return;
		if ( !EditorPreferences.McpServerEnabled ) return;

		LogBuffer.StartCapture();

		Port = EditorPreferences.McpServerPort;

		listener = new HttpListener();

		try
		{
			// Loopback only. We take the whole port rather than just /mcp so unknown
			// paths get a clean 404 from us instead of a confusing one from http.sys.
			listener.Prefixes.Add( $"http://127.0.0.1:{Port}/" );
			listener.Prefixes.Add( $"http://localhost:{Port}/" );
			listener.Start();
		}
		catch ( Exception e )
		{
			log.Warning( $"Couldn't start MCP server on port {Port} ({e.Message})" );
			listener.Close();
			listener = null;
			return;
		}

		_ = ListenLoop( listener );

		log.Info( $"MCP server listening on {Url}" );
	}

	internal static void Stop()
	{
		if ( listener is null )
			return;

		try
		{
			listener.Stop();
			listener.Close();
		}
		catch ( Exception )
		{
			// already dead, don't care
		}

		listener = null;
	}

	internal static void Restart()
	{
		Stop();
		Start();
	}

	static async Task ListenLoop( HttpListener incoming )
	{
		while ( incoming.IsListening )
		{
			HttpListenerContext context;

			try
			{
				context = await incoming.GetContextAsync();
			}
			catch ( Exception )
			{
				// listener was stopped out from under us
				return;
			}

			_ = Task.Run( () => HandleRequest( context ) );
		}
	}

	static async Task HandleRequest( HttpListenerContext context )
	{
		var request = context.Request;
		var response = context.Response;

		try
		{
			// Browsers send an Origin header on cross site requests. A malicious web page can
			// resolve its own domain to 127.0.0.1 (dns rebinding), so only loopback origins get in.
			if ( request.Headers["Origin"] is string origin && !IsLoopbackOrigin( origin ) )
			{
				response.StatusCode = (int)HttpStatusCode.Forbidden;
				return;
			}

			if ( request.Url?.AbsolutePath.TrimEnd( '/' ) is not "/mcp" )
			{
				response.StatusCode = (int)HttpStatusCode.NotFound;
				return;
			}

			if ( request.HttpMethod != "POST" )
			{
				// We don't do server initiated streams (GET) or sessions (DELETE)
				response.AddHeader( "Allow", "POST" );
				response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
				return;
			}

			await HandlePost( request, response );
		}
		catch ( Exception e )
		{
			log.Warning( e, $"Unhandled exception handling MCP request: {e.Message}" );

			try { response.StatusCode = (int)HttpStatusCode.InternalServerError; } catch ( Exception ) { }
		}
		finally
		{
			try { response.Close(); } catch ( Exception ) { }
		}
	}

	static async Task HandlePost( HttpListenerRequest request, HttpListenerResponse response )
	{
		if ( request.ContentLength64 > MaxRequestSize )
		{
			response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
			return;
		}

		var body = await ReadBody( request );

		if ( body is null )
		{
			response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
			return;
		}

		JsonRpcMessage message;

		try
		{
			message = JsonSerializer.Deserialize<JsonRpcMessage>( body );
		}
		catch ( JsonException )
		{
			// JSON-RPC batches (arrays) were removed in protocol revision 2025-06-18
			var error = body.AsSpan().TrimStart().StartsWith( "[" )
				? JsonRpcResponse.Failure( null, JsonRpcError.InvalidRequest, "Batching is not supported" )
				: JsonRpcResponse.Failure( null, JsonRpcError.ParseError, "Parse error" );

			WriteJson( response, HttpStatusCode.BadRequest, error );
			return;
		}

		if ( message is null )
		{
			WriteJson( response, HttpStatusCode.BadRequest, JsonRpcResponse.Failure( null, JsonRpcError.InvalidRequest, "Invalid request" ) );
			return;
		}

		var result = await Handle( message );

		// Notifications get acknowledged without a body
		if ( result is null )
		{
			response.StatusCode = (int)HttpStatusCode.Accepted;
			return;
		}

		WriteJson( response, HttpStatusCode.OK, result );
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
		This server is embedded in the s&box editor and operates on the project that is currently open in it. Start with editor_status to see what's open and whether play mode is running. Your tool list is just the entry points - the real tools live in editor and addon code and come and go as code hotloads. Find them with search_tools, invoke them with call_tool, or batch several invocations with call_tools. After doing anything, read_console shows what the editor logged - errors and exceptions land there. Tool failures come back as results with isError rather than protocol errors, so read them and adapt.

		Conventions, everywhere in the registry:
		- Paging is 'limit' and 'offset'. Defaults and ranges are part of each parameter's schema, and out of range values clamp rather than error.
		- Vectors and angles are comma strings, not arrays or objects - a position is 'x,y,z', view angles are 'pitch,yaw,roll'.
		- The coordinate system is Source engine convention, like Half-Life 2: one unit is one inch, +x is forward, +y is left, +z is up. Angles are degrees.
		- Game objects and components are identified by guid. Assets are identified by the relative path asset_search returns.
		- Every tool that edits the scene pushes an undo step, so the user can ctrl+z your changes like their own.
		""";

	/// <summary>
	/// Handle a single protocol message, returning null when it doesn't warrant a response
	/// (notifications). Runs on the thread pool - tool calls marshal themselves onto the
	/// main thread inside <see cref="ToolRegistry"/>.
	/// </summary>
	static async Task<JsonRpcResponse> Handle( JsonRpcMessage message )
	{
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
				"tools/list" => ToolRegistry.ListJson(),
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
				["name"] = "sbox-editor",
				["title"] = "s&box Editor",
				["version"] = $"{Sandbox.Application.Version}",
				["description"] = "The s&box editor - live tools for the project open in it"
			},
			["instructions"] = Instructions
		};
	}

	static Task<JsonNode> ToolsCall( JsonNode param )
	{
		var name = param?["name"]?.ToString()?.Trim();

		if ( string.IsNullOrEmpty( name ) )
			throw new McpException( JsonRpcError.InvalidParams, "Missing tool name" );

		return ToolRegistry.Call( name, ToolRegistry.AsArguments( param?["arguments"], "arguments" ) );
	}

	/// <summary>
	/// Read the request body, null when it exceeds <see cref="MaxRequestSize"/>. The Content-Length
	/// header can't be trusted to tell us first - chunked bodies report -1 - so cap the read itself.
	/// </summary>
	static async Task<string> ReadBody( HttpListenerRequest request )
	{
		using var buffer = new MemoryStream();

		var chunk = new byte[81920];
		int read;

		while ( (read = await request.InputStream.ReadAsync( chunk )) > 0 )
		{
			buffer.Write( chunk, 0, read );

			if ( buffer.Length > MaxRequestSize )
				return null;
		}

		return request.ContentEncoding.GetString( buffer.GetBuffer(), 0, (int)buffer.Length );
	}

	static void WriteJson( HttpListenerResponse response, HttpStatusCode status, JsonRpcResponse payload )
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes( payload );

		response.StatusCode = (int)status;
		response.ContentType = "application/json";
		response.ContentLength64 = bytes.Length;
		response.OutputStream.Write( bytes );
	}

	static bool IsLoopbackOrigin( string origin )
	{
		return Uri.TryCreate( origin, UriKind.Absolute, out var uri ) && uri.IsLoopback;
	}
}
