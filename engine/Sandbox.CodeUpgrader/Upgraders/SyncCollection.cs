namespace Sandbox.CodeUpgrader;

/// <summary>
/// Ordinary synced collections remain supported, but networked containers avoid hashing their contents each tick.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class SyncCollectionAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.SyncCollection;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeProperty, SyntaxKind.PropertyDeclaration );
	}

	void AnalyzeProperty( SyntaxNodeAnalysisContext context )
	{
		var syntax = (PropertyDeclarationSyntax)context.Node;
		if ( context.SemanticModel.GetDeclaredSymbol( syntax, context.CancellationToken ) is not IPropertySymbol property ) return;
		if ( !SymbolHelpers.HasAttribute( property, "Sandbox.SyncAttribute", "Sandbox.HostSyncAttribute" ) ) return;

		var type = property.Type;
		// NetList/NetDictionary implement this engine-internal change-tracking interface.
		if ( Implements( type, context.Compilation, "Sandbox.INetworkSerializer" ) ) return;

		// Match NetworkTable.Entry.Init: only non-generic collection contracts opt into polling.
		string replacement;
		if ( Implements( type, context.Compilation, "System.Collections.IDictionary" ) )
			replacement = "NetDictionary<TKey, TValue>";
		else if ( type is IArrayTypeSymbol || Implements( type, context.Compilation, "System.Collections.IList" ) )
			replacement = "NetList<T>";
		else
			return;

		context.ReportDiagnostic( Diagnostic.Create( Rule, syntax.Type.GetLocation(), property.Name, replacement ) );
	}

	static bool Implements( ITypeSymbol type, Compilation compilation, params string[] metadataNames )
	{
		foreach ( var name in metadataNames )
		{
			var contract = compilation.GetTypeByMetadataName( name );
			if ( contract is null ) continue;
			if ( SymbolEqualityComparer.Default.Equals( type.OriginalDefinition, contract )
				|| type.AllInterfaces.Any( x => SymbolEqualityComparer.Default.Equals( x.OriginalDefinition, contract ) ) )
				return true;
		}
		return false;
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Concrete collections, arrays, aliases, derived types and interface declarations.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Collections;
			using System.Collections.Generic;
			using Scores = System.Collections.Generic.List<int>;
			using Replicated = Sandbox.SyncAttribute;

			public class ScoreList : List<int> {}
			public class ScoreMap : Dictionary<string, int> {}
			public class GameState
			{
				[Sync] public [|List<int>|] List { get; set; } = new();
				[Sync(SyncFlags.FromHost)] public [|Dictionary<string, int>|] Map { get; set; } = new();
				[Sync] public [|int[]|] Array { get; set; }
				[Sync] public [|int[,]|] Grid { get; set; }
				[Replicated] public [|Scores|] Aliased { get; set; }
				[global::Sandbox.SyncAttribute] public [|ScoreList|] Derived { get; set; }
				[Sync] public [|ScoreMap|] DerivedMap { get; set; }
				[Sync] public [|IList|] UntypedList { get; set; }
				[Sync] public [|IDictionary|] UntypedMap { get; set; }
			}
			""" );

		// Only real sync attributes count; ordinary properties and network serializers need no warning.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Collections.Generic;

			public class GameState
			{
				[Sync] public NetList<int> List { get; set; } = new();
				[Sync] public NetDictionary<string, int> Map { get; set; } = new();
				// Polling depends on the declared type, not the concrete value.
				[Sync] public IList<int> ListInterface { get; set; } = new List<int>();
				[Sync] public IDictionary<string, int> MapInterface { get; set; } = new Dictionary<string, int>();
				[Sync] public IReadOnlyList<int> ReadOnlyList { get; set; }
				[Sync] public IReadOnlyDictionary<string, int> ReadOnlyMap { get; set; }
				[Sync] public string Name { get; set; }
				[Sync] public int Score { get; set; }
				public List<int> LocalList { get; set; }
				[Property] public Dictionary<string, int> LocalMap { get; set; }
			}
			namespace Other
			{
				public class SyncAttribute : System.Attribute {}
				public class GameState
				{
					[Sync] public List<int> List { get; set; }
				}
			}
			""" );

		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Collections.Generic;
			public class GameState
			{
			#pragma warning disable CS0618
				[HostSync] public [|List<int>|] Legacy { get; set; }
			#pragma warning restore CS0618
			#pragma warning disable SB3007
				[Sync] public List<int> Suppressed { get; set; }
			#pragma warning restore SB3007
			}
			""" );
	}
}
