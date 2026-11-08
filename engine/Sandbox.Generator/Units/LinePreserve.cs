using Sandbox;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Generator
{
	static class LinePreserve
	{
		public static T AddLineNumber<T>( T node, T originalNode, SyntaxTree tree, Worker master ) where T : SyntaxNode
		{
			if ( !master.IsFullGeneration )
				return node;

			if ( tree.FilePath.StartsWith( "_gen_" ) )
				return node;

			var lineSpan = originalNode.GetLocation().GetLineSpan();
			if ( originalNode.HasLeadingTrivia ) lineSpan = originalNode.GetLeadingTrivia().First().GetLocation().GetLineSpan();

			var lineNm = SyntaxFactory.Literal( $"{lineSpan.StartLinePosition.Line + 1}", lineSpan.StartLinePosition.Line + 1 );
			var file = SyntaxFactory.Literal( $"{tree.FilePath}" );

			// "\r#line 12 "file"\r\n", built with its whitespace in place - NormalizeWhitespace on every
			// statement was a good part of the generator's time
			var line = SyntaxFactory.LineDirectiveTrivia(
				SyntaxFactory.Token( SyntaxFactory.TriviaList( SyntaxFactory.CarriageReturn ), SyntaxKind.HashToken, SyntaxFactory.TriviaList() ),
				SyntaxFactory.Token( SyntaxFactory.TriviaList(), SyntaxKind.LineKeyword, SyntaxFactory.TriviaList( SyntaxFactory.Space ) ),
				lineNm.WithLeadingTrivia().WithTrailingTrivia( SyntaxFactory.Space ),
				file.WithLeadingTrivia().WithTrailingTrivia(),
				SyntaxFactory.Token( SyntaxFactory.TriviaList(), SyntaxKind.EndOfDirectiveToken, SyntaxFactory.TriviaList( SyntaxFactory.CarriageReturnLineFeed ) ),
				true );

			var trivia = node.HasLeadingTrivia ? node.GetLeadingTrivia() : SyntaxFactory.TriviaList();

			trivia = trivia.Insert( 0, SyntaxFactory.Trivia( line ) );

			return node.WithLeadingTrivia( trivia );
		}
	}
}
