using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns the float from the first mapping whose ID matches the input, or <b>Default Value</b> when none match.
/// <br/><br/><b>Use when:</b>&#160;Translating named states into numeric controls or blend weights.
/// <br/><b>Important:</b>&#160;Missing mappings, blank mapping IDs, or a disconnected ID input produce a warning and return the default value.
/// </summary>
[Title( "ID To Float" ), Group( "Values" ), Icon( "swap_horiz" )]
public class IDToFloatNode : Node
{
	public class Mapping
	{
		[Property, KeyProperty]
		public string Id { get; set; } = "";

		[Property, KeyProperty]
		public float Value { get; set; }
	}

	[Input( typeof( IDLink ) )]
	public NodeInput Id { get; set; }

	[Property]
	public float DefaultValue { get; set; }

	[Property]
	public List<Mapping> Mappings { get; set; } = [];

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var mappings = Mappings ?? [];
		if ( mappings.Count == 0 )
		{
			compiler.Warn( "ID To Float has no mappings" );
			return compiler.ConstFloat( DefaultValue );
		}

		if ( mappings.Any( m => string.IsNullOrWhiteSpace( m.Id ) ) )
		{
			compiler.Warn( "ID To Float has a mapping with no ID" );
			return compiler.ConstFloat( DefaultValue );
		}

		var input = compiler.EmitValue( this, Id );
		if ( input is null )
		{
			compiler.Warn( "ID To Float has no ID input" );
			return compiler.ConstFloat( DefaultValue );
		}

		return compiler.Builder.IdToFloat( input, mappings.Select( m => (m.Id, m.Value) ), DefaultValue );
	}

}
