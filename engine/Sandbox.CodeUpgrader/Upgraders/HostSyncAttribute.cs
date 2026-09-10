using System.Collections.Generic;

namespace Sandbox.CodeUpgrader;

[DiagnosticAnalyzer( LanguageNames.CSharp )]
public partial class HostSyncAttributeAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.HostSyncAttribute;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.Attribute );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		var attributeSyntax = (AttributeSyntax)context.Node;

		// Check if the attribute name is "HostSync"
		if ( attributeSyntax.Name.ToString() == "HostSync" || attributeSyntax.Name.ToString() == "Sandbox.HostSync" )
		{
			var diagnostic = Diagnostic.Create( Rule, attributeSyntax.GetLocation() );
			context.ReportDiagnostic( diagnostic );
		}
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		await tester.TestWithMarkup( """
				using Sandbox;

				public class MyClass
				{
					[[|HostSync|]]
					public int MySyncProperty { get; set; }
				}
				""" );
	}
}


[ExportCodeFixProvider( LanguageNames.CSharp ), Shared]
public class HostSyncAttributeFix : Fixer<HostSyncAttributeAnalyzer>
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
				title: "Use SyncFlags.FromHost",
				createChangedDocument: c => ChangeAttributeAsync( context.Document, attributeNode ),
				equivalenceKey: "ChangeHostSyncToFlag",
				priority: CodeActionPriority.High );

		// Register a code action that will invoke the fix
		context.RegisterCodeFix( action, diagnostic );
	}

	private async Task<Document> ChangeAttributeAsync( Document document, AttributeSyntax attributeNode )
	{
		var root = await document.GetSyntaxRootAsync();
		var siblingSync = attributeNode.Parent
			.ChildNodes()
			.FirstOrDefault( n => n is AttributeSyntax asn && asn.Name.ToString() is "Sync" or "Sandbox.Sync" ) as AttributeSyntax;

		// Do we already have a Sync node as well - update that one because we shouldn't have duplicates
		if ( siblingSync is not null )
		{
			var arguments = siblingSync.ArgumentList?.Arguments.ToList() ?? new List<AttributeArgumentSyntax>();
			var syncFlagsArgument = arguments.FirstOrDefault( arg => arg.Expression.ToString().StartsWith( "SyncFlags" ) );

			// Create the new SyncFlags.FromHost expression
			var fromHostExpression = SyntaxFactory.ParseExpression( "SyncFlags.FromHost" );

			if ( syncFlagsArgument != null )
			{
				// Combine existing SyncFlags with SyncFlags.FromHost using a bitwise OR
				var combinedExpression = SyntaxFactory.BinaryExpression(
					SyntaxKind.BitwiseOrExpression,
					syncFlagsArgument.Expression,
					fromHostExpression );

				// Replace the old SyncFlags argument
				syncFlagsArgument = syncFlagsArgument.WithExpression( combinedExpression );
			}
			else
			{
				// Add a new SyncFlags argument
				syncFlagsArgument = SyntaxFactory.AttributeArgument( fromHostExpression );
			}

			var reorderedArguments = new List<AttributeArgumentSyntax>();
			if ( syncFlagsArgument is not null )
				reorderedArguments.Add( syncFlagsArgument );

			reorderedArguments.AddRange( arguments.Where( arg => arg.NameEquals != null ) );

			// Create a new AttributeArgumentList
			var updatedArgumentList = SyntaxFactory.AttributeArgumentList( SyntaxFactory.SeparatedList( reorderedArguments ) );
			var newAttribute = siblingSync.WithArgumentList( updatedArgumentList );

			var newRoot = root.TrackNodes( attributeNode, siblingSync );
			newRoot = newRoot.RemoveNode( newRoot.GetCurrentNode( attributeNode ), SyntaxRemoveOptions.KeepNoTrivia );
			newRoot = newRoot.ReplaceNode( newRoot.GetCurrentNode( siblingSync ), newAttribute );
			root = newRoot;
		}
		else
		{
			var arguments = attributeNode.ArgumentList?.Arguments.ToList() ?? new List<AttributeArgumentSyntax>();
			var syncFlagsArgument = arguments.FirstOrDefault( arg => arg.Expression.ToString().StartsWith( "SyncFlags" ) );

			// Create the new SyncFlags.FromHost expression
			var fromHostExpression = SyntaxFactory.ParseExpression( "SyncFlags.FromHost" );

			if ( syncFlagsArgument != null )
			{
				// Combine existing SyncFlags with SyncFlags.FromHost using a bitwise OR
				var combinedExpression = SyntaxFactory.BinaryExpression(
					SyntaxKind.BitwiseOrExpression,
					syncFlagsArgument.Expression,
					fromHostExpression );

				// Replace the old SyncFlags argument
				syncFlagsArgument = syncFlagsArgument.WithExpression( combinedExpression );
			}
			else
			{
				// Add a new SyncFlags argument
				syncFlagsArgument = SyntaxFactory.AttributeArgument( fromHostExpression );
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
		}

		// Return the updated document
		return document.WithSyntaxRoot( root );
	}

	public override async Task RunTests( IFixerTest tester )
	{
		await tester.Test( """
				using Sandbox;

				public class MyClass
				{
					[[|HostSync|]]
					public int MySyncProperty { get; set; }
				}
				""",
				"""
				using Sandbox;

				public class MyClass
				{
					[[|Sync(SyncFlags.FromHost)|]]
					public int MySyncProperty { get; set; }
				}
				""" );
	}
}
