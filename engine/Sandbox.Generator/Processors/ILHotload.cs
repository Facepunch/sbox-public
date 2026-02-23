using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Sandbox.Generator;

public static class ILHotloadProcessor
{
	internal static void Process( Processor processor )
	{
		processor.ILHotloadSupported = false;

		if ( processor.LastSuccessfulCompilation == null || processor.BeforeILHotloadProcessingTrees.IsDefault )
		{
			return;
		}

		var oldTrees = GetSyntaxTreeDict( processor.BeforeILHotloadProcessingTrees );
		var newTrees = GetSyntaxTreeDict( processor.Compilation.SyntaxTrees );

		//
		// Early out: check if files added / removed
		//

		if ( oldTrees.Count != newTrees.Count )
		{
			return;
		}

		if ( oldTrees.Keys.Any( x => !newTrees.ContainsKey( x ) ) )
		{
			return;
		}

		if ( newTrees.Keys.Any( x => !oldTrees.ContainsKey( x ) ) )
		{
			return;
		}

		//
		// Compare each syntax tree.
		// If only statement blocks have changed, inject attributes to mark the containing definitions.
		//

		var onlyBlocksChanged = true;
		var updatedNewTrees = new ConcurrentBag<SyntaxTree>();
		var exceptions = new ConcurrentBag<Exception>();

		Parallel.ForEach( oldTrees.Values, oldTree =>
		{
			try
			{
				var newTree = newTrees[oldTree.FilePath];

				if ( !HaveOnlyBlocksChanged( oldTree, newTree, out var updatedNewTree ) )
				{
					Volatile.Write( ref onlyBlocksChanged, false );
					return;
				}

				updatedNewTrees.Add( updatedNewTree );
			}
			catch ( Exception e )
			{
				Volatile.Write( ref onlyBlocksChanged, false );
				exceptions.Add( e );
			}
		} );

		if ( exceptions.Count == 1 )
		{
			throw exceptions.First();
		}

		if ( exceptions.Count > 1 )
		{
			throw new AggregateException( exceptions );
		}

		//
		// Abort if any tree had a more substantial change than just a statement block.
		//

		if ( !onlyBlocksChanged )
		{
			return;
		}

		processor.ILHotloadSupported = true;

		//
		// Add SupportsILHotloadAttribute
		//

		var oldAssemblyVersion = processor.LastSuccessfulCompilation.Assembly.Identity.Version;

		var compilerExtraTree = processor.Compilation.SyntaxTrees
			.Single( x => x.FilePath == Processor.CompilerExtraPath );

		var compilerExtraText = compilerExtraTree.GetText();

		var updatedCompilerExtraTree = compilerExtraTree.WithChangedText( compilerExtraText.WithChanges( new TextChange(
			new TextSpan( compilerExtraText.Length, 0 ),
			$"{Environment.NewLine}[assembly: global::Sandbox.SupportsILHotloadAttribute(\"{oldAssemblyVersion}\")]" ) ) );

		processor.ReplaceSyntaxTree( compilerExtraTree, updatedCompilerExtraTree );

		//
		// Apply changed syntax trees
		//

		foreach ( var updatedNewTree in updatedNewTrees )
		{
			var newTree = newTrees[updatedNewTree.FilePath];
			processor.ReplaceSyntaxTree( newTree, updatedNewTree );
		}
	}

	private static Dictionary<string, SyntaxTree> GetSyntaxTreeDict( ImmutableArray<SyntaxTree> trees )
	{
		return trees
			.Where( x => !x.FilePath.Equals( Processor.CompilerExtraPath, StringComparison.OrdinalIgnoreCase ) )
			.ToDictionary( x => x.FilePath, x => x );
	}

	/// <summary>
	/// For debugging, get a string representing the path to a node in an AST. Type and method names
	/// are included.
	/// </summary>
	/// <param name="node">Node to find the path of.</param>
	/// <returns>A string representing the path</returns>
	private static string FormatNodePath( SyntaxNode node )
	{
		var thisNodeFormat = node.Kind().ToString();

		switch ( node )
		{
			case MethodDeclarationSyntax methodDef:
				thisNodeFormat = $"{thisNodeFormat}( {methodDef.Identifier.ValueText} )";
				break;
			case TypeDeclarationSyntax classDef:
				thisNodeFormat = $"{thisNodeFormat}( {classDef.Identifier.ValueText} )";
				break;
		}

		if ( node.Parent == null )
		{
			return thisNodeFormat;
		}

		return $"{FormatNodePath( node.Parent )} -> {thisNodeFormat}";
	}

	private static bool NodeIsDeclaration( SyntaxNode node )
	{
		// TODO: This might not be exhaustive!

		switch ( node.Kind() )
		{
			case SyntaxKind.UsingDirective:
			case SyntaxKind.ClassDeclaration:
			case SyntaxKind.DelegateDeclaration:
			case SyntaxKind.EnumDeclaration:
			case SyntaxKind.StructDeclaration:
			case SyntaxKind.RecordDeclaration:
			case SyntaxKind.RecordStructDeclaration:
			case SyntaxKind.MethodDeclaration:
			case SyntaxKind.PropertyDeclaration:
			case SyntaxKind.FieldDeclaration:
			case SyntaxKind.EventDeclaration:
			case SyntaxKind.ConstructorDeclaration:
			case SyntaxKind.DestructorDeclaration:
			case SyntaxKind.ConversionOperatorDeclaration:
			case SyntaxKind.AddAccessorDeclaration:
			case SyntaxKind.RemoveAccessorDeclaration:
			case SyntaxKind.GetAccessorDeclaration:
			case SyntaxKind.SetAccessorDeclaration:
			case SyntaxKind.IndexerDeclaration:
			case SyntaxKind.OperatorDeclaration:
				return true;

			default:
				return false;
		}
	}

	/// <summary>
	/// Test to see if the given node contains any declarations that would exclude it
	/// from using <see cref="ILHotload"/>'s fast path.
	/// </summary>
	private static bool NodeContainsDeclarations( SyntaxNode node )
	{
		foreach ( var descendant in node.DescendantNodesAndSelf() )
		{
			if ( NodeIsDeclaration( descendant ) )
			{
				return true;
			}
		}

		return false;
	}

	private static bool HaveOnlyBlocksChanged( SyntaxTree oldTree, SyntaxTree newTree, out SyntaxTree updatedNewTree )
	{
		if ( oldTree == null && newTree == null )
		{
			throw new ArgumentNullException( nameof( newTree ),
				$"Expected at least one of ({nameof( oldTree )}, {nameof( newTree )}) to not be null" );
		}

		updatedNewTree = oldTree;

		if ( oldTree == null || newTree == null )
		{
			return false;
		}

		var oldDefines = new HashSet<string>( oldTree.Options.PreprocessorSymbolNames );

		if ( !oldDefines.SetEquals( newTree.Options.PreprocessorSymbolNames ) )
		{
			return false;
		}

		if ( newTree.IsEquivalentTo( oldTree ) )
		{
			return true;
		}

		var changes = newTree.GetChanges( oldTree );

		if ( changes.Count == 0 )
		{
			return true;
		}

		if ( !oldTree.TryGetRoot( out var oldRoot ) || !newTree.TryGetRoot( out var newRoot ) )
		{
			// Maybe throw?
			return false;
		}

		var newSpanStartOffset = 0;
		var replacements = new Dictionary<SyntaxNode, SyntaxNode>();

		foreach ( var change in changes )
		{
			// For each change:
			//   1. Find the deepest node containing that change in both the old and new ASTs
			//   2. If either node isn't within a method declaration, fail the test
			//   3. If either node contains a type or member declaration, fail the test
			//   4. We found a valid change, add it to the set if given

			var newSpan = new TextSpan( change.Span.Start + newSpanStartOffset, change.NewText?.Length ?? 0 );

			newSpanStartOffset += (change.NewText?.Length ?? 0) - change.Span.Length;

			var oldNode = oldRoot.FindNode( change.Span, getInnermostNodeForTie: true );
			var newNode = newRoot.FindNode( newSpan, getInnermostNodeForTie: true );

			var oldIsTrivia = IsTrivialChange( oldNode, change.Span );
			var newIsTrivia = IsTrivialChange( newNode, newSpan );

			if ( newIsTrivia && oldIsTrivia && string.Equals( newNode.ToString(), oldNode.ToString(), StringComparison.Ordinal ) )
			{
				// Only trivia changed
				continue;
			}

			var oldMethodBlock = oldNode.FirstAncestorOrSelf<SyntaxNode>( IsDeclaringBlock );
			var newMethodBlock = newNode.FirstAncestorOrSelf<SyntaxNode>( IsDeclaringBlock );

			if ( oldMethodBlock == null || newMethodBlock == null )
			{
				// Ignore SourceLocation attribute parameter changes

				var sourceLocation = SyntaxFactory.ParseName( "Sandbox.Internal.SourceLocation" );

				var oldAttrib = oldNode.FirstAncestorOrSelf<AttributeListSyntax>( x => IsSingleAttribute( x, sourceLocation ) );
				var newAttrib = newNode.FirstAncestorOrSelf<AttributeListSyntax>( x => IsSingleAttribute( x, sourceLocation ) );

				if ( oldAttrib == null || newAttrib == null )
				{
					return false;
				}

				continue;
			}

			if ( NodeContainsDeclarations( oldNode ) || NodeContainsDeclarations( newNode ) )
			{
				return false;
			}

			if ( TryHandleMethodDecl( oldMethodBlock, newMethodBlock, replacements ) )
			{
				continue;
			}

			if ( TryHandlePropertyDecl( oldMethodBlock, newMethodBlock, replacements ) )
			{
				continue;
			}

			return false;
		}

		if ( replacements.Count > 0 )
		{
			var root = newTree.GetRoot();
			updatedNewTree = newTree.WithRootAndOptions(
				root.ReplaceNodes( replacements.Keys, ( a, b ) => replacements[a] ),
				newTree.Options );
		}

		return true;
	}

	/// <summary>
	/// Can we ignore the given change because it only involves whitespace / comments?
	/// </summary>
	private static bool IsTrivialChange( SyntaxNode changedNode, TextSpan changedSpan )
	{
		return !changedNode.Span.OverlapsWith( changedSpan );
	}

	private static bool IsSingleAttribute( AttributeListSyntax node, NameSyntax name )
	{
		return node.Attributes.Count == 1 && node.Attributes[0].Name.IsEquivalentTo( name );
	}

	private static bool IsDeclaringBlock( SyntaxNode node )
	{
		if ( node.IsKind( SyntaxKind.ArrowExpressionClause ) )
		{
			return true;
		}

		if ( !node.IsKind( SyntaxKind.Block ) && !node.IsKind( SyntaxKind.ArrowExpressionClause ) )
		{
			return false;
		}

		return node.Parent.IsKind( SyntaxKind.MethodDeclaration )
			|| node.Parent.IsKind( SyntaxKind.GetAccessorDeclaration )
			|| node.Parent.IsKind( SyntaxKind.SetAccessorDeclaration );
	}

	private static bool TryHandleMethodDecl( SyntaxNode oldBlock, SyntaxNode newBlock, Dictionary<SyntaxNode, SyntaxNode> replacements )
	{
		if ( oldBlock.Parent is not MethodDeclarationSyntax || newBlock.Parent is not MethodDeclarationSyntax newMethodDecl )
		{
			return false;
		}

		var replacingMethodDecl = newMethodDecl.WithAttributeLists(
			newMethodDecl.AttributeLists.Add(
				SyntaxFactory.AttributeList( SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Attribute( SyntaxFactory.QualifiedName(
						SyntaxFactory.AliasQualifiedName(
							SyntaxFactory.IdentifierName( SyntaxFactory.Token( SyntaxKind.GlobalKeyword ) ),
							SyntaxFactory.IdentifierName( "Sandbox" ) ),
						SyntaxFactory.IdentifierName( "MethodBodyChangeAttribute" )
					) )
				) )
			)
		);

		replacements[newMethodDecl] = replacingMethodDecl;
		return true;
	}

	private static bool TryHandlePropertyDecl( SyntaxNode oldBlock, SyntaxNode newBlock, Dictionary<SyntaxNode, SyntaxNode> replacements )
	{
		if ( !TryGetPropertyDeclarationFromBlock( oldBlock, out _, out _ ) )
		{
			return false;
		}

		if ( !TryGetPropertyDeclarationFromBlock( newBlock, out var newPropertyDecl, out var accessor ) )
		{
			return false;
		}

		var replacingPropertyDecl = newPropertyDecl.WithAttributeLists(
			newPropertyDecl.AttributeLists.Add(
				SyntaxFactory.AttributeList( SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Attribute(
						SyntaxFactory.QualifiedName( SyntaxFactory.AliasQualifiedName(
								SyntaxFactory.IdentifierName( SyntaxFactory.Token( SyntaxKind.GlobalKeyword ) ),
								SyntaxFactory.IdentifierName( "Sandbox" ) ),
							SyntaxFactory.IdentifierName( "PropertyAccessorBodyChangeAttribute" )
						),
						SyntaxFactory.AttributeArgumentList( SyntaxFactory.SingletonSeparatedList(
							SyntaxFactory.AttributeArgument( SyntaxFactory.MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.MemberAccessExpression(
									SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.AliasQualifiedName(
										SyntaxFactory.IdentifierName( SyntaxFactory.Token( SyntaxKind.GlobalKeyword ) ),
										SyntaxFactory.IdentifierName( "Sandbox" ) ),
									SyntaxFactory.IdentifierName( "PropertyAccessor" ) ),
								SyntaxFactory.IdentifierName( accessor.ToString() ) ) ) ) )
					)
				) )
			)
		);

		replacements[newPropertyDecl] = replacingPropertyDecl;
		return true;
	}

	private enum PropertyAccessor
	{
		Get,
		Set
	}

	private static bool TryGetPropertyDeclarationFromBlock( SyntaxNode block, out PropertyDeclarationSyntax outPropDecl, out PropertyAccessor accessor )
	{
		if ( block.Parent is PropertyDeclarationSyntax propDecl )
		{
			outPropDecl = propDecl;
			accessor = PropertyAccessor.Get;
			return true;
		}

		if ( block.Parent is AccessorDeclarationSyntax accDecl && accDecl.Parent is AccessorListSyntax accList )
		{
			outPropDecl = accList.Parent as PropertyDeclarationSyntax;
			accessor = accDecl.Keyword.Text == "get" ? PropertyAccessor.Get : PropertyAccessor.Set;
			return outPropDecl != null;
		}

		outPropDecl = null;
		accessor = default;
		return false;
	}

	/// <summary>
	/// Compare lists of syntax trees, and log any changes. Includes the syntax node path to each change.
	/// </summary>
	/// <param name="oldTrees">Syntax trees from a previous compilation</param>
	/// <param name="newTrees">New syntax trees to compare</param>
	public static string GetChanges( SyntaxTree oldTree, SyntaxTree newTree )
	{
		var changes = newTree.GetChanges( oldTree );

		if ( changes.Count == 0 )
		{
			return null;
		}

		if ( !oldTree.TryGetRoot( out var oldRoot ) )
		{
			return null;
		}

		var writer = new StringWriter();

		foreach ( var change in changes )
		{
			var node = oldRoot.FindNode( change.Span, getInnermostNodeForTie: true );
			var isTrivia = !node.Span.OverlapsWith( change.Span );

			var startPosition = oldTree.GetLocation( change.Span ).GetLineSpan().StartLinePosition;

			writer.WriteLine( $"{FormatNodePath( node )}{(isTrivia ? " (trivia)" : "")}" );

			var oldLines = oldRoot.GetText().ToString( change.Span ).Split( '\n' );
			var newLines = change.NewText?.Split( '\n' ) ?? Array.Empty<string>();

			if ( oldLines.Length + newLines.Length > 5 )
			{
				writer.WriteLine( $"REMOVED {oldLines.Length}, ADDED {newLines.Length} LINES" );
				continue;
			}

			var offset = 0;
			foreach ( var line in oldLines )
			{
				writer.WriteLine( $"LINE {(startPosition.Line + ++offset).ToString().PadRight( 4 )} --- {line}" );
			}

			offset = 0;
			foreach ( var line in newLines )
			{
				writer.WriteLine( $"LINE {(startPosition.Line + ++offset).ToString().PadRight( 4 )} +++ {line}" );
			}
		}

		return writer.ToString();
	}
}
