using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Produces an always-set target containing a fixed rotation and position.
/// <br/><br/><b>Use when:</b>&#160;Supplying a constant transform to IK, target warping, or another target-consuming node.
/// </summary>
[Title( "Transform Target" ), Group( "Values" ), Icon( "gps_fixed" )]
public class ConstTargetNode : Node
{
	[Property]
	public Rotation Rotation { get; set; } = Rotation.Identity;

	[Property, Title( "Position" )]
	public Vector3 Translation { get; set; }

	[Output( typeof( TargetLink ) )]
	[JsonIgnore]
	public object Target => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
		=> compiler.Builder.ConstantTarget( Translation, Rotation );

}
