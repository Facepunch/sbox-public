namespace Editor.ShaderGraph.Nodes;

public abstract class BooleanLogic : ShaderNode
{
	[Input( typeof( bool ) )]
	[Hide]
	[Title( "A" )]
	public NodeInput A { get; set; }

	[Input( typeof( bool ) )]
	[Hide]
	[Title( "B" )]
	public NodeInput B { get; set; }

	[InputDefault( nameof( A ) )]
	public bool DefaultA { get; set; } = false;

	[InputDefault( nameof( B ) )]
	public bool DefaultB { get; set; } = true;

	[Hide]
	protected abstract OperatorType Op { get; }

	public enum OperatorType
	{
		AND,
		NAND,
		OR,
	}

	public BooleanLogic() : base()
	{
		ExpandSize = new Vector2( -48, 5 );
	}

	[Output( typeof( bool ) )]
	[Hide]
	[Title( "Result" )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var resultA = compiler.ResultOrDefault( A, DefaultA );
		var resultB = compiler.ResultOrDefault( B, DefaultB );

		string result = Op switch
		{
			OperatorType.AND => $"{resultA} && {resultA}",
			OperatorType.NAND => $"!{resultA} && !{resultA}",
			OperatorType.OR => $"{resultA} || {resultA}",
			_ => throw new NotImplementedException(),
		};

		return new NodeResult( NodeResultType.Bool, result );
	};

	[JsonIgnore, Hide, Browsable( false )]
	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = base.DisplayInfo;
			info.Icon = null;
			return info;
		}
	}
}

[Title( "And" ), Category( "Logic" )]
public sealed class ANDNode : BooleanLogic
{
	protected override OperatorType Op => OperatorType.AND;
}

[Title( "Nand" ), Category( "Logic" )]
public sealed class NANDNode : BooleanLogic
{
	protected override OperatorType Op => OperatorType.NAND;
}

[Title( "Not" ), Category( "Logic" )]
public sealed class NOTNode : ShaderNode
{
	[Input( typeof( bool ) )]
	[Hide]
	[Title( "A" )]
	public NodeInput A { get; set; }

	[InputDefault( nameof( A ) )]
	public bool DefaultA { get; set; } = false;

	public NOTNode() : base()
	{
		ExpandSize = new Vector2( -48, 5 );
	}

	[Output( typeof( bool ) )]
	[Hide]
	[Title( "Result" )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var result = compiler.ResultOrDefault( A, DefaultA );

		return new NodeResult( NodeResultType.Bool, $"!{result}" );
	};

}

[Title( "OR" ), Category( "Logic" )]
public sealed class ORNode : BooleanLogic
{
	protected override OperatorType Op => OperatorType.OR;
}
