using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Multiplies a child pose's playback speed and adjusts its reported duration.
/// <br/><br/><b>Use when:</b>&#160;Dynamically speeding up, slowing down, or freezing a pose.
/// <br/><b>Important:</b>&#160;Negative speed values are treated as zero.
/// </summary>
[Title( "Speed Scale" ), Group( "Animation" ), Icon( "speed" )]
public class SpeedScaleNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Source { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Speed { get; set; }

	[Property]
	public float DefaultSpeed { get; set; } = 1.0f;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Source, "Speed Scale" );
		var speed = compiler.EmitValue( this, Speed );
		if ( speed is null )
			speed = compiler.ConstFloat( DefaultSpeed );
		return compiler.Builder.SpeedScale( child, speed );
	}

}
