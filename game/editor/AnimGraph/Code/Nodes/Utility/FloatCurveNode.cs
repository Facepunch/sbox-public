using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Evaluates the configured curve at the input float and outputs the interpolated curve value.
/// <br/><br/><b>Use when:</b>&#160;Applying a custom nonlinear response or authored remapping to a numeric parameter.
/// <br/><b>Important:</b>&#160;A disconnected input evaluates the curve at 0.
/// </summary>
[Title( "Curve" ), Group( "Values" ), Icon( "show_chart" )]
public class FloatCurveNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Property]
	public Curve Curve { get; set; } = new( new Curve.Frame( 0.0f, 0.0f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );
		(float Parameter, float Value, float InTangent, float OutTangent)[] points =
			Curve.Frames.IsDefaultOrEmpty
				? []
				: Curve.Frames
					.Select( frame => (frame.Time, frame.Value, frame.In, frame.Out) )
					.ToArray();

		return compiler.Builder.Curve( value, points );
	}

}
