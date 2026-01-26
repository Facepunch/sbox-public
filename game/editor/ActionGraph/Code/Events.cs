using System;
using System.Collections.Concurrent;
using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Sandbox.ActionGraphs;

namespace Editor.ActionGraphs;

public record ActionGraphEditorEvent( ActionGraphView View )
{
	public EditorActionGraph EditorGraph => View.EditorGraph;
	public ActionGraph ActionGraph => EditorGraph.Graph;
}

public record PopulateNodeMenuEvent(
	ActionGraphView View,
	Menu Menu,
	Vector2 ClickPos,
	Plug TargetPlug,
	string Filter ) : ActionGraphEditorEvent( View )
{
	public const string EventName = "actiongraph.nodemenu";
}

public record PopulateInputPlugMenuEvent(
	ActionGraphView View,
	ActionInputPlug Plug,
	Menu Menu ) : ActionGraphEditorEvent( View )
{
	public const string EventName = "actiongraph.inputplugmenu";
}

public record PopulateOutputPlugMenuEvent(
	ActionGraphView View,
	ActionOutputPlug Plug,
	Menu Menu ) : ActionGraphEditorEvent( View )
{
	public const string EventName = "actiongraph.outputplugmenu";
}

public record PopulateCreateSubGraphMenuEvent(
	ActionGraphView View,
	Menu Menu,
	IReadOnlyList<(NodeUI NodeUI, Node ActionNode)> Nodes ) : ActionGraphEditorEvent( View )
{
	public const string EventName = "actiongraph.createsubgraphmenu";
}

public record GoToPlugSourceEvent(
	ActionGraphView View,
	ActionInputPlug Plug ) : ActionGraphEditorEvent( View )
{
	public const string EventName = "actiongraph.gotoplugsource";

	public bool Handled { get; set; }
}

public record FindGraphTargetEvent( ActionGraph Graph )
{
	public const string EventName = "actiongraph.findtarget";

	public Type TargetType { get; set; }
	public object TargetValue { get; set; }
}

public record BuildInputLabelEvent( ActionGraphView View, ActionInputPlug Plug ) : ActionGraphEditorEvent( View )
{
	public Node.Input Input => Plug.Parameter;
	public Link Link => Plug.InputLink;

	public const string EventName = "actiongraph.inputlabel";

	public bool Handled { get; set; }

	public object Value { get; set; }
	public string Text { get; set; }
	public string Icon { get; set; }
}

public record GetEditorPropertiesEvent( EditorActionGraph EditorGraph )
{
	public const string EventName = "actiongraph.geteditorproperties";

	public ActionGraph ActionGraph => EditorGraph.Graph;

	public bool CanModifyParameters { get; set; }
}

/// <summary>
/// Fetches common node types that should always show up for every graph.
/// These will get filtered automatically when dragging from a plug, or
/// if a filter string is typed in. These also get cached, which gets
/// invalidated on hotload.
/// </summary>
public record GetGlobalNodeTypesEvent( ConcurrentBag<INodeType> Output )
{
	public const string EventName = "actiongraph.globalnodes";
}

/// <summary>
/// Fetches common node types that should always show up for a specific graph.
/// These will get filtered automatically when dragging from a plug, or
/// if a filter string is typed in.
/// </summary>
public record GetLocalNodeTypesEvent( EditorActionGraph EditorGraph, IEnumerable<INodeType> GlobalNodeTypes, ConcurrentBag<INodeType> Output )
{
	public ActionGraph ActionGraph => EditorGraph.Graph;

	public const string EventName = "actiongraph.localnodes";
}

/// <summary>
/// Fetches node types that are relevant to a query. No need to include anything
/// provided by <see cref="GetGlobalNodeTypesEvent"/> or <see cref="GetLocalNodeTypesEvent"/>.
/// </summary>
public record QueryNodeTypesEvent( NodeQuery Query, IEnumerable<INodeType> GlobalNodeTypes, ConcurrentBag<INodeType> Output )
{
	public const string EventName = "actiongraph.querynodes";

	public EditorActionGraph EditorGraph => (EditorActionGraph)Query.Graph;
	public ActionGraph ActionGraph => EditorGraph.Graph;
}

public record FindReflectionNodeTypesEvent( TypeDescription Type,
	IReadOnlyList<MemberDescription> Members,
	IReadOnlyList<TypeDescription> ComponentTypes,
	ConcurrentBag<INodeType> Output )
{
	public const string EventName = "actiongraph.findreflectionnodes";

	private static bool IsMemberPublic( MemberDescription memberDesc )
	{
		if ( memberDesc.Name.StartsWith( "<" ) )
		{
			return false;
		}

		if ( memberDesc.IsPublic )
		{
			return true;
		}

		return memberDesc.GetCustomAttribute<Sandbox.PropertyAttribute>() is { }
			|| memberDesc.GetCustomAttribute<Sandbox.ActionGraphIncludeAttribute>() is { };
	}

	private static bool ShouldIncludeMember( MemberDescription member )
	{
		if ( !IsMemberPublic( member ) ) return false;
		if ( member.IsActionGraphIgnored() ) return false;

		if ( member.HasAttribute<NodeAttribute>() || member.HasAttribute<ActionGraphNodeAttribute>() )
		{
			// Already included as a LibraryNodeType
			return false;
		}

		return true;
	}

	public FindReflectionNodeTypesEvent( TypeDescription typeDescription,
		IReadOnlyList<TypeDescription> componentTypes,
		ConcurrentBag<INodeType> output )
		: this( typeDescription, typeDescription.DeclaredMembers.Where( ShouldIncludeMember ).ToArray(), componentTypes, output )
	{

	}
}
