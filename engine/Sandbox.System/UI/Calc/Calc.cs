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
	/// Evaluate a full CSS calc expression and return the calculated value.
	/// </summary>
	public static float Evaluate( string expression, float dimension = 1.0f )
	{
		var tokens = Tokenize( expression );
		var value = Parse( tokens, dimension );

		return value;
	}
}
