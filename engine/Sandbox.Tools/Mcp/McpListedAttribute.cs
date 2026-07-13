using System;

namespace Editor.Mcp;

/// <summary>
/// Puts a tool in the client's tool list alongside the other built in entry points.
/// Internal on purpose - addon tools stay out of that list and get found through
/// search_tools, so a list the client fetched once never goes stale as code hotloads.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
internal class McpListedAttribute : Attribute
{
}
