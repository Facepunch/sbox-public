using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Linq;

namespace UITests.Controls;

[TestClass, DoNotParallelize]
public class ScriptAnalysisTests
{
	GlobalContext previous;
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		var types = new TypeLibrary();
		types.AddIntrinsicTypes();
		types.AddAssembly( typeof( Script ).Assembly, false );
		types.AddAssembly( typeof( ScriptAnalysisTests ).Assembly, true );
		GlobalContext.Current = new GlobalContext { TypeLibrary = types };
	}
	[TestCleanup]
	public void Cleanup() => GlobalContext.Current = previous;

	public sealed class StateHost
	{
		public float Get( string name, float initialValue ) => throw new Exception( "Analysis must not execute this" );
	}

	[TestMethod]
	public void MemberAndLocalInformationSurvivesUnresolvedArguments()
	{
		const string source = "var x = State.Get(missing, 1.0f); return x;";
		var analysis = Game.Scripting.Analyze( source, [new Script.Input( "State", typeof( StateHost ) )] );
		var method = analysis.GetSymbol( source.IndexOf( "Get", StringComparison.Ordinal ) );
		Assert.AreEqual( typeof( StateHost ), analysis.GetSymbol( source.IndexOf( "State", StringComparison.Ordinal ) )?.Type );
		Assert.IsTrue( analysis.GetCompletions( source.IndexOf( "Get", StringComparison.Ordinal ) + 3 ).Items.Any( x => x.Label == "Get" ) );
		StringAssert.Contains( method.Value.Detail, "Get(string name, float initialValue)" );
		Assert.AreEqual( typeof( float ), analysis.GetSymbol( source.LastIndexOf( 'x' ) )?.Type );
		Assert.IsNotNull( analysis.GetSignatureHelp( source.IndexOf( "missing", StringComparison.Ordinal ) ) );
	}

	[TestMethod]
	public void InterpolatedSlotNamesPreserveMemberAndLocalTypes()
	{
		const string source = "for (var i = 0; i < 10; i++) { var ball = $\"ball{i}.\"; var x = State.Get(ball + \"x\", 20.0f + i * 73.0f); x += 1; }";
		var analysis = Game.Scripting.Analyze( source, [new Script.Input( "State", typeof( StateHost ) )] );
		Assert.AreEqual( typeof( float ), analysis.GetSymbol( source.IndexOf( "Get", StringComparison.Ordinal ) )?.Type );
		Assert.AreEqual( typeof( float ), analysis.GetSymbol( source.LastIndexOf( "x +=", StringComparison.Ordinal ) )?.Type );
	}

	[TestMethod]
	public void MethodHoverIncludesParameterNames()
	{
		const string source = "return MathF.Sin(1.0f);";
		var analysis = Game.Scripting.Analyze( source );
		StringAssert.Contains( analysis.GetSymbol( source.IndexOf( "Sin", StringComparison.Ordinal ) ).Value.Detail, "float MathF.Sin(float x)" );
	}

	[TestMethod]
	public void SignatureHelpTracksNestedIncompleteCalls()
	{
		const string source = "return MathF.FusedMultiplyAdd(MathF.Sin(1), 0, ";
		var analysis = Game.Scripting.Analyze( source );
		Assert.AreEqual( 0, analysis.GetSignatureHelp( source.IndexOf( "1)", StringComparison.Ordinal ) )?.Argument );
		var outer = analysis.GetSignatureHelp( source.Length ).Value;
		Assert.AreEqual( 2, outer.Argument );
		var signature = outer.Overloads[0];
		var parameter = signature.Parameters[2];
		Assert.AreEqual( "float z", signature.Text.Substring( parameter.Start, parameter.Length ) );
	}
}
