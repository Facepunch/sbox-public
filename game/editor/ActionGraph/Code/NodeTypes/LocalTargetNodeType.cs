using System;
using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.ActionGraphs;
using DisplayInfo = Sandbox.DisplayInfo;

namespace Editor.ActionGraphs;

#nullable enable

/// <summary>
/// Node creation menu entry for nodes targeting the <see cref="GameObject"/>
/// or <see cref="GameResource"/> that contains the current graph.
/// </summary>
public class LocalTargetNodeType : INodeType
{
	/// <summary>
	/// <para>
	/// For each global node type that accepts the graph target type as a target input,
	/// create a helper node type that implicitly passes in the target.
	/// </para>
	/// <para>
	/// Same idea as how in C# you can replace <c>this.Foo()</c> with just <c>Foo()</c>.
	/// </para>
	/// </summary>
	[Event( GetLocalNodeTypesEvent.EventName )]
	public static void OnGetLocalNodeTypes( GetLocalNodeTypesEvent ev )
	{
		if ( ev.ActionGraph.GetTargetType() is not { } targetType )
		{
			return;
		}

		var targetTypeTitle = DisplayInfo.ForType( targetType ).Name;

		Parallel.ForEach( ev.GlobalNodeTypes, x =>
		{
			if ( x is not LibraryNodeType libraryNode ) return;
			if ( libraryNode.TargetInput is not { } targetInput ) return;
			if ( libraryNode.Inputs.ContainsKey( targetInput.Name ) ) return;
			if ( !targetInput.Type.IsAssignableFromExtended( targetType ) ) return;

			ev.Output.Add( new LocalTargetNodeType( libraryNode, targetTypeTitle ) );
		} );
	}

	public LibraryNodeType Inner { get; }
	public Menu.PathElement[] Path { get; }

	public bool IsCommon => Inner.IsCommonWithTarget;

	public LocalTargetNodeType( LibraryNodeType inner, string? typeTitle = null )
	{
		Inner = inner;
		Path = inner.Path.ToArray();

		Path[0] = new Menu.PathElement( typeTitle is null ? "This" : $"This ({typeTitle})", Order: -100, IsHeading: true );
	}

	public bool TryGetInput( Type valueType, [NotNullWhen( true )] out string? name )
	{
		return Inner.TryGetInput( valueType, false, out name );
	}

	public bool TryGetOutput( Type valueType, [NotNullWhen( true )] out string? name )
	{
		return Inner.TryGetOutput( valueType, out name );
	}

	public INode CreateNode( IGraph graph )
	{
		var editorNode = (EditorNode)Inner.CreateNode( graph );
		var actionNode = editorNode.Node;
		var targetInput = actionNode.Inputs.Values.First( x => x.IsTarget );

		var editorGraph = (EditorActionGraph)graph;
		var actionGraph = editorGraph.Graph;

		if ( actionGraph.TargetOutput is null )
		{
			actionGraph.AddRequiredNodes();
		}

		targetInput.SetLink( actionGraph.TargetOutput! );

		return editorNode;
	}

	public Node CreateNode( ActionGraph graph, Node? parent )
	{
		var node = Inner.CreateNode( graph, parent );
		var targetInput = node.Inputs.Values.First( x => x.IsTarget );

		if ( graph.TargetOutput is null )
		{
			graph.AddRequiredNodes();
		}

		targetInput.SetLink( graph.TargetOutput! );

		return node;
	}
}
