using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Scales a child pose's playback speed so its reported duration matches a requested number of seconds.
/// <br/><br/><b>Use when:</b>&#160;Fitting an animation to a known time window.
/// <br/><b>Important:</b>&#160;An unconnected input requires a positive default duration. Negative driven durations are treated as zero.
/// </summary>
[Title( "Duration Scale" ), Group( "Animation" ), Icon( "timer" )]
public class DurationScaleNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Source { get; set; }

	[Input( typeof( FloatLink ) ), Title( "New Duration" )]
	public NodeInput Duration { get; set; }

	/// <summary>
	/// Duration in seconds used when the input is not connected. Must be positive.
	/// </summary>
	[Property]
	public float DefaultDuration { get; set; } = 1.0f;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Source, "Duration Scale" );
		var input = compiler.EmitValue( this, Duration );

		if ( input is null && DefaultDuration <= 0.0f )
		{
			compiler.Warn( "Duration Scale requires a positive default duration when the input is not connected" );
			return child;
		}

		return compiler.Builder.DurationScale(
			 child,
			 input,
			DefaultDuration );
	}

}
