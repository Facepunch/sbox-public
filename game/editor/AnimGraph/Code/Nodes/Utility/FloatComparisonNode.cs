using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Compares <b>Value</b> with the connected Comparand, or with <b>Comparison Value</b> when disconnected. Near-equal uses the configured Epsilon.
/// <br/><br/><b>Use when:</b>&#160;Converting a numeric threshold or approximate equality test into a boolean condition.
/// <br/><b>Important:</b>&#160;A disconnected Value uses 0.
/// </summary>
[Title( "Float Comparison" ), Group( "Values" ), Icon( "compare_arrows" )]
public class FloatComparisonNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	/// <summary>
	/// Optional float to compare against. When unconnected, Comparison Value is used.
	/// </summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput Comparand { get; set; }

	[Property]
	public GraphBuilder.FloatComparison Comparison { get; set; } = GraphBuilder.FloatComparison.GreaterThanEqual;

	/// <summary>
	/// Constant to compare against when no comparand is connected.
	/// </summary>
	[Property]
	public float ComparisonValue { get; set; }

	/// <summary>
	/// Tolerance used by the near-equal comparison.
	/// </summary>
	[Property]
	public float Epsilon { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );

		var comparand = compiler.EmitValue( this, Comparand );
		return comparand is not null
			? compiler.Builder.Compare( value, Comparison, comparand, Epsilon )
			: compiler.Builder.Compare( value, Comparison, ComparisonValue, Epsilon );
	}

}
