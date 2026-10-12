namespace Sandbox.DataModel;

/// <summary>
/// Project-wide preferences for AI tooling in the editor.
/// </summary>
[Expose]
public sealed class ProjectAISettings
{
	/// <summary>
	/// Maintain the s&amp;box section in AGENTS.md and the local installation guide.
	/// Disabling this leaves existing files untouched.
	/// </summary>
	[Title( "Write to AGENTS.md" )]
	[Description( "Write s&box instructions when creating or opening this project. Existing instructions are preserved. Disable this to stop writing; existing files will remain." )]
	public bool WriteAgentInstructions { get; set; } = true;

	/// <summary>
	/// Allow local MCP clients to discover and control this project's editor.
	/// Standalone games still require the -mcp launch argument.
	/// </summary>
	[Title( "Enable MCP Communication" )]
	[Description( "Allow local AI tools to connect to this project's editor. Disabling this disconnects existing clients when saved. Standalone games require -mcp." )]
	public bool EnableMcpCommunication { get; set; } = true;
}
