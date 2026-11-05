using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Applies two-bone IK to configured left and right foot bones using independent target transforms, with optional enable control and timed blending.
/// <br/><br/><b>Use when:</b>&#160;Positioning one or both feet against runtime targets while preserving an animated input pose.
/// <br/><b>Important:</b>&#160;Both foot bone names are required, but either target may be omitted to operate on only the other foot. <b>Effector</b> blends the target before solving; <b>Pose</b> blends the pre-IK and post-IK poses at greater cost.
/// </summary>
[Title( "Foot IK" ), Group( "IK" ), Icon( "do_not_step" )]
public class FootIKNode : VariationDataNode<FootIKNode.Data>
{
	/// <summary>
	/// The parts of a foot IK node that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		[Property, Editor( "animgraph.bone" )]
		public string LeftFootBone { get; set; } = "";

		[Property, Editor( "animgraph.bone" )]
		public string RightFootBone { get; set; } = "";

		/// <summary> The amount of time to blend IK in or out. </summary>
		[Property]
		public float BlendTime { get; set; }

		public override VisualState GetVisualState()
		{
			return string.IsNullOrWhiteSpace( LeftFootBone ) || string.IsNullOrWhiteSpace( RightFootBone )
				? VisualState.HasUnsetData
				: VisualState.None;
		}
	}

	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( TargetLink ) ), Title( "Left Foot Target" )]
	public NodeInput LeftFootTarget { get; set; }

	[Input( typeof( TargetLink ) ), Title( "Right Foot Target" )]
	public NodeInput RightFootTarget { get; set; }

	[Input( typeof( BoolLink ) )]
	public NodeInput Enable { get; set; }

	[Property]
	public GraphBuilder.IKBlendMode BlendMode { get; set; } = GraphBuilder.IKBlendMode.Effector;

	[Property]
	public bool IsTargetInWorldSpace { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#56b6c2" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Foot IK" );
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( string.IsNullOrWhiteSpace( data.LeftFootBone ) || string.IsNullOrWhiteSpace( data.RightFootBone ) )
		{
			compiler.Warn( "Foot IK has no foot bones set" );
			return child;
		}

		var left = compiler.EmitValue( this, LeftFootTarget );
		var right = compiler.EmitValue( this, RightFootTarget );

		if ( left is null && right is null )
		{
			compiler.Warn( "Foot IK has no foot targets connected" );
			return child;
		}

		var enabled = compiler.EmitValue( this, Enable );
		return compiler.Builder.FootIK(
			child,
			data.LeftFootBone,
			data.RightFootBone,
			new GraphBuilder.FootIKSettings
			{
				LeftTarget = left,
				RightTarget = right,
				Enabled = enabled,
				BlendTime = MathF.Max( data.BlendTime, 0 ),
				BlendMode = BlendMode,
				WorldSpace = IsTargetInWorldSpace,
			} );
	}

}
