using Editor.MapDoc;
using Editor.MapEditor;
using System;
using System.Text.Json.Nodes;
using MapInstance = Editor.MapDoc.MapInstance;

namespace Editor.Mcp;

/// <summary>
/// Hammer, the map editor, driven through its C# API. Hammer maps are a tree of map nodes - entities,
/// meshes, game objects, groups - addressed by the integer node id Hammer saves with the map. They're
/// separate from scenes, so the scene toolset doesn't see them.
/// </summary>
[McpToolset( "hammer", "Hammer, the map editor - find and inspect map nodes, place entities, game objects and brush geometry, assign materials, run Hammer commands, move the camera and screenshot the map" )]
internal static partial class HammerTools
{
	// Engine assemblies don't get the xml-summary-to-attribute codegen that addons get,
	// so these tools describe themselves with [Description] directly.

	[McpTool.ReadOnly( "hammer_status" )]
	[Description( "Whether Hammer is open and what map it's editing - the map's asset path, unsaved changes, how many nodes of each kind it has, the selection mode and count, and the material active in Hammer's material browser. Start here before any other hammer_ tool. If Hammer isn't open, hammer_open_map opens a map in it." )]
	public static HammerStatus Status()
	{
		if ( !Hammer.Open || !Hammer.ActiveMap.IsValid() )
		{
			return new HammerStatus { Open = Hammer.Open };
		}

		var map = Hammer.ActiveMap;
		var nodes = Nodes( map ).ToArray();

		return new HammerStatus
		{
			Open = true,
			MapOpen = true,
			Map = Hammer.MapAsset?.Path,
			File = map.PathName,
			HasUnsavedChanges = Hammer.HasUnsavedChanges,
			NodeCounts = nodes.GroupBy( KindOf ).OrderBy( x => x.Key ).ToDictionary( x => x.Key, x => x.Count() ),
			SelectionMode = Selection.SelectMode.ToString(),
			SelectedCount = Selection.All.Count(),
			CurrentMaterial = Hammer.CurrentMaterial?.ResourcePath,
			ViewCount = Hammer.MapViews.Count()
		};
	}

	[McpTool( "hammer_open_map" )]
	[Description( "Open a map (.vmap) in Hammer, opening Hammer itself if it isn't already. Loading happens in the background - poll hammer_status until MapOpen is true and Map matches." )]
	public static object OpenMap( [Description( "Map asset path, e.g. 'maps/test.vmap' - asset_search type:vmap finds maps." )] string path )
	{
		var asset = AssetSystem.FindByPath( path );

		if ( asset is null || !string.Equals( asset.AssetType?.FileExtension, "vmap", StringComparison.OrdinalIgnoreCase ) )
			throw new Exception( $"No map asset at '{path}' - asset_search type:vmap finds maps" );

		asset.OpenInEditor();

		return $"Opening {asset.Path} in Hammer - poll hammer_status until it's loaded";
	}

	[McpTool.ReadOnly( "hammer_find_nodes" )]
	[Description( "Search the open map's nodes. All filters are optional and combine; with 'near' the results come back nearest first. Each result has the node id the other hammer_ tools take, its kind (Entity, Mesh, GameObject, Group, Instance, Path, Overlay...), name, entity class, position and Hammer's one line description. Nodes inside prefabs aren't included." )]
	public static FoundNodes FindNodes(
		[Description( "Only this kind of node - Entity, Mesh, GameObject, Group, Instance, Path, PathNode or Overlay. Case insensitive." )] string kind = "",
		[Description( "Case insensitive substring of the node's name, entity class, game object name or description." )] string query = "",
		[Description( "Case insensitive substring of the entity class, e.g. 'light' for every light entity." )] string className = "",
		[Description( "Only nodes within 'radius' of this point, as 'x,y,z'. Sorts results nearest first." )] string near = "",
		[Description( "Search radius around 'near', in units." )] float radius = 512,
		[Description( "Only nodes that are currently selected." )] bool selectedOnly = false,
		[Description( "How many results to return." ), Range( 1, 500 )] int limit = 100,
		[Description( "How many matching results to skip, for paging." ), Range( 0, int.MaxValue )] int offset = 0 )
	{
		var map = ActiveMap();
		var matches = Nodes( map ).Where( x => x is not MapWorld );

		if ( !string.IsNullOrWhiteSpace( kind ) )
			matches = matches.Where( x => string.Equals( KindOf( x ), kind.Trim(), StringComparison.OrdinalIgnoreCase ) );

		if ( !string.IsNullOrWhiteSpace( className ) )
			matches = matches.Where( x => x is MapEntity e && (e.ClassName?.Contains( className.Trim(), StringComparison.OrdinalIgnoreCase ) ?? false) );

		if ( !string.IsNullOrWhiteSpace( query ) )
		{
			var q = query.Trim();
			matches = matches.Where( x => NameOf( x ).Contains( q, StringComparison.OrdinalIgnoreCase )
				|| ((x as MapEntity)?.ClassName?.Contains( q, StringComparison.OrdinalIgnoreCase ) ?? false)
				|| DescriptionOf( x ).Contains( q, StringComparison.OrdinalIgnoreCase ) );
		}

		if ( selectedOnly )
			matches = matches.Where( x => x.IsSelected );

		if ( !string.IsNullOrWhiteSpace( near ) )
		{
			var point = Vector3.Parse( near );
			matches = matches.Where( x => x.Position.Distance( point ) <= radius ).OrderBy( x => x.Position.Distance( point ) );
		}

		var all = matches.ToArray();

		return new FoundNodes
		{
			Total = all.Length,
			Nodes = all.Skip( offset ).Take( limit ).Select( Info ).ToArray()
		};
	}

	[McpTool.ReadOnly( "hammer_get_node" )]
	[Description( "Everything about one map node - transform, parent and children, and depending on its kind: an entity's class and every keyvalue, a mesh's face materials, a game object's id and components, or an instance's target." )]
	public static MapNodeDetails GetNode(
		[Description( "The node id, from hammer_find_nodes or hammer_get_selection." )] int id,
		[Description( "Include the full serialized properties of a game object's components, not just their types." )] bool includeComponentProperties = false )
	{
		var node = FindNode( ActiveMap(), id );
		var details = new MapNodeDetails();

		Fill( details, node );

		details.Angles = node.Angles;
		details.Scale = node.Scale;

		var children = node.Children.ToArray();
		details.ChildCount = children.Length;
		details.Children = children.Take( 200 ).Select( Info ).ToArray();

		if ( node is MapEntity entity )
		{
			details.EntityClassTitle = entity.MapClass?.DisplayName;
			details.KeyValues = KeyValuesOf( entity );
		}

		if ( node is MapMesh mesh )
		{
			details.Materials = mesh.GetFaceMaterialAssets().Select( x => x.Path ).ToArray();
		}

		if ( node is MapGameObject mgo && mgo.GameObject.IsValid() )
		{
			details.Components = mgo.GameObject.Components.GetAll().Select( x => new ComponentInfo
			{
				Type = x.GetType().Name,
				Id = x.Id,
				Enabled = x.Enabled,
				Properties = includeComponentProperties ? x.Serialize() : null
			} ).ToArray();

			details.Tags = mgo.GameObject.Tags.TryGetAll().ToArray();
		}

		if ( node is MapInstance instance && instance.Target.IsValid() )
		{
			details.InstanceTarget = instance.Target.NodeId;
		}

		return details;
	}

	[McpTool.ReadOnly( "hammer_get_selection" )]
	[Description( "What's selected in Hammer - the selection mode (Groups, Objects, Meshes, Vertices, Edges, Faces...), the pivot position, and the selected nodes. In a component mode like Faces the nodes list is the objects owning the selection." )]
	public static HammerSelection GetSelection()
	{
		ActiveMap();

		var selected = Selection.All.ToArray();

		return new HammerSelection
		{
			Mode = Selection.SelectMode.ToString(),
			Pivot = Selection.PivotPosition,
			Count = selected.Length,
			Nodes = selected.Take( 200 ).Select( Info ).ToArray()
		};
	}

	[McpTool( "hammer_select" )]
	[Description( "Change Hammer's selection - select nodes by id and/or everything using an asset, optionally switching selection mode first. Selecting is how you show the user what you mean, and some tools (hammer_run_command, hammer_assign_asset) act on the selection. Returns the new selection." )]
	public static HammerSelection Select(
		[Description( "Node ids to select. Empty selects nothing by id." )] int[] ids = null,
		[Description( "Also select every node using this asset - a model or material path." )] string usingAsset = "",
		[Description( "Add to the current selection instead of replacing it." )] bool add = false,
		[Description( "Switch selection mode first - Groups, Objects or Meshes. Empty keeps the current mode." )] SelectMode? mode = null )
	{
		var map = ActiveMap();
		var nodes = (ids ?? []).Select( x => FindNode( map, x ) ).ToArray();
		var asset = string.IsNullOrWhiteSpace( usingAsset ) ? null : FindAsset( usingAsset );

		if ( mode is not null )
			Selection.SelectMode = mode.Value;

		if ( !add )
			Selection.Clear();

		foreach ( var node in nodes )
		{
			Selection.Add( node );
		}

		if ( asset is not null )
			Hammer.SelectObjectsUsingAsset( asset );

		return GetSelection();
	}

	[McpTool.ReadOnly( "hammer_trace" )]
	[Description( "Trace a ray through the open map and report the first node it hits, where, and the surface normal - find the floor under a point, check what's in front of the camera, or probe where to place something. Traces Hammer's own geometry, so it works before the map is compiled." )]
	public static HammerTraceHit TraceRay(
		[Description( "Ray start as 'x,y,z'." )] string from,
		[Description( "Ray end as 'x,y,z'." )] string to,
		[Description( "Only hit mesh geometry, ignoring entities and game objects." )] bool meshesOnly = false,
		[Description( "Don't hit tools materials like clip or trigger." )] bool skipToolsMaterials = false,
		[Description( "Hit faces from behind too. A back face hit (the normal points along the ray) means the ray started inside something." )] bool hitBackfaces = false )
	{
		var map = ActiveMap();
		var trace = Editor.Trace.Ray( Vector3.Parse( from ), Vector3.Parse( to ) );

		if ( meshesOnly ) trace = trace.MeshesOnly();
		if ( skipToolsMaterials ) trace = trace.SkipToolsMaterials();
		if ( hitBackfaces ) trace = trace.HitBackfaces();

		var result = trace.Run( map.World );

		return new HammerTraceHit
		{
			Hit = result.Hit,
			Position = result.Hit ? result.HitPosition : Vector3.Parse( to ),
			Normal = result.Normal,
			Node = result.MapNode.IsValid() ? Info( result.MapNode ) : null
		};
	}

	[McpTool.ReadOnly( "hammer_entity_classes" )]
	[Description( "The entity classes that can be placed with hammer_create_entity. Search by name, title or category to list matches; give an exact class name to get its full documentation - every keyvalue with type, default and description, plus inputs and outputs." )]
	public static object EntityClasses(
		[Description( "Case insensitive substring of the class name, title, category or description. Empty lists every class." )] string query = "",
		[Description( "An exact class name like 'light_spot' to document fully." )] string name = "",
		[Description( "How many classes to list." ), Range( 1, 500 )] int limit = 100 )
	{
		if ( !string.IsNullOrWhiteSpace( name ) )
		{
			var mapClass = FindEntityClass( name );

			return new
			{
				mapClass.Name,
				Title = mapClass.DisplayName,
				mapClass.Description,
				mapClass.Category,
				Kind = ClassKindOf( mapClass ),
				Keys = mapClass.Variables.Select( x => new
				{
					Key = x.Name,
					Title = x.LongName,
					Type = x.PropertyTypeOverride ?? x.PropertyType?.Name,
					Default = x.DefaultValue?.ToString(),
					x.Description,
					Group = x.GroupName
				} ).ToArray(),
				Inputs = mapClass.Inputs.Select( x => new { x.Name, Type = x.Type.ToString(), x.Description } ).ToArray(),
				Outputs = mapClass.Outputs.Select( x => new { x.Name, Type = x.Type.ToString(), x.Description } ).ToArray(),
				mapClass.Tags
			};
		}

		var q = query?.Trim() ?? "";
		var matches = GameData.EntityClasses
			.Where( x => x.IsPointClass || x.IsSolidClass || x.IsPathClass || x.IsCableClass )
			.Where( x => q.Length == 0
				|| x.Name.Contains( q, StringComparison.OrdinalIgnoreCase )
				|| (x.DisplayName?.Contains( q, StringComparison.OrdinalIgnoreCase ) ?? false)
				|| (x.Category?.Contains( q, StringComparison.OrdinalIgnoreCase ) ?? false)
				|| (x.Description?.Contains( q, StringComparison.OrdinalIgnoreCase ) ?? false) )
			.OrderBy( x => x.Name, StringComparer.Ordinal )
			.ToArray();

		return new
		{
			Total = matches.Length,
			Classes = matches.Take( limit ).Select( x => new { x.Name, Title = x.DisplayName, x.Category, Kind = ClassKindOf( x ), x.Description } ).ToArray()
		};
	}

	/// <summary>
	/// The active map, or a helpful error when there isn't one.
	/// </summary>
	static MapDocument ActiveMap()
	{
		if ( !Hammer.Open )
			throw new Exception( "Hammer isn't open - hammer_open_map opens a map in it" );

		var map = Hammer.ActiveMap;

		if ( !map.IsValid() )
			throw new Exception( "Hammer has no map open - hammer_open_map opens one" );

		return map;
	}

	/// <summary>
	/// Every node in the map's own world - nodes inside prefabs live in other worlds with their own ids.
	/// </summary>
	static IEnumerable<MapNode> Nodes( MapDocument map )
	{
		var world = map.World;
		return world.Children.Where( x => x.World == world );
	}

	static MapNode FindNode( MapDocument map, int id )
	{
		var node = map.World.FindNode( id );

		if ( !node.IsValid() || node is MapWorld )
			throw new Exception( $"No map node with id {id} - hammer_find_nodes lists node ids" );

		return node;
	}

	static Asset FindAsset( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new Exception( "Give an asset path, e.g. 'materials/dev/reflectivity_30.vmat'" );

		return AssetSystem.FindByPath( path.Trim() )
			?? throw new Exception( $"No asset at '{path}' - asset_search finds assets" );
	}

	static MapClass FindEntityClass( string name )
	{
		var mapClass = GameData.EntityClasses.FirstOrDefault( x => string.Equals( x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase ) );

		if ( mapClass is not null )
			return mapClass;

		var similar = GameData.EntityClasses
			.Where( x => x.Name.Contains( name?.Trim() ?? "", StringComparison.OrdinalIgnoreCase ) )
			.Select( x => x.Name ).Order().Take( 10 ).ToArray();

		throw new Exception( similar.Length > 0
			? $"No entity class '{name}'. Did you mean: {string.Join( ", ", similar )}"
			: $"No entity class '{name}' - hammer_entity_classes lists them" );
	}

	internal static string KindOf( MapNode node ) => node switch
	{
		MapGameObject => "GameObject",
		MapStaticOverlay => "Overlay",
		MapMesh => "Mesh",
		MapPathNode => "PathNode",
		MapPath => "Path",
		MapEntity => "Entity",
		MapGroup => "Group",
		MapInstance => "Instance",
		MapWorld => "World",
		_ => node.TypeString
	};

	static string ClassKindOf( MapClass mapClass ) =>
		mapClass.IsPointClass ? "Point" : mapClass.IsSolidClass ? "Solid" : mapClass.IsPathClass ? "Path" : mapClass.IsCableClass ? "Cable" : "Other";

	static string NameOf( MapNode node ) => (node is MapGameObject mgo ? mgo.GameObject?.Name : node.Name) ?? "";

	static string DescriptionOf( MapNode node ) => node.native.GetDescription() ?? "";

	static Dictionary<string, string> KeyValuesOf( MapEntity entity )
	{
		var keys = entity.MapClass?.Variables.Select( x => x.Name ) ?? [];

		return keys.Prepend( "targetname" )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.Select( x => (Key: x, Value: entity.GetKeyValue( x )) )
			.Where( x => x.Value is not null )
			.ToDictionary( x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase );
	}

	static MapNodeInfo Info( MapNode node )
	{
		var info = new MapNodeInfo();
		Fill( info, node );
		return info;
	}

	static void Fill( MapNodeInfo info, MapNode node )
	{
		info.Id = node.NodeId;
		info.Kind = KindOf( node );
		info.Name = NameOf( node );
		info.ClassName = (node as MapEntity)?.ClassName;
		info.Description = DescriptionOf( node );
		info.Position = node.Position;
		info.Parent = node.Parent is { } parent && parent is not MapWorld ? parent.NodeId : null;
		info.GameObject = (node as MapGameObject)?.GameObject?.Id;
		info.Selected = node.IsSelected;
		info.Visible = node.Visible;
	}

	/// <summary>
	/// Flag the map as modified, for edits Hammer's own change tracking doesn't see - game object
	/// component changes most of all.
	/// </summary>
	static void MarkModified( MapDocument map )
	{
		map.World?.EditorSession?.HasUnsavedChanges = true;
	}

	/// <summary>What hammer_status reports.</summary>
	public class HammerStatus
	{
		/// <summary>Whether the Hammer window exists.</summary>
		public bool Open { get; set; }

		/// <summary>Whether Hammer has a map open. Everything below is empty when it doesn't.</summary>
		public bool MapOpen { get; set; }

		/// <summary>The map's asset path. Null for a map that has never been saved.</summary>
		public string Map { get; set; }

		/// <summary>The map's file on disk.</summary>
		public string File { get; set; }

		public bool HasUnsavedChanges { get; set; }

		/// <summary>How many nodes of each kind the map has.</summary>
		public Dictionary<string, int> NodeCounts { get; set; }

		public string SelectionMode { get; set; }
		public int SelectedCount { get; set; }

		/// <summary>The material active in Hammer's material browser - what new brush geometry gets by default.</summary>
		public string CurrentMaterial { get; set; }

		/// <summary>How many 2D and 3D views are open on the map.</summary>
		public int ViewCount { get; set; }
	}

	/// <summary>A map node - enough to recognise it and find it again.</summary>
	public class MapNodeInfo
	{
		/// <summary>Hammer's node id, unique in the map and saved with it. Every hammer_ tool takes it.</summary>
		public int Id { get; set; }

		/// <summary>Entity, Mesh, GameObject, Group, Instance, Path, PathNode, Overlay, or Hammer's own type name for anything else.</summary>
		public string Kind { get; set; }

		public string Name { get; set; }

		/// <summary>The entity class, for entities.</summary>
		public string ClassName { get; set; }

		/// <summary>Hammer's one line description, like 'Mesh (6 faces)'.</summary>
		public string Description { get; set; }

		public Vector3 Position { get; set; }

		/// <summary>The parent node's id. Null at the top level of the map.</summary>
		public int? Parent { get; set; }

		/// <summary>The game object's guid, for GameObject nodes.</summary>
		public Guid? GameObject { get; set; }

		public bool Selected { get; set; }
		public bool Visible { get; set; }
	}

	/// <summary>Everything about one map node.</summary>
	public class MapNodeDetails : MapNodeInfo
	{
		public Angles Angles { get; set; }
		public Vector3 Scale { get; set; }

		public int ChildCount { get; set; }

		/// <summary>The direct children, at most 200.</summary>
		public MapNodeInfo[] Children { get; set; }

		/// <summary>The entity class's display name, for entities.</summary>
		public string EntityClassTitle { get; set; }

		/// <summary>Every keyvalue of an entity, as Hammer stores them.</summary>
		public Dictionary<string, string> KeyValues { get; set; }

		/// <summary>The materials on a mesh's faces.</summary>
		public string[] Materials { get; set; }

		/// <summary>A game object's components.</summary>
		public ComponentInfo[] Components { get; set; }

		/// <summary>A game object's tags.</summary>
		public string[] Tags { get; set; }

		/// <summary>The node an instance copies.</summary>
		public int? InstanceTarget { get; set; }
	}

	/// <summary>One component on a map game object.</summary>
	public class ComponentInfo
	{
		public string Type { get; set; }
		public Guid Id { get; set; }
		public bool Enabled { get; set; }

		/// <summary>The serialized properties. Null unless includeComponentProperties was set.</summary>
		public JsonNode Properties { get; set; }
	}

	/// <summary>What hammer_find_nodes matched.</summary>
	public class FoundNodes
	{
		/// <summary>How many matched in total, beyond what's shown.</summary>
		public int Total { get; set; }
		public MapNodeInfo[] Nodes { get; set; }
	}

	/// <summary>Hammer's selection.</summary>
	public class HammerSelection
	{
		public string Mode { get; set; }
		public Vector3 Pivot { get; set; }
		public int Count { get; set; }

		/// <summary>The selected nodes, at most 200.</summary>
		public MapNodeInfo[] Nodes { get; set; }
	}

	/// <summary>What hammer_trace hit.</summary>
	public class HammerTraceHit
	{
		public bool Hit { get; set; }

		/// <summary>The hit point, or the end of the ray on a miss.</summary>
		public Vector3 Position { get; set; }

		public Vector3 Normal { get; set; }

		/// <summary>The node hit. Null on a miss.</summary>
		public MapNodeInfo Node { get; set; }
	}
}
