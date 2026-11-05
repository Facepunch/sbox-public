namespace Editor.AnimGraph.Nodes;

[Title( "Group Input" ), Group( "Graphs" ), Icon( "input" )]
[AvailableIn( GraphKind.BlendTree )]
public sealed class GroupInputNode : Node
{
	[Property]
	public string Name { get; set; } = "Input";

	private GraphBuilder.NodeKind _valueType;

	[Property]
	public GraphBuilder.NodeKind ValueType
	{
		get => _valueType;
		set
		{
			if ( _valueType == value )
				return;
			_valueType = value;
			RefreshMetadata();
		}
	}

	[Hide]
	public NodeInput Source { get; set; }

	[JsonIgnore]
	internal Type LinkType => LinkTypeFor( ValueType );

	private static Type LinkTypeFor( GraphBuilder.NodeKind kind ) => kind switch
	{
		GraphBuilder.NodeKind.Pose => typeof( PoseLink ),
		GraphBuilder.NodeKind.Bool => typeof( BoolLink ),
		GraphBuilder.NodeKind.Float => typeof( FloatLink ),
		GraphBuilder.NodeKind.Id => typeof( IDLink ),
		GraphBuilder.NodeKind.Vector => typeof( VectorLink ),
		GraphBuilder.NodeKind.Target => typeof( TargetLink ),
		GraphBuilder.NodeKind.BoneMask => typeof( BoneMaskLink ),
		_ => throw new InvalidOperationException( $"Unsupported group input type '{kind}'" )
	};

	internal static GraphBuilder.NodeKind KindFor( Type type )
	{
		foreach ( var kind in Enum.GetValues<GraphBuilder.NodeKind>() )
			if ( LinkTypeFor( kind ) == type )
				return kind;
		throw new ArgumentException( $"Pose groups cannot expose a {type.Name} input", nameof( type ) );
	}

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = base.DisplayInfo;
			info.Name = Name;
			return info;
		}
	}

	private AnimPlugOut _output;

	protected override IEnumerable<IPlugOut> DynamicOutputs
	{
		get
		{
			if ( _output is null || _output.Type != LinkType )
				_output = new AnimPlugOut( this, new AnimPlugInfo( "Result", () => Name, null, null ), LinkType );
			yield return _output;
		}
	}

	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( Graph is not Data { Owner: PoseGroupNode owner } )
		{
			compiler.Error( "Group inputs must belong to a pose group", this );
			return compiler.ConstDefault( LinkType );
		}

		var output = owner.GetConnectedOutput( Source );
		if ( output?.Node is not Node source || output.Type != LinkType )
		{
			compiler.Error( $"Group input '{Name}' requires a {ValueType} connection", owner );
			return compiler.ConstDefault( LinkType );
		}

		return compiler.Emit( source );
	}
}
