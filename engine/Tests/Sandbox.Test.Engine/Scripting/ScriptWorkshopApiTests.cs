using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace ScriptingTests;

/// <summary>
/// Workshop scripts can inspect collections, manipulate JSON and use runtime asset APIs.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptWorkshopApiTests
{
	GlobalContext previous;
	GlobalContext previousGame;

	/// <summary>
	/// Creates an isolated runtime using the engine's normal exposed catalogue.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Restores the caller's contexts and releases this test's runtime.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// Native read-only collection instances expose inherited count and indexing contracts.
	/// </summary>
	[TestMethod]
	public void ReadOnlyCollectionsExposeCountAndIndexer()
	{
		var script = Game.Scripting.CreateScript( "return Values.Count + Values[1];" );
		script.SetArgument( "Values", new ReadOnlyCollection<int>( new[] { 10, 40 } ) );
		Assert.AreEqual( 42, script.RunAndReturn<int>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// API completion results support the member access used by TerryInspect.
	/// </summary>
	[TestMethod]
	public void CompletionResultsExposeCount()
	{
		var script = Game.Scripting.CreateScript( "var source = \"GameObject value; value.\"; var completion = Game.Scripting.Analyze(source).GetCompletions(source.Length); return completion.Items.Count > 0;" );
		Assert.IsTrue( script.RunAndReturn<bool>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Engine JSON helpers preserve typed data through a script round trip.
	/// </summary>
	[TestMethod]
	public void JsonHelpersRoundTripData()
	{
		var script = Game.Scripting.CreateScript( "var text = Json.Serialize(new Dictionary<string, int> { { \"answer\", 42 } }); return Json.Deserialize<Dictionary<string, int>>(text)[\"answer\"];" );
		Assert.AreEqual( 42, script.RunAndReturn<int>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Scripts can parse, edit, clone and read ordinary JSON objects, arrays and values.
	/// </summary>
	[TestMethod]
	public void JsonDomSupportsEditing()
	{
		var script = Game.Scripting.CreateScript( "var root = System.Text.Json.Nodes.JsonNode.Parse(\"{\\\"values\\\":[1]}\").AsObject(); var values = root[\"values\"].AsArray(); values.Add(System.Text.Json.Nodes.JsonValue.Create(42)); var copy = root.DeepClone(); return copy[\"values\"][1].GetValue<int>();" );
		Assert.AreEqual( 42, script.RunAndReturn<int>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Embedded resource definitions retain their compiler and generated JSON data.
	/// </summary>
	[TestMethod]
	public void ScriptsCreateEmbeddedResources()
	{
		var script = Game.Scripting.CreateScript( "var resource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = \"embed\", Data = new System.Text.Json.Nodes.JsonObject() }; resource.Data[\"answer\"] = System.Text.Json.Nodes.JsonValue.Create(42); return resource.ResourceCompiler == \"embed\" && resource.Data[\"answer\"].GetValue<int>() == 42;" );
		Assert.IsTrue( script.RunAndReturn<bool>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Shader failures expose every diagnostic rather than only the first exception message.
	/// </summary>
	[TestMethod]
	public void ScriptsReadShaderDiagnostics()
	{
		var script = Game.Scripting.CreateScript( "return Failure.Path + \":\" + Failure.Errors.Count + \":\" + Failure.Errors[1];" );
		script.SetArgument( "Failure", new ShaderCompileException( "test.shader", new[] { "first", "second" } ) );
		Assert.AreEqual( "test.shader:2:second", script.RunAndReturn<string>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Dates support UTC logging, explicit kinds and stable formatted timestamps.
	/// </summary>
	[TestMethod]
	public void ScriptsFormatDates()
	{
		var script = Game.Scripting.CreateScript( "var date = new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc); return date.AddDays(1).ToString(\"yyyy-MM-dd\") + \":\" + (DateTime.UtcNow.Year >= 2026);" );
		Assert.AreEqual( "2026-11-16:True", script.RunAndReturn<string>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Local package helpers execute without downloading or requesting backend data.
	/// </summary>
	[TestMethod]
	public void ScriptsUsePackageHelpers()
	{
		var script = Game.Scripting.CreateScript( "var package = new Package { Ident = \"test\", Title = \"Test\" }; return Package.FormatIdent(\"facepunch\", package.Ident) + \":\" + package.Title;" );
		Assert.AreEqual( "facepunch.test:Test", script.RunAndReturn<string>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Cloud loading is discoverable without running network operations.
	/// </summary>
	[TestMethod]
	public void CloudLoadingIsExposed()
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		var members = new List<MemberInfo>();
		Assert.AreEqual( typeof( Cloud ), resolver.ResolveType( "Sandbox.Cloud" ) );
		resolver.GetMembers( typeof( Cloud ), members );
		Assert.IsTrue( members.Any( member => member.Name == "Load" ) );
		Assert.IsFalse( members.Any( member => member.Name == "UpdateTypes" ) );
	}

	/// <summary>
	/// Package mounting and metadata are discoverable without running network operations.
	/// </summary>
	[TestMethod]
	public void PackageLoadingIsExposed()
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		var members = new List<MemberInfo>();
		resolver.GetMembers( typeof( Package ), members );
		foreach ( var name in new[] { "FetchAsync", "MountAsync", "PrimaryAsset", "TypeName" } )
		{
			Assert.IsTrue( members.Any( member => member.Name == name ), name );
		}
	}

	/// <summary>
	/// New API exposure does not grant reflection or internal serializer state.
	/// </summary>
	[TestMethod]
	[DataRow( "return Json.options;" )]
	[DataRow( "return DateTime.UtcNow.GetType();" )]
	[DataRow( "return System.Text.Json.Nodes.JsonNode.Parse(\"{}\").GetType();" )]
	public void PrivateAndReflectionMembersRemainUnavailable( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.Run() );
		Assert.IsNotNull( script.LastError );
	}
}
