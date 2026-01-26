using Editor.NodeEditor;

namespace Editor.ShaderGraph;

public class TextureNodeType : ClassNodeType
{
	string ImagePath;

	public TextureNodeType( TypeDescription type, string imagePath ) : base( type )
	{
		ImagePath = imagePath;
	}
	public override INode CreateNode( IGraph graph )
	{
		var node = base.CreateNode( graph );
		if ( node is ITextureParameterNode textureNode )
		{
			textureNode.Image = ImagePath;
		}
		return node;
	}
}

public class ClassNodeType : INodeType
{
	public virtual string Identifier => Type.FullName;

	public TypeDescription Type { get; }
	public DisplayInfo DisplayInfo { get; protected set; }

	public Menu.PathElement[] Path => Menu.GetSplitPath( DisplayInfo );

	public ClassNodeType( TypeDescription type )
	{
		Type = type;
		if ( Type is not null )
			DisplayInfo = DisplayInfo.ForType( Type.TargetType );
		else
			DisplayInfo = new DisplayInfo();
	}

	public bool TryGetInput( Type valueType, out string name )
	{
		var property = Type.Properties
			.Select( x => (Property: x, Attrib: x.GetCustomAttribute<BaseNode.InputAttribute>()) )
			.Where( x => x.Attrib != null )
			.FirstOrDefault( x => x.Attrib.Type?.IsAssignableFrom( valueType ) ?? true )
			.Property;

		name = property?.Name;
		return name is not null;
	}

	public bool TryGetOutput( Type valueType, out string name )
	{
		var property = Type.Properties
			.Select( x => (Property: x, Attrib: x.GetCustomAttribute<BaseNode.OutputAttribute>()) )
			.Where( x => x.Attrib != null )
			.FirstOrDefault( x => x.Attrib.Type?.IsAssignableTo( valueType ) ?? true )
			.Property;

		name = property?.Name;
		return name is not null;
	}

	public virtual INode CreateNode( IGraph graph )
	{
		var node = Type.Create<BaseNode>();

		node.Graph = graph;

		return node;
	}
}

public sealed class SubgraphNodeType : ClassNodeType
{
	public override string Identifier => AssetPath;
	string AssetPath { get; }

	public SubgraphNodeType( string assetPath, TypeDescription type ) : base( type )
	{
		AssetPath = assetPath;
	}

	public void SetDisplayInfo( ShaderGraph subgraph )
	{
		var info = DisplayInfo;
		if ( !string.IsNullOrEmpty( subgraph.Title ) )
			info.Name = subgraph.Title;
		else
			info.Name = System.IO.Path.GetFileNameWithoutExtension( AssetPath );
		if ( !string.IsNullOrEmpty( subgraph.Description ) )
			info.Description = subgraph.Description;
		if ( !string.IsNullOrEmpty( subgraph.Icon ) )
			info.Icon = subgraph.Icon;
		if ( !string.IsNullOrEmpty( subgraph.Category ) )
			info.Group = subgraph.Category;
		DisplayInfo = info;
	}

	public override INode CreateNode( IGraph graph )
	{
		var node = base.CreateNode( graph );

		if ( node is SubgraphNode subgraphNode )
		{
			subgraphNode.SubgraphPath = AssetPath;
			subgraphNode.OnNodeCreated();
		}

		return node;
	}
}

public sealed class ParameterNodeType : ClassNodeType
{
	BlackboardParameter Parameter;

	public ParameterNodeType( TypeDescription type, BlackboardParameter parameter ) : base( type )
	{
		Parameter = parameter;
	}

	public override INode CreateNode( IGraph graph )
	{
		var node = BlackboardParameter.InitilzeNode( Parameter );
		node.Graph = graph;

		return node;
	}
}
