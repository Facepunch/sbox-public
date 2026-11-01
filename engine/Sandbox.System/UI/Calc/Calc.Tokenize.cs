namespace Sandbox.UI;

partial class Calc
{
	private enum TokenType
	{
		Literal,

		Add,            // +
		Subtract,       // -
		Multiply,       // *
		Divide,         // /		
	};

	private struct Token
	{
		public TokenType Type;
		public Length? Value;

		public static Token Literal( Length? value ) => new Token { Type = TokenType.Literal, Value = value };
	}

	private static List<Token> Tokenize( string expression )
	{
		var tokens = new List<Token>();
		expression = expression.Trim();
		var p = new Parse( expression );

		while ( !p.IsEnd )
		{
			//
			// Skip bullshit
			//
			p.SkipWhitespaceAndNewlines();

			if ( p.TrySkip( "(" ) ) continue;
			if ( p.TrySkip( "calc(", ignorecase: true ) ) continue;
			if ( p.TrySkip( ")" ) ) continue;

			p.SkipWhitespaceAndNewlines();

			if ( p.IsEnd ) break;

			//
			// Read
			//			
			if ( p.TryReadLength( out var length ) )
			{
				tokens.Add( new Token()
				{
					Type = TokenType.Literal,
					Value = length
				} );

				continue;
			}
			else
			{
				var token = new Token();

				var word = p.ReadUntilWhitespaceOrNewlineOrEnd().Trim();
				word = word.ToLowerInvariant();

				if ( word == "+" ) token.Type = TokenType.Add;
				else if ( word == "-" ) token.Type = TokenType.Subtract;
				else if ( word == "*" ) token.Type = TokenType.Multiply;
				else if ( word == "/" ) token.Type = TokenType.Divide;
				else if ( word == "pi" ) token = Token.Literal( float.Pi );
				else if ( word == "e" ) token = Token.Literal( float.E );
				else if ( word == "(" || word == ")" ) continue; // Skip parentheses
				else throw new( $"Invalid token '{word}'" );

				tokens.Add( token );
			}
		}

		return tokens;
	}
}
