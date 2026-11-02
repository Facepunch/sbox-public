using System.Collections.Generic;
using System.Threading;

namespace Sandbox.CodeUpgrader;

[DiagnosticAnalyzer( LanguageNames.CSharp )]
public class ModelPhysicsCollectionAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.ModelPhysicsCollection;

	public override void Init( AnalysisContext context ) => context.RegisterCompilationStartAction( start =>
	{
		if ( start.Compilation is not CSharpCompilation original ) return;
		if ( !ModelPhysicsCollectionUpgrader.CanUpgrade( original ) ) return;
		start.RegisterSemanticModelAction( model =>
		{
			var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
			ModelPhysicsCollectionUpgrader.FindReplacements( model.SemanticModel, replacements, model.CancellationToken );
			if ( replacements.Count == 0 ) return;
			// One diagnostic offers a project-wide fix. Don't perform that fix just to report it.
			model.ReportDiagnostic( Diagnostic.Create( Rule, replacements.First().Key.GetLocation() ) );
		} );
	} );
}

[ExportCodeFixProvider( LanguageNames.CSharp ), Shared]
public class ModelPhysicsCollectionFix : Fixer<ModelPhysicsCollectionAnalyzer>
{
	public override bool CanApplyDuringCompilation( IEnumerable<SyntaxTree> trees ) => ModelPhysicsCollectionUpgrader.MightNeedUpgrade( trees );

	public override Task RegisterCodeFixesAsync( CodeFixContext context )
	{
		context.RegisterCodeFix( CodeAction.Create(
			"Update ModelPhysics collections in this project",
			ct => UpgradeProjectAsync( context.Document.Project, ct ),
			nameof( ModelPhysicsCollectionFix ) ), context.Diagnostics );
		return Task.CompletedTask;
	}

	static async Task<Solution> UpgradeProjectAsync( Project project, CancellationToken cancellationToken )
	{
		var solution = project.Solution;
		if ( await project.GetCompilationAsync( cancellationToken ).ConfigureAwait( false ) is not CSharpCompilation original )
			return solution;
		var upgraded = ModelPhysicsCollectionUpgrader.Upgrade( original, cancellationToken );

		// Change dependent declarations together, including forwarders in other documents.
		foreach ( var pair in original.SyntaxTrees.Zip( upgraded.SyntaxTrees, ( before, after ) => (before, after) ) )
		{
			if ( pair.before == pair.after ) continue;
			var document = project.GetDocument( pair.before );
			if ( document is null ) continue;
			solution = solution.WithDocumentSyntaxRoot( document.Id, await pair.after.GetRootAsync( cancellationToken ).ConfigureAwait( false ) );
		}
		return solution;
	}
}

/// <summary>
/// Source edits for ModelPhysics.Bodies/Joints changing from List to NetList.
/// Shared by the IDE code fix and automatic upgrades of downloaded code archives.
/// </summary>
static class ModelPhysicsCollectionUpgrader
{
	public static bool MightNeedUpgrade( IEnumerable<SyntaxTree> trees )
	{
		bool lists = false, physics = false;
		foreach ( var tree in trees )
		{
			var text = tree.GetText().ToString();
			lists |= text.Contains( "List" );
			physics |= text.Contains( "ModelPhysics" );
			if ( lists && physics ) return true;
		}
		return false;
	}

	internal static CSharpCompilation Upgrade( CSharpCompilation compilation, CancellationToken cancellationToken = default )
	{
		if ( !CanUpgrade( compilation ) ) return compilation;
		var trees = compilation.SyntaxTrees.ToArray();
		// Rebind after changing declarations: forwarders and independent List fallbacks
		// can expose further conversions. Each edit removes an incompatible conversion.
		while ( true )
		{
			var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
			foreach ( var tree in trees )
				FindReplacements( compilation.GetSemanticModel( tree ), replacements, cancellationToken );
			if ( replacements.Count == 0 ) return compilation;
			var names = new HashSet<string>();
			foreach ( var node in replacements.Keys )
			{
				var declaration = node.Parent;
				if ( declaration is ParameterSyntax ) declaration = declaration.Parent?.Parent;
				switch ( declaration )
				{
					case PropertyDeclarationSyntax property: names.Add( property.Identifier.ValueText ); break;
					case MethodDeclarationSyntax method: names.Add( method.Identifier.ValueText ); break;
					case LocalFunctionStatementSyntax function: names.Add( function.Identifier.ValueText ); break;
					case VariableDeclarationSyntax variables:
						foreach ( var variable in variables.Variables ) names.Add( variable.Identifier.ValueText );
						break;
				}
			}
			var changedTrees = new HashSet<SyntaxTree>();
			foreach ( var group in replacements.GroupBy( x => x.Key.SyntaxTree ) )
			{
				var root = group.Key.GetRoot( cancellationToken ).ReplaceNodes( group.Select( x => x.Key ),
					( original, _ ) => replacements[original].WithTriviaFrom( original ) );
				var updated = group.Key.WithRootAndOptions( root, group.Key.Options );
				compilation = compilation.ReplaceSyntaxTree( group.Key, updated );
				changedTrees.Add( updated );
			}
			// Only changed files and references to changed declarations can gain new errors.
			// Match identifier tokens so escaped names work too; semantic checks remain authoritative.
			trees = compilation.SyntaxTrees.Where( tree => changedTrees.Contains( tree )
				|| tree.GetRoot( cancellationToken ).DescendantTokens().Any( token => token.IsKind( SyntaxKind.IdentifierToken ) && names.Contains( token.ValueText ) ) ).ToArray();
		}
	}

	internal static bool CanUpgrade( CSharpCompilation compilation )
	{
		if ( !MightNeedUpgrade( compilation.SyntaxTrees ) ) return false;
		var physics = compilation.GetTypeByMetadataName( "Sandbox.ModelPhysics" );
		return physics?.ContainingAssembly.Name == "Sandbox.Engine"
			&& physics.GetMembers( "Bodies" ).OfType<IPropertySymbol>().Any( x => IsCollection( x.Type, "Sandbox.NetList<T>" ) );
	}

	internal static void FindReplacements( SemanticModel model, Dictionary<SyntaxNode, SyntaxNode> replacements, CancellationToken cancellationToken )
	{
		// Only incompatible conversions can need this fix. Reuse compiler diagnostics
		// instead of binding every assignment, initializer and return in the project.
		var candidates = new HashSet<SyntaxNode>();
		foreach ( var diagnostic in model.GetDiagnostics( cancellationToken: cancellationToken ) )
		{
			if ( diagnostic.Id is not ("CS0029" or "CS0266" or "CS1503") || !diagnostic.Location.IsInSource ) continue;
			var node = diagnostic.Location.SourceTree.GetRoot( cancellationToken ).FindNode( diagnostic.Location.SourceSpan, getInnermostNodeForTie: true );
			foreach ( var ancestor in node.AncestorsAndSelf().TakeWhile( x => x is not MemberDeclarationSyntax ) )
				if ( ancestor is EqualsValueClauseSyntax or AssignmentExpressionSyntax or ArrowExpressionClauseSyntax or ReturnStatementSyntax or InvocationExpressionSyntax )
					candidates.Add( ancestor );
		}

		cancellationToken.ThrowIfCancellationRequested();
		HashSet<string> names = null;
		foreach ( var node in candidates )
		{
			switch ( node )
			{
				case EqualsValueClauseSyntax initializer:
					Consider( initializer.Value, model.GetDeclaredSymbol( initializer.Parent ), model, replacements );
					Convert( initializer.Value, model, replacements, ref names );
					break;
				case AssignmentExpressionSyntax assignment when assignment.IsKind( SyntaxKind.SimpleAssignmentExpression ):
					Consider( assignment.Right, model.GetSymbolInfo( assignment.Left ).Symbol, model, replacements );
					Convert( assignment.Right, model, replacements, ref names, model.GetTypeInfo( assignment.Left ).Type );
					break;
				case ArrowExpressionClauseSyntax arrow:
					Consider( arrow.Expression, ReturnSymbol( model, arrow.Expression ), model, replacements );
					Convert( arrow.Expression, model, replacements, ref names );
					break;
				case ReturnStatementSyntax { Expression: { } expression }:
					Consider( expression, ReturnSymbol( model, expression ), model, replacements );
					Convert( expression, model, replacements, ref names );
					break;
				case InvocationExpressionSyntax invocation:
					var info = model.GetSymbolInfo( invocation );
					var method = info.Symbol as IMethodSymbol;
					if ( method is null && info.CandidateSymbols.Length == 1 )
						method = info.CandidateSymbols[0] as IMethodSymbol;
					if ( method is null ) break;
					for ( var i = 0; i < invocation.ArgumentList.Arguments.Count; i++ )
					{
						var argument = invocation.ArgumentList.Arguments[i];
						var parameter = argument.NameColon is { } argumentName
							? method.Parameters.FirstOrDefault( x => x.Name == argumentName.Name.Identifier.ValueText )
							: method.Parameters.ElementAtOrDefault( i );
						if ( parameter?.RefKind != RefKind.None || parameter.IsParams ) continue;
						Consider( argument.Expression, parameter.OriginalDefinition, model, replacements );
					}
					break;
			}
		}
	}

	static ISymbol ReturnSymbol( SemanticModel model, ExpressionSyntax expression )
	{
		var symbol = model.GetEnclosingSymbol( expression.SpanStart );
		if ( symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } ) return null;
		return symbol is IMethodSymbol { AssociatedSymbol: { } property } ? property : symbol;
	}

	static bool IsCollection( ITypeSymbol type, string definition )
	{
		return type is INamedTypeSymbol { TypeArguments.Length: 1 } named
			&& named.OriginalDefinition.ToDisplayString() == definition
			&& named.TypeArguments[0] is INamedTypeSymbol element
			&& element.Name is "Body" or "Joint"
			&& element.ContainingType?.ToDisplayString() == "Sandbox.ModelPhysics"
			&& element.ContainingAssembly.Name == "Sandbox.Engine";
	}

	static bool SameElement( ITypeSymbol a, ITypeSymbol b ) =>
		SymbolEqualityComparer.Default.Equals( ((INamedTypeSymbol)a).TypeArguments[0], ((INamedTypeSymbol)b).TypeArguments[0] );

	static void Consider( ExpressionSyntax expression, ISymbol destination, SemanticModel model, Dictionary<SyntaxNode, SyntaxNode> replacements )
	{
		if ( destination is null || destination.DeclaringSyntaxReferences.Length != 1 ) return;
		var target = destination switch
		{
			IPropertySymbol p => p.Type,
			IMethodSymbol m => m.ReturnType,
			ILocalSymbol l => l.Type,
			IFieldSymbol f => f.Type,
			IParameterSymbol p => p.Type,
			_ => null
		};
		if ( !IsCollection( target, "System.Collections.Generic.List<T>" ) ) return;
		var source = model.GetTypeInfo( expression ).Type;
		if ( !IsCollection( source, "Sandbox.NetList<T>" ) && !IsCollection( source, "System.Collections.Generic.IList<T>" ) ) return;
		if ( !SameElement( source, target ) ) return;

		// Don't change an override/interface contract or ref parameter. Those need an explicit source fix.
		var member = destination is IParameterSymbol ? destination.ContainingSymbol : destination;
		if ( member.IsOverride || member.IsVirtual || member.IsAbstract ) return;
		if ( member is IMethodSymbol method && (method.ExplicitInterfaceImplementations.Length > 0 || method.MethodKind == MethodKind.AnonymousFunction) ) return;
		if ( member is IPropertySymbol property && property.ExplicitInterfaceImplementations.Length > 0 ) return;
		if ( member.ContainingType is { } containingType && containingType.AllInterfaces.Any( i => i.GetMembers().Any(
			x => SymbolEqualityComparer.Default.Equals( containingType.FindImplementationForInterfaceMember( x ), member ) ) ) ) return;
		if ( destination is IParameterSymbol parameter && parameter.RefKind != RefKind.None ) return;

		var declaration = destination.DeclaringSyntaxReferences[0].GetSyntax();
		var type = declaration switch
		{
			PropertyDeclarationSyntax p => p.Type,
			MethodDeclarationSyntax m => m.ReturnType,
			LocalFunctionStatementSyntax m => m.ReturnType,
			VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: 1 } v } => v.Type,
			ParameterSyntax p => p.Type,
			_ => null
		};
		if ( type is null ) return;

		// Helpers may also receive independent Lists. IList preserves both references without
		// copying either. Forwarding properties/locals retain the concrete NetList API.
		var collection = destination is IParameterSymbol || IsCollection( source, "System.Collections.Generic.IList<T>" )
			? "global::System.Collections.Generic.IList" : "global::Sandbox.NetList";
		if ( replacements.TryGetValue( type, out var existing ) && existing.ToString().StartsWith( "global::System.Collections.Generic.IList<" ) )
			return;
		var element = ((INamedTypeSymbol)target).TypeArguments[0].ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat );
		replacements[type] = SyntaxFactory.ParseTypeName( $"{collection}<{element}>" );
	}

	static void Convert( ExpressionSyntax expression, SemanticModel model, Dictionary<SyntaxNode, SyntaxNode> replacements, ref HashSet<string> names, ITypeSymbol target = null )
	{
		var info = model.GetTypeInfo( expression );
		target ??= info.ConvertedType;
		if ( !IsCollection( info.Type, "System.Collections.Generic.List<T>" ) || !IsCollection( target, "Sandbox.NetList<T>" ) || !SameElement( info.Type, target ) )
			return;

		names ??= new HashSet<string>( expression.SyntaxTree.GetRoot().DescendantTokens().Where( x => x.IsKind( SyntaxKind.IdentifierToken ) ).Select( x => x.ValueText ) );
		var nextName = 0;
		string name;
		do { name = $"__modelPhysicsList{nextName++}"; } while ( !names.Add( name ) );
		var element = ((INamedTypeSymbol)target).TypeArguments[0].ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat );
		// A replacement assignment intentionally copies the List. Evaluate once and preserve null.
		replacements[expression] = SyntaxFactory.ParseExpression( $"(({expression.WithoutTrivia()}) is {{ }} {name} ? new global::Sandbox.NetList<{element}>( {name} ) : null)" );
	}
}
