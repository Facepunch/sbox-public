using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Applies the selected arithmetic, rounding, sign, or fractional operation to A and, where required, connected B or constant <b>Value B</b>.
/// <br/><br/><b>Use when:</b>&#160;Deriving a numeric control value directly inside the graph.
/// <br/><b>Important:</b>&#160;Divide or modulo by a value near zero returns 0. Absolute Result is applied before Negate Result.
/// </summary>
[Title( "Float Math" ), Group( "Values" ), Icon( "calculate" )]
public class FloatMathNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput A { get; set; }

	/// <summary>
	/// Optional second operand. When unconnected, Value B is used.
	/// </summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput B { get; set; }

	[Property]
	public GraphBuilder.FloatMathOperation Operator { get; set; } = GraphBuilder.FloatMathOperation.Add;

	/// <summary>
	/// Constant second operand when B isn't connected.
	/// </summary>
	[Property]
	public float ValueB { get; set; }

	/// <summary>
	/// Return the absolute value of the result.
	/// </summary>
	[Property]
	public bool AbsoluteResult { get; set; }

	/// <summary>
	/// Negate the result.
	/// </summary>
	[Property]
	public bool NegateResult { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var a = compiler.EmitValue( this, A );
		if ( a is null )
			a = compiler.ConstFloat( 0.0f );
		var b = compiler.EmitValue( this, B );
		return compiler.Builder.FloatMath( Operator, a, new GraphBuilder.FloatMathSettings
		{
			B = b,
			ValueB = ValueB,
			AbsoluteResult = AbsoluteResult,
			NegateResult = NegateResult,
		} );
	}

}
