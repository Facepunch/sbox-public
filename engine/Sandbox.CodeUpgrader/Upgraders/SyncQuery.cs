using System.Collections.Generic;

namespace Sandbox.CodeUpgrader;

[DiagnosticAnalyzer( LanguageNames.CSharp )]
public partial class SyncQueryAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.SyncQuery;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.Attribute );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		var attributeSyntax = (AttributeSyntax)context.Node;

		// Check if the attribute name is Sync
		if ( attributeSyntax.Name.ToString() != "Sync" && attributeSyntax.Name.ToString() != "Sandbox.Sync" )
			return;

		if ( attributeSyntax.ArgumentList == null )
			return;

		var queryArgument = attributeSyntax.ArgumentList.Arguments.FirstOrDefault( arg => arg.NameEquals?.Name.Identifier.Text == "Query" );

		if ( queryArgument == null )
			return;

		var diagnostic = Diagnostic.Create( Rule, attributeSyntax.GetLocation() );
		context.ReportDiagnostic( diagnostic );
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// A plain [Sync] has no Query argument and must NOT be flagged
		await tester.TestWithMarkup( """
				using Sandbox;

				public class MyClass
				{
					[Sync]
					public int MySyncProperty { get; set; }
				}
				""" );

		// Query is no longer a settable attribute property, so old code carries a
		// CS0617 compiler error alongside our diagnostic - declare both.
		await tester.TestWithMarkup( """
				using Sandbox;

				public class MyClass
				{
					[[|Sync( {|CS0617:Query|} = true )|]]
					public int MySyncProperty { get; set; }
				}
				""" );
	}
}


[ExportCodeFixProvider( LanguageNames.CSharp ), Shared]
public class SyncQueryFix : Fixer<SyncQueryAnalyzer>
{
	public override async Task RegisterCodeFixesAsync( CodeFixContext context )
	{
		var root = await context.Document.GetSyntaxRootAsync( context.CancellationToken ).ConfigureAwait( false );

		// Locate the diagnostic in the source code
		var diagnostic = context.Diagnostics.First();
		var diagnosticSpan = diagnostic.Location.SourceSpan;

		// Find the attribute syntax node
		var attributeNode = root.FindToken( diagnosticSpan.Start ).Parent.AncestorsAndSelf()
			.OfType<AttributeSyntax>()
			.FirstOrDefault();

		if ( attributeNode == null ) return;

		var action = CodeAction.Create(
				title: "Use SyncFlags.Query",
				createChangedDocument: c => ChangeAttributeAsync( context.Document, attributeNode ),
				equivalenceKey: "ChangeQuerySyncToFlag",
				priority: CodeActionPriority.High );

		// Register a code action that will invoke the fix
		context.RegisterCodeFix( action, diagnostic );
	}

	private async Task<Document> ChangeAttributeAsync( Document document, AttributeSyntax attributeNode )
	{
		var root = await document.GetSyntaxRootAsync();
		var arguments = attributeNode.ArgumentList?.Arguments.ToList() ?? new List<AttributeArgumentSyntax>();
		var queryArgument = arguments.FirstOrDefault( arg => arg.NameEquals?.Name.Identifier.Text == "Query" );
		var syncFlagsArgument = arguments.FirstOrDefault( arg => arg.Expression.ToString().StartsWith( "SyncFlags" ) );
		var hadQueryTrue = false;

		// Remove the Query argument if it exists
		if ( queryArgument != null )
		{
			arguments.Remove( queryArgument );

			if ( queryArgument.Expression is LiteralExpressionSyntax literal &&
				 literal.IsKind( SyntaxKind.TrueLiteralExpression ) )
			{
				hadQueryTrue = true;
			}
		}

		if ( hadQueryTrue )
		{
			// Create the new SyncFlags.Query expression
			var queryExpression = SyntaxFactory.ParseExpression( "SyncFlags.Query" );

			if ( syncFlagsArgument != null )
			{
				// Combine existing SyncFlags with SyncFlags.Query using a bitwise OR
				var combinedExpression = SyntaxFactory.BinaryExpression(
					SyntaxKind.BitwiseOrExpression,
					syncFlagsArgument.Expression,
					queryExpression );

				// Replace the old SyncFlags argument
				syncFlagsArgument = syncFlagsArgument.WithExpression( combinedExpression );
			}
			else
			{
				// Add a new SyncFlags argument
				syncFlagsArgument = SyntaxFactory.AttributeArgument( queryExpression );
			}
		}

		var reorderedArguments = new List<AttributeArgumentSyntax>();
		if ( syncFlagsArgument is not null )
			reorderedArguments.Add( syncFlagsArgument );

		reorderedArguments.AddRange( arguments.Where( arg => arg.NameEquals != null ) );

		// Create a new AttributeArgumentList
		var updatedArgumentList = SyntaxFactory.AttributeArgumentList( SyntaxFactory.SeparatedList( reorderedArguments ) );

		// Create the new Sync attribute
		var newAttribute = SyntaxFactory.Attribute( SyntaxFactory.ParseName( "Sync" ) )
			.WithArgumentList( updatedArgumentList );

		// Replace the old HostSync attribute with the new Sync attribute
		root = root.ReplaceNode( attributeNode, newAttribute );

		// Return the updated document
		return document.WithSyntaxRoot( root );
	}

	public override async Task RunTests( IFixerTest tester )
	{
		await tester.Test( """
				using Sandbox;

				public class MyClass
				{
					[[|Sync( {|CS0617:Query|} = true )|]]
					public int MySyncProperty { get; set; }
				}
				""",
				"""
				using Sandbox;

				public class MyClass
				{
					[[|Sync(SyncFlags.Query)|]]
					public int MySyncProperty { get; set; }
				}
				""" );
	}
}
