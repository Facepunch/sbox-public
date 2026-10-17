using Editor.Mcp;
using System.Text.Json.Nodes;

namespace McpTests;

/// <summary>
/// Tool fixtures for the MCP registry tests. These are real <see cref="McpToolAttribute"/>
/// methods in the test assembly, which <see cref="TestInit"/> registers into
/// EditorTypeLibrary - so the registry discovers them exactly the way it discovers the
/// editor's own tools, with no hand registration.
/// Every name here is prefixed so it can't collide with a real editor tool.
/// </summary>
[McpToolset( "mcp_test", "Fixture tools for the MCP registry tests - never called by the editor." )]
public static class McpTestTools
{
	// A read only tool with a required string and two defaulted parameters. Plain comments,
	// not xml summaries, so nothing but the [Description] attribute can supply the description
	// the schema tests assert on.
	[McpTool.ReadOnly( "mcp_test_echo" )]
	[Description( "Echoes text back so argument binding can be asserted." )]
	public static string Echo(
		[Description( "The text to echo." )] string text,
		[Description( "How many times to repeat it." )] int count = 1,
		bool shout = false )
	{
		var line = shout ? text.ToUpperInvariant() : text;

		return string.Join( " ", Enumerable.Repeat( line, count ) );
	}

	// A write tool - no hints, so it should get no annotations block.
	[McpTool( "mcp_test_write" )]
	[Description( "Pretends to write something." )]
	public static string Write( string value ) => value;

	// Engine math types arrive as comma strings.
	[McpTool( "mcp_test_vector" )]
	[Description( "Takes a position and an angle." )]
	public static string Vector( Vector3 position, Angles angles )
		=> $"{position.x:0.##}|{position.y:0.##}|{position.z:0.##}|{angles.yaw:0.##}";

	// An enum parameter, so the schema should list its names.
	[McpTool( "mcp_test_mode" )]
	[Description( "Takes a mode." )]
	public static string Mode( McpTestMode mode ) => mode.ToString();

	// A ranged integer, so the schema should carry minimum/maximum and out of range values
	// should clamp rather than error.
	[McpTool( "mcp_test_limit" )]
	[Description( "Takes a limit." )]
	public static int Limit( [Range( 1, 500 )] int limit = 50 ) => limit;

	// A JsonNode parameter, which takes whatever the client sent.
	[McpTool( "mcp_test_payload" )]
	[Description( "Takes a json object and reads one number out of it." )]
	public static int Payload( JsonObject payload ) => payload?["a"]?.GetValue<int>() ?? -1;

	// Declares a data return type, so it should get an output schema and structured content.
	[McpTool( "mcp_test_report" )]
	[Description( "Returns a small data object." )]
	public static McpTestReport Report( string name ) => new() { Name = name, Count = name?.Length ?? 0 };

	// No name on the attribute, so the tool name comes from the method name in snake_case.
	[McpTool]
	[Description( "Named after its method." )]
	public static string McpTestDerivedName() => "derived";
}

/// <summary>
/// Two tools claiming the same name - the registry should keep one and skip the other.
/// </summary>
[McpToolset( "mcp_test_dupe" )]
public static class McpTestDuplicateTools
{
	[McpTool( "mcp_test_duplicate" )]
	[Description( "First claim on the name." )]
	public static string First() => "first";

	[McpTool( "mcp_test_duplicate" )]
	[Description( "Second claim on the name." )]
	public static string Second() => "second";
}

/// <summary>
/// No [McpToolset], and a class name ending in "Tools" - the toolset name should be
/// derived from the class name with that suffix stripped.
/// </summary>
public static class McpFixtureTools
{
	[McpTool( "mcp_test_unattributed_toolset" )]
	[Description( "Belongs to a toolset named after its class." )]
	public static string Something() => "something";
}

/// <summary>
/// The mode <see cref="McpTestTools.Mode"/> takes.
/// </summary>
public enum McpTestMode
{
	Fast,
	Slow
}

/// <summary>
/// A plain data type, so the registry can describe its shape from its properties.
/// </summary>
public class McpTestReport
{
	public string Name { get; set; }
	public int Count { get; set; }
}

/// <summary>
/// A component for the result tests to collapse into a component reference.
/// </summary>
public class McpTestComponent : Component
{
}

/// <summary>
/// Shared helpers for reaching the fixture tools through the registry.
/// </summary>
public static class McpFixture
{
	/// <summary>
	/// The registry's entry for a tool name, failing the test if discovery didn't find it -
	/// a missing fixture means the test assembly isn't in EditorTypeLibrary, which would
	/// otherwise show up as a confusing null reference.
	/// </summary>
	public static MethodDescription Tool( string name )
	{
		var found = ToolRegistry.All().FirstOrDefault( x => x.Name == name ).Method;

		Assert.IsNotNull( found, $"Tool '{name}' isn't in the registry" );

		return found;
	}

	/// <summary>
	/// Invoke a tool and wait for it synchronously. Tools run inline when the caller is
	/// already the main thread, so this never blocks - and staying on one thread keeps
	/// <see cref="ThreadSafe.IsMainThread"/> (which is thread static) true for the whole call.
	/// </summary>
	public static object Invoke( string name, JsonObject arguments )
		=> ToolRegistry.Invoke( name, arguments ).GetAwaiter().GetResult();
}
