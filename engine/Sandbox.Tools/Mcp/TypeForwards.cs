using System.Runtime.CompilerServices;
using Editor.Mcp;

// Preserve binary compatibility for editor addons compiled before the shared MCP
// types moved into Sandbox.Engine. Their established namespace remains unchanged.
[assembly: TypeForwardedTo( typeof( McpToolAttribute ) )]
[assembly: TypeForwardedTo( typeof( McpToolHints ) )]
[assembly: TypeForwardedTo( typeof( McpTool ) )]
[assembly: TypeForwardedTo( typeof( McpToolsetAttribute ) )]
[assembly: TypeForwardedTo( typeof( McpResult ) )]
