namespace Editor.AnimGraph.Nodes;

[Title( "Pose Group" ), Group( "Graphs" ), Icon( "folder" )]
[AvailableIn( GraphKind.BlendTree )]
public sealed class PoseGroupNode : Node
{
	[Property]
	public string Name { get; set; } = "Pose Group";

	[Hide]
	public Data Contents { get; set; } = Data.CreateSubGraph<PoseResultNode>();

	[Output( typeof( PoseLink ) ), JsonIgnore]
	public object Result => default;

	public override IEnumerable<Data> SubGraphs => [Contents];

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = base.DisplayInfo;
			info.Name = Name;
			return info;
		}
	}

	private readonly Dictionary<GroupInputNode, AnimPlugIn> _inputs = [];

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			var inputs = Contents.Nodes.OfType<GroupInputNode>().ToArray();
			foreach ( var removed in _inputs.Keys.Except( inputs ).ToArray() )
				_inputs.Remove( removed );

			foreach ( var input in inputs )
			{
				if ( !_inputs.TryGetValue( input, out var plug ) || plug.Identifier != input.Identifier || plug.Type != input.LinkType )
				{
					var info = new AnimPlugInfo( input.Identifier, () => input.Name, _ => input.Source, ( _, source ) => input.Source = source );
					_inputs[input] = plug = new AnimPlugIn( this, info, input.LinkType );
				}

				yield return plug;
			}
		}
	}

	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var output = Contents.Nodes.OfType<PoseResultNode>().SingleOrDefault();
		if ( output is null )
		{
			compiler.Error( $"Group '{Name}' has no pose output", this );
			return compiler.ReferencePose();
		}

		return compiler.EmitPose( output, output.Pose, Name );
	}
}
