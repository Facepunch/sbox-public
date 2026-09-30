using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Linq;

namespace ScriptingTests;

[TestClass, DoNotParallelize]
public class BreenUpgradeTests
{
	GlobalContext previous;
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		var types = new TypeLibrary();
		types.AddIntrinsicTypes();
		types.AddAssembly( typeof( Script ).Assembly, false );
		types.AddAssembly( typeof( Vector2 ).Assembly, false );
		types.AddAssembly( typeof( BreenUpgradeTests ).Assembly, true );
		GlobalContext.Current = new GlobalContext { TypeLibrary = types };
	}
	[TestCleanup]
	public void Cleanup() => GlobalContext.Current = previous;

	public sealed class Host
	{
		public int Touches { get; private set; }
		public T Echo<T>( T value ) => value;
		public int Optional( int value = 7 ) => value;
		public int Sum( params int[] values ) => values.Sum();
		public void Fail() => throw new InvalidOperationException( "Expected" );
		public void Touch() => Touches++;
	}

	[TestMethod]
	public void GenericCallsOptionalParamsAndCollectionInitializers()
	{
		var script = Game.Scripting.CreateScript( "var values = new List<int> { Host.Echo(2), Host.Echo<int>(3) }; var total = 0; foreach (var value in values) total += value; return Host.Sum(total, Host.Optional());" );
		script.SetArgument( "Host", new Host() );
		Assert.AreEqual( 12, script.RunAndReturn<int>(), script.LastError?.ToString() );
		Assert.IsNull( script.LastError );
		Assert.IsFalse( script.Run(), "Arguments must still expire after a run." );
	}

	[TestMethod]
	public void ExceptionsRunCatchAndFinally()
	{
		var host = new Host();
		var script = Game.Scripting.CreateScript( "try { Host.Fail(); } catch { return 13; } finally { Host.Touch(); } return 0;" );
		script.SetArgument( "Host", host );
		Assert.AreEqual( 13, script.RunAndReturn<int>(), script.LastError?.ToString() );
		Assert.AreEqual( 1, host.Touches );
	}

	[TestMethod]
	public void RectangularAndJaggedArrays()
	{
		var script = Game.Scripting.CreateScript( "var grid = new int[2,2]; grid[1,1] = 6; var jagged = new int[][] { new[] { 4 } }; return grid[1,1] + jagged[0][0];" );
		Assert.AreEqual( 10, script.RunAndReturn<int>(), script.LastError?.ToString() );
	}

	[TestMethod]
	public void EditorDescribesGenericOptionalAndVariadicMethods()
	{
		const string source = "Host.Echo(2); Host.Optional(); Host.Sum(1, 2, 3);";
		var analysis = Game.Scripting.Analyze( source, [new Script.Input( "Host", typeof( Host ) )] );
		var completions = analysis.GetCompletions( source.IndexOf( "Echo", StringComparison.Ordinal ) + 4 );
		StringAssert.Contains( completions.Items.Single( x => x.Label == "Echo" ).Detail, "Echo<T>(T value)" );
		var optional = analysis.GetSignatureHelp( source.IndexOf( "Optional(", StringComparison.Ordinal ) + 9 ).Value;
		StringAssert.Contains( optional.Overloads[0].Text, "int value = 7" );
		var variadic = analysis.GetSignatureHelp( source.LastIndexOf( '3' ) ).Value;
		Assert.IsTrue( variadic.Overloads[0].IsVariadic );
		Assert.AreEqual( 2, variadic.Argument );
		StringAssert.Contains( variadic.Overloads[0].Text, "params int[] values" );
	}

	[TestMethod]
	public void TypedStateAndPersistentFieldsShareTheirStore()
	{
		var state = Game.Scripting.CreateStore();
		state.Set( "Position", new Vector2( 4, 5 ) );
		var script = Game.Scripting.CreateScript( "[Local] float Speed = 2; State.Set<float>(\"Speed\", State.Get<float>(\"Speed\") + 1); return State.Get<Vector2>(\"Position\").x * Speed;", state );
		script.SetArgument( "State", state );
		Assert.AreEqual( 12f, script.RunAndReturn<float>(), script.LastError?.ToString() );
		Assert.AreEqual( 3f, state.Get<float>( "Speed" ) );
	}

	[TestMethod]
	public void StoresSurviveEditsAndGlobalsAreShared()
	{
		var state = Game.Scripting.CreateStore();
		var first = Game.Scripting.CreateScript( "[Global] int Total = 0; [Local] int Count = 0; var fresh = 0; Total++; Count++; return ++fresh;", state );
		Assert.AreEqual( 1, first.RunAndReturn<int>(), first.LastError?.ToString() );
		Assert.AreEqual( 1, first.RunAndReturn<int>(), first.LastError?.ToString() );
		var edited = Game.Scripting.CreateScript( "[Global] int Total = 999; [Local] int Count = 999; Total++; return ++Count;", state );
		Assert.AreEqual( 3, edited.RunAndReturn<int>(), edited.LastError?.ToString() );
		Assert.AreEqual( 3, Game.Scripting.Globals.Get<int>( "Total" ) );
		var isolated = Game.Scripting.CreateScript( "[Global] int Total = 999; [Local] int Count = 0; return Total + Count;" );
		Assert.AreEqual( 3, isolated.RunAndReturn<int>(), isolated.LastError?.ToString() );
		var mismatch = Game.Scripting.CreateScript( "[Local] string Count = \"wrong\";", state );
		Assert.IsFalse( mismatch.Run() );
		Assert.AreEqual( "SCR_STORE_TYPE", mismatch.LastError.Code );
	}

	[TestMethod]
	public void StoreAttributesAndTypedGetAreAnalyzed()
	{
		const string source = "[Global] float Gravity = 720; [Local] float Speed = 1; return State.Get<float>(\"Speed\") + Gravity;";
		var analysis = Game.Scripting.Analyze( source, [new Script.Input( "State", typeof( ScriptStore ) )] );
		Assert.IsFalse( analysis.Diagnostics.Any( d => d.Severity == Script.DiagnosticSeverity.Error ), string.Join( "; ", analysis.Diagnostics ) );
		Assert.IsTrue( analysis.GetCompletions( source.IndexOf( "Get<", StringComparison.Ordinal ) + 3 ).Items.Any( x => x.Label == "Get" ) );
	}

	[TestMethod]
	[DataRow( "struct", 3 )]
	[DataRow( "class", 8 )]
	public void ScriptTypesAndFunctionsPreserveCopySemantics( string kind, int expected )
	{
		var script = Game.Scripting.CreateScript( $"{kind} Item {{ public int Value; public void Add() {{ Value += 5; }} }} Item Change(Item item) {{ item.Add(); return item; }} var first = new Item {{ Value = 3 }}; var second = Change(first); return first.Value;" );
		Assert.AreEqual( expected, script.RunAndReturn<int>(), script.LastError?.ToString() );
	}

	[TestMethod]
	public void ScriptStructsPersistAcrossRunsButSourceEditsRequireNewStore()
	{
		const string source = "struct Ball { public float X; } [Local] Ball[] Balls = new Ball[1]; void Tick() { Balls[0].X += 2; } Tick(); return Balls[0].X;";
		var store = Game.Scripting.CreateStore();
		var script = Game.Scripting.CreateScript( source, store );
		Assert.AreEqual( 2f, script.RunAndReturn<float>(), script.LastError?.ToString() );
		Assert.AreEqual( 4f, script.RunAndReturn<float>(), script.LastError?.ToString() );
		var edited = Game.Scripting.CreateScript( source.Replace( "+= 2", "+= 3" ), store );
		Assert.IsFalse( edited.Run() );
		Assert.AreEqual( "SCR_STORE_TYPE", edited.LastError.Code );
		var reset = Game.Scripting.CreateScript( source.Replace( "+= 2", "+= 3" ), Game.Scripting.CreateStore() );
		Assert.AreEqual( 3f, reset.RunAndReturn<float>(), reset.LastError?.ToString() );
	}

	[TestMethod]
	public void ScriptTypeAndFunctionEditorInformation()
	{
		const string source = "struct Item { public int Number; public int Read() => Number; } int Twice(int value) => value * 2; var item = new Item(); return Twice(item.Number);";
		var analysis = Game.Scripting.Analyze( source );
		Assert.IsFalse( analysis.Diagnostics.Any( d => d.Severity == Script.DiagnosticSeverity.Error ), string.Join( "; ", analysis.Diagnostics ) );
		Assert.IsTrue( analysis.GetCompletions( source.LastIndexOf( "item.", StringComparison.Ordinal ) + 5 ).Items.Any( x => x.Label == "Number" ) );
		Assert.AreEqual( typeof( int ), analysis.GetSymbol( source.LastIndexOf( "Number", StringComparison.Ordinal ) )?.Type );
		StringAssert.Contains( analysis.GetSymbol( source.LastIndexOf( "Twice", StringComparison.Ordinal ) ).Value.Detail, "Twice(int value)" );
	}

	[TestMethod]
	public void ScriptEnums()
	{
		var script = Game.Scripting.CreateScript( "enum Mode { A = 2, B = 3 } return (int)Mode.B;" );
		Assert.AreEqual( 3, script.RunAndReturn<int>(), script.LastError?.ToString() );
	}
}
