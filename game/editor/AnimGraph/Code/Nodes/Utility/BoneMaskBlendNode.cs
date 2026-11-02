using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Blends from the Source bone mask to the Target bone mask using a float weight.
/// <br/><br/><b>Use when:</b>&#160;Smoothly changing which bones or mask weights affect a layered animation.
/// <br/><b>Important:</b>&#160;Weights at or below 0 return Source; weights at or above 1 return Target.
/// </summary>
[Title( "Bone Mask Blend" ), Group( "Bone Mask" ), Icon( "blender" )]
public class BoneMaskBlendNode : Node
{
	[Input( typeof( BoneMaskLink ) )]
	public NodeInput Source { get; set; }

	[Input( typeof( BoneMaskLink ) )]
	public NodeInput Target { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Weight { get; set; }

	[Property, Range( 0, 1 )]
	public float DefaultWeight { get; set; } = 0.5f;

	[Output( typeof( BoneMaskLink ) ), Title( "Bone Mask" )]
	[JsonIgnore]
	public object Mask => default;

	public override Color PrimaryColor => Color.Parse( "#d19a66" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitMask( this, Source );
		var target = compiler.EmitMask( this, Target );
		var weight = compiler.EmitValue( this, Weight );
		if ( weight is null )
			weight = compiler.ConstFloat( DefaultWeight );
		return compiler.Builder.BoneMaskBlend( source, target, weight );
	}

}
