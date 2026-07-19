using System;
using Microsoft.CodeAnalysis.Formatting;

namespace Sandbox.CodeUpgrader;

#nullable enable

/// <summary>
/// Hotload (currently) can't discover static members of generic types, which could lead to unexpected
/// behaviour after hotloading. Let's warn people about it, and they can suppress the warning with <c>[SkipHotload]</c>.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class HotloadUnsupportedAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.GenericStaticMembersUnsupported;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeNode, SyntaxKind.FieldDeclaration, SyntaxKind.PropertyDeclaration );
	}

	void AnalyzeNode( SyntaxNodeAnalysisContext context )
	{
		var memberSyntax = (MemberDeclarationSyntax)context.Node;

		if ( memberSyntax is FieldDeclarationSyntax && context.ContainingSymbol is IFieldSymbol { IsConst: true } )
		{
			// We deliberate don't / can't copy old values of consts when hotloading

			return;
		}

		if ( memberSyntax is PropertyDeclarationSyntax && !IsAutoProperty( context.ContainingSymbol ) )
		{
			// If this isn't an auto property, we visit any backing fields involved anyway

			return;
		}

		if ( context.ContainingSymbol is not { } symbol )
			return;

		if ( !symbol.IsStatic )
			return;

		if ( symbol.ContainingType is not { IsGenericType: true } )
			return;

		// [SkipHotload] suppresses this warning safely

		if ( HasAttribute( symbol, "Sandbox.SkipHotloadAttribute" ) )
			return;

		context.ReportDiagnostic( Diagnostic.Create( Rule, memberSyntax.GetLocation() ) );
	}

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Matches on static fields of generic types

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class MyClass<T>
			{
				[|public static object StaticField;|]
			}
			""" );

		// Matches on static auto-properties of generic types

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class MyClass<T>
			{
				[|public static object StaticProperty { get; set; }|]
			}
			""" );

		// Doesn't match on static non-auto properties of generic types

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class MyClass<T>
			{
				public static object StaticProperty => null;
			}
			""" );

		// [SkipHotload] suppresses this warning

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class MyClass<T>
			{
				[SkipHotload]
				public static object StaticField;
			}
			""" );

		await tester.TestWithMarkup(
			"""
			using Sandbox;

			public class MyClass<T>
			{
				[global::Sandbox.SkipHotloadAttribute]
				public static object StaticField;
			}
			""" );
	}

	private static bool IsAutoProperty( ISymbol? symbol )
	{
		if ( symbol is not IPropertySymbol propertySymbol ) return false;

		return propertySymbol.ContainingType
			.GetMembers()
			.OfType<IFieldSymbol>()
			.Any( field => SymbolEqualityComparer.Default.Equals( field.AssociatedSymbol, propertySymbol ) );
	}

	private static bool HasAttribute( ISymbol symbol, string name )
	{
		return symbol.GetAttributes()
			.Any( y => y.AttributeClass?.ToDisplayString() == name );
	}
}

[ExportCodeFixProvider( LanguageNames.CSharp ), Shared]
public sealed class HotloadUnsupportedFixer : Fixer<HotloadUnsupportedAnalyzer>
{
	public override async Task RegisterCodeFixesAsync( CodeFixContext context )
	{
		var root = await context.Document.GetSyntaxRootAsync( context.CancellationToken ).ConfigureAwait( false );

		var diagnostic = context.Diagnostics.First();
		var diagnosticSpan = diagnostic.Location.SourceSpan;

		var memberDeclNode = root?.FindNode( diagnosticSpan )
			.DescendantNodesAndSelf()
			.OfType<MemberDeclarationSyntax>()
			.FirstOrDefault();

		if ( memberDeclNode == null ) return;

		var action = CodeAction.Create(
			title: "Add [SkipHotload] Attribute",
			createChangedDocument: c => AddSkipHotloadAttributeAsync( context.Document, memberDeclNode ),
			equivalenceKey: "AddStatic",
			priority: CodeActionPriority.High );

		context.RegisterCodeFix( action, diagnostic );
	}

	private async Task<Document> AddSkipHotloadAttributeAsync( Document document, MemberDeclarationSyntax memberDeclNode )
	{
		var attribute = SyntaxFactory.Attribute( SyntaxFactory.ParseName( "SkipHotload" ) );
		var attributes = new SeparatedSyntaxList<AttributeSyntax>().Add( attribute );
		var list = SyntaxFactory.AttributeList( attributes )
			.WithTrailingTrivia( SyntaxFactory.EndOfLine( GetLineEnding( memberDeclNode ) ) );

		var replaced = memberDeclNode.AddAttributeLists( list );

		var root = await document.GetSyntaxRootAsync();
		var formattedRoot = Formatter.Format(
			root!.ReplaceNode( memberDeclNode, replaced ),
			document.Project.Solution.Workspace );
		var newline = GetLineEnding( memberDeclNode );
		var normalizedText = formattedRoot.ToFullString()
			.Replace( "\r\n", "\n" )
			.Replace( "\n", newline );
		var newRoot = CSharpSyntaxTree.ParseText( normalizedText ).GetRoot();

		return document.WithSyntaxRoot( newRoot );
	}

	private static string GetLineEnding( MemberDeclarationSyntax memberDeclNode )
	{
		var sourceText = memberDeclNode.SyntaxTree.GetText().ToString();

		if ( sourceText.Contains( "\r\n" ) )
			return "\r\n";

		if ( sourceText.Contains( "\n" ) )
			return "\n";

		return "\n";
	}

	public override async Task RunTests( IFixerTest tester )
	{
		// Fix up static fields

		await tester.Test(
			"""
			using Sandbox;
			public static class MyClass<T>
			{
			    [|public static object StaticField;|]
			}
			""",
			"""
			using Sandbox;
			public static class MyClass<T>
			{
			    [|[SkipHotload]
			    public static object StaticField;|]
			}
			""" );

		// Fix up static properties

		await tester.Test(
			"""
			using Sandbox;
			public static class MyClass<T>
			{
			    [|public static object StaticProperty { get; set; }|]
			}
			""",
			"""
			using Sandbox;
			public static class MyClass<T>
			{
			    [|[SkipHotload]
			    public static object StaticProperty { get; set; }|]
			}
			""" );

		// Keep existing attributes

		await tester.Test(
			"""
			using Sandbox;
			public static class MyClass<T>
			{
			    [|[Property]
			    public static object StaticField;|]
			}
			""",
			"""
			using Sandbox;
			public static class MyClass<T>
			{
			    [|[Property]
			    [SkipHotload]
			    public static object StaticField;|]
			}
			""" );
	}
}
