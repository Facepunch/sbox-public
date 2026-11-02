using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs a constant float value that can differ per graph variation.
/// <br/><br/><b>Use when:</b>&#160;A tuning value needs a different constant per variation, such as a per-character speed or reach.
/// <br/><b>Important:</b>&#160;Variations without their own override inherit the nearest parent variation's value.
/// </summary>
[Title( "Variation Float" ), Group( "Values" ), Icon( "looks_one" )]
public class VariationFloatNode : VariationDataNode<VariationFloatNode.Data>
{
	/// <summary>
	/// The parts of a variation float node that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		[Property]
		public float Value { get; set; }
	}

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#c678dd" ).Value;

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = DisplayInfo.For( this );
			info.Name = $"Variation Float: {((Data)SelectedVariationData).Value:0.###}";
			return info;
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );
		return compiler.Builder.Constant( data.Value );
	}

}
