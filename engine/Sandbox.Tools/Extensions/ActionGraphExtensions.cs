using Facepunch.ActionGraphs;

namespace Sandbox.ActionGraphs;

#nullable enable

/// <summary>
/// An <see cref="Facepunch.ActionGraphs.Node"/> from an <see cref="ActionGraph"/> that references a
/// <see cref="GameObject"/> or <see cref="Component"/>.
/// </summary>
public readonly record struct SceneReferenceNode(
	Node Node,
	GameObject TargetObject,
	Component? TargetComponent = null );

/// <summary>
/// Helper methods for action graph editor tools. Mostly workaround for <see cref="GameObjectReference"/>
/// and <see cref="ComponentReference"/> being internal.
/// </summary>
public static class ActionGraphEditorExtensions
{
	/// <summary>
	/// Find all <see cref="GameObject"/>s and <see cref="Component"/>s referenced by the given <see cref="IActionGraphDelegate"/>.
	/// </summary>
	public static IEnumerable<SceneReferenceNode> GetSceneReferences( this IActionGraphDelegate actionGraphDelegate )
	{
		if ( actionGraphDelegate.GetEmbeddedTarget() is not GameObject { Scene: { } scene, IsValid: true } ) yield break;

		foreach ( var node in actionGraphDelegate.Graph.Nodes.Values )
		{
			if ( node.GetSceneReference( scene, actionGraphDelegate ) is { } sceneRef )
			{
				yield return sceneRef;
			}
		}
	}

	public static SceneReferenceNode? GetSceneReference( this Node node, Scene scene, IActionGraphDelegate? actionGraphDelegate = null )
	{
		if ( node.Definition is not SceneRefNodeDefinition ) return null;

		if ( node.Outputs.Result.Type == typeof( GameObject ) )
		{
			if ( node.GetPropertyOrDefault<GameObjectReference>( "gameobject", actionGraphDelegate ) is not { } goRef ) return null;
			if ( goRef.Resolve( scene ) is not { } targetObj ) return null;

			return new SceneReferenceNode( node, targetObj );
		}

		if ( node.GetPropertyOrDefault<ComponentReference>( "component", actionGraphDelegate ) is not { } cmpRef ) return null;

		try
		{
			if ( cmpRef.Resolve( scene ) is not { } targetCmp ) return null;
			return new SceneReferenceNode( node, targetCmp.GameObject, targetCmp );
		}
		catch
		{
			// Can throw if component type is missing

			return null;
		}
	}

	private static T? GetPropertyOrDefault<T>( this Node node, string name, IActionGraphDelegate? actionGraphDelegate = null )
		where T : struct
	{
		if ( !node.Properties.TryGetValue( name, out var property ) ) return default;

		// IActionGraphDelegate wraps an ActionGraph with some property values overridden.
		// Look up an overridden property value, defaulting to the original graph's
		// value for that property.

		if ( actionGraphDelegate?.Defaults.TryGetValue( $"{node.Id}.{name}", out var rawValue ) is not true )
		{
			rawValue = property.Value;
		}

		return rawValue is T typedValue ? typedValue : default;
	}

	public static IReadOnlyDictionary<string, object> GetNodeProperties( GameObject go )
	{
		return new Dictionary<string, object>
		{
			{ "gameobject", GameObjectReference.FromInstance( go ) }
		};
	}

	public static IReadOnlyDictionary<string, object> GetNodeProperties( string prefabPath )
	{
		return new Dictionary<string, object>
		{
			{ "gameobject", GameObjectReference.FromPrefabPath( prefabPath ) }
		};
	}

	public static IReadOnlyDictionary<string, object> GetNodeProperties( Component component )
	{
		return new Dictionary<string, object>
		{
			{ "component", ComponentReference.FromInstance( component ) }
		};
	}
}
