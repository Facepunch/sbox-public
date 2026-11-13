using Editor.Mcp;
using SceneTests;
using System.Text.Json.Nodes;

namespace McpTests;

/// <summary>
/// What a tool returns is what the agent reads, so the content blocks and the compact
/// references for live scene objects are behaviour, not formatting. A scene object serialized
/// in full would dump a whole subtree into the agent's context by accident.
/// </summary>
[TestClass]
[DoNotParallelize]
public class McpResultTest : SceneTest
{
	/// <summary>
	/// A string becomes a single text block, as sent.
	/// </summary>
	[TestMethod]
	public void TextResultIsOneTextBlock()
	{
		var json = McpResult.Text( "hello" ).ToJson();

		var content = json["content"].AsArray();

		Assert.AreEqual( 1, content.Count );
		Assert.AreEqual( "text", content[0]["type"].GetValue<string>() );
		Assert.AreEqual( "hello", content[0]["text"].GetValue<string>() );
		Assert.IsNull( json["structuredContent"] );
		Assert.IsNull( json["isError"] );
	}

	/// <summary>
	/// Anything that isn't a string is serialized to json before it becomes text.
	/// </summary>
	[TestMethod]
	public void NonStringTextResultIsSerialized()
	{
		var json = McpResult.Text( new McpTestReport { Name = "abcd", Count = 4 } ).ToJson();

		var text = JsonNode.Parse( json["content"][0]["text"].GetValue<string>() );

		Assert.AreEqual( "abcd", text[nameof( McpTestReport.Name )].GetValue<string>() );
		Assert.AreEqual( 4, text[nameof( McpTestReport.Count )].GetValue<int>() );
	}

	/// <summary>
	/// A structured result carries the value as structuredContent for clients that bind it,
	/// paired with the same json as text for clients that don't.
	/// </summary>
	[TestMethod]
	public void StructuredResultCarriesBothShapes()
	{
		var json = McpResult.Structured( new McpTestReport { Name = "abcd", Count = 4 } ).ToJson();

		Assert.AreEqual( "abcd", json["structuredContent"][nameof( McpTestReport.Name )].GetValue<string>() );

		var text = JsonNode.Parse( json["content"][0]["text"].GetValue<string>() );

		Assert.AreEqual( "abcd", text[nameof( McpTestReport.Name )].GetValue<string>() );
		Assert.AreEqual( 4, text[nameof( McpTestReport.Count )].GetValue<int>() );
	}

	/// <summary>
	/// Errors go back in band, flagged on the result rather than as a protocol failure.
	/// </summary>
	[TestMethod]
	public void ErrorResultsAreFlagged()
	{
		Assert.IsTrue( McpResult.Text( "nope" ).ToJson( isError: true )["isError"].GetValue<bool>() );
	}

	/// <summary>
	/// A live game object collapses to { type, id, name } - the id chains into get_game_object,
	/// and nobody gets a serialized scene subtree by accident.
	/// </summary>
	[TestMethod]
	public void GameObjectCollapsesToAReference()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var go = scene.CreateObject();
		go.Name = "Hero";

		var reference = McpResult.Structured( go ).ToJson()["structuredContent"].AsObject();

		Assert.AreEqual( nameof( GameObject ), reference["type"].GetValue<string>() );
		Assert.AreEqual( go.Id.ToString(), reference["id"].GetValue<string>() );
		Assert.AreEqual( "Hero", reference["name"].GetValue<string>() );

		// The whole point of the converter - no hierarchy or component data comes along
		Assert.IsNull( reference["Children"] );
		Assert.IsNull( reference["Components"] );

		scene.Destroy();
	}

	/// <summary>
	/// A component collapses to { type, id, gameObject }, naming the component class and
	/// pointing at its owner.
	/// </summary>
	[TestMethod]
	public void ComponentCollapsesToAReference()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var go = scene.CreateObject();
		go.Name = "Hero";

		var component = go.Components.Create<McpTestComponent>();

		var reference = McpResult.Structured( component ).ToJson()["structuredContent"].AsObject();

		Assert.AreEqual( nameof( McpTestComponent ), reference["type"].GetValue<string>() );
		Assert.AreEqual( component.Id.ToString(), reference["id"].GetValue<string>() );
		Assert.AreEqual( go.Id.ToString(), reference["gameObject"].GetValue<string>() );

		scene.Destroy();
	}

	/// <summary>
	/// A destroyed object serializes as null instead of throwing - tools hand back whatever
	/// they were holding, and a dead reference shouldn't fail the whole call.
	/// </summary>
	[TestMethod]
	public void DestroyedGameObjectSerializesAsNull()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var go = scene.CreateObject();
		go.Destroy();
		scene.GameTick();

		Assert.IsFalse( go.IsValid );

		var json = McpResult.Structured( go ).ToJson();

		Assert.IsNull( json["structuredContent"] );

		scene.Destroy();
	}

	/// <summary>
	/// Plain return values get shaped into a result: null means the tool succeeded and had
	/// nothing to say, and an McpResult passes through untouched.
	/// </summary>
	[TestMethod]
	public void ShapeWrapsPlainReturnValues()
	{
		Assert.AreEqual( "ok", ToolRegistry.Shape( null ).ToJson()["content"][0]["text"].GetValue<string>() );
		Assert.AreEqual( "hello", ToolRegistry.Shape( "hello" ).ToJson()["content"][0]["text"].GetValue<string>() );

		var result = McpResult.Text( "mine" );

		Assert.AreSame( result, ToolRegistry.Shape( result ) );
	}
}
