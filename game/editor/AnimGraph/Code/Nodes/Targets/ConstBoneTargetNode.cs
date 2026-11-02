using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Produces an always-set target that refers to a character bone instead of storing an explicit transform.
/// <br/><br/><b>Use when:</b>&#160;Passing a character bone through a Target connection.
/// <br/><b>Important:</b>&#160;An empty bone name produces a warning and an unset target.
/// </summary>
[Title( "Bone Target" ), Group( "Values" ), Icon( "gps_fixed" )]
public class ConstBoneTargetNode : Node
{
	[Property, Editor( "animgraph.bone" )]
	public string Bone { get; set; } = "";

	[Output( typeof( TargetLink ) )]
	[JsonIgnore]
	public object Target => default;

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = DisplayInfo.For( this );
			if ( !string.IsNullOrWhiteSpace( Bone ) )
				info.Name = $"Bone Target: {Bone}";
			return info;
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( string.IsNullOrWhiteSpace( Bone ) )
		{
			compiler.Warn( "Bone Target has no bone set" );
			return compiler.Builder.UnsetTarget();
		}

		return compiler.Builder.BoneTarget( Bone );
	}

}
