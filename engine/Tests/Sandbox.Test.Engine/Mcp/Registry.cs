using Editor.Mcp;
using System;

namespace McpTests;

/// <summary>
/// Discovery goes through EditorTypeLibrary, so nothing registers by hand - which makes the
/// registry's surface (what names tools get, what toolset they land in, what happens when two
/// tools claim a name) the thing worth pinning down.
/// </summary>
[TestClass]
public class ToolRegistrySurfaceTest
{
	/// <summary>
	/// Tools declared in a loaded assembly are discovered without registering anything.
	/// </summary>
	[TestMethod]
	public void FixtureToolsAreDiscovered()
	{
		CollectionAssert.Contains( ToolRegistry.All().Select( x => x.Name ).ToArray(), "mcp_test_echo" );
	}

	/// <summary>
	/// A tool with no name on its attribute is named after its method, in snake_case.
	/// </summary>
	[TestMethod]
	public void ToolNameFallsBackToSnakeCasedMethodName()
	{
		CollectionAssert.Contains( ToolRegistry.All().Select( x => x.Name ).ToArray(), "mcp_test_derived_name" );
	}

	/// <summary>
	/// Tools come back sorted by name, so a client's tool list is stable between calls.
	/// </summary>
	[TestMethod]
	public void ToolsAreSortedByName()
	{
		var names = ToolRegistry.All().Select( x => x.Name ).ToArray();

		CollectionAssert.AreEqual( names.OrderBy( x => x, StringComparer.Ordinal ).ToArray(), names );
	}

	/// <summary>
	/// An [McpToolset] names the group deliberately, and its description comes with it.
	/// </summary>
	[TestMethod]
	public void ToolsetAttributeNamesTheGroup()
	{
		var toolset = ToolRegistry.ToolsetOf( McpFixture.Tool( "mcp_test_echo" ) );

		Assert.AreEqual( "mcp_test", toolset.Name );
		Assert.AreEqual( "Fixture tools for the MCP registry tests - never called by the editor.", toolset.Description );
	}

	/// <summary>
	/// Without the attribute the toolset name comes from the class name, with a trailing
	/// "Tools" stripped - McpFixtureTools becomes "mcp_fixture" - and there's no description.
	/// </summary>
	[TestMethod]
	public void ToolsetFallsBackToTheClassNameWithoutItsSuffix()
	{
		var toolset = ToolRegistry.ToolsetOf( McpFixture.Tool( "mcp_test_unattributed_toolset" ) );

		Assert.AreEqual( "mcp_fixture", toolset.Name );
		Assert.IsNull( toolset.Description );
	}

	/// <summary>
	/// Two tools claiming one name would make calls ambiguous, so the second is skipped and
	/// the name resolves to exactly one method.
	/// </summary>
	[TestMethod]
	public void DuplicateToolNamesAreSkipped()
	{
		Assert.AreEqual( 1, ToolRegistry.All().Count( x => x.Name == "mcp_test_duplicate" ) );
	}

	/// <summary>
	/// No name in the whole registry is ambiguous - not just the fixture pair.
	/// </summary>
	[TestMethod]
	public void EveryToolNameIsUnique()
	{
		var names = ToolRegistry.All().Select( x => x.Name ).ToArray();

		Assert.AreEqual( names.Length, names.Distinct().Count() );
	}

	/// <summary>
	/// Every discovered tool produces a schema without throwing - a tool whose parameters the
	/// schema builder can't describe would break tools/list for every other tool too.
	/// </summary>
	[TestMethod]
	public void EveryToolProducesASchema()
	{
		foreach ( var (name, method) in ToolRegistry.All() )
		{
			var json = ToolRegistry.ToolJson( name, method );

			Assert.AreEqual( name, json["name"].GetValue<string>(), $"'{name}' didn't describe itself" );
			Assert.AreEqual( "object", json["inputSchema"]["type"].GetValue<string>(), $"'{name}' has no input schema" );
		}
	}
}
