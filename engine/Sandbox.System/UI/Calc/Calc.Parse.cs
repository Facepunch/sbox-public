namespace Sandbox.UI;

partial class Calc
{
	private enum TreeNodeType
	{
		Literal,
		Expression
	};

	private struct TreeNode
	{
		public TreeNodeType Type;
		public Length Value;

		public List<TreeNode> Children;

		public TreeNode()
		{
			Children = new();
		}
	}

	private static float Parse( List<Token> tokens, float dimension )
	{
		Stack<TreeNode> operands = new();
		Stack<TokenType> operators = new();

		foreach ( var token in tokens )
		{
			if ( token.Type == TokenType.Literal )
			{
				TreeNode node = new()
				{
					Type = TreeNodeType.Literal,
					Value = token.Value.Value
				};

				operands.Push( node );
			}
			else
			{
				while ( operators.Count > 0 && HasHigherPrecedence( operators.Peek(), token.Type ) )
				{
					ProcessOperation( operands, operators.Pop(), dimension );
				}

				operators.Push( token.Type );
			}
		}

		while ( operators.Count > 0 )
			ProcessOperation( operands, operators.Pop(), dimension );

		if ( operands.Count != 1 )
			throw new Exception( "Invalid expression" );

		return operands.Pop().Value.GetScaledPixels( dimension );
	}
}
