using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sandbox;

internal class BlacklistCodeWalker( SemanticModel semanticModel ) : CSharpSyntaxWalker
{
	public static readonly DiagnosticDescriptor BlacklistDescriptor = new( "SB500", "Blacklist Error", "Prohibited type '{0}' used", "Sandbox.Compiling", DiagnosticSeverity.Error, true );

	public List<Diagnostic> Diagnostics { get; set; } = [];

	record LineSymbol( int Line, ISymbol Symbol );
	HashSet<LineSymbol> LineSymbols = [];

	private readonly SemanticModel _semanticModel = semanticModel;

	static readonly SymbolDisplayFormat FullyQualifiedSymbolFormat = new SymbolDisplayFormat(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
		memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
		parameterOptions: SymbolDisplayParameterOptions.IncludeType,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
	);

	/// <summary>
	/// Visits every node type, unless that node type has been explicitly overridden.
	/// This gives us complete coverage of every type of possible syntax node.
	/// From there we can see if it references any blacklisted symbols.
	/// </summary>
	public override void DefaultVisit( SyntaxNode node )
	{
		var symbolInfo = _semanticModel.GetSymbolInfo( node );

		// Consider the CandidateSymbols even though you'd assume it can't compile.. It can!
		var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

		if ( symbol is null )
		{
			base.DefaultVisit( node );
			return;
		}

		var fullyQualifiedName = symbol.ToDisplayString( FullyQualifiedSymbolFormat );

		if ( !CompilerRules.IsBlocked( fullyQualifiedName ) )
		{
			base.DefaultVisit( node );
			return;
		}

		// This isn't strictly needed, it just makes the diagnostics a bit neater and easier to test against
		var lineSymbol = new LineSymbol( node.GetLocation().GetMappedLineSpan().StartLinePosition.Line, symbol );
		if ( LineSymbols.Add( lineSymbol ) == false )
		{
			base.DefaultVisit( node );
			return;
		}

		Diagnostics.Add( Diagnostic.Create( BlacklistDescriptor, node.GetLocation(), fullyQualifiedName ) );

		base.DefaultVisit( node );
	}
}
