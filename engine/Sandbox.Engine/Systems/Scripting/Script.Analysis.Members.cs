namespace Sandbox;

public sealed partial class Script
{
	/// <summary>A permitted method overload and the ranges of its parameters in the display text.</summary>
	public sealed record Signature( string Text, IReadOnlyList<SourceSpan> Parameters, bool IsVariadic = false );

	/// <summary>The innermost call at the caret. Argument is zero based.</summary>
	public readonly record struct SignatureHelp( SourceSpan Span, int Argument, IReadOnlyList<Signature> Overloads );

	public sealed partial class Analysis
	{
		/// <summary>Returns parameter help without executing host code. Handles incomplete and nested calls.</summary>
		public SignatureHelp? GetSignatureHelp( int position )
		{
			CheckPosition( position );
			if ( snapshot.GetSignatureHelp( position ) is not { } help ) return null;
			return new( help.Span.ToEngineSpan(), help.Argument, Array.AsReadOnly( help.Overloads.Select( signature =>
				new Signature( signature.Text, Array.AsReadOnly( signature.Parameters.Select( span => span.ToEngineSpan() ).ToArray() ), signature.IsVariadic ) ).ToArray() ) );
		}
	}
}
