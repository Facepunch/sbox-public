using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Applies rotation and position offsets to a target, optionally evaluating those offsets in bone space.
/// <br/><br/><b>Use when:</b>&#160;Adjusting a bone target to a grip, contact point, attachment, or other offset transform.
/// <br/><b>Important:</b>&#160;The target input is required.
/// </summary>
[Title( "Target Offset" ), Group( "Values" ), Icon( "open_in_full" )]
public class TargetOffsetNode : Node
{
	[Input( typeof( TargetLink ) )]
	public NodeInput Target { get; set; }

	[Input( typeof( VectorLink ) )]
	public NodeInput Translation { get; set; }

	[Property]
	public bool IsBoneSpaceOffset { get; set; } = true;

	[Property]
	public Rotation RotationOffset { get; set; } = Rotation.Identity;

	[Property]
	public Vector3 TranslationOffset { get; set; }

	[Output( typeof( TargetLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Target );
		if ( input is null )
		{
			compiler.Warn( "Target Offset has no Target connected" );
			return compiler.Builder.UnsetTarget();
		}

		return compiler.Builder.TargetOffset( input, TranslationOffset, RotationOffset, IsBoneSpaceOffset, compiler.EmitValue( this, Translation ) );
	}

}
