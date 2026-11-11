using Sandbox.Engine;
using System;

namespace ScriptingTests;

/// <summary>
/// Registered nested types use C# names consistently in execution and editor discovery.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptNestedTypeTests
{
	GlobalContext previous;
	GlobalContext previousGame;

	/// <summary>
	/// Uses the shared engine and system catalog in an isolated scripting context.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Ends the test's scripting context and restores the original context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// LL-003's enum expression and cast use the same registered type as fully qualified C# names.
	/// </summary>
	[TestMethod]
	[DataRow( "return (int)TextRenderer.BillboardMode.Always;", 1 )]
	[DataRow( "return (int)(TextRenderer.BillboardMode)1;", 1 )]
	[DataRow( "TextRenderer.BillboardMode mode = TextRenderer.BillboardMode.Always; return (int)mode;", 1 )]
	[DataRow( "return (int)(Sandbox.TextRenderer.BillboardMode)1;", 1 )]
	[DataRow( "return (int)TextRenderer.HAlignment.Center;", 2 )]
	[DataRow( "return (int)TextRenderer.VAlignment.Top;", 1 )]
	public void ScriptsCanUseNestedEnums( string source, int expected )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.AreEqual( expected, script.RunAndReturn<int>(), script.LastError?.Message );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Nested types resolve relative to the selected outer type without accepting unrelated suffixes.
	/// </summary>
	[TestMethod]
	public void NestedNamesUseTheirResolvedOuterType()
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		Assert.AreEqual( typeof( TextRenderer.BillboardMode ), resolver.ResolveType( "TextRenderer.BillboardMode" ) );
		Assert.AreEqual( typeof( TextRenderer.BillboardMode ), resolver.ResolveType( "Sandbox.TextRenderer.BillboardMode" ) );
		Assert.AreEqual( typeof( TextRenderer.BillboardMode ), resolver.ResolveType( "Sandbox.TextRenderer+BillboardMode" ) );
		Assert.IsNull( resolver.ResolveType( "Panel.BillboardMode" ) );
		Assert.IsNull( resolver.ResolveType( "MissingNamespace.TextRenderer.BillboardMode" ) );
		Assert.AreEqual( typeof( Sandbox.Input ), resolver.ResolveType( "Input" ) );
	}

	/// <summary>
	/// A local value sharing a host type's name still uses ordinary instance member access.
	/// </summary>
	[TestMethod]
	public void LocalValuesKeepMemberAccessPrecedence()
	{
		var script = Game.Scripting.CreateScript( "var TextRenderer = new Vector3(3, 4, 5); return TextRenderer.x;" );
		Assert.AreEqual( 3.0f, script.RunAndReturn<float>(), script.LastError?.Message );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Type discovery shows a C# spelling that can be copied directly into a cast.
	/// </summary>
	[TestMethod]
	public void DiscoveryShowsUsableNestedTypeNames()
	{
		const string source = "TextRenderer.";
		var analysis = Game.Scripting.Analyze( source );
		var completion = analysis.GetCompletions( source.Length ).Items.Single( item => item.Type == typeof( TextRenderer.BillboardMode ) );
		Assert.AreEqual( "Sandbox.TextRenderer.BillboardMode", completion.Detail );

		var script = Game.Scripting.CreateScript( $"return (int)({completion.Detail})1;" );
		Assert.AreEqual( 1, script.RunAndReturn<int>(), script.LastError?.Message );
	}

	/// <summary>
	/// Completion follows the outer type into its nested enum and then its constants.
	/// </summary>
	[TestMethod]
	public void CompletionIncludesNestedTypesAndEnumConstants()
	{
		const string outer = "TextRenderer.";
		var nested = Game.Scripting.Analyze( outer ).GetCompletions( outer.Length ).Items
			.Single( item => item.Label == "BillboardMode" );
		Assert.AreEqual( Script.SymbolKind.Type, nested.Kind );
		Assert.AreEqual( typeof( TextRenderer.BillboardMode ), nested.Type );
		Assert.AreEqual( "Sandbox.TextRenderer.BillboardMode", nested.Detail );

		var prefix = outer + nested.InsertText + ".";
		var constant = Game.Scripting.Analyze( prefix ).GetCompletions( prefix.Length ).Items.Single( item => item.Label == "Always" );
		var script = Game.Scripting.CreateScript( $"return (int){prefix}{constant.InsertText};" );
		Assert.AreEqual( 1, script.RunAndReturn<int>(), script.LastError?.Message );
	}
}
