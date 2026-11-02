using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Warps an animation clip's root motion to reach a world-space transform using target-warp events authored on the clip.
/// <br/><br/><b>Use when:</b>&#160;Adapting authored movement so an action lands at a runtime target.
/// <br/><b>Important:</b>&#160;The source must be an Animation Clip with target-warp events, and <b>World Target</b> must be connected. Update rules control how target changes are handled while the warp is active.
/// </summary>
[Title( "Target Warp" ), Group( "Root Motion" ), Icon( "ads_click" )]
public class TargetWarpNode : VariationDataNode<TargetWarpNode.Data>
{
	public sealed class Data : VariationDataNode.VariationData
	{
		/// <summary> Which bone do we want to align with the target </summary>
		[Property, Editor( "animgraph.bone" )]
		public string AlignmentBone { get; set; } = "";
	}

	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( TargetLink ) ), Title( "World Target" )]
	public NodeInput WorldTarget { get; set; }

	/// <summary> Can this warp be updated when the target changes? </summary>
	[Property]
	public GraphBuilder.TargetWarpUpdate UpdateRule { get; set; } = GraphBuilder.TargetWarpUpdate.None;

	/// <summary> Do we want to be aligned with the warp target at the end of the last warp event? </summary>
	[Property]
	public bool AlignWithTargetAtLastWarpEvent { get; set; }

	/// <summary> The sampling mode for the warped motion </summary>
	[Property]
	public GraphBuilder.RootMotionSamplingMode SamplingMode { get; set; } = GraphBuilder.RootMotionSamplingMode.WorldSpace;

	/// <summary> The error threshold to exceed, when accurately sampling, before switching to inaccurate sampling </summary>
	[Property]
	public float SamplingPositionErrorThreshold { get; set; } = 0.05f;

	/// <summary> Length limit on generated warp curve tangents. The lower the length the lower the curvature. </summary>
	[Property]
	public float MaxTangentLength { get; set; } = 1.25f;

	/// <summary> The distance under which we fall back to lerping the translation instead of generating a curve </summary>
	[Property]
	public float LerpFallbackDistanceThreshold { get; set; } = 0.1f;

	/// <summary> The distance the target needs to move from the original target before we update the warp </summary>
	[Property]
	public float TargetUpdateDistanceThreshold { get; set; } = 0.1f;

	/// <summary> The angle (degrees) the target needs to rotate from the original target before we update the warp </summary>
	[Property]
	public float TargetUpdateAngleThreshold { get; set; } = 5.0f;

	[Hide, JsonIgnore]
	public string AlignmentBone
	{
		get => SelectedData.AlignmentBone;
		set => SelectedData.AlignmentBone = value;
	}

	[Hide, JsonPropertyName( "AlignmentBone" )]
	public string LegacyAlignmentBone
	{
		set => DefaultVariationData.AlignmentBone = value;
	}

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Target Warp" );
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( !child.IsClip() )
		{
			compiler.Warn( "Target Warp input must be an animation clip" );
			return child;
		}

		var target = compiler.EmitValue( this, WorldTarget );
		if ( target is null )
		{
			compiler.Warn( "Target Warp has no world target connected" );
			return child;
		}

		return compiler.Builder.TargetWarp(
			child,
			target,
			new GraphBuilder.TargetWarpSettings
			{
				UpdateRule = UpdateRule,
				SamplingMode = SamplingMode,
				AlignAtLastWarpEvent = AlignWithTargetAtLastWarpEvent,
				AlignmentBone = data.AlignmentBone,
				SamplingPositionErrorThreshold = SamplingPositionErrorThreshold,
				MaxTangentLength = MaxTangentLength,
				LerpFallbackDistance = LerpFallbackDistanceThreshold,
				TargetUpdateDistance = TargetUpdateDistanceThreshold,
				TargetUpdateAngleDegrees = TargetUpdateAngleThreshold,
			} );
	}

}
