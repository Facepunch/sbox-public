using System;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;


namespace Editor.Mcp;

[McpToolset( "instance", "Tool discovery and recent console output" )]
internal static class TopLevelTools
{
	// Engine assemblies don't get the xml-summary-to-attribute codegen that addons get,
	// so built in tools describe themselves with [Description] directly.

	/// <summary>
	/// Group the available tools into named toolsets.
	/// </summary>
	[McpTool.ReadOnly( "list_toolsets" ), McpListed]
	[Description( "The tool registry grouped into toolsets - named groups of related tools, with every tool name in each. Get a toolset's full input schemas with describe_toolset and invoke with call_tool. Toolsets live in editor and addon code and come and go as code hotloads, so this never goes stale." )]
	internal static object ListToolsets()
	{
		var toolsets = new JsonArray();

		// Classes sharing a toolset name merge into one group, whatever the casing
		foreach ( var group in ToolRegistry.All().GroupBy( x => ToolRegistry.ToolsetOf( x.Method ).Name, StringComparer.OrdinalIgnoreCase ).OrderBy( x => x.Key, StringComparer.Ordinal ) )
		{
			var description = group.Select( x => ToolRegistry.ToolsetOf( x.Method ).Description )
				.FirstOrDefault( x => !string.IsNullOrWhiteSpace( x ) );

			toolsets.Add( new JsonObject
			{
				["name"] = group.Key,
				["description"] = description,
				["tools"] = new JsonArray( group.Select( x => (JsonNode)x.Name ).ToArray() )
			} );
		}

		return new JsonObject { ["toolsets"] = toolsets };
	}

	/// <summary>
	/// Return the description and input schemas for a toolset.
	/// </summary>
	[McpTool.ReadOnly( "describe_toolset" ), McpListed]
	[Description( "Every tool in one toolset with its full input schema, ready to invoke with call_tool. Toolset names come from list_toolsets." )]
	internal static object DescribeToolset( [Description( "The toolset name, as returned by list_toolsets." )] string name )
	{
		name = name?.Trim() ?? "";

		var tools = new JsonArray();
		string description = null;

		foreach ( var (toolName, method) in ToolRegistry.All() )
		{
			var toolset = ToolRegistry.ToolsetOf( method );

			if ( !string.Equals( toolset.Name, name, StringComparison.OrdinalIgnoreCase ) )
				continue;

			// Classes sharing the toolset name might not all describe it - first one that does wins
			if ( string.IsNullOrWhiteSpace( description ) )
				description = toolset.Description;

			tools.Add( ToolRegistry.ToolJson( toolName, method ) );
		}

		if ( tools.Count == 0 )
		{
			var available = string.Join( ", ", ToolRegistry.All().Select( x => ToolRegistry.ToolsetOf( x.Method ).Name ).Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.Ordinal ) );
			throw new McpException( JsonRpcError.InvalidParams, $"Unknown toolset '{name}'. Available: {available}" );
		}

		return new JsonObject { ["name"] = name, ["description"] = description, ["tools"] = tools };
	}

	/// <summary>
	/// Find tools whose metadata matches every search keyword.
	/// </summary>
	[McpTool.ReadOnly( "search_tools" ), McpListed]
	[Description( "Search the live tool registry. Tools live in editor and addon code and come and go as code hotloads, so the tool list from when you connected can be stale - this one never is. Every keyword must match the name, description or parameters; empty returns every tool. Invoke anything you find with call_tool." )]
	internal static object SearchTools( [Description( "Space separated keywords. Empty for every tool." )] string query = "" )
	{
		var terms = (query ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		var all = ToolRegistry.All().ToList();
		var tools = new JsonArray();

		foreach ( var (name, method) in all )
		{
			var parameters = string.Join( ' ', method.Parameters.Select( x => $"{x.Name} {x.GetCustomAttribute<DescriptionAttribute>()?.Value}" ) );
			var text = $"{name} {ToolRegistry.ToolsetOf( method ).Name} {method.Title} {method.Description} {parameters}";

			if ( terms.All( x => text.Contains( x, StringComparison.OrdinalIgnoreCase ) ) )
			{
				tools.Add( ToolRegistry.ToolJson( name, method ) );
			}
		}

		var result = new JsonObject { ["tools"] = tools, ["total"] = all.Count };

		if ( tools.Count == 0 && terms.Length > 0 )
		{
			result["hint"] = $"Nothing matched '{query}'. Every keyword must match, so try fewer or more general keywords - or an empty query to list all {all.Count} tools.";
		}

		return result;
	}

	/// <summary>
	/// Invoke a named tool from the live registry.
	/// </summary>
	[McpTool( "call_tool" ), McpListed]
	[Description( "Call a tool by name, including tools that registered after you connected and aren't in your tool list. Find tools and their input schemas with search_tools." )]
	internal static Task<object> CallTool(
		[Description( "The tool to call, as named by search_tools." )] string name,
		[Description( "Arguments matching the tool's input schema, as a json object. Omit for tools that take none." )] JsonObject arguments = null )
	{
		return ToolRegistry.Invoke( name, arguments );
	}

	/// <summary>
	/// Invoke an ordered batch, stopping at the first failure.
	/// </summary>
	[McpTool( "call_tools" ), McpListed]
	[Description( "Call several tools in one request, in order - cheaper than a call_tool round trip per call. Results come back as content blocks in call order. A failing call stops the batch, and the error says how many calls were skipped." )]
	internal static async Task<object> CallTools(
		[Description( "The calls to make, in order." )] ToolCall[] calls )
	{
		if ( calls is null || calls.Length == 0 )
			throw new McpException( JsonRpcError.InvalidParams, "calls is empty - pass at least one {\"name\": ..., \"arguments\": ...} object" );

		// Validate the whole batch before running anything, so a malformed later call
		// doesn't leave earlier ones half applied
		for ( int i = 0; i < calls.Length; i++ )
		{
			if ( string.IsNullOrWhiteSpace( calls[i]?.Name ) )
				throw new McpException( JsonRpcError.InvalidParams, $"calls[{i}] needs a string 'name'" );
		}

		var result = new McpResult();

		for ( int i = 0; i < calls.Length; i++ )
		{
			try
			{
				result.Append( ToolRegistry.Shape( await ToolRegistry.Invoke( calls[i].Name.Trim(), calls[i].Arguments ) ) );
			}
			catch ( Exception e )
			{
				result.WithText( $"{calls[i].Name} failed: {ToolRegistry.Describe( e )} ({calls.Length - i - 1} later calls not run)" );
				break;
			}
		}

		return result;
	}

	/// <summary>
	/// Return recent console output and a cursor for the next read.
	/// </summary>
	[McpTool.ReadOnly( "read_console" ), McpListed]
	[Description( "Read recent console output, oldest first - what the editor and game logged: prints, warnings, errors, exceptions, compile results. This is how you see the effect of what you just did. Errors come with the top of their stack trace. Every read ends with a cursor - pass it back as 'since' next time to get only what was logged after it, instead of re-reading the same output." )]
	internal static object ReadConsole(
		[Description( "How many of the most recent matching entries to return." ), Range( 1, 500 )] int limit = 50,
		[Description( "Lowest severity to include." )] LogLevel minimumLevel = LogLevel.Trace,
		[Description( "Only entries whose message or logger name contains this, case insensitive." )] string filter = "",
		[Description( "Cursor from a previous read - only entries logged after it come back. 0 reads the most recent." )] long since = 0 )
	{
		var snapshot = LogBuffer.Snapshot();
		var cursor = snapshot.Length == 0 ? 0 : snapshot[^1].Sequence;

		var matching = snapshot.Where( x => x.Sequence > since && x.Event.Level >= minimumLevel );

		if ( !string.IsNullOrWhiteSpace( filter ) )
		{
			matching = matching.Where( x => (x.Event.Message?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false)
				|| (x.Event.Logger?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false) );
		}

		var entries = matching.TakeLast( limit ).Select( x => x.Event ).ToArray();

		if ( entries.Length == 0 )
			return $"No matching console output ({snapshot.Length} entries buffered). Cursor: {cursor}";

		var builder = new StringBuilder();

		builder.AppendLine( $"{entries.Length} entries, oldest first ({snapshot.Length} buffered):" );

		foreach ( var e in entries )
		{
			builder.Append( $"{e.Time:HH:mm:ss} [{e.Logger}] {e.Level}: {e.Message}" );

			if ( e.Repeats > 1 )
				builder.Append( $" (x{e.Repeats})" );

			builder.AppendLine();

			if ( e.Level == LogLevel.Error && !string.IsNullOrWhiteSpace( e.Stack ) )
			{
				foreach ( var frame in e.Stack.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Take( 3 ) )
				{
					builder.AppendLine( $"    {frame}" );
				}
			}
		}

		builder.Append( $"Cursor: {cursor}" );

		return builder.ToString();
	}
}

/// <summary>
/// One call in a call_tools batch.
/// </summary>
internal class ToolCall
{
	/// <summary>
	/// Name of the tool to invoke.
	/// </summary>
	[Description( "The tool to call, as named by search_tools." )]
	public required string Name { get; set; }

	/// <summary>
	/// Named arguments matching the tool schema.
	/// </summary>
	[Description( "Arguments matching the tool's input schema, as a json object. Omit for tools that take none." )]
	public JsonObject Arguments { get; set; }
}
