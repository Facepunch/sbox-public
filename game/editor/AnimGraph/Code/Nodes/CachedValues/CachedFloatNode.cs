using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Caches a float value for the node's active lifetime.
/// <br/><br/><b>Use when:</b>&#160;Preserving an entry-time number, or carrying the last active number into exit logic.
/// <br/><b>Important:</b>&#160;<b>On Entry</b> samples once during initialization. <b>On Exit</b> follows the live input while active, then locks the last value when the branch becomes inactive. A disconnected input warns and uses 0.
/// </summary>
[Title( "Cached Float" ), Group( "Values" ), Icon( "cached" )]
public class CachedFloatNode : CachedValueNode
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached Float has no Value connected" );
			source = compiler.ConstFloat( 0.0f );
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}

}
