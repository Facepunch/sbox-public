using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sandbox.Generator;

static class StringBuilderRedirect
{
	// global::Sandbox.Internal.SafeStringBuilder
	private static readonly NameSyntax s_safeStringBuilderName = SyntaxFactory.QualifiedName(
		SyntaxFactory.QualifiedName(
			SyntaxFactory.AliasQualifiedName(
				SyntaxFactory.IdentifierName( SyntaxFactory.Token( SyntaxKind.GlobalKeyword ) ),
				SyntaxFactory.IdentifierName( "Sandbox" ) ),
			SyntaxFactory.IdentifierName( "Internal" ) ),
		SyntaxFactory.IdentifierName( "SafeStringBuilder" ) );

	/// <summary>
	/// Called from <see cref="Worker.VisitIdentifierName"/>. Redirects a bare <c>StringBuilder</c>
	/// identifier that resolves to <c>System.Text.StringBuilder</c> to
	/// <c>global::Sandbox.Internal.SafeStringBuilder</c>.
	/// </summary>
	internal static SyntaxNode VisitIdentifierName( IdentifierNameSyntax node, Worker worker )
	{
		// Fast syntactic pre-check before touching the semantic model.
		if ( !node.Identifier.ValueText.Equals( "StringBuilder", StringComparison.Ordinal ) )
			return null;

		if ( !worker.CorelibPolyfillsEnabled )
			return null;

		// Don't replace the right-hand side of a qualified name (e.g. System.Text.StringBuilder
		// or Text.StringBuilder) — the VisitQualifiedName path handles those.
		if ( node.Parent is QualifiedNameSyntax q && q.Right == node )
			return null;

		if ( worker.Model?.GetSymbolInfo( node ).Symbol is not INamedTypeSymbol typeSymbol )
			return null;

		if ( !IsStringBuilderType( typeSymbol ) )
			return null;

		return s_safeStringBuilderName.WithTriviaFrom( node );
	}

	/// <summary>
	/// Called from <see cref="Worker.VisitQualifiedName"/>. Redirects a fully-qualified
	/// <c>System.Text.StringBuilder</c> (or any qualified form resolving to it) to
	/// <c>global::Sandbox.Internal.SafeStringBuilder</c>.
	/// </summary>
	internal static SyntaxNode VisitQualifiedName( QualifiedNameSyntax node, Worker worker )
	{
		// Fast check: rightmost identifier must be "StringBuilder".
		if ( !node.Right.Identifier.ValueText.Equals( "StringBuilder", StringComparison.Ordinal ) )
			return null;

		if ( !worker.CorelibPolyfillsEnabled )
			return null;

		if ( worker.Model?.GetSymbolInfo( node ).Symbol is not INamedTypeSymbol typeSymbol )
			return null;

		if ( !IsStringBuilderType( typeSymbol ) )
			return null;

		return s_safeStringBuilderName.WithTriviaFrom( node );
	}

	private static bool IsStringBuilderType( INamedTypeSymbol type ) =>
		type.Name == "StringBuilder" &&
		type.ContainingNamespace?.ToDisplayString() == "System.Text";
}
