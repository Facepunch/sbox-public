using Editor.Mcp;
using SceneTests;
using System.Text.Json.Nodes;

namespace McpTests;

/// <summary>
/// Argument binding is where an agent's guess meets the tool's signature, so what binds,
/// what clamps and what fails loudly is behaviour the agent depends on. These go through
/// <see cref="ToolRegistry.Invoke"/> against the fixture tools, on a thread marked as the
/// main thread so tools execute inline.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ToolBindingTest : SceneTest
{
	static JsonObject Args( string json ) => JsonNode.Parse( json ).AsObject();

	/// <summary>
	/// A well formed call binds every argument and hands back the method's return value.
	/// </summary>
	[TestMethod]
	public void CorrectCallRoundTrips()
	{
		var result = McpFixture.Invoke( "mcp_test_echo", Args( """{ "text": "hi", "count": 2 }""" ) );

		Assert.AreEqual( "hi hi", result );
	}

	/// <summary>
	/// Omitted optional arguments fall back to the method's defaults.
	/// </summary>
	[TestMethod]
	public void OmittedArgumentsUseTheirDefaults()
	{
		Assert.AreEqual( "hi", McpFixture.Invoke( "mcp_test_echo", Args( """{ "text": "hi" }""" ) ) );
	}

	/// <summary>
	/// Argument names bind case insensitively - agents produce all sorts of casing.
	/// </summary>
	[TestMethod]
	public void ArgumentNamesBindCaseInsensitively()
	{
		Assert.AreEqual( "HI", McpFixture.Invoke( "mcp_test_echo", Args( """{ "TEXT": "hi", "Shout": true }""" ) ) );
	}

	/// <summary>
	/// A name the tool doesn't take is a typo or a schema misread, so it fails loudly rather
	/// than being silently ignored, and the error lists what the tool does take.
	/// </summary>
	[TestMethod]
	public void UnknownArgumentIsRejected()
	{
		var e = Assert.ThrowsException<McpException>( () => { _ = McpFixture.Invoke( "mcp_test_echo", Args( """{ "text": "hi", "txt": 1 }""" ) ); } );

		StringAssert.Contains( e.Message, "Unknown argument 'txt'" );
		StringAssert.Contains( e.Message, "text (string)" );
		StringAssert.Contains( e.Message, "count (integer, optional)" );
	}

	/// <summary>
	/// A missing argument with no default errors, naming the argument.
	/// </summary>
	[TestMethod]
	public void MissingRequiredArgumentIsRejected()
	{
		var e = Assert.ThrowsException<McpException>( () => { _ = McpFixture.Invoke( "mcp_test_echo", Args( "{ }" ) ); } );

		StringAssert.Contains( e.Message, "Missing required argument 'text'" );
	}

	/// <summary>
	/// A near miss on a tool name comes back with the tool it probably meant, so an agent
	/// can correct itself in one turn instead of listing the whole registry.
	/// </summary>
	[TestMethod]
	public void UnknownToolSuggestsTheNearMiss()
	{
		var e = Assert.ThrowsException<McpException>( () => { _ = McpFixture.Invoke( "mcp_test_ech", null ); } );

		StringAssert.Contains( e.Message, "Unknown tool 'mcp_test_ech'" );
		StringAssert.Contains( e.Message, "Did you mean" );
		StringAssert.Contains( e.Message, "mcp_test_echo" );
	}

	/// <summary>
	/// A tool name that resembles nothing gets no suggestion, just the pointer to search_tools.
	/// </summary>
	[TestMethod]
	public void UnfamiliarToolNameGetsNoSuggestion()
	{
		var e = Assert.ThrowsException<McpException>( () => { _ = McpFixture.Invoke( "zzqqxx", null ); } );

		StringAssert.Contains( e.Message, "Unknown tool 'zzqqxx'" );
		Assert.IsFalse( e.Message.Contains( "Did you mean" ), e.Message );
	}

	/// <summary>
	/// Vectors and angles arrive as the comma strings the schema advertises.
	/// </summary>
	[TestMethod]
	public void MathTypesBindFromCommaStrings()
	{
		var result = McpFixture.Invoke( "mcp_test_vector", Args( """{ "position": "1,2,3", "angles": "0,90,0" }""" ) );

		Assert.AreEqual( "1|2|3|90", result );
	}

	/// <summary>
	/// Enum names bind whatever their case.
	/// </summary>
	[TestMethod]
	public void EnumBindsCaseInsensitively()
	{
		Assert.AreEqual( nameof( McpTestMode.Slow ), McpFixture.Invoke( "mcp_test_mode", Args( """{ "mode": "slow" }""" ) ) );
	}

	/// <summary>
	/// A value outside a [Range] clamps rather than erroring - agents overshoot limits, and
	/// failing the whole call over it costs a round trip for nothing.
	/// </summary>
	[TestMethod]
	[DataRow( 9999, 500 )]
	[DataRow( 0, 1 )]
	[DataRow( 200, 200 )]
	public void OutOfRangeValuesClamp( int sent, int expected )
	{
		Assert.AreEqual( expected, McpFixture.Invoke( "mcp_test_limit", Args( $$"""{ "limit": {{sent}} }""" ) ) );
	}

	/// <summary>
	/// A number that arrived encoded inside a string still binds.
	/// </summary>
	[TestMethod]
	public void NumberEncodedInAStringUnwraps()
	{
		Assert.AreEqual( 42, McpFixture.Invoke( "mcp_test_limit", Args( """{ "limit": "42" }""" ) ) );
	}

	/// <summary>
	/// A json object that arrived encoded inside a string unwraps into a JsonObject parameter -
	/// a common enough client bug that failing on it would just waste turns.
	/// </summary>
	[TestMethod]
	public void JsonObjectEncodedInAStringUnwraps()
	{
		Assert.AreEqual( 7, McpFixture.Invoke( "mcp_test_payload", Args( """{ "payload": "{ \"a\": 7 }" }""" ) ) );
	}

	/// <summary>
	/// A json object sent as an object binds straight through.
	/// </summary>
	[TestMethod]
	public void JsonObjectBindsDirectly()
	{
		Assert.AreEqual( 7, McpFixture.Invoke( "mcp_test_payload", Args( """{ "payload": { "a": 7 } }""" ) ) );
	}

	/// <summary>
	/// A tool that declared a data return type gets its value wrapped as structured content,
	/// because that's the shape its output schema promised.
	/// </summary>
	[TestMethod]
	public void DeclaredDataReturnComesBackStructured()
	{
		var result = McpFixture.Invoke( "mcp_test_report", Args( """{ "name": "abcd" }""" ) );

		Assert.IsInstanceOfType<McpResult>( result );

		var json = ((McpResult)result).ToJson();

		Assert.AreEqual( "abcd", json["structuredContent"][nameof( McpTestReport.Name )].GetValue<string>() );
		Assert.AreEqual( 4, json["structuredContent"][nameof( McpTestReport.Count )].GetValue<int>() );
	}
}
