using Editor.MapDoc;
using Editor.MapEditor;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.Mcp;

internal static partial class HammerTools
{
	[McpTool( "hammer_create_entity" )]
	[Description( "Place an entity in the open map - a light, prop, spawn point, sound, anything hammer_entity_classes lists. Set any keyvalues in the same call. The user can undo this. Returns the new node." )]
	public static MapNodeInfo CreateEntity(
		[Description( "The entity class, e.g. 'light_omni' or 'prop_static'. hammer_entity_classes lists them and documents their keyvalues." )] string className,
		[Description( "World position as 'x,y,z'." )] string position,
		[Description( "Rotation as 'pitch,yaw,roll'." )] string angles = "",
		[Description( "The entity's name (targetname), for other entities to refer to it." )] string name = "",
		[Description( "Keyvalues as a json object, e.g. {\"model\": \"models/dev/box.vmdl\"} or {\"color\": \"255 200 150\"}. Strings are stored as given, numbers as written, booleans as 1/0 and arrays space separated - the way Hammer stores them." )] JsonObject keyValues = null )
	{
		var map = ActiveMap();
		var entity = CreateEntityNode( map, new EntityItem { ClassName = className, Position = position, Angles = angles, Name = name, KeyValues = keyValues } );

		History.MarkUndoPosition( $"New Entity: {entity.ClassName}" );
		History.KeepNew( entity );

		return Info( entity );
	}

	[McpTool( "hammer_create_entities" )]
	[Description( "Place many entities in one call, each taking the same fields hammer_create_entity does - lay out a row of lights or scatter props without a round trip each. Everything validates before anything is created, and one undo step covers the batch. Returns the new nodes in order." )]
	public static MapNodeInfo[] CreateEntities( [Description( "The entities to create." )] EntityItem[] items )
	{
		if ( items is null || items.Length == 0 )
			throw new Exception( "Give at least one item like {\"className\": \"light_omni\", \"position\": \"0,0,64\"}" );

		var map = ActiveMap();

		// Validate the classes up front so a typo in item 10 doesn't leave 9 entities behind
		foreach ( var item in items )
		{
			FindEntityClass( item?.ClassName ?? throw new Exception( "Items can't be null" ) );
			Vector3.Parse( item.Position );
		}

		var created = items.Select( x => CreateEntityNode( map, x ) ).ToArray();

		History.MarkUndoPosition( $"New {created.Length} Entities" );

		foreach ( var entity in created )
		{
			History.KeepNew( entity );
		}

		return created.Select( Info ).ToArray();
	}

	[McpTool( "hammer_set_node" )]
	[Description( "Change a map node - rename it, move, rotate or scale it, hide or show it, or set entity keyvalues. Only the arguments you give change. The user can undo this. Returns the node's new state." )]
	public static MapNodeDetails SetNode(
		[Description( "The node id, from hammer_find_nodes." )] int id,
		[Description( "New name." )] string name = "",
		[Description( "World position as 'x,y,z'." )] string position = "",
		[Description( "Rotation as 'pitch,yaw,roll'." )] string angles = "",
		[Description( "Scale as 'x,y,z', or a single number for uniform." )] string scale = "",
		[Description( "Show or hide the node in Hammer's views." )] bool? visible = null,
		[Description( "Entity keyvalues to set, as a json object. Same forms as hammer_create_entity." )] JsonObject keyValues = null )
	{
		var map = ActiveMap();
		var node = FindNode( map, id );

		if ( keyValues is { Count: > 0 } && node is not MapEntity )
			throw new Exception( $"Node {id} is a {KindOf( node )} - only entities have keyvalues" );

		var parsedPosition = string.IsNullOrWhiteSpace( position ) ? (Vector3?)null : Vector3.Parse( position );
		var parsedAngles = string.IsNullOrWhiteSpace( angles ) ? (Angles?)null : Angles.Parse( angles );
		var parsedScale = string.IsNullOrWhiteSpace( scale ) ? (Vector3?)null : ParseScale( scale );

		History.MarkUndoPosition( $"Edit {KindOf( node )}" );
		History.Keep( node );

		if ( !string.IsNullOrWhiteSpace( name ) )
			SetName( node, name );

		if ( parsedPosition is not null ) node.Position = parsedPosition.Value;
		if ( parsedAngles is not null ) node.Angles = parsedAngles.Value;
		if ( parsedScale is not null ) node.Scale = parsedScale.Value;
		if ( visible is not null ) node.Visible = visible.Value;

		if ( node is MapEntity entity )
			ApplyKeyValues( entity, keyValues );

		MarkModified( map );

		return GetNode( id );
	}

	[McpTool( "hammer_move_nodes" )]
	[Description( "Move several nodes by the same offset in one undo step - shift a room, nudge a cluster of props. A node whose parent is also in the list moves with its parent rather than twice." )]
	public static MapNodeInfo[] MoveNodes(
		[Description( "The node ids to move." )] int[] ids,
		[Description( "How far to move them, as 'x,y,z'." )] string offset )
	{
		var map = ActiveMap();
		var nodes = TopmostOf( ResolveNodes( map, ids ) );
		var delta = Vector3.Parse( offset );

		History.MarkUndoPosition( $"Move {nodes.Length} Nodes" );

		foreach ( var node in nodes )
		{
			History.Keep( node );
			node.Position += delta;
		}

		MarkModified( map );

		return nodes.Select( Info ).ToArray();
	}

	[McpTool( "hammer_delete_nodes" )]
	[Description( "Delete nodes from the map, with all their children. The user can undo this." )]
	public static object DeleteNodes( [Description( "The node ids to delete." )] int[] ids )
	{
		var map = ActiveMap();
		var nodes = TopmostOf( ResolveNodes( map, ids ) );

		History.MarkUndoPosition( $"Delete {nodes.Length} Nodes" );

		foreach ( var node in nodes )
		{
			map.DeleteNode( node );
		}

		return $"Deleted {nodes.Length} nodes";
	}

	[McpTool( "hammer_duplicate_nodes" )]
	[Description( "Copy nodes, offsetting each copy - 'count' copies make an evenly spaced row, so one call builds a fence, a colonnade or a run of lights. One undo step covers it. Returns the new nodes." )]
	public static MapNodeInfo[] DuplicateNodes(
		[Description( "The node ids to copy." )] int[] ids,
		[Description( "Offset of each copy from the one before, as 'x,y,z'." )] string offset,
		[Description( "How many copies of each node to make." ), Range( 1, 256 )] int count = 1 )
	{
		var map = ActiveMap();
		var nodes = TopmostOf( ResolveNodes( map, ids ) );
		var step = Vector3.Parse( offset );
		var created = new List<MapNode>();

		History.MarkUndoPosition( $"Duplicate {nodes.Length} Nodes" );

		for ( int i = 1; i <= count; i++ )
		{
			foreach ( var node in nodes )
			{
				// Copy() keeps the new node for undo itself
				var copy = node.Copy() ?? throw new Exception( $"Node {node.NodeId} ({KindOf( node )}) can't be copied" );
				copy.Position = node.Position + step * i;

				created.Add( copy );
			}
		}

		MarkModified( map );

		return created.Select( Info ).ToArray();
	}

	[McpTool( "hammer_assign_asset" )]
	[Description( "Apply an asset to nodes the way dropping it onto them in Hammer does - a material paints every face of the meshes (or just the selected faces, in Faces mode), a model swaps a prop's model. Give node ids, or nothing to apply to the current selection. Selects the nodes it applied to. The user can undo this." )]
	public static object AssignAsset(
		[Description( "The material or model asset path." )] string asset,
		[Description( "Node ids to apply to. Empty applies to the current selection." )] int[] ids = null )
	{
		var map = ActiveMap();
		var found = FindAsset( asset );

		if ( ids is { Length: > 0 } )
		{
			var nodes = ResolveNodes( map, ids );

			// Asset assignment acts on the selection, and node ids only mean anything to an object selection
			if ( Selection.SelectMode >= SelectMode.Verticies )
				Selection.SelectMode = SelectMode.Meshes;

			Selection.Clear();

			foreach ( var node in nodes )
			{
				Selection.Add( node );
			}
		}
		else if ( Selection.SelectMode < SelectMode.Verticies && !Selection.All.Any() )
		{
			throw new Exception( "Nothing is selected - give node ids, or select something with hammer_select" );
		}

		Hammer.AssignAssetToSelection( found );
		MarkModified( map );

		return $"Assigned {found.Path} to the selection ({Selection.SelectMode} mode)";
	}

	[McpTool( "hammer_replace_material" )]
	[Description( "Replace one material with another on every face in the map that uses it - retexture a whole map in one call. Leaves those faces selected in Faces mode so the user sees what changed. The user can undo this." )]
	public static object ReplaceMaterial(
		[Description( "The material to replace, e.g. 'materials/dev/reflectivity_30.vmat'. hammer_asset_usage lists the map's materials." )] string from,
		[Description( "The material to use instead." )] string to )
	{
		var map = ActiveMap();
		var source = FindAsset( from );
		var target = FindAsset( to );

		if ( source.AssetType != AssetType.Material || target.AssetType != AssetType.Material )
			throw new Exception( "Both 'from' and 'to' must be materials (.vmat)" );

		var meshes = Nodes( map ).OfType<MapMesh>().Count( x => x.GetFaceMaterialAssets().Contains( source ) );

		if ( meshes == 0 )
			throw new Exception( $"No mesh in the map uses {source.Path} - hammer_asset_usage lists the materials it does use" );

		Hammer.SelectFacesUsingMaterial( source );
		Hammer.AssignAssetToSelection( target );
		MarkModified( map );

		return $"Replaced {source.Path} with {target.Path} on {meshes} meshes. Those faces are selected, in Faces mode.";
	}

	/// <summary>
	/// One entity to create - the same fields hammer_create_entity takes.
	/// </summary>
	public class EntityItem
	{
		[Description( "The entity class, e.g. 'light_omni'." )]
		public required string ClassName { get; set; }

		[Description( "World position as 'x,y,z'." )]
		public required string Position { get; set; }

		[Description( "Rotation as 'pitch,yaw,roll'." )]
		public string Angles { get; set; } = "";

		[Description( "The entity's name (targetname)." )]
		public string Name { get; set; } = "";

		[Description( "Keyvalues as a json object." )]
		public JsonObject KeyValues { get; set; }
	}

	static MapEntity CreateEntityNode( MapDocument map, EntityItem item )
	{
		var mapClass = FindEntityClass( item.ClassName );
		var position = Vector3.Parse( item.Position );
		var angles = string.IsNullOrWhiteSpace( item.Angles ) ? (Angles?)null : Angles.Parse( item.Angles );

		var entity = new MapEntity( map )
		{
			ClassName = mapClass.Name,
			Position = position
		};

		if ( angles is not null )
			entity.Angles = angles.Value;

		if ( !string.IsNullOrWhiteSpace( item.Name ) )
			SetName( entity, item.Name );

		ApplyKeyValues( entity, item.KeyValues );

		return entity;
	}

	static void ApplyKeyValues( MapEntity entity, JsonObject keyValues )
	{
		if ( keyValues is null )
			return;

		foreach ( var (key, value) in keyValues )
		{
			entity.SetKeyValue( key, KeyValueString( value ) );
		}
	}

	/// <summary>
	/// A json value the way Hammer stores keyvalues - strings as is, booleans as 1/0, arrays space separated.
	/// </summary>
	static string KeyValueString( JsonNode value ) => value switch
	{
		null => "",
		JsonArray array => string.Join( " ", array.Select( KeyValueString ) ),
		JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
		JsonValue v when v.GetValueKind() == JsonValueKind.True => "1",
		JsonValue v when v.GetValueKind() == JsonValueKind.False => "0",
		JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.GetValue<double>().ToString( CultureInfo.InvariantCulture ),
		_ => value.ToJsonString()
	};

	/// <summary>
	/// Name a node. An entity's targetname is what other entities refer to it by, so that gets set too.
	/// </summary>
	static void SetName( MapNode node, string name )
	{
		if ( node is MapGameObject mgo && mgo.GameObject.IsValid() )
		{
			mgo.GameObject.Name = name;
			return;
		}

		node.Name = name;

		if ( node is MapEntity entity )
			entity.SetKeyValue( "targetname", name );
	}

	static Vector3 ParseScale( string scale ) => scale.Contains( ',' ) ? Vector3.Parse( scale ) : new Vector3( scale.ToFloat() );

	static MapNode[] ResolveNodes( MapDocument map, int[] ids )
	{
		if ( ids is null || ids.Length == 0 )
			throw new Exception( "Give at least one node id - hammer_find_nodes lists them" );

		// Resolve everything first so a bad id doesn't leave the batch half done
		return ids.Distinct().Select( x => FindNode( map, x ) ).ToArray();
	}

	/// <summary>
	/// Drop nodes whose ancestor is also in the set - they move, copy and delete with it.
	/// </summary>
	static MapNode[] TopmostOf( MapNode[] nodes )
	{
		var set = nodes.ToHashSet();

		bool HasAncestorInSet( MapNode node )
		{
			for ( var parent = node.Parent; parent.IsValid() && parent is not MapWorld; parent = parent.Parent )
			{
				if ( set.Contains( parent ) ) return true;
			}

			return false;
		}

		return nodes.Where( x => !HasAncestorInSet( x ) ).ToArray();
	}

	/// <summary>
	/// Asset paths a node references directly - face materials, asset keyvalues, component resources.
	/// </summary>
	/// <remarks>
	/// Counts references whether or not the asset is installed - a map can reference cloud packages that
	/// haven't been downloaded, and those still matter.
	/// </remarks>
	static IEnumerable<string> AssetsUsedBy( MapNode node )
	{
		switch ( node )
		{
			case MapMesh mesh:
				return mesh.GetFaces().Select( x => x.Material ).Where( x => !string.IsNullOrEmpty( x ) ).Select( NormalizeAssetPath ).Distinct();

			case MapEntity entity:
				return KeyValuesOf( entity ).Values
					.Where( LooksLikeAssetPath )
					.Select( NormalizeAssetPath )
					.Distinct();

			case MapGameObject { GameObject: { } go } when go.IsValid():
				// The serialized form keeps the paths of resources that failed to load, the live components don't
				var paths = new List<string>();
				CollectAssetPaths( go.Serialize(), paths );

				if ( go.IsPrefabInstanceRoot && !string.IsNullOrEmpty( go.PrefabInstanceSource ) )
					paths.Add( go.PrefabInstanceSource );

				return paths.Select( NormalizeAssetPath ).Distinct();

			default:
				return [];
		}
	}

	static readonly HashSet<string> AssetExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".vmdl", ".vmat", ".vtex", ".vpcf", ".vsnd", ".sound", ".sndscape", ".prefab", ".scene", ".decal",
		".surface", ".clothing", ".vanmgrph", ".vmap", ".shader", ".png", ".jpg", ".tga", ".psd"
	};

	static bool LooksLikeAssetPath( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) || value.Length > 260 )
			return false;

		return AssetExtensions.Contains( System.IO.Path.GetExtension( value.Trim() ) );
	}

	/// <summary>
	/// The asset's own path when it's installed, otherwise the reference tidied up the same way.
	/// </summary>
	static string NormalizeAssetPath( string value )
	{
		value = value.Trim().Replace( '\\', '/' );

		if ( value.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			value = value[..^2];

		return AssetSystem.FindByPath( value )?.Path ?? value.ToLowerInvariant();
	}

	static void CollectAssetPaths( System.Text.Json.Nodes.JsonNode node, List<string> paths )
	{
		switch ( node )
		{
			case System.Text.Json.Nodes.JsonObject obj:
				foreach ( var (_, value) in obj ) CollectAssetPaths( value, paths );
				break;

			case System.Text.Json.Nodes.JsonArray array:
				foreach ( var value in array ) CollectAssetPaths( value, paths );
				break;

			case System.Text.Json.Nodes.JsonValue value when value.TryGetValue<string>( out var text ) && LooksLikeAssetPath( text ):
				paths.Add( text );
				break;
		}
	}
}
