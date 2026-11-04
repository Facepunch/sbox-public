using System;
using System.Collections.Generic;

namespace Sandbox.CodeUpgrader;

#nullable enable

static class MigrationSymbols
{
	public const string Component = "Sandbox.Component";
	public const string GameObjectSystem = "Sandbox.GameObjectSystem";
	public const string Connection = "Sandbox.Connection";

	public static bool IsSynced( ISymbol symbol )
	{
		return SymbolHelpers.HasAttribute( symbol, "Sandbox.SyncAttribute", "Sandbox.HostSyncAttribute" );
	}

	public static bool MentionsType( ITypeSymbol? type, string fullName )
	{
		if ( type is null ) return false;
		if ( type.ToDisplayString() == fullName ) return true;

		if ( type is IArrayTypeSymbol array )
			return MentionsType( array.ElementType, fullName );

		if ( type is INamedTypeSymbol { IsGenericType: true } named )
		{
			foreach ( var arg in named.TypeArguments )
			{
				if ( MentionsType( arg, fullName ) ) return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Inside an if on IsHost/IsProxy, after an early return guarded by one, or in a host RPC.
	/// </summary>
	public static bool IsHostGated( SyntaxNode node, SemanticModel model )
	{
		for ( var current = node; current is not null; current = current.Parent )
		{
			if ( current is IfStatementSyntax ifStatement )
			{
				var whenTrue = ifStatement.Else is null || !ifStatement.Else.Span.Contains( node.Span );
				if ( RequiresAuthority( ifStatement.Condition, whenTrue, model ) )
					return true;
			}

			if ( current is BlockSyntax block )
			{
				foreach ( var statement in block.Statements )
				{
					if ( statement.SpanStart >= node.SpanStart ) break;
					if ( statement is IfStatementSyntax guard && IsExit( guard.Statement ) && RequiresAuthority( guard.Condition, false, model ) )
						return true;
				}
			}

			if ( current is MemberDeclarationSyntax member )
				return model.GetDeclaredSymbol( member ) is { } symbol && SymbolHelpers.HasAttribute( symbol, "Sandbox.Rpc.HostAttribute" );
		}

		return false;
	}

	static bool IsExit( StatementSyntax statement )
	{
		if ( statement is BlockSyntax block )
			return block.Statements.Count == 1 && IsExit( block.Statements[0] );

		return statement is ReturnStatementSyntax or ContinueStatementSyntax;
	}

	static bool RequiresAuthority( ExpressionSyntax condition, bool whenTrue, SemanticModel model )
	{
		if ( condition is ParenthesizedExpressionSyntax parentheses )
			return RequiresAuthority( parentheses.Expression, whenTrue, model );

		if ( condition is PrefixUnaryExpressionSyntax negation && negation.IsKind( SyntaxKind.LogicalNotExpression ) )
			return RequiresAuthority( negation.Operand, !whenTrue, model );

		if ( condition is BinaryExpressionSyntax binary && (binary.IsKind( SyntaxKind.LogicalAndExpression ) || binary.IsKind( SyntaxKind.LogicalOrExpression )) )
		{
			var left = RequiresAuthority( binary.Left, whenTrue, model );
			var right = RequiresAuthority( binary.Right, whenTrue, model );
			return binary.IsKind( SyntaxKind.LogicalAndExpression ) == whenTrue ? left || right : left && right;
		}

		if ( model.GetSymbolInfo( condition ).Symbol is not IPropertySymbol property ) return false;

		return whenTrue
			? property.Name == "IsHost" && property.ContainingType.ToDisplayString() == "Sandbox.Networking"
			: property.Name == "IsProxy" && property.ContainingType.ToDisplayString() is Component or "Sandbox.GameObject";
	}


	public static bool IsEnabled( SyntaxNodeAnalysisContext context )
	{
		if ( !context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue( "build_property.SandboxHostMigration", out var value ) )
			return true;

		return !string.Equals( value, "false", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// A field or auto-property on a Component or GameObjectSystem, with its type.
	/// </summary>
	public static bool TryGetStoredMember( SyntaxNodeAnalysisContext context, out ISymbol symbol, out ITypeSymbol type )
	{
		symbol = null!;
		type = null!;

		if ( !IsEnabled( context ) ) return false;
		if ( context.ContainingSymbol is not { } s ) return false;
		if ( !SymbolHelpers.DerivesFrom( s.ContainingType, Component ) && !SymbolHelpers.DerivesFrom( s.ContainingType, GameObjectSystem ) ) return false;
		if ( context.Node is PropertyDeclarationSyntax && !SymbolHelpers.IsAutoProperty( s ) ) return false;

		var t = s switch
		{
			IFieldSymbol f => f.Type,
			IPropertySymbol p => p.Type,
			_ => null
		};

		if ( t is null ) return false;

		symbol = s;
		type = t;
		return true;
	}
}

/// <summary>
/// A stored Connection is meaningless on another machine; ids survive, synced connections too.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class ConnectionStoredAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.ConnectionStored;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.FieldDeclaration, SyntaxKind.PropertyDeclaration );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		if ( !MigrationSymbols.TryGetStoredMember( context, out var symbol, out var type ) ) return;
		if ( MigrationSymbols.IsSynced( symbol ) ) return;
		if ( !MigrationSymbols.MentionsType( type, MigrationSymbols.Connection ) ) return;

		context.ReportDiagnostic( Diagnostic.Create( Rule, context.Node.GetLocation(), symbol.Name ) );
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Connection field and auto-property on a component

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class DeathCamera : Component
			{
				[|public Connection Owner { get; set; }|]
				[|Connection killer;|]
			}
			""" );

		// Collections keyed by Connection on a system

		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Collections.Generic;

			public class Scores : GameObjectSystem
			{
				public Scores( Scene scene ) : base( scene ) { }

				[|Dictionary<Connection, int> kills = new();|]
			}
			""" );

		// Synced connections, ids, and computed properties are fine

		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System;

			public class DeathCamera : Component
			{
				[Sync] public Connection Killer { get; set; }
				[Sync] public Guid OwnerId { get; set; }
				public Connection Owner => Connection.Find( OwnerId );
			}
			""" );

		// Off when the project ends the lobby with the host

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class DeathCamera : Component
			{
				public Connection Owner { get; set; }
			}
			""",
			"build_property.SandboxHostMigration = false" );

		// Not a component, not our business

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class Helper
			{
				public Connection Connection { get; set; }
			}
			""" );
	}
}

/// <summary>
/// A plain timer the host drives resets on host migration.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class UnsyncedTimerAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.UnsyncedTimer;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.FieldDeclaration, SyntaxKind.PropertyDeclaration );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		// Cheap syntactic filter before any symbol work
		var typeName = context.Node switch
		{
			FieldDeclarationSyntax f => f.Declaration.Type.ToString(),
			PropertyDeclarationSyntax p => p.Type.ToString(),
			_ => null
		};

		if ( typeName is not ("TimeSince" or "TimeUntil" or "Sandbox.TimeSince" or "Sandbox.TimeUntil") ) return;

		if ( !MigrationSymbols.TryGetStoredMember( context, out var symbol, out var type ) ) return;
		if ( symbol.IsStatic ) return;
		if ( MigrationSymbols.IsSynced( symbol ) || SymbolHelpers.HasAttribute( symbol, "Sandbox.PropertyAttribute" ) ) return;
		if ( type.ToDisplayString() is not ("Sandbox.TimeSince" or "Sandbox.TimeUntil") ) return;

		// Only timers the host logic touches. A local effect timer on the same component is fine.
		if ( context.Node.Parent is not TypeDeclarationSyntax typeDeclaration ) return;
		if ( !IsUsedUnderHostGate( context, typeDeclaration, symbol ) ) return;

		context.ReportDiagnostic( Diagnostic.Create( Rule, context.Node.GetLocation(), symbol.Name ) );
	}

	static bool IsUsedUnderHostGate( SyntaxNodeAnalysisContext context, TypeDeclarationSyntax type, ISymbol member )
	{
		foreach ( var node in type.DescendantNodes() )
		{
			if ( node is not IdentifierNameSyntax id || id.Identifier.Text != member.Name ) continue;
			if ( !MigrationSymbols.IsHostGated( id, context.SemanticModel ) ) continue;
			if ( !SymbolEqualityComparer.Default.Equals( context.SemanticModel.GetSymbolInfo( id ).Symbol, member ) ) continue;

			return true;
		}

		return false;
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Timer driven by host code

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class Pickup : Component
			{
				[Sync] public bool IsEnabled { get; set; }
				[|TimeUntil respawnAt;|]

				protected override void OnUpdate()
				{
					if ( Networking.IsHost && !IsEnabled && respawnAt )
						IsEnabled = true;
				}
			}
			""" );

		// Early return guard counts as a gate

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class Door : Component
			{
				[|TimeSince sinceOpened;|]

				protected override void OnFixedUpdate()
				{
					if ( IsProxy ) return;

					if ( sinceOpened > 5f )
						GameObject.Destroy();
				}
			}
			""" );

		// Synced timers, and timers nothing host-gated touches, are fine

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class Pickup : Component
			{
				[Sync] public bool IsEnabled { get; set; }
				[Sync] public TimeUntil RespawnAt { get; set; }
				TimeSince sinceFlashed;

				protected override void OnUpdate()
				{
					if ( sinceFlashed > 1f )
						sinceFlashed = 0f;
				}
			}
			""" );
	}
}

/// <summary>
/// A pending Invoke scheduled by host code dies with the host.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class HostInvokeAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.HostInvoke;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.InvocationExpression );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		var name = invocation.Expression switch
		{
			IdentifierNameSyntax id => id.Identifier.Text,
			MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access => access.Name.Identifier.Text,
			_ => null
		};

		if ( name != "Invoke" ) return;
		if ( !MigrationSymbols.IsHostGated( invocation, context.SemanticModel ) ) return;
		if ( !MigrationSymbols.IsEnabled( context ) ) return;

		if ( context.SemanticModel.GetSymbolInfo( invocation ).Symbol is not IMethodSymbol method ) return;
		if ( method.ContainingType?.ToDisplayString() != MigrationSymbols.Component ) return;

		context.ReportDiagnostic( Diagnostic.Create( Rule, invocation.GetLocation() ) );
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Invoke under a host gate

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class Pickup : Component
			{
				[Sync] public bool IsEnabled { get; set; }

				void OnTaken()
				{
					if ( !Networking.IsHost ) return;

					IsEnabled = false;
					[|Invoke( 10f, () => IsEnabled = true )|];
				}
			}
			""" );

		// Invoke in a host RPC

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class Door : Component
			{
				[Rpc.Host]
				public void RequestOpen()
				{
					[|Invoke( 5f, Close )|];
				}

				void Close() { }
			}
			""" );

		// Branch polarity and symbols matter; aliases still refer to the real property.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using Net = Sandbox.Networking;

			public class Gates : Component
			{
				bool IsHost => true;
				bool Other => true;

				void Check()
				{
					if ( !Net.IsHost ) Invoke( 1f, () => {} );
					if ( IsProxy ) Invoke( 1f, () => {} );
					if ( IsHost ) Invoke( 1f, () => {} );
					if ( Net.IsHost || Other ) Invoke( 1f, () => {} );
					if ( Net.IsHost ) { } else Invoke( 1f, () => {} );
					if ( !Net.IsHost ) { } else [|Invoke( 1f, () => {} )|];
					if ( Net.IsHost && Other ) [|Invoke( 1f, () => {} )|];
					if ( !IsProxy ) [|Invoke( 1f, () => {} )|];
				}

				void ClientOnly()
				{
					if ( Net.IsHost ) return;
					Invoke( 1f, () => {} );
				}

				void HostOnly()
				{
					if ( !Net.IsHost || !Other ) return;
					[|Invoke( 1f, () => {} )|];
				}
			}
			""" );

		// Invoke that runs everywhere is fine

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class Flash : Component
			{
				[Sync] public bool IsEnabled { get; set; }

				protected override void OnStart()
				{
					Invoke( 1f, () => Enabled = false );
				}
			}
			""" );
	}
}

/// <summary>
/// Synced state written after an await is a host loop; it dies with the host and leaves the earlier state stuck.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class SyncWrittenAfterAwaitAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.SyncWrittenAfterAwait;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.MethodDeclaration );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		var method = (MethodDeclarationSyntax)context.Node;
		if ( !method.Modifiers.Any( SyntaxKind.AsyncKeyword ) ) return;

		AwaitExpressionSyntax? firstAwait = null;
		var assignments = new List<AssignmentExpressionSyntax>();

		foreach ( var node in method.DescendantNodes( descendIntoChildren: node => node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax ) )
		{
			if ( node is AwaitExpressionSyntax await ) firstAwait ??= await;
			else if ( node is AssignmentExpressionSyntax assignment ) assignments.Add( assignment );
		}

		if ( firstAwait is null || assignments.Count == 0 ) return;
		if ( !MigrationSymbols.IsEnabled( context ) ) return;

		if ( context.SemanticModel.GetDeclaredSymbol( method ) is not IMethodSymbol symbol ) return;
		if ( !SymbolHelpers.DerivesFrom( symbol.ContainingType, MigrationSymbols.Component ) ) return;

		foreach ( var assignment in assignments )
		{
			if ( assignment.SpanStart < firstAwait.SpanStart ) continue;
			if ( context.SemanticModel.GetSymbolInfo( assignment.Left ).Symbol is not IPropertySymbol property ) continue;
			if ( !MigrationSymbols.IsSynced( property ) ) continue;

			context.ReportDiagnostic( Diagnostic.Create( Rule, assignment.GetLocation(), property.Name, method.Identifier.Text ) );
			return;
		}
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Synced flag written after an await

		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Threading.Tasks;

			public class Door : Component
			{
				[Sync] public bool IsMoving { get; set; }

				async void Open()
				{
					IsMoving = true;
					await Task.Delay( 1000 );
					[|IsMoving = false|];
				}
			}
			""" );

		// Nested functions have their own execution order. Neither their awaits nor their
		// writes belong to the outer method's post-await path.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System;
			using System.Threading.Tasks;

			public class Door : Component
			{
				[Sync] public bool IsMoving { get; set; }

				async Task NestedWrites()
				{
					await Task.Delay( 1 );
					Action<bool> simple = value => IsMoving = value;
					Action parenthesized = () => IsMoving = false;
					Action anonymous = delegate { IsMoving = false; };
					void Local() { IsMoving = false; }
				}

				async Task NestedAwaits()
				{
					Func<Task> callback = async () => { await Task.Delay( 1 ); };
					async Task Local() { await Task.Delay( 1 ); }
					IsMoving = true;
					await Task.Delay( 1 );
				}
			}
			""" );

		// Written before the await, or not synced, or not async: fine

		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Threading.Tasks;

			public class Door : Component
			{
				[Sync] public bool IsMoving { get; set; }
				public bool Local { get; set; }

				async void Open()
				{
					IsMoving = true;
					await Task.Delay( 1000 );
					Local = false;
				}

				void Close()
				{
					IsMoving = false;
				}
			}
			""" );
	}
}
