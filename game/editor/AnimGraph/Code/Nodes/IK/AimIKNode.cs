using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

[Title( "Aim At" ), Group( "IK" ), Icon( "my_location" )]
[Description( "Aims a weapon bone at a world-space point using weaponTip and weaponEnd child bones. Follow with Two Bone IK nodes targeting hand helpers parented to the weapon bone." )]
public class AimIKNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( VectorLink ) ), Title( "World Target" )]
	public NodeInput WorldTarget { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#56b6c2" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Aim At" );
		var target = compiler.EmitValue( this, WorldTarget );
		if ( target is null )
		{
			compiler.Warn( "Aim At has no world target connected" );
			return child;
		}

		return compiler.Builder.AimAt( child, target );
	}

}
