using Editor.Mcp;
using System.Text.Json.Nodes;

namespace McpTests;

/// <summary>
/// The json schema the registry publishes for a tool is the only thing an agent has to go
/// on before it calls one, so these assert the shape of <see cref="ToolRegistry.ToolJson"/>
/// against the fixture tools in <see cref="McpTestTools"/>.
/// </summary>
[TestClass]
public class ToolSchemaTest
{
	static JsonObject Schema( string name ) => ToolRegistry.ToolJson( name, McpFixture.Tool( name ) );

	static JsonObject Properties( string name ) => Schema( name )["inputSchema"]["properties"].AsObject();

	static string[] Required( string name )
		=> Schema( name )["inputSchema"]["required"].AsArray().Select( x => x.GetValue<string>() ).ToArray();

	/// <summary>
	/// A tool's name, toolset and description come through as sent, and the input schema is
	/// a json schema object.
	/// </summary>
	[TestMethod]
	public void ToolCarriesNameToolsetAndSchemaType()
	{
		var json = Schema( "mcp_test_echo" );

		Assert.AreEqual( "mcp_test_echo", json["name"].GetValue<string>() );
		Assert.AreEqual( "mcp_test", json["toolset"].GetValue<string>() );
		Assert.AreEqual( "Echoes text back so argument binding can be asserted.", json["description"].GetValue<string>() );
		Assert.AreEqual( "object", json["inputSchema"]["type"].GetValue<string>() );
	}

	/// <summary>
	/// The primitive parameter types map onto the json schema type names, not the C# ones.
	/// </summary>
	[TestMethod]
	public void PrimitiveParametersMapToJsonSchemaTypes()
	{
		var properties = Properties( "mcp_test_echo" );

		Assert.AreEqual( "string", properties["text"]["type"].GetValue<string>() );
		Assert.AreEqual( "integer", properties["count"]["type"].GetValue<string>() );
		Assert.AreEqual( "boolean", properties["shout"]["type"].GetValue<string>() );
	}

	/// <summary>
	/// A [Description] on a parameter is what the agent reads, so it has to reach the schema.
	/// </summary>
	[TestMethod]
	public void ParameterDescriptionsReachTheSchema()
	{
		var properties = Properties( "mcp_test_echo" );

		Assert.AreEqual( "The text to echo.", properties["text"]["description"].GetValue<string>() );
		Assert.IsNull( properties["shout"]["description"] );
	}

	/// <summary>
	/// Engine math types serialize as comma strings, so their schema type is "string" - an
	/// agent told "array" or "object" here would send something that never binds.
	/// </summary>
	[TestMethod]
	public void MathTypesAreCommaStrings()
	{
		var properties = Properties( "mcp_test_vector" );

		Assert.AreEqual( "string", properties["position"]["type"].GetValue<string>() );
		Assert.AreEqual( "string", properties["angles"]["type"].GetValue<string>() );
	}

	/// <summary>
	/// An enum parameter is a string with its names enumerated.
	/// </summary>
	[TestMethod]
	public void EnumParameterListsItsNames()
	{
		var mode = Properties( "mcp_test_mode" )["mode"];

		Assert.AreEqual( "string", mode["type"].GetValue<string>() );

		CollectionAssert.AreEquivalent(
			new[] { nameof( McpTestMode.Fast ), nameof( McpTestMode.Slow ) },
			mode["enum"].AsArray().Select( x => x.GetValue<string>() ).ToArray() );
	}

	/// <summary>
	/// Only parameters without a default are required, and a default that documents
	/// something appears in the schema.
	/// </summary>
	[TestMethod]
	public void DefaultedParametersAreOptionalAndDocumentTheirDefault()
	{
		CollectionAssert.AreEqual( new[] { "text" }, Required( "mcp_test_echo" ) );

		var properties = Properties( "mcp_test_echo" );

		// DefaultToNode widens every integer default to Int64
		Assert.AreEqual( 1L, properties["count"]["default"].GetValue<long>() );
		Assert.IsFalse( properties["shout"]["default"].GetValue<bool>() );
	}

	/// <summary>
	/// A parameter with no default is required.
	/// </summary>
	[TestMethod]
	public void ParametersWithoutDefaultsAreRequired()
	{
		CollectionAssert.AreEqual( new[] { "position", "angles" }, Required( "mcp_test_vector" ) );
	}

	/// <summary>
	/// A [Range] becomes the schema's minimum and maximum.
	/// </summary>
	[TestMethod]
	public void RangeBecomesMinimumAndMaximum()
	{
		var limit = Properties( "mcp_test_limit" )["limit"];

		Assert.AreEqual( 1f, limit["minimum"].GetValue<float>() );
		Assert.AreEqual( 500f, limit["maximum"].GetValue<float>() );
		Assert.AreEqual( 50L, limit["default"].GetValue<long>() );
	}

	/// <summary>
	/// A read only tool gets the readOnlyHint annotation. A tool with no hints gets no
	/// annotations at all, so clients keep treating it as a destructive write.
	/// </summary>
	[TestMethod]
	public void ReadOnlyToolsAnnounceThemselvesAndOthersDont()
	{
		Assert.IsTrue( Schema( "mcp_test_echo" )["annotations"]["readOnlyHint"].GetValue<bool>() );
		Assert.IsNull( Schema( "mcp_test_write" )["annotations"] );
	}

	/// <summary>
	/// A tool that declares a plain data return type promises its shape in an output schema.
	/// </summary>
	[TestMethod]
	public void DeclaredDataReturnGetsAnOutputSchema()
	{
		var output = Schema( "mcp_test_report" )["outputSchema"];

		Assert.AreEqual( "object", output["type"].GetValue<string>() );
		Assert.AreEqual( "string", output["properties"][nameof( McpTestReport.Name )]["type"].GetValue<string>() );
		Assert.AreEqual( "integer", output["properties"][nameof( McpTestReport.Count )]["type"].GetValue<string>() );
	}

	/// <summary>
	/// A tool returning a bare string promises no shape - strings aren't objects, and the
	/// protocol only allows an object output schema.
	/// </summary>
	[TestMethod]
	public void UndeclaredReturnGetsNoOutputSchema()
	{
		Assert.IsNull( Schema( "mcp_test_echo" )["outputSchema"] );
	}
}
