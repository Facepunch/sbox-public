using Editor.MapDoc;
using Editor.MapEditor;
using System;
using System.Text.Json.Nodes;

namespace Editor.Mcp;

internal static partial class HammerTools
{
	[McpTool( "hammer_create_game_object" )]
	[Description( "Put a game object in the open map - empty, from a prefab, and/or with components and their properties set in the same call. This is how s&box gameplay goes in a map. The user can undo the creation. Returns the new node, whose GameObject field is the object's guid." )]
	public static MapNodeInfo CreateGameObject(
		[Description( "Name for the object. Empty uses the prefab's name, or 'GameObject'." )] string name = "",
		[Description( "World position as 'x,y,z'." )] string position = "",
		[Description( "Rotation as 'pitch,yaw,roll'." )] string angles = "",
		[Description( "Prefab asset path to instantiate, from asset_search type:prefab." )] string prefab = "",
		[Description( "Components to add, as a json object of type name to properties - e.g. {\"ModelRenderer\": {\"Model\": \"models/dev/box.vmdl\"}, \"BoxCollider\": {}}. get_component_type documents types and properties. A type the prefab already has gets its properties set instead." )] JsonObject components = null )
	{
		var map = ActiveMap();
		var scene = map.World.Scene;
		var transform = new Transform(
			string.IsNullOrWhiteSpace( position ) ? Vector3.Zero : Vector3.Parse( position ),
			string.IsNullOrWhiteSpace( angles ) ? Rotation.Identity : Rotation.From( Angles.Parse( angles ) ) );

		// Resolve every type and property before creating anything
		var edits = ResolveComponentEdits( components );

		using var sceneScope = scene.Push();

		GameObject go;

		if ( !string.IsNullOrWhiteSpace( prefab ) )
		{
			go = GameObject.Clone( prefab, transform, name: string.IsNullOrWhiteSpace( name ) ? null : name )
				?? throw new Exception( $"Couldn't instantiate prefab '{prefab}' - asset_search type:prefab finds prefabs" );
		}
		else
		{
			go = new GameObject( true, string.IsNullOrWhiteSpace( name ) ? "GameObject" : name );
			go.WorldTransform = transform;
		}

		ApplyComponentEdits( go, edits );

		var node = new MapGameObject( map, go );

		History.MarkUndoPosition( $"New {go.Name}" );
		History.KeepNew( node );

		return Info( node );
	}

	[McpTool( "hammer_set_components" )]
	[Description( "Edit a map game object's components - add components, set their properties, remove them. Properties take the serialized forms hammer_get_node shows with includeComponentProperties. Everything validates before anything changes. Hammer can't undo component edits yet, so read the current values first if you might need to put them back." )]
	public static MapNodeDetails SetComponents(
		[Description( "The game object's node id, from hammer_find_nodes kind:GameObject." )] int id,
		[Description( "A json object of component type to properties, e.g. {\"PointLight\": {\"Radius\": 512}}. A type the object doesn't have yet gets added." )] JsonObject components = null,
		[Description( "Component type names to remove." )] string[] remove = null )
	{
		var map = ActiveMap();
		var node = FindNode( map, id ) as MapGameObject
			?? throw new Exception( $"Node {id} isn't a game object - hammer_find_nodes kind:GameObject lists them" );

		var go = node.GameObject;

		if ( !go.IsValid() )
			throw new Exception( $"Node {id} has lost its game object" );

		var edits = ResolveComponentEdits( components );
		var removals = (remove ?? []).Select( type =>
		{
			var td = FindComponentType( type );
			return go.Components.GetAll().FirstOrDefault( x => x.GetType() == td.TargetType )
				?? throw new Exception( $"'{go.Name}' has no {td.Name} to remove" );
		} ).ToArray();

		using ( map.World.Scene.Push() )
		{
			ApplyComponentEdits( go, edits );

			foreach ( var component in removals )
			{
				component.Destroy();
			}
		}

		MarkModified( map );

		return GetNode( id, true );
	}

	/// <summary>
	/// Component types and their parsed property values, resolved up front so a bad name or value
	/// fails before anything changes.
	/// </summary>
	static List<(TypeDescription Type, List<(PropertyDescription Property, object Value)> Values)> ResolveComponentEdits( JsonObject components )
	{
		var edits = new List<(TypeDescription, List<(PropertyDescription, object)>)>();

		if ( components is null )
			return edits;

		foreach ( var (typeName, value) in components )
		{
			var td = FindComponentType( typeName );
			var values = new List<(PropertyDescription, object)>();

			if ( value is JsonObject properties )
			{
				foreach ( var (key, propertyValue) in properties )
				{
					// Only the [Property] surface - what the inspector edits and the scene serializes
					var property = td.Properties.FirstOrDefault( x => !x.IsStatic && x.CanWrite && x.HasAttribute<PropertyAttribute>()
						&& string.Equals( x.Name, key, StringComparison.OrdinalIgnoreCase ) )
						?? throw new Exception( $"{td.Name} has no writable property '{key}'. Writable: {string.Join( ", ", td.Properties.Where( x => !x.IsStatic && x.CanWrite && x.HasAttribute<PropertyAttribute>() ).Select( x => x.Name ).Order() )}" );

					try
					{
						values.Add( (property, Json.FromNode( propertyValue, property.PropertyType )) );
					}
					catch ( Exception e )
					{
						throw new Exception( $"Couldn't convert {td.Name}.{key} to {property.PropertyType.Name} - {e.Message}" );
					}
				}
			}
			else if ( value is not null )
			{
				throw new Exception( $"'{typeName}' needs a json object of properties - {{}} for none" );
			}

			edits.Add( (td, values) );
		}

		return edits;
	}

	static void ApplyComponentEdits( GameObject go, List<(TypeDescription Type, List<(PropertyDescription Property, object Value)> Values)> edits )
	{
		foreach ( var (td, values) in edits )
		{
			var component = go.Components.GetAll().FirstOrDefault( x => x.GetType() == td.TargetType )
				?? go.Components.Create( td, true );

			foreach ( var (property, value) in values )
			{
				property.SetValue( component, value );
			}
		}
	}

	static TypeDescription FindComponentType( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( "Give a component type name, e.g. 'ModelRenderer'" );

		name = name.Trim();

		var all = EditorTypeLibrary.GetTypes<Component>().Where( x => !x.IsAbstract && !x.IsInterface ).ToArray();

		var type = all.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( x.ClassName, name, StringComparison.OrdinalIgnoreCase ) );

		if ( type is not null )
			return type;

		var candidates = all.Where( x => x.Name.Contains( name, StringComparison.OrdinalIgnoreCase ) ).ToArray();

		return candidates.Length switch
		{
			1 => candidates[0],
			> 1 => throw new Exception( $"'{name}' matches several component types: {string.Join( ", ", candidates.Select( x => x.Name ).Order().Take( 10 ) )}" ),
			_ => throw new Exception( $"No component type matches '{name}' - get_component_type documents what exists" )
		};
	}
}
