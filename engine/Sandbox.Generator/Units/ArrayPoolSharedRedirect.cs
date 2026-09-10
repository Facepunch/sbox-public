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

		return Redirect( originalNode, memberAccess, worker );
	}

	// using static ArrayPool<T> brings Shared in bare, with no member access to catch it.
	internal static ExpressionSyntax VisitIdentifierName( IdentifierNameSyntax node, Worker worker )
	{
		if ( !worker.CorelibPolyfillsEnabled )
		{
			return null;
		}

		if ( !node.Identifier.ValueText.Equals( "Shared", StringComparison.Ordinal ) )
		{
			return null;
		}

		// The name half belongs to the path that owns the whole expression.
		if ( node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node )
		{
			return null;
		}

		if ( node.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == node )
		{
			return null;
		}

		if ( node.Parent is QualifiedNameSyntax qualified && qualified.Right == node )
		{
			return null;
		}

		return Redirect( node, node, worker );
	}

	// Decided on the symbol, not the spelling: an alias or a derived type binds here too.
	private static ExpressionSyntax Redirect( ExpressionSyntax originalNode, ExpressionSyntax currentNode, Worker worker )
	{
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

		return SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				publicArrayPoolType,
				sharedName )
			.WithTriviaFrom( currentNode );
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
}
