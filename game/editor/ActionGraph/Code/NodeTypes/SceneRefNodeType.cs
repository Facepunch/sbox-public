using System;
using Sandbox;
using System.Collections.Generic;
using Editor.NodeEditor;
using Sandbox.ActionGraphs;

namespace Editor.ActionGraphs;

/// <summary>
/// Node creation menu entry for a scene object or component reference.
/// </summary>
public class SceneRefNodeType : LibraryNodeType
{
	[Event( QueryNodeTypesEvent.EventName )]
	public static void OnQueryNodeTypes( QueryNodeTypesEvent ev )
	{
		if ( ev.EditorGraph.HostObject is not GameObject hostObject )
		{
			return;
		}

		if ( ev.Query.Plug is IPlugIn { Type: { } inputType } && (inputType.IsAssignableTo( typeof( Component ) ) || inputType.IsInterface) )
		{
			// If we're dragging from an input that accepts a component, suggest all matching components in the current scene

			var components = hostObject.Scene.Components.GetAll( inputType, FindMode.EverythingInDescendants );

			foreach ( var comp in components )
			{
				ev.Output.Add( new SceneRefNodeType( comp ) );
			}

			return;
		}

		// If we have a filter, search the whole scene. Otherwise, just within the host object.

		var rootObject = ev.Query.Filter.Count == 0
			? hostObject
			: hostObject.Scene;

		foreach ( var go in rootObject.GetAllObjects( false ) )
		{
			if ( go is Scene ) continue;

			// I think we shouldn't be able to reference things from within prefab instances, right?

			if ( go.IsPrefabInstance && !go.IsPrefabInstanceRoot ) continue;

			ev.Output.Add( new SceneRefNodeType( go, rootObject ) );

			if ( go.IsPrefabInstance ) continue;

			foreach ( var comp in go.GetComponents<Component>( true ) )
			{
				ev.Output.Add( new SceneRefNodeType( comp, rootObject ) );
			}
		}
	}

	public object Value { get; }
	public string PrefabPath { get; }

	private static IReadOnlyList<Menu.PathElement> GetPath( object value, GameObject root = null )
	{
		var (go, comp) = value switch
		{
			GameObject o => (o, null),
			Component c => (c.GameObject, c),
			_ => (null, null)
		};

		if ( go is null ) return null;

		root ??= go.Scene;

		var path = new List<Menu.PathElement>();

		if ( comp is not null )
		{
			var display = DisplayInfo.For( comp );

			path.Add( new Menu.PathElement( display.Name, display.Icon, display.Description ) );
			path.Add( new Menu.PathElement( "Components", IsHeading: true, Order: 200 ) );
		}
		else
		{
			path.Add( new Menu.PathElement( go.Name, "radio_button_unchecked", Description: $"Guid: {go.Id}" ) );
			path.Add( new Menu.PathElement( "Game Object", IsHeading: true, Order: 100 ) );
		}

		while ( go is not null )
		{
			path.Add( new Menu.PathElement( go.Name, "layers" ) );

			if ( go == root ) break;

			path.Add( new Menu.PathElement( "Children", IsHeading: true, Order: 300 ) );

			go = go.Parent;
		}

		path.Add( new Menu.PathElement( "Scene Reference", IsHeading: true ) );

		path.Reverse();

		return path;
	}

	public SceneRefNodeType( GameObject value, GameObject root = null )
		: base( EditorNodeLibrary.Get( "scene.ref" )!, GetPath( value, root ), ActionGraphEditorExtensions.GetNodeProperties( value ) )
	{
		Value = value;
	}

	public SceneRefNodeType( Component value, GameObject root = null )
		: base( EditorNodeLibrary.Get( "scene.ref" )!, GetPath( value, root ), ActionGraphEditorExtensions.GetNodeProperties( value ) )
	{
		Value = value;
	}

	public SceneRefNodeType( string prefabPath, GameObject root = null )
		: base( EditorNodeLibrary.Get( "scene.ref" )!, GetPath( prefabPath, root ), ActionGraphEditorExtensions.GetNodeProperties( prefabPath ) )
	{
		PrefabPath = prefabPath;
	}
}
