using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sandbox.Generator;

static class ArrayPoolSharedRedirect
{
	private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

	private static readonly QualifiedNameSyntax globalSandboxUtility = SyntaxFactory.QualifiedName(
		SyntaxFactory.AliasQualifiedName(
			SyntaxFactory.IdentifierName( SyntaxFactory.Token( SyntaxKind.GlobalKeyword ) ),
			SyntaxFactory.IdentifierName( "Sandbox" ) ),
		SyntaxFactory.IdentifierName( "Internal" ) );

	private static readonly SyntaxToken publicArrayPoolIdentifier = SyntaxFactory.Identifier( "PublicArrayPool" );
	private static readonly IdentifierNameSyntax sharedName = SyntaxFactory.IdentifierName( "Shared" );

	internal static ExpressionSyntax VisitMemberAccess( MemberAccessExpressionSyntax originalNode, ExpressionSyntax currentNode, Worker worker )
	{
		// Only apply when corelib polyfills are enabled (whitelist/access checks) during a full generation pass.
		if ( !worker.CorelibPolyfillsEnabled )
		{
			return null;
		}

		if ( currentNode is not MemberAccessExpressionSyntax memberAccess )
		{
			return null;
		}

		if ( !memberAccess.Name.Identifier.ValueText.Equals( "Shared", StringComparison.Ordinal ) )
		{
			return null;
		}

		// Fast syntactic pre-check before touching the semantic model
		if ( !ExpressionEndsWithName( memberAccess.Expression, "ArrayPool" ) )
		{
			return null;
		}

		var semanticModel = worker.Model;
		if ( semanticModel == null )
		{
			return null;
		}

		var symbolInfo = semanticModel.GetSymbolInfo( originalNode );
		if ( symbolInfo.Symbol is not IPropertySymbol property )
		{
			return null;
		}

		if ( property.ContainingType is not INamedTypeSymbol containingType )
		{
			return null;
		}

		if ( !IsArrayPoolType( containingType ) )
		{
			return null;
		}

		if ( containingType.TypeArguments.Length != 1 )
		{
			return null;
		}

		var elementType = containingType.TypeArguments[0];
		if ( elementType == null || elementType.Kind == SymbolKind.ErrorType )
		{
			return null;
		}

		var elementTypeName = elementType.ToDisplayString( TypeFormat );

		var publicArrayPoolType = SyntaxFactory.QualifiedName(
			globalSandboxUtility,
			SyntaxFactory.GenericName( publicArrayPoolIdentifier )
				.WithTypeArgumentList( SyntaxFactory.TypeArgumentList(
					SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
						SyntaxFactory.ParseTypeName( elementTypeName ) ) ) ) );

		var replacement = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				publicArrayPoolType,
				sharedName )
			.WithTriviaFrom( memberAccess );

		return replacement;
	}

	private static bool IsArrayPoolType( INamedTypeSymbol type )
	{
		if ( type.Arity != 1 )
		{
			return false;
		}

		if ( !type.Name.Equals( "ArrayPool", StringComparison.Ordinal ) )
		{
			return false;
		}

		var ns = type.ContainingNamespace?.ToDisplayString();
		return ns == "System.Buffers";
	}

	/// <summary>
	/// Returns true if the rightmost identifier of <paramref name="expr"/> matches <paramref name="name"/>.
	/// Used as a cheap syntactic pre-filter before invoking the semantic model.
	/// </summary>
	private static bool ExpressionEndsWithName( ExpressionSyntax expr, string name ) => expr switch
	{
		GenericNameSyntax g => g.Identifier.ValueText.Equals( name, StringComparison.Ordinal ),
		IdentifierNameSyntax i => i.Identifier.ValueText.Equals( name, StringComparison.Ordinal ),
		MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText.Equals( name, StringComparison.Ordinal ),
		AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText.Equals( name, StringComparison.Ordinal ),
		QualifiedNameSyntax q => q.Right.Identifier.ValueText.Equals( name, StringComparison.Ordinal ),
		_ => false,
	};
}
