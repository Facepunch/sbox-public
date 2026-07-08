using System;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Editor.Mcp;

[McpToolset( "editor", "The editor itself - status, console output, and the meta tools that find and call everything else" )]
internal static class TopLevelTools
{
	// Engine assemblies don't get the xml-summary-to-attribute codegen that addons get,
	// so built in tools describe themselves with [Description] directly.

	[McpTool.ReadOnly( "list_toolsets" ), McpListed]
	[Description( "The tool registry grouped into toolsets - named groups of related tools, with every tool name in each. Get a toolset's full input schemas with describe_toolset and invoke with call_tool. Toolsets live in editor and addon code and come and go as code hotloads, so this never goes stale." )]
	public static object ListToolsets()
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

	[McpTool.ReadOnly( "describe_toolset" ), McpListed]
	[Description( "Every tool in one toolset with its full input schema, ready to invoke with call_tool. Toolset names come from list_toolsets." )]
	public static object DescribeToolset( [Description( "The toolset name, as returned by list_toolsets." )] string name )
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

	[McpTool.ReadOnly( "search_tools" ), McpListed]
	[Description( "Search the live tool registry. Tools live in editor and addon code and come and go as code hotloads, so the tool list from when you connected can be stale - this one never is. Every keyword must match the name, description or parameters; empty returns every tool. Invoke anything you find with call_tool." )]
	public static object SearchTools( [Description( "Space separated keywords. Empty for every tool." )] string query = "" )
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

	[McpTool( "call_tool" ), McpListed]
	[Description( "Call a tool by name, including tools that registered after you connected and aren't in your tool list. Find tools and their input schemas with search_tools." )]
	public static Task<object> CallTool(
		[Description( "The tool to call, as named by search_tools." )] string name,
		[Description( "Arguments matching the tool's input schema, as a json object. Omit for tools that take none." )] JsonObject arguments = null )
	{
		return ToolRegistry.Invoke( name, arguments );
	}

	[McpTool( "call_tools" ), McpListed]
	[Description( "Call several tools in one request, in order - cheaper than a call_tool round trip per call. Results come back as content blocks in call order. A failing call stops the batch, and the error says how many calls were skipped." )]
	public static async Task<object> CallTools(
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

	[McpTool.ReadOnly( "editor_status" ), McpListed]
	[Description( "Get the current state of the editor - engine version, which project is open, the active scene and whether it has unsaved changes, whether play mode is running or paused, how many tools are registered, and the directory paths that matter (logs, project code and assets)." )]
	public static EditorStatus GetEditorStatus()
	{
		var scene = Game.ActiveScene;

		return new EditorStatus
		{
			EngineVersion = $"{Sandbox.Application.Version}",
			Project = Project.Current?.Config?.Ident,
			ProjectTitle = Project.Current?.Config?.Title,
			ActiveScene = scene?.Name,
			ActiveScenePath = scene?.Source?.ResourcePath,
			SceneHasUnsavedChanges = SceneEditorSession.Active?.HasUnsavedChanges ?? false,
			IsPlaying = Game.IsPlaying,
			IsPaused = Game.IsPaused,
			ToolCount = ToolRegistry.All().Count(),
			Paths = new EditorPaths
			{
				Engine = FileSystem.Root.GetFullPath( "/" ),
				Logs = FileSystem.Root.GetFullPath( "/logs/" ),
				ProjectRoot = Project.Current?.GetRootPath(),
				ProjectSettings = FileSystem.ProjectSettings?.GetFullPath( "/" )
			}
		};
	}

	[McpTool.ReadOnly( "read_console" ), McpListed]
	[Description( "Read recent console output, oldest first - what the editor and game logged: prints, warnings, errors, exceptions, compile results. This is how you see the effect of what you just did. Errors come with the top of their stack trace." )]
	public static object ReadConsole(
		[Description( "How many of the most recent matching entries to return." ), Range( 1, 500 )] int limit = 50,
		[Description( "Lowest severity to include." )] LogLevel minimumLevel = LogLevel.Trace,
		[Description( "Only entries whose message or logger name contains this, case insensitive." )] string filter = "" )
	{
		var snapshot = LogBuffer.Snapshot();

		var matching = snapshot.Where( x => x.Level >= minimumLevel );

		if ( !string.IsNullOrWhiteSpace( filter ) )
		{
			matching = matching.Where( x => (x.Message?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false)
				|| (x.Logger?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false) );
		}

		var entries = matching.TakeLast( limit ).ToArray();

		if ( entries.Length == 0 )
			return $"No matching console output ({snapshot.Length} entries buffered).";

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

		return builder.ToString();
	}
}

/// <summary>
/// One call in a call_tools batch.
/// </summary>
internal class ToolCall
{
	[Description( "The tool to call, as named by search_tools." )]
	public required string Name { get; set; }

	[Description( "Arguments matching the tool's input schema, as a json object. Omit for tools that take none." )]
	public JsonObject Arguments { get; set; }
}

/// <summary>
/// What editor_status reports.
/// </summary>
internal class EditorStatus
{
	public string EngineVersion { get; set; }
	public string Project { get; set; }
	public string ProjectTitle { get; set; }
	public string ActiveScene { get; set; }
	public string ActiveScenePath { get; set; }
	public bool SceneHasUnsavedChanges { get; set; }
	public bool IsPlaying { get; set; }
	public bool IsPaused { get; set; }
	public int ToolCount { get; set; }
	public EditorPaths Paths { get; set; }
}

/// <summary>
/// The directory paths that matter to an agent working with the editor.
/// </summary>
internal class EditorPaths
{
	public string Engine { get; set; }
	public string Logs { get; set; }
	public string ProjectRoot { get; set; }
	public string ProjectSettings { get; set; }
}
