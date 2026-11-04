namespace Sandbox;

public sealed partial class Script
{
	/// <summary>A range in the original source, measured in UTF-16 code units.</summary>
	public readonly record struct SourceSpan( int Start, int Length );

	/// <summary>An external input's metadata. No runtime value is assigned by this definition.</summary>
	public readonly record struct Input( string Name, Type Type = null, string Description = null );

	/// <summary>Lexical categories for syntax highlighting.</summary>
	public enum TokenKind
	{
		Identifier,
		Keyword,
		Number,
		String,
		Comment,
		Punctuation
	}

	/// <summary>A lexical category and its location in the original source.</summary>
	public readonly record struct Token( SourceSpan Span, TokenKind Kind );

	/// <summary>Importance of an editor diagnostic.</summary>
	public enum DiagnosticSeverity
	{
		Info,
		Warning,
		Error
	}

	/// <summary>An editor diagnostic, independent of execution errors.</summary>
	public readonly record struct Diagnostic( string Code, DiagnosticSeverity Severity, string Message, SourceSpan Span );

	/// <summary>Semantic categories for symbols and completion suggestions.</summary>
	public enum SymbolKind
	{
		Input,
		Local,
		Type,
		Member,
		Keyword,
		Literal
	}

	/// <summary>Metadata for a symbol. Type may be unknown; Declaration is only present for script declarations.</summary>
	public readonly record struct Symbol( string Name, SymbolKind Kind, SourceSpan Span, Type Type,
		string Detail, string Description, SourceSpan? Declaration );

	/// <summary>A completion's display text, insertion text and semantic metadata.</summary>
	public readonly record struct Completion( string Label, string InsertText, SymbolKind Kind,
		Type Type, string Detail, string Description );

	/// <summary>Read-only completion candidates and the source range to replace when accepting one.</summary>
	public readonly record struct CompletionList( SourceSpan ReplacementSpan, IReadOnlyList<Completion> Items );
}
