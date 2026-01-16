using Sandbox;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ActionGraphs;
using Editor.NodeEditor;

namespace Editor.ActionGraphs;

#nullable enable

/// <summary>
/// Node creation menu entry for accessing <see cref="Component"/>s on the
/// <see cref="GameObject"/> that contains the current graph.
/// </summary>
public class ComponentNodeType : LibraryNodeType
{
	/// <summary>
	/// Add a <see cref="GameObject.GetComponent{T}"/> node for each component type.
	/// They'll only appear when searching by name.
	/// </summary>
	[Event( FindReflectionNodeTypesEvent.EventName )]
	public static void OnFindReflectionNodeTypes( FindReflectionNodeTypesEvent e )
	{
		if ( !IsComponentType( e.Type, e.ComponentTypes ) ) return;

		e.Output.Add( new ComponentNodeType( e.Type ) );
	}

	/// <summary>
	/// Add a <c>this.GetComponent&lt;T&gt;()</c> node for each component in the
	/// host object.
	/// </summary>
	[Event( GetLocalNodeTypesEvent.EventName )]
	public static void OnGetLocalNodeTypes( GetLocalNodeTypesEvent ev )
	{
		if ( ev.EditorGraph.HostObject is not GameObject hostObject ) return;

		var hostObjectName = hostObject.Name;
		var hostTypeDesc = TypeLibrary.GetType( typeof( GameObject ) );

		var componentTypes = hostObject.GetComponents<Component>( true )
			.Select( x => x.GetType() )
			.Distinct();

		foreach ( var componentType in componentTypes )
		{
			var typeDesc = TypeLibrary.GetType( componentType );

			if ( typeDesc is null ) continue;

			ev.Output.Add( new LocalTargetNodeType( new ComponentNodeType( typeDesc, hostObjectName ), hostTypeDesc.Title ) );
		}
	}

	/// <summary>
	/// If we're dragging from a scene ref output, add a <c>value.GetComponent&lt;T&gt;()</c>
	/// node for each component in the referenced object.
	/// </summary>
	[Event( QueryNodeTypesEvent.EventName )]
	public static void OnQueryNodeTypes( QueryNodeTypesEvent ev )
	{
		if ( ev.EditorGraph.HostObject is not GameObject hostObject ) return;

		if ( ev.Query.Plug is not ActionOutputPlug { Parameter.Node: { Definition.Identifier: "scene.ref" } node } )
		{
			return;
		}

		if ( node.GetSceneReference( hostObject.Scene ) is not { } sceneRefNode ) return;

		var typeTitle = DisplayInfo.ForType( node.Outputs.Result.Type ).Name;

		foreach ( var component in sceneRefNode.TargetObject.Components.GetAll<Component>( FindMode.EverythingInSelf ) )
		{
			var typeDesc = TypeLibrary.GetType( component.GetType() );
			if ( typeDesc is null ) continue;

			ev.Output.Add( new SelectedOutputNodeType( new ComponentNodeType( typeDesc, sceneRefNode.TargetObject.Name ), typeTitle ) );
		}
	}

	private static bool IsComponentType( TypeDescription type, IReadOnlyList<TypeDescription> componentTypes )
	{
		if ( componentTypes.Contains( type ) ) return true;

		// Also allow interfaces implemented by a component type

		return type.IsInterface && componentTypes.Any( x => x.TargetType.IsAssignableTo( type.TargetType ) );
	}

	public TypeDescription Type { get; }
	public bool KnownToExist { get; }

	public override bool IsCommonWithTarget => KnownToExist;

	private static IReadOnlyList<Menu.PathElement> GetPath( TypeDescription type, string? gameObjectName )
	{
		if ( gameObjectName is not null )
		{
			return MemberPath( TypeLibrary.GetType<IComponentLister>(),
				new Menu.PathElement( $"From Object ({gameObjectName})", Order: -100, IsHeading: true ),
				new Menu.PathElement( type.Title, type.Icon, type.Description ) );
		}

		return MemberPath( type,
			new Menu.PathElement( "Expressions", Order: MethodNodeType.ExpressionsOrder, IsHeading: true ),
			new Menu.PathElement( "Get", type.Icon, type.Description ) );
	}

	public ComponentNodeType( TypeDescription type, string? gameObjectName = null )
		: base( EditorNodeLibrary.Get( "scene.get" )!, GetPath( type, gameObjectName ), new Dictionary<string, object?>
		{
			{ "T", type.TargetType }
		} )
	{
		Type = type;
		KnownToExist = gameObjectName is not null;
	}
}
