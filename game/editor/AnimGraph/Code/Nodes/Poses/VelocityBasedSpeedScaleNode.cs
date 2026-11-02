using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Scales an Animation Clip's playback speed so its average root-motion velocity matches a desired velocity.
/// <br/><br/><b>Use when:</b>&#160;Matching locomotion playback to actual movement speed.
/// <br/><b>Important:</b>&#160;The source must be an Animation Clip. Negative desired velocities become zero, and a clip with zero average velocity is frozen.
/// </summary>
[Title( "Velocity Based Speed Scale" ), Group( "Animation" ), Icon( "directions_run" )]
public class VelocityBasedSpeedScaleNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Source { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Desired Velocity" )]
	public NodeInput DesiredVelocity { get; set; }

	/// <summary>
	/// Velocity used when the input is not connected. Must be positive.
	/// </summary>
	[Property]
	public float DefaultVelocity { get; set; } = 1.0f;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Source, "Velocity Based Speed Scale" );
		if ( !child.IsClip() )
		{
			compiler.Warn( "Velocity Based Speed Scale's input must be an Animation Clip" );
			return child;
		}

		var input = compiler.EmitValue( this, DesiredVelocity );
		if ( input is null && DefaultVelocity <= 0.0f )
		{
			compiler.Warn( "Velocity Based Speed Scale requires a positive default velocity when the input is not connected" );
			return child;
		}

		return compiler.Builder.VelocityScale(
			 child,
			 input,
			DefaultVelocity );
	}

}
