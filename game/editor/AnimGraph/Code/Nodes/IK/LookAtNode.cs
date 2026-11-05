using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

[Title( "Look At" ), Group( "IK" ), Icon( "visibility" )]
[Description( "Aims a weighted bone chain using Source 2's LookAt solver. Pitch and yaw are model-space angles; chain bones are ordered from the end bone toward the root. Reference angles define forward relative to the reference bone." )]
public class LookAtNode : Node
{
	public sealed class ChainBone
	{
		[Property, Editor( "animgraph.bone" )]
		public string Bone { get; set; } = "";

		[Property, Range( 0, 1 )]
		public float Weight { get; set; } = 1;
	}

	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Pitch { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Yaw { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Weight { get; set; }

	[Property, Editor( "animgraph.bone" )]
	public string ReferenceBone { get; set; } = "";

	[Property]
	public Angles ReferenceAngles { get; set; }

	[Property]
	public List<ChainBone> Bones { get; set; } = [];

	[Property, Range( 0, 180 )]
	public float YawLimit { get; set; } = 140;

	[Property, Range( 0, 180 )]
	public float PitchLimit { get; set; } = 140;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#56b6c2" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Look At" );
		var pitch = compiler.EmitValue( this, Pitch );
		var yaw = compiler.EmitValue( this, Yaw );
		if ( pitch is null || yaw is null || string.IsNullOrWhiteSpace( ReferenceBone ) || Bones.Count == 0 )
		{
			compiler.Warn( "Look At requires pitch/yaw, a reference bone and a bone chain" );
			return child;
		}
		return compiler.Builder.LookAt( child, pitch, yaw, new GraphBuilder.LookAtSettings
		{
			ReferenceBone = ReferenceBone,
			ReferenceRotation = Rotation.From( ReferenceAngles ),
			Bones = Bones.Select( bone => new GraphBuilder.LookAtBone( bone.Bone, bone.Weight ) ).ToArray(),
			Weight = compiler.EmitValue( this, Weight ),
			YawLimit = YawLimit,
			PitchLimit = PitchLimit
		} );
	}

}
