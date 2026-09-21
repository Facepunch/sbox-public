namespace Sandbox.CodeUpgrader;

/// <summary>
/// Host-only async work needs an explicit way to resume on the successor, even when
/// its state writes happen inside another method rather than directly after an await.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class HostAsyncAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.HostAsync;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.AwaitExpression, SyntaxKind.InvocationExpression );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		if ( !MigrationSymbols.IsEnabled( context ) || !MigrationSymbols.IsHostGated( context.Node, context.SemanticModel ) )
		{
			return;
		}

		if ( context.Node is InvocationExpressionSyntax invocation )
		{
			// The await gets the warning for awaited calls. Calls started without awaiting
			// them still need a warning, including helpers that return someone else's task.
			if ( invocation.Ancestors()
				.TakeWhile( x => x is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax and not MemberDeclarationSyntax )
				.OfType<AwaitExpressionSyntax>().Any() )
			{
				return;
			}

			if ( context.SemanticModel.GetSymbolInfo( invocation ).Symbol is not IMethodSymbol method
				|| (!method.IsAsync && !ReturnsTask( method.ReturnType )) )
			{
				return;
			}
		}

		context.ReportDiagnostic( Diagnostic.Create( Rule, context.Node.GetLocation() ) );
	}

	static bool ReturnsTask( ITypeSymbol type )
	{
		for ( var current = type as INamedTypeSymbol; current is not null; current = current.BaseType )
		{
			if ( current.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
				&& current.Name is "Task" or "ValueTask" )
			{
				return true;
			}
		}

		return false;
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// The synced deadline survives, but the helper's pending continuation does not.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Threading.Tasks;

			public class Round : Component
			{
				[Sync] public TimeUntil RoundEnds { get; set; }

				protected override void OnStart()
				{
					if ( Networking.IsHost && RoundEnds )
					{
						_ = [|EndRoundLater()|];
					}
				}

				async Task EndRoundLater()
				{
					await GameTask.DelaySeconds( RoundEnds );
					EndRound();
				}

				void EndRound() { }
			}
			""" );

		// Awaits get a single warning; helper bodies need not write synced properties.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Threading.Tasks;

			public class Round : Component
			{
				async Task Run()
				{
					if ( !Networking.IsHost ) return;
					[|await EndRoundLater()|];
					EndRound();
				}

				Task EndRoundLater() => Task.Delay( 1000 );
				void EndRound() { }

				[Rpc.Host]
				public async void StartRound()
				{
					[|await Task.Delay( 1000 )|];
					EndRound();
				}

				void StartOtherWork()
				{
					if ( !IsProxy )
					{
						_ = [|EndRoundLater()|];
						_ = [|GetResult()|];
						_ = [|GetValue()|];
						EndRound();
					}
				}

				Task<int> GetResult() => Task.FromResult( 1 );
				ValueTask<int> GetValue() => new ValueTask<int>( 1 );
			}
			""" );

		// Local/client async work is not assumed to be authoritative. A same-named
		// property is not a host gate either, and a client branch stays a client branch.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Threading.Tasks;

			public class LocalWork : Component
			{
				bool IsHost => true;
				protected override void OnStart() { _ = Run(); }
				async Task Run() { await Task.Delay( 1000 ); }

				void Check()
				{
					if ( IsHost ) _ = Run();
					if ( !Networking.IsHost ) _ = Run();
					if ( IsProxy ) _ = Run();
					if ( Networking.IsHost ) { } else _ = Run();
				}
			}
			""" );

		// An outer await must not hide host-only work started inside a callback.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Threading.Tasks;

			public class Callback : Component
			{
				async Task Run()
				{
					await System.Threading.Tasks.Task.Run( () =>
					{
						if ( Networking.IsHost )
						{
							_ = [|Task.Delay( 1000 )|];
						}
					} );
				}
			}
			""" );

		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Threading.Tasks;

			public class Round : Component
			{
				async Task Run()
				{
					if ( !Networking.IsHost )
					{
						return;
					}
					await Task.Delay( 1000 );
				}
			}
			""",
			"build_property.SandboxHostMigration = false" );
	}
}
