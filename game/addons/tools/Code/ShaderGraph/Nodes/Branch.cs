
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// If True, do this, if False, do that.
/// </summary>
[Title( "Branch" ), Category( "Logic" ), Icon( "alt_route" )]
public sealed class Branch : ShaderNode
{
	[Input( typeof( bool ) ), Hide]
	public NodeInput Predicate { get; set; }

	[Input, Hide]
	public NodeInput True { get; set; }

	[Input, Hide]
	public NodeInput False { get; set; }

	[Title( "Default Predicate" )]
	[InputDefault( nameof( Predicate ) )]
	public bool Enabled { get; set; }

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var resultPredicate = compiler.ResultOrDefault( Predicate, Enabled );
		var results = compiler.Result( True, False, 0.0f, 0.0f );

		return new NodeResult( results.Item1.ResultType, $"{(Predicate.IsValid ? $"{resultPredicate}" : $"{resultPredicate}".ToLower())} ? {results.Item1} : {results.Item2}" );
	};
}

/// <summary>
/// Compare Input 'A' with Input 'B' and output the input from either 'True' or 'False' based on the result of the comparison.
/// </summary>
[Title( "Comparison" ), Category( "Logic" ), Icon( "compare" )]
public sealed class Comparison : ShaderNode
{
	[Hide]
	public override string Title => $"{DisplayInfo.For( this ).Name} (A {Op} B)";

	[Input, Hide]
	public NodeInput True { get; set; }

	[Input, Hide]
	public NodeInput False { get; set; }

	[Input, Hide]
	public NodeInput A { get; set; }

	[Input, Hide]
	public NodeInput B { get; set; }

	public enum OperatorType
	{
		Equal,
		NotEqual,
		GreaterThan,
		LessThan,
		GreaterThanOrEqual,
		LessThanOrEqual
	}

	public OperatorType Operator { get; set; }

	[Hide]
	private string Op
	{
		get
		{
			return Operator switch
			{
				OperatorType.Equal => "==",
				OperatorType.NotEqual => "!=",
				OperatorType.GreaterThan => ">",
				OperatorType.LessThan => "<",
				OperatorType.GreaterThanOrEqual => ">=",
				OperatorType.LessThanOrEqual => "<=",
				_ => throw new NotImplementedException(),
			};
		}
	}

	[Output( typeof( bool ) ), Title( "Predicate" )]
	[Description( "Either 'true' or 'false' depending on the result of the comparison with Input 'A' and Input 'B'." )]
	[Hide]
	public NodeResult.Func BoolResult => ( GraphCompiler compiler ) =>
	{
		var results = compiler.Result( True, False, 0.0f, 0.0f );
		var resultA = compiler.ResultOrDefault( A, 0.0f );
		var resultB = compiler.ResultOrDefault( B, 0.0f );

		return new NodeResult( NodeResultType.Bool, $"{resultA} {Op} {resultB}" );
	};

	[Output, Title( "Result" )]
	[Description( "Result from either the 'True' or 'False' inputs depending on the result of the comparison with Input 'A' and Input 'B'." )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var results = compiler.Result( True, False, 0.0f, 0.0f );
		var resultA = compiler.ResultOrDefault( A, 0.0f );
		var resultB = compiler.ResultOrDefault( B, 0.0f );

		return new NodeResult( results.Item1.ResultType, $"{resultA.Cast( 1 )} {Op} {resultB.Cast( 1 )}" );
	};
}
