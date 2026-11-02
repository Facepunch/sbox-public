using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Redirects an animation clip's root-motion facing toward a character-space direction or by an angle offset relative to the character or original motion.
/// <br/><br/><b>Use when:</b>&#160;Turning authored root-motion movement without replacing its translation.
/// <br/><b>Important:</b>&#160;The source must be an Animation Clip, and exactly one of <b>Direction</b> or <b>Angle Offset</b> must be connected. Invalid configurations pass the input pose through unchanged.
/// </summary>
[Title( "Orientation Warp" ), Group( "Root Motion" ), Icon( "explore" )]
public class OrientationWarpNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( VectorLink ) ), Title( "Direction (Character)" )]
	public NodeInput Direction { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Angle Offset (Deg)" )]
	public NodeInput AngleOffset { get; set; }

	public enum OffsetType
	{
		[Title( "Relative To Character" )] RelativeToCharacter,
		[Title( "Relative To Original Root Motion" )] RelativeToOriginalRootMotion,
	}

	/// <summary> The coordinate space of the supplied offset </summary>
	[Property]
	public OffsetType Offset { get; set; } = OffsetType.RelativeToCharacter;

	/// <summary> The sampling mode for the warped motion </summary>
	[Property]
	public GraphBuilder.RootMotionSamplingMode SamplingMode { get; set; } = GraphBuilder.RootMotionSamplingMode.WorldSpace;

	[Property]
	public GraphBuilder.OrientationWarpAlignmentMode AlignmentMode { get; set; } = GraphBuilder.OrientationWarpAlignmentMode.MovementDirection;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Orientation Warp" );

		if ( !child.IsClip() )
		{
			compiler.Warn( "Orientation Warp input must be an animation clip" );
			return child;
		}

		var direction = compiler.EmitValue( this, Direction );
		var offset = compiler.EmitValue( this, AngleOffset );

		if ( direction is not null && offset is not null )
		{
			compiler.Warn( "Orientation Warp cannot have both the direction and the offset inputs set, these are mutually exclusive" );
			return child;
		}

		var target = direction ?? offset;
		if ( target is null )
		{
			compiler.Warn( "Orientation Warp needs either the direction or the offset input connected" );
			return child;
		}

		return compiler.Builder.OrientationWarp(
			 child, target,
			Offset == OffsetType.RelativeToCharacter,
			SamplingMode, AlignmentMode );
	}

}
