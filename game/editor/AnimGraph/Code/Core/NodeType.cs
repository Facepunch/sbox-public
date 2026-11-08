using Editor.NodeEditor;

namespace Editor.AnimGraph;

/// <summary>
/// Restricts where a node can be created - the graph kinds that may create it,
/// or none for auto-managed nodes users never create. Nodes without this
/// attribute are available everywhere, except pose nodes in value graphs.
/// </summary>
[AttributeUsage( AttributeTargets.Class )]
public sealed class AvailableInAttribute : Attribute
{
	public GraphKind[] Kinds { get; }
	public AvailableInAttribute( params GraphKind[] kinds ) => Kinds = kinds;
}

/// <summary>
/// Node type descriptor for AnimGraph nodes. Used by GraphView for
/// node creation and the context menu.
/// </summary>
public class NodeType : INodeType
{
	public string Identifier => TypeDescription.FullName;
	public TypeDescription TypeDescription { get; }
	public DisplayInfo DisplayInfo { get; }
	public Menu.PathElement[] Path => Menu.GetSplitPath( DisplayInfo );
	public bool IsCommon => true;

	public NodeType( TypeDescription type )
	{
		TypeDescription = type;
		DisplayInfo = type is not null
			? Node.GetTypeDisplayInfo( type.TargetType )
			: new DisplayInfo();
	}

	public INode CreateNode( IGraph graph )
	{
		var node = TypeDescription.Create<Node>();
		node.Graph = graph;
		return node;
	}

	/// <summary>
	/// Whether this node type can be created in the given graph - its
	/// [AvailableIn] kinds if declared, otherwise anywhere except pose
	/// nodes in value graphs.
	/// </summary>
	public bool IsAvailableIn( Data graph )
	{
		if ( TypeDescription.GetAttribute<AvailableInAttribute>( inherited: true ) is { } available )
			return graph is not null && available.Kinds.Contains( graph.Kind );

		if ( graph is not { IsValueGraph: true } )
			return true;

		return !TypeDescription.Properties.Any( p =>
			p.GetCustomAttribute<Node.InputAttribute>()?.Type == typeof( PoseLink ) ||
			p.GetCustomAttribute<Node.OutputAttribute>()?.Type == typeof( PoseLink ) );
	}

	public bool TryGetInput( Type valueType, out string name )
	{
		var property = TypeDescription.Properties
			.Select( x => (Property: x, Attrib: x.GetCustomAttribute<Node.InputAttribute>()) )
			.Where( x => x.Attrib is not null )
			.FirstOrDefault( x => x.Attrib.Type?.IsAssignableFrom( valueType ) ?? true )
			.Property;

		name = property?.Name;
		return name is not null;
	}

	public bool TryGetOutput( Type valueType, out string name )
	{
		var property = TypeDescription.Properties
			.Select( x => (Property: x, Attrib: x.GetCustomAttribute<Node.OutputAttribute>()) )
			.Where( x => x.Attrib is not null )
			.FirstOrDefault( x => x.Attrib.Type?.IsAssignableTo( valueType ) ?? true )
			.Property;

		name = property?.Name;
		return name is not null;
	}
}
