using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Caches a boolean value for the node's active lifetime.
/// <br/><br/><b>Use when:</b>&#160;Preserving a decision made on entry, or carrying its last active value into exit logic.
/// <br/><b>Important:</b>&#160;<b>On Entry</b> samples once during initialization. <b>On Exit</b> follows the live input while active, then locks the last value when the branch becomes inactive. A disconnected input warns and uses false.
/// </summary>
[Title( "Cached Bool" ), Group( "Values" ), Icon( "cached" )]
public class CachedBoolNode : CachedValueNode
{
	[Input( typeof( BoolLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached Bool has no Value connected" );
			source = compiler.ConstBool( false );
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}

}
