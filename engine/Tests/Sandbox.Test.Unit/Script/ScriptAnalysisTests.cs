using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.Tests;

[TestClass]
[DoNotParallelize]
public class ScriptAnalysisTests
{
	private GlobalContext previous;
	private GlobalContext context;

	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		var library = new TypeLibrary();
		library.AddIntrinsicTypes();
		library.AddAssembly( typeof( Script ).Assembly, false );
		library.AddAssembly( typeof( ScriptAnalysisTests ).Assembly, false );
		GlobalContext.Current = context = new GlobalContext { TypeLibrary = library };
		AnalysisHost.Calls = 0;
	}

	[TestCleanup]
	public void Cleanup() => GlobalContext.Current = previous;

	[Expose]
	public sealed class AnalysisHost
	{
		public static int Calls;
		public float Health { get { Calls++; throw new Exception( "Getter must not execute" ); } }
		public float Heal( float amount ) { Calls++; throw new Exception( "Method must not execute" ); }
		private float Hidden() => 0;
	}

	public sealed class UnexposedHost
	{
		public float Secret => 1;
	}

	[TestMethod]
	public void TokensPreserveSourceAndUtf16Offsets()
	{
		const string source = "// 😀 comment\r\nvar x = Math.Sin(1.0); return \"text\";";
		var analysis = Game.Scripting.Analyze( source );
		Assert.AreEqual( source, analysis.Source );
		foreach ( var kind in Enum.GetValues<Script.TokenKind>() )
			Assert.IsTrue( analysis.Tokens.Any( t => t.Kind == kind ), kind.ToString() );
		int math = source.IndexOf( "Math", StringComparison.Ordinal );
		Assert.AreEqual( new Script.SourceSpan( math, 4 ), analysis.GetToken( math ).Value.Span );
		Assert.AreEqual( Script.SymbolKind.Type, analysis.GetSymbol( math ).Value.Kind );
		Assert.IsNull( analysis.GetToken( source.IndexOf( " x", StringComparison.Ordinal ) ) );
		Assert.IsNull( analysis.GetSymbol( source.Length ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => analysis.GetCompletions( -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => analysis.GetToken( source.Length + 1 ) );
	}

	[TestMethod]
	public void TypeAndMemberCompletionsHaveExactReplacementRanges()
	{
		var types = Game.Scripting.Analyze( "ma" ).GetCompletions( 2 );
		Assert.IsTrue( types.Items.Any( item => item.Label == "Math" && item.Kind == Script.SymbolKind.Type ) );
		Assert.AreEqual( new Script.SourceSpan( 0, 2 ), types.ReplacementSpan );
		var members = Game.Scripting.Analyze( "Math." ).GetCompletions( 5 );
		Assert.IsTrue( members.Items.Any( item => item.Label == "Sin" ) );
		Assert.AreEqual( new Script.SourceSpan( 5, 0 ), members.ReplacementSpan );
		const string source = "Math.Sinn";
		var filtered = Game.Scripting.Analyze( source ).GetCompletions( 6 );
		var sin = filtered.Items.First( item => item.Label == "Sin" );
		Assert.AreEqual( new Script.SourceSpan( 5, 4 ), filtered.ReplacementSpan );
		Assert.AreEqual( "Math.Sin", source.Remove( filtered.ReplacementSpan.Start, filtered.ReplacementSpan.Length ).Insert( filtered.ReplacementSpan.Start, sin.InsertText ) );
		Assert.IsTrue( filtered.Items.All( item => item.Label.StartsWith( "S", StringComparison.OrdinalIgnoreCase ) ) );
	}

	[TestMethod]
	public void InputsAndLocalsCarryMetadataWithoutExecutingCode()
	{
		var inputs = new[] { new Script.Input( "Player", typeof( AnalysisHost ), "The current player" ) };
		const string source = "var health = Player.Health; return Player.Heal(health);";
		var analysis = Game.Scripting.Analyze( source, inputs );
		var player = analysis.GetSymbol( source.IndexOf( "Player", StringComparison.Ordinal ) ).Value;
		Assert.AreEqual( typeof( AnalysisHost ), player.Type );
		Assert.AreEqual( "The current player", player.Description );
		var local = analysis.GetSymbol( source.LastIndexOf( "health", StringComparison.Ordinal ) ).Value;
		Assert.AreEqual( Script.SymbolKind.Local, local.Kind );
		Assert.AreEqual( typeof( float ), local.Type );
		Assert.IsNotNull( local.Declaration );
		var suggestions = Game.Scripting.Analyze( "Player.He", inputs ).GetCompletions( 9 );
		Assert.IsTrue( suggestions.Items.Any( item => item.Label == "Health" ) );
		Assert.IsTrue( suggestions.Items.Any( item => item.Label == "Heal" ) );
		Assert.AreEqual( 0, AnalysisHost.Calls );
	}

	[TestMethod]
	public void UnknownInputsAndSyntaxErrorsAreAvailableInIncompleteCode()
	{
		var analysis = Game.Scripting.Analyze( "return Player.Health + Unknown;" );
		Assert.IsTrue( analysis.Inputs.Any( input => input.Name == "Player" && input.Type is null ) );
		Assert.IsTrue( analysis.Inputs.Any( input => input.Name == "Unknown" && input.Type is null ) );
		var broken = Game.Scripting.Analyze( "return (;" );
		Assert.IsTrue( broken.Diagnostics.Any( d => d.Severity == Script.DiagnosticSeverity.Error ) );
		Assert.IsTrue( broken.Diagnostics.All( d => d.Span.Start >= 0 && d.Span.Start + d.Span.Length <= broken.Source.Length ) );
		Assert.IsTrue( Game.Scripting.Analyze( "Math." ).Tokens.Count > 0 );
	}

	[TestMethod]
	public void CompletionPermissionsMatchTheRuntime()
	{
		var permitted = Game.Scripting.Analyze( "Player.", [new( "Player", typeof( AnalysisHost ) )] ).GetCompletions( 7 );
		Assert.IsTrue( permitted.Items.Any( item => item.Label == "Health" ) );
		Assert.IsFalse( permitted.Items.Any( item => item.Label == "Hidden" || item.Label == "GetType" ) );
		var denied = Game.Scripting.Analyze( "Target.", [new( "Target", typeof( UnexposedHost ) )] ).GetCompletions( 7 );
		Assert.AreEqual( 0, denied.Items.Count );
		Assert.IsFalse( Game.Scripting.Analyze( "Unexposed" ).GetCompletions( 9 ).Items.Any( item => item.Label == nameof( UnexposedHost ) ) );
		var generic = Game.Scripting.Analyze( "Items.Co", [new( "Items", typeof( List<int> ) )] ).GetCompletions( 8 );
		Assert.IsTrue( generic.Items.Any( item => item.Label == "Count" ) );
	}

	[TestMethod]
	public void SnapshotsStayImmutableAfterInputAndContextChanges()
	{
		var system = Game.Scripting;
		var inputs = new List<Script.Input> { new( "Player", typeof( AnalysisHost ) ) };
		var old = system.Analyze( "Player.", inputs );
		inputs[0] = new( "Player", typeof( UnexposedHost ) );
		Assert.AreEqual( 0, system.Analyze( "Player.", inputs ).GetCompletions( 7 ).Items.Count );
		context.TypeLibrary.RemoveAssembly( typeof( AnalysisHost ).Assembly );
		context.OnHotload();
		// The name can now be offered as an unknown input, but never as an exposed type.
		Assert.IsFalse( system.Analyze( "AnalysisHost" ).GetCompletions( 12 ).Items.Any( x => x.Label == "AnalysisHost" && x.Kind == Script.SymbolKind.Type ) );
		context.Shutdown();
		Assert.ThrowsException<InvalidOperationException>( () => system.Analyze( "Math" ) );
		Assert.IsTrue( old.GetCompletions( 7 ).Items.Any( item => item.Label == "Health" ) );
		Assert.ThrowsException<NotSupportedException>( () => ((IList<Script.Token>)old.Tokens).Clear() );
	}

	[TestMethod]
	public void TextAndCommentsDoNotOfferCompletions()
	{
		foreach ( string source in new[] { "// Math.S", "\"Math.S", "123" } )
			Assert.AreEqual( 0, Game.Scripting.Analyze( source ).GetCompletions( source.Length ).Items.Count );
	}

	[TestMethod]
	public void SymbolAndTokenQueriesAllocateNothing()
	{
		var analysis = Game.Scripting.Analyze( "return Math.Sin(0);" );
		for ( int i = 0; i < 1000; i++ ) { analysis.GetSymbol( 7 ); analysis.GetToken( 7 ); }
		long before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 1000; i++ ) { analysis.GetSymbol( 7 ); analysis.GetToken( 7 ); }
		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
	}

	[TestMethod]
	public void EditorApiDoesNotExposeBreen()
	{
		static void CheckType( Type type )
		{
			Assert.AreNotEqual( "Breen", type.Assembly.GetName().Name );
			foreach ( var argument in type.GetGenericArguments() ) CheckType( argument );
		}
		foreach ( var type in typeof( Script ).GetNestedTypes().Append( typeof( ScriptSystem ) ) )
		{
			foreach ( var member in type.GetMethods( BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly ) )
			{
				CheckType( member.ReturnType );
				foreach ( var parameter in member.GetParameters() ) CheckType( parameter.ParameterType );
			}
		}
	}
}
