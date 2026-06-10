namespace Sandbox.UI;

static partial class Calc
{
	/// <summary>
	/// Determine which token has higher precedence over another
	/// </summary>
	private static bool HasHigherPrecedence( TokenType a, TokenType b )
	{
		if ( a == TokenType.Multiply || a == TokenType.Divide ) return true;
		if ( b == TokenType.Add || b == TokenType.Subtract ) return true;

		return false;
	}

	/// <summary>
	/// Process a bunch of tree nodes
	/// </summary>
	private static void ProcessOperation( Stack<TreeNode> operands, TokenType operation, float dimension )
	{
		if ( operands.Count < 2 )
			throw new Exception( "Not enough operands for operation" );

		TreeNode right = operands.Pop();
		TreeNode left = operands.Pop();

		if ( operation == TokenType.Divide && right.Value == 0 )
			throw new DivideByZeroException();

		TreeNode result = new()
		{
			Type = TreeNodeType.Expression,
			Value = new()
			{
				// don't use LengthUnit.Pixels to avoid multiple scaling
				Unit = LengthUnit.Auto,
				Value = operation switch
				{
					TokenType.Add => left.Value.GetScaledPixels( dimension ) + right.Value.GetScaledPixels( dimension ),
					TokenType.Subtract => left.Value.GetScaledPixels( dimension ) - right.Value.GetScaledPixels( dimension ),
					TokenType.Multiply => left.Value.GetScaledPixels( dimension ) * right.Value.GetPixels( dimension ),
					TokenType.Divide => left.Value.GetScaledPixels( dimension ) / right.Value.GetPixels( dimension ),
					_ => throw new Exception( "Invalid operation" )
				}
			}
		};

		operands.Push( result );
	}

	/// <summary>
	/// Evaluate a full CSS calc/min/max/clamp expression and return the calculated value.
	/// </summary>
	public static float Evaluate( string expression, float dimension = 1.0f )
	{
		expression = expression.Trim();

		// min()/max()/clamp() are function calls rather than arithmetic - handle them here (recursively,
		// so each argument can itself be a length, percentage, calc() or another function).
		if ( TryEvaluateFunction( expression, dimension, out var functionResult ) )
			return functionResult;

		var tokens = Tokenize( expression );
		var value = Parse( tokens, dimension );

		return value;
	}

	/// <summary>
	/// Evaluates a top-level min()/max()/clamp() call. Returns false if the whole expression isn't one.
	/// </summary>
	private static bool TryEvaluateFunction( string expression, float dimension, out float result )
	{
		result = 0;

		string fn = null;
		if ( expression.StartsWith( "min(", StringComparison.OrdinalIgnoreCase ) ) fn = "min";
		else if ( expression.StartsWith( "max(", StringComparison.OrdinalIgnoreCase ) ) fn = "max";
		else if ( expression.StartsWith( "clamp(", StringComparison.OrdinalIgnoreCase ) ) fn = "clamp";
		else return false;

		int open = expression.IndexOf( '(' );

		// Find the close paren matching the function's opening bracket.
		int depth = 0, close = -1;
		for ( int i = open; i < expression.Length; i++ )
		{
			if ( expression[i] == '(' ) depth++;
			else if ( expression[i] == ')' && --depth == 0 ) { close = i; break; }
		}

		// Only handle the case where the whole expression is this single function call.
		if ( close < 0 || expression.Substring( close + 1 ).Trim().Length > 0 )
			return false;

		var args = SplitTopLevelArgs( expression.Substring( open + 1, close - open - 1 ) );

		if ( fn == "clamp" )
		{
			if ( args.Count != 3 ) return false;

			float min = Evaluate( args[0], dimension );
			float val = Evaluate( args[1], dimension );
			float max = Evaluate( args[2], dimension );
			result = MathF.Max( min, MathF.Min( val, max ) );
			return true;
		}

		if ( args.Count < 1 ) return false;

		result = Evaluate( args[0], dimension );
		for ( int i = 1; i < args.Count; i++ )
		{
			float v = Evaluate( args[i], dimension );
			result = fn == "min" ? MathF.Min( result, v ) : MathF.Max( result, v );
		}

		return true;
	}

	/// <summary>
	/// Splits comma-separated function arguments, respecting nested parentheses.
	/// </summary>
	private static List<string> SplitTopLevelArgs( string inner )
	{
		var parts = new List<string>();
		int depth = 0, start = 0;

		for ( int i = 0; i < inner.Length; i++ )
		{
			char c = inner[i];
			if ( c == '(' ) depth++;
			else if ( c == ')' ) depth--;
			else if ( c == ',' && depth == 0 ) { parts.Add( inner.Substring( start, i - start ) ); start = i + 1; }
		}

		parts.Add( inner.Substring( start ) );
		return parts;
	}
}
