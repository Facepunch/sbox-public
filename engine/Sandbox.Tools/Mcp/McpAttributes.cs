using System;

namespace Editor.Mcp;

/// <summary>
/// Expose a static method as an MCP tool, callable by AI agents connected to the editor's
/// MCP server. The method name (converted to snake_case) becomes the tool name unless one
/// is given here, and the XML summary becomes the tool description the agent reads.
/// Parameters become the tool's input schema - describe them with [Description]. Tools are
/// invoked on the main thread and can return a Task to be async. Return values get serialized
/// to json for the agent - return a <see cref="Bitmap"/> to send an image, or an
/// <see cref="McpResult"/> to compose text and images yourself.
/// Agents find tools through search_tools and run them through call_tool - so the name and
/// description are what make a tool discoverable.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
public class McpToolAttribute : Attribute
{
	/// <summary>
	/// The name agents call this tool by. Treat it as public API - agents and their
	/// workflows break when it changes.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Behaviour hints reported to the client, which may use them for permission prompts.
	/// A tool with no hints is assumed to write, and possibly destroy, state.
	/// </summary>
	public McpToolHints Hints { get; set; }

	public McpToolAttribute()
	{
	}

	public McpToolAttribute( string name )
	{
		Name = name;
	}
}

/// <summary>
/// Behaviour hints for <see cref="McpToolAttribute"/> tools.
/// </summary>
[Flags]
public enum McpToolHints
{
	None = 0,

	/// <summary>
	/// Only reads - never changes project, scene or editor state.
	/// </summary>
	ReadOnly = 1,
}

/// <summary>
/// Shorthand variants of <see cref="McpToolAttribute"/> with hints baked in.
/// </summary>
public static class McpTool
{
	/// <summary>
	/// An MCP tool that only reads - never changes project, scene or editor state. Clients
	/// can run these without asking the user's permission.
	/// </summary>
	public class ReadOnlyAttribute : McpToolAttribute
	{
		public ReadOnlyAttribute()
		{
			Hints = McpToolHints.ReadOnly;
		}

		public ReadOnlyAttribute( string name ) : base( name )
		{
			Hints = McpToolHints.ReadOnly;
		}
	}
}

/// <summary>
/// Groups a class of <see cref="McpToolAttribute"/> tools into a named toolset, which agents
/// browse with list_toolsets and describe_toolset. Without this a class's tools still group
/// under a name derived from the class name - the attribute exists to pick that name
/// deliberately and describe what the group is for.
/// </summary>
[AttributeUsage( AttributeTargets.Class )]
public class McpToolsetAttribute : Attribute
{
	/// <summary>
	/// The toolset name agents browse by. Treat it as public API - agents and their
	/// workflows break when it changes.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// What this group of tools is for. Shown by list_toolsets, so it's what makes the
	/// toolset worth describing.
	/// </summary>
	public string Description { get; set; }

	public McpToolsetAttribute( string name, string description = null )
	{
		Name = name;
		Description = description;
	}
}
