namespace Sandbox;

public sealed partial class Script
{
	/// <summary>Immutable editor data for one source, input and permission snapshot.</summary>
	public sealed partial class Analysis
	{
		private readonly Breen.EditorAnalysis snapshot;

		/// <summary>The exact source text to which all ranges refer.</summary>
		public string Source => snapshot.Document.Source;

		/// <summary>Lexical ranges for syntax highlighting, excluding whitespace.</summary>
		public IReadOnlyList<Token> Tokens { get; }

		/// <summary>Editor diagnostics. An empty list does not guarantee successful execution.</summary>
		public IReadOnlyList<Diagnostic> Diagnostics { get; }

		/// <summary>Host input metadata captured by this snapshot, including inputs not used by the source.</summary>
		public IReadOnlyList<Input> Inputs { get; }

		internal Analysis( Breen.EditorAnalysis snapshot )
		{
			this.snapshot = snapshot;
			Tokens = Array.AsReadOnly( snapshot.Document.Tokens.Select( token => token.ToEngineToken() ).ToArray() );
			Diagnostics = Array.AsReadOnly( snapshot.Diagnostics.Select( error => error.ToEngineDiagnostic() ).ToArray() );
			Inputs = Array.AsReadOnly( snapshot.Inputs.Select( input => input.ToEngineInput() ).ToArray() );
		}

		/// <summary>Returns the token covering a UTF-16 character offset, or null for whitespace or EOF.</summary>
		public Token? GetToken( int position )
		{
			CheckPosition( position );
			return snapshot.Document.GetTokenAt( position )?.ToEngineToken();
		}

		/// <summary>Returns symbol information at a UTF-16 character offset without allocating.</summary>
		public Symbol? GetSymbol( int position )
		{
			CheckPosition( position );
			return snapshot.GetSymbol( position )?.ToEngineSymbol();
		}

		/// <summary>
		/// Returns permitted suggestions at a UTF-16 caret offset. Accept a suggestion by replacing
		/// ReplacementSpan with its InsertText in this snapshot's source. Queries do not execute host code.
		/// </summary>
		public CompletionList GetCompletions( int position )
		{
			CheckPosition( position );
			return snapshot.GetCompletions( position ).ToEngineCompletionList();
		}

		private void CheckPosition( int position ) => ArgumentOutOfRangeException.ThrowIfGreaterThan( (uint)position, (uint)Source.Length, nameof( position ) );
	}
}
