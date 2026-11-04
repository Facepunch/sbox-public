namespace Editor.AnimGraph.Nodes;

[Hide, Title( "Reroute" ), Icon( "route" )]
public sealed class RerouteNode : Node, IRerouteNode
{
	public string Comment { get; set; }

	[Input, Hide, Title( "" )]
	public NodeInput Input { get; set; }

	[Output, Hide, Title( "" ), JsonIgnore]
	public object Result => default;

	[JsonIgnore]
	internal IPlugOut SourceOutput
	{
		get
		{
			if ( Graph is not Data graph )
				return null;

			var input = Input;
			for ( var remaining = graph.Nodes.Count(); remaining > 0 && input.IsValid; remaining-- )
			{
				if ( graph.FindNode( input.Identifier ) is not RerouteNode reroute )
					return GetConnectedOutput( input );

				if ( input.Output != nameof( Result ) )
					return null;

				input = reroute.Input;
			}

			return null;
		}
	}

	public override GraphValueKind OutputKind => SourceOutput?.Node is Node node ? node.OutputKind : GraphValueKind.Unknown;

	public override NodeUI CreateUI( GraphView view ) => new RerouteUI( view, this );

	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( GetConnectedOutput( Input )?.Node is not Node source )
		{
			compiler.Error( "Reroute has no input connected", this );
			return compiler.ReferencePose();
		}

		return compiler.Emit( source );
	}
}
