namespace Sandbox;

/// <summary>Translates values and metadata between Breen and the engine.</summary>
internal static class ScriptConversions
{
	internal static Script.SourceSpan ToEngineSpan( this Breen.SourceSpan span )
		=> new( span.Start, span.Length );

	internal static Script.Token ToEngineToken( this Breen.Token token )
		=> new( token.Span.ToEngineSpan(), (Script.TokenKind)token.Kind );

	internal static Script.Diagnostic ToEngineDiagnostic( this Breen.Diagnostic diagnostic )
		=> new( diagnostic.Code, (Script.DiagnosticSeverity)diagnostic.Severity, diagnostic.Message, diagnostic.Span.ToEngineSpan() );

	internal static Script.ScriptError ToEngineError( this Breen.Diagnostic diagnostic )
		=> new( diagnostic.Code, diagnostic.Message, diagnostic.Span.Start, diagnostic.Span.Length );

	internal static Script.Input ToEngineInput( this Breen.EditorInput input )
		=> new( input.Name, input.Type, input.Description );

	internal static Breen.EditorInput ToBreenInput( this Script.Input input )
		=> new( input.Name, input.Type, Description: input.Description );

	internal static Breen.EditorType ToBreenEditorType( this TypeDescription type )
		=> new( type.Name, type.TargetType, type.Description );

	internal static Script.Symbol ToEngineSymbol( this Breen.EditorSymbol symbol )
		=> new( symbol.Name, (Script.SymbolKind)symbol.Kind, symbol.Span.ToEngineSpan(), symbol.Type,
			symbol.Detail, symbol.Description, symbol.Declaration?.ToEngineSpan() );

	internal static Script.Completion ToEngineCompletion( this Breen.CompletionItem item )
		=> new( item.Label, item.InsertText, (Script.SymbolKind)item.Kind, item.Type, item.Detail, item.Description );

	internal static Script.CompletionList ToEngineCompletionList( this Breen.CompletionList list )
		=> new( list.ReplacementSpan.ToEngineSpan(), Array.AsReadOnly( list.Items.Select( item => item.ToEngineCompletion() ).ToArray() ) );
}
