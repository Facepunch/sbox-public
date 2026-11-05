using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Caches a vector for the node's active lifetime.
/// <br/><br/><b>Use when:</b>&#160;Preserving an entry-time direction or position, or carrying the last active vector into exit logic.
/// <br/><b>Important:</b>&#160;<b>On Entry</b> samples once during initialization. <b>On Exit</b> follows the live input while active, then locks the last vector when the branch becomes inactive. A disconnected input warns and uses the zero vector.
/// </summary>
[Title( "Cached Vector" ), Group( "Values" ), Icon( "cached" )]
public class CachedVectorNode : CachedValueNode
{
	[Input( typeof( VectorLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( VectorLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached Vector has no Value connected" );
			source = compiler.ConstVector( Vector3.Zero );
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}

}
