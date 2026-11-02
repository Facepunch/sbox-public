using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Solves a two-bone chain by rotating the effector bone's two parents so the effector reaches and aligns to the supplied target.
/// <br/><br/><b>Use when:</b>&#160;Placing an end bone such as a hand or foot at a runtime target.
/// <br/><b>Important:</b>&#160;The effector bone and target are required. <b>Effector</b> blends the target before solving; <b>Pose</b> blends the pre-IK and post-IK poses at greater cost.
/// </summary>
[Title( "Two Bone IK" ), Group( "IK" ), Icon( "pan_tool_alt" )]
public class TwoBoneIKNode : VariationDataNode<TwoBoneIKNode.Data>
{
	/// <summary>
	/// The parts of a two bone IK node that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		/// <summary> The effector bone. </summary>
		[Property, Editor( "animgraph.bone" )]
		public string EffectorBone { get; set; } = "";

		/// <summary> The amount of time to blend IK in or out. </summary>
		[Property]
		public float BlendTime { get; set; }

		public override VisualState GetVisualState()
		{
			return string.IsNullOrWhiteSpace( EffectorBone ) ? VisualState.HasUnsetData : VisualState.None;
		}
	}

	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( TargetLink ) ), Title( "Effector Target" )]
	public NodeInput EffectorTarget { get; set; }

	[Input( typeof( BoolLink ) )]
	public NodeInput Enable { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Weight { get; set; }

	[Property]
	public GraphBuilder.IKBlendMode BlendMode { get; set; } = GraphBuilder.IKBlendMode.Effector;

	[Property]
	public bool IsTargetInWorldSpace { get; set; }

	[Property, Range( 0, 1 ), JsonPropertyName( "ReferencePoseTwistWeight" )]
	public float ChainRotationWeight { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#56b6c2" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Two Bone IK" );
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( string.IsNullOrWhiteSpace( data.EffectorBone ) )
		{
			compiler.Warn( "Two Bone IK has no effector bone set" );
			return child;
		}

		var target = compiler.EmitValue( this, EffectorTarget );
		if ( target is null )
		{
			compiler.Warn( "Two Bone IK has no effector target connected" );
			return child;
		}

		var enabled = compiler.EmitValue( this, Enable );
		var weight = compiler.EmitValue( this, Weight );
		return compiler.Builder.TwoBoneIK(
			child,
			data.EffectorBone,
			target,
			new GraphBuilder.TwoBoneIKSettings
			{
				Enabled = enabled,
				Weight = weight,
				BlendTime = MathF.Max( data.BlendTime, 0 ),
				WorldSpace = IsTargetInWorldSpace,
				BlendMode = BlendMode,
				ChainRotationWeight = ChainRotationWeight,
			} );
	}

}
