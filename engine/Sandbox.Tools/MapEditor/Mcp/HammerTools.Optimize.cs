using Editor.MapDoc;
using Editor.MapEditor;
using System;
using System.IO;
using System.Text.Json.Nodes;
using MapInstance = Editor.MapDoc.MapInstance;

namespace Editor.Mcp;

internal static partial class HammerTools
{
	const string NodrawMaterial = "materials/tools/toolsnodraw.vmat";

	[McpTool.ReadOnly( "hammer_asset_usage" )]
	[Description( "Which assets the map uses and how heavily - mesh materials with how many faces, triangles and square units of surface they cover, models and other assets in entity keyvalues, and resources on game object components. Counts the map as placed: everything inside a prefab counts once per placement, nested prefabs included, so Nodes, Faces and Triangles track what gets drawn. With no asset, lists every asset sorted by 'sortBy'; give an asset to list where it's placed - inside a prefab, that's the prefab node placed in the map." )]
	public static object AssetUsage(
		[Description( "An asset path to find users of. Empty summarises everything." )] string asset = "",
		[Description( "Sort the summary by Nodes, Faces, Triangles or Area." )] string sortBy = "Nodes",
		[Description( "Count what's inside prefabs placed in the map, not just the map's own nodes." )] bool includePrefabs = true,
		[Description( "How many entries to return." ), Range( 1, 1000 )] int limit = 200 )
	{
		var map = ActiveMap();
		var placements = includePrefabs ? PlacedNodes( map ).Placements : Nodes( map ).Select( x => new Placement( x, x, null ) ).ToList();

		// The same source node is placed many times through prefabs - read its assets once
		var cache = new Dictionary<MapNode, (MapNode Node, string Path, int Faces, int Triangles, float Area)[]>();
		var uses = placements
			.SelectMany( p => (cache.TryGetValue( p.Node, out var u ) ? u : cache[p.Node] = UsesOf( p.Node ).ToArray()).Select( u => (Placement: p, Use: u) ) )
			.ToArray();

		if ( !string.IsNullOrWhiteSpace( asset ) )
		{
			var path = NormalizeAssetPath( asset );
			var users = uses.Where( x => string.Equals( x.Use.Path, path, StringComparison.OrdinalIgnoreCase ) )
				.GroupBy( x => x.Placement.Owner )
				.Select( x => new
				{
					Node = Info( x.Key ),
					Prefabs = x.Select( y => y.Placement.Prefab ).Where( y => y is not null ).Distinct().ToArray(),
					Uses = x.Count(),
					Faces = x.Sum( y => y.Use.Faces ),
					Triangles = x.Sum( y => y.Use.Triangles )
				} )
				.OrderByDescending( x => x.Uses )
				.ToArray();

			return new { Asset = path, Placements = users.Sum( x => x.Uses ), Total = users.Length, Nodes = users.Take( limit ).ToArray() };
		}

		var assets = uses
			.GroupBy( x => x.Use.Path, StringComparer.OrdinalIgnoreCase )
			.Select( x => new AssetUse
			{
				Asset = x.Key,
				Type = AssetSystem.FindByPath( x.Key )?.AssetType?.FriendlyName ?? TypeFromExtension( x.Key ),
				Installed = AssetSystem.FindByPath( x.Key ) is not null,
				Nodes = x.Count(),
				Faces = x.Sum( y => y.Use.Faces ),
				Triangles = x.Sum( y => y.Use.Triangles ),
				Area = MathF.Round( x.Sum( y => y.Use.Area ) )
			} );

		var sorted = sortBy?.Trim().ToLowerInvariant() switch
		{
			"faces" => assets.OrderByDescending( x => x.Faces ),
			"triangles" => assets.OrderByDescending( x => x.Triangles ),
			"area" => assets.OrderByDescending( x => x.Area ),
			_ => assets.OrderByDescending( x => x.Nodes )
		};

		var all = sorted.ThenBy( x => x.Asset ).ToArray();
		var missing = all.Where( x => !x.Installed ).ToArray();

		return new
		{
			Total = all.Length,
			NotInstalled = missing.Length,
			Hint = missing.Length > 0 ? "Some referenced assets aren't installed - usually cloud packages the map's EditorReferences list but this project hasn't downloaded. Their types come from the file extension." : null,
			Assets = all.Take( limit ).ToArray()
		};
	}

	[McpTool.ReadOnly( "hammer_prefab_placements" )]
	[Description( "Every prefab map the open map places, and how many times it ends up in the world - nested prefabs count once for every placement of the prefab holding them. Also how many nodes each placement adds directly (not counting nested prefabs), so you can see which prefabs multiply the most content." )]
	public static object PrefabPlacements()
	{
		var map = ActiveMap();
		var placed = PlacedNodes( map );

		var prefabs = placed.PrefabPlacements
			.Select( x => new
			{
				Prefab = x.Key,
				Placements = x.Value,
				NodesPerPlacement = placed.Placements.Count( p => p.Prefab == x.Key ) / x.Value
			} )
			.OrderByDescending( x => x.Placements ).ThenBy( x => x.Prefab )
			.ToArray();

		return new { PlacedNodes = placed.Placements.Count, Prefabs = prefabs };
	}

	[McpTool.ReadOnly( "hammer_mesh_stats" )]
	[Description( "Geometry cost of the map's meshes - per mesh its face and triangle count, size, and faces per material, heaviest first, plus map totals. Use it to find the meshes worth simplifying." )]
	public static object MeshStats(
		[Description( "Mesh node ids to measure. Empty measures every mesh." )] int[] ids = null,
		[Description( "Sort by Triangles, Faces or Size (bounding box volume)." )] string sortBy = "Triangles",
		[Description( "How many meshes to list." ), Range( 1, 1000 )] int limit = 50 )
	{
		var map = ActiveMap();
		var meshes = MeshesOf( map, ids );

		var stats = meshes.Select( mesh =>
		{
			var faces = mesh.GetFaces();
			var bounds = faces.Count == 0 ? new BBox( mesh.Position, mesh.Position ) : BBox.FromPoints( faces.SelectMany( x => x.Vertices ) );

			return new
			{
				mesh.NodeId,
				Kind = KindOf( mesh ),
				Faces = faces.Count,
				Triangles = faces.Sum( x => x.TriangleCount ),
				Size = bounds.Size,
				Center = bounds.Center,
				Materials = faces.GroupBy( x => x.Material ?? "" ).OrderByDescending( x => x.Count() ).ToDictionary( x => x.Key, x => x.Count() )
			};
		} ).ToArray();

		var sorted = sortBy?.Trim().ToLowerInvariant() switch
		{
			"faces" => stats.OrderByDescending( x => x.Faces ),
			"size" => stats.OrderByDescending( x => x.Size.x * x.Size.y * x.Size.z ),
			_ => stats.OrderByDescending( x => x.Triangles )
		};

		return new
		{
			Meshes = stats.Length,
			Faces = stats.Sum( x => x.Faces ),
			Triangles = stats.Sum( x => x.Triangles ),
			Results = sorted.Take( limit ).ToArray()
		};
	}

	[McpTool.ReadOnly( "hammer_mesh_faces" )]
	[Description( "A mesh's individual faces - each one's index, material, normal, center, area and triangle count. Face indices are what hammer_set_face_material takes." )]
	public static object MeshFaces(
		[Description( "The mesh node id." )] int id,
		[Description( "Include each face's corner positions." )] bool includeVertices = false,
		[Description( "How many faces to return." ), Range( 1, 5000 )] int limit = 500 )
	{
		var mesh = FindNode( ActiveMap(), id ) as MapMesh
			?? throw new Exception( $"Node {id} isn't a mesh - hammer_find_nodes kind:Mesh lists them" );

		var faces = mesh.GetFaces();

		return new
		{
			Total = faces.Count,
			Faces = faces.Take( limit ).Select( x => new
			{
				x.Index,
				x.Material,
				x.Normal,
				x.Center,
				Area = MathF.Round( x.Area, 1 ),
				Triangles = x.TriangleCount,
				Vertices = includeVertices ? x.Vertices : null
			} ).ToArray()
		};
	}

	[McpTool( "hammer_set_face_material" )]
	[Description( "Paint individual faces of a mesh with a material, by face index from hammer_mesh_faces. The user can undo this." )]
	public static object SetFaceMaterial(
		[Description( "The mesh node id." )] int id,
		[Description( "Face indices from hammer_mesh_faces." )] int[] faces,
		[Description( "The material to paint them with." )] string material )
	{
		var map = ActiveMap();
		var mesh = FindNode( map, id ) as MapMesh
			?? throw new Exception( $"Node {id} isn't a mesh - hammer_find_nodes kind:Mesh lists them" );

		var count = mesh.GetFaces().Count;
		var bad = (faces ?? []).Where( x => x < 0 || x >= count ).ToArray();

		if ( faces is null || faces.Length == 0 )
			throw new Exception( "Give at least one face index - hammer_mesh_faces lists them" );

		if ( bad.Length > 0 )
			throw new Exception( $"Mesh {id} has {count} faces, so {string.Join( ", ", bad )} aren't face indices" );

		var materialPath = FindMaterial( material );

		History.MarkUndoPosition( "Set Face Material" );
		History.Keep( mesh );

		mesh.SetFaceMaterial( faces, Material.Load( materialPath ) );
		MarkModified( map );

		return $"Painted {faces.Distinct().Count()} faces of mesh {id} with {materialPath}";
	}

	[McpTool( "hammer_find_hidden_faces" )]
	[Description( "Find mesh faces nobody can see because they're buried inside or pressed flat against other geometry - the underside of a crate sitting on a floor, wall ends tucked into other walls. They still cost rendering and lightmap space. By default this only reports; set apply to paint them nodraw (or another material) in one undo step. A face counts as hidden only if every sample point on it is enclosed by opaque, non-tools mesh geometry, so partly covered faces are left alone. Meshes that aren't closed can fool it - check the result with hammer_focus_nodes and hammer_screenshot before applying on a map you care about." )]
	public static object FindHiddenFaces(
		[Description( "Mesh node ids to check. Empty checks every mesh." )] int[] ids = null,
		[Description( "Paint the hidden faces with 'material'. False only reports them." )] bool apply = false,
		[Description( "The material to paint hidden faces with." )] string material = NodrawMaterial,
		[Description( "How many meshes to list in the report." ), Range( 1, 1000 )] int limit = 100 )
	{
		var map = ActiveMap();
		var meshes = MeshesOf( map, ids );
		var materialPath = apply ? FindMaterial( material ) : null;

		// Anything in the map can cover a face, not just the meshes being checked
		var occluders = new MeshOccluders( Nodes( map ).OfType<MapMesh>() );
		var toolsMaterials = new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );

		var results = new List<(MapMesh Mesh, MapMeshFace[] Faces)>();
		var checkedFaces = 0;

		foreach ( var mesh in meshes )
		{
			var hidden = new List<MapMeshFace>();

			foreach ( var face in occluders.Faces[mesh] )
			{
				// Tools faces don't render anyway, and painting over them would change what they do
				var path = face.Material ?? "";
				if ( !toolsMaterials.TryGetValue( path, out var isTools ) )
					toolsMaterials[path] = isTools = Material.Load( path )?.Flags.GetInt( "tools.toolsmaterial" ) is not 0;

				if ( isTools )
					continue;

				checkedFaces++;

				if ( IsFaceHidden( occluders, face ) )
					hidden.Add( face );
			}

			if ( hidden.Count > 0 )
				results.Add( (mesh, hidden.ToArray()) );
		}

		if ( apply && results.Count > 0 )
		{
			var nodraw = Material.Load( materialPath );

			History.MarkUndoPosition( "Nodraw Hidden Faces" );

			foreach ( var (mesh, faces) in results )
			{
				History.Keep( mesh );
				mesh.SetFaceMaterial( faces.Select( x => x.Index ), nodraw );
			}

			MarkModified( map );
		}

		return new
		{
			CheckedFaces = checkedFaces,
			HiddenFaces = results.Sum( x => x.Faces.Length ),
			HiddenTriangles = results.Sum( x => x.Faces.Sum( y => y.TriangleCount ) ),
			HiddenArea = MathF.Round( results.Sum( x => x.Faces.Sum( y => y.Area ) ) ),
			Applied = apply ? materialPath : null,
			Meshes = results
				.OrderByDescending( x => x.Faces.Length )
				.Take( limit )
				.Select( x => new { Id = x.Mesh.NodeId, Faces = x.Faces.Select( y => y.Index ).ToArray(), Materials = x.Faces.Select( y => y.Material ).Distinct().ToArray() } )
				.ToArray()
		};
	}

	[McpTool( "hammer_overlays_to_decals" )]
	[Description( "Replace overlays with Decal game objects - both static overlays (kind Overlay) and info_overlay entities. Each overlay's material becomes a .decal asset (made once per material from its color, normal and roughness textures, reused if one already exists in the folder), and a game object with a Decal component goes on the surface the overlay covered, the same size and facing the same way. One undo step covers the whole swap. Set dryRun to see what would happen first." )]
	public static async Task<object> OverlaysToDecals(
		[Description( "Overlay node ids to convert. Empty converts every overlay in the map." )] int[] ids = null,
		[Description( "Folder in the project's assets for the generated .decal files." )] string decalFolder = "decals/overlays",
		[Description( "Keep the overlays after creating the decals, instead of deleting them." )] bool keepOverlays = false,
		[Description( "Only report what would be converted - create and delete nothing." )] bool dryRun = false )
	{
		var map = ActiveMap();

		var overlays = (ids is { Length: > 0 } ? ResolveNodes( map, ids ) : Nodes( map ).ToArray())
			.Where( IsOverlay )
			.ToArray();

		if ( ids is { Length: > 0 } && overlays.Length != ids.Distinct().Count() )
			throw new Exception( "Some of those nodes aren't overlays - hammer_find_nodes kind:Overlay or className:info_overlay finds them" );

		// Overlays float off the surface they cover and project onto it, decals have to sit on it
		var surfaces = new MeshOccluders( Nodes( map ).OfType<MapMesh>().Where( x => x is not MapStaticOverlay ) );
		var plans = overlays.Select( x => PlanDecal( x, surfaces ) ).ToArray();
		var ready = plans.Where( x => x.Error is null ).ToArray();
		var skipped = plans.Where( x => x.Error is not null ).Select( x => new { Overlay = x.OverlayId, Reason = x.Error } ).ToArray();

		if ( dryRun || ready.Length == 0 )
		{
			return new
			{
				DryRun = dryRun,
				Converted = 0,
				Overlays = ready.Select( x => new { Overlay = x.OverlayId, Material = x.Material.ResourcePath, x.Size, x.Center } ).ToArray(),
				Skipped = skipped
			};
		}

		// Make the .decal assets first - a freshly compiled asset only loads a few frames later,
		// and nothing in the map should change until every one of them has
		var created = new List<string>();
		var assets = ready.Select( x => x.Material ).Distinct()
			.ToDictionary( x => x, x => EnsureDecalAsset( decalFolder, x, created ) );

		var definitions = new Dictionary<Material, DecalDefinition>();

		for ( int frame = 0; frame < 100 && definitions.Count < assets.Count; frame++ )
		{
			foreach ( var (material, asset) in assets )
			{
				if ( !definitions.ContainsKey( material ) && asset.TryLoadResource<DecalDefinition>( out var definition ) )
					definitions[material] = definition;
			}

			if ( definitions.Count < assets.Count )
			{
				await Task.Delay( 50 );
				await MainThread.Wait();
			}
		}

		var missing = assets.Where( x => !definitions.ContainsKey( x.Key ) ).Select( x => x.Value.Path ).ToArray();

		if ( missing.Length > 0 )
			throw new Exception( $"Couldn't load {string.Join( ", ", missing )} - nothing in the map was changed. read_console may say why" );

		map = ActiveMap();
		History.MarkUndoPosition( $"Convert {ready.Length} Overlays to Decals" );

		var converted = new List<object>();

		using ( map.World.Scene.Push() )
		{
			foreach ( var plan in ready )
			{
				if ( !plan.Overlay.IsValid() )
					continue;

				var definition = definitions[plan.Material];

				var go = new GameObject( true, $"Decal {Path.GetFileNameWithoutExtension( plan.Material.ResourcePath )}" );
				go.WorldPosition = plan.Center;
				go.WorldRotation = plan.Rotation;

				var decal = go.Components.Create<Decal>();
				decal.Decals = [definition];
				decal.Size = new Vector2( plan.Size.x / MathF.Max( definition.Width, 0.01f ), plan.Size.y / MathF.Max( definition.Height, 0.01f ) );
				decal.Rotation = 0f;
				decal.Scale = 1f;
				decal.ColorTint = TintOf( plan.Material );

				var node = new MapGameObject( map, go );
				History.KeepNew( node );

				if ( !keepOverlays )
					map.DeleteNode( plan.Overlay );

				converted.Add( new { Overlay = plan.OverlayId, Decal = node.NodeId, Material = plan.Material.ResourcePath, plan.Size } );
			}
		}

		MarkModified( map );

		return new
		{
			DryRun = false,
			Converted = converted.Count,
			Overlays = converted,
			CreatedDecalAssets = created,
			Skipped = skipped
		};
	}

	[McpTool( "hammer_build_map" )]
	[Description( "Compile the open map, like pressing Build in Hammer's Build Map dialog (which opens to show progress). Saves the map first, since the compile reads what's on disk. Runs in the background - poll hammer_build_status until Building is false, then hammer_compiled_map_stats breaks down what was built. Doesn't launch the map unless loadInEngine is set." )]
	public static object BuildMap(
		[Description( "Full, Fast (no vis or lighting - quickest), Final (final quality lighting), EntitiesOnly, or Custom for the dialog's current settings." )] MapBuildPreset preset = MapBuildPreset.Fast,
		[Description( "Load the map in engine when it's built, if the dialog is set to." )] bool loadInEngine = false )
	{
		ActiveMap();

		if ( Hammer.IsBuildingMap )
			throw new Exception( "A build is already running - hammer_build_status shows its progress" );

		if ( Hammer.MapAsset is null )
			throw new Exception( "This map has never been saved - the user needs to save it in Hammer before it can be built" );

		if ( Hammer.HasUnsavedChanges && !Hammer.Save() )
			throw new Exception( "Couldn't save the map before building - read_console may say why" );

		if ( !Hammer.BuildMap( preset, loadInEngine ) )
			throw new Exception( $"The build didn't start: {Hammer.BuildStatus}. A dialog asking the user something may be open." );

		return $"Building {Hammer.MapAsset.Path} ({preset}) - poll hammer_build_status";
	}

	[McpTool.ReadOnly( "hammer_build_status" )]
	[Description( "Progress of the map compile hammer_build_map started - whether it's still running, the status line (Done, Failed...), any errors and warnings it printed, and the tail of its log." )]
	public static object BuildStatus( [Description( "How many lines from the end of the log to include." ), Range( 0, 2000 )] int logLines = 40 )
	{
		ActiveMap();

		// The dialog's text box separates lines with unicode line and paragraph separators
		var log = (Hammer.BuildLog ?? "").Split( ['\n', '\u2028', '\u2029'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

		return new
		{
			Building = Hammer.IsBuildingMap,
			Status = Hammer.BuildStatus,
			Problems = log.Where( x => (x.Contains( "error", StringComparison.OrdinalIgnoreCase )
					|| x.Contains( "warning", StringComparison.OrdinalIgnoreCase )
					|| x.Contains( "missing", StringComparison.OrdinalIgnoreCase ))
				// the compiler's own shutdown chatter, not the map's problem
				&& !x.Contains( "was not unregistered", StringComparison.OrdinalIgnoreCase )
				&& !x.Contains( "pipeline cache", StringComparison.OrdinalIgnoreCase ) ).Take( 100 ).ToArray(),
			Log = log.TakeLast( logLines ).ToArray()
		};
	}

	[McpTool.ReadOnly( "hammer_compiled_map_stats" )]
	[Description( "What the compiled map (.vpk) is made of - total size, size by kind (world geometry, lightmaps, physics, vis, entities...), the largest files, and whether it's older than the map's last save. Build with hammer_build_map first." )]
	public static object CompiledMapStats( [Description( "How many of the largest files to list." ), Range( 1, 500 )] int largest = 20 )
	{
		ActiveMap();

		var path = Hammer.CompiledMapPath
			?? throw new Exception( "This map has never been saved, so it has never been built" );

		if ( !File.Exists( path ) )
			throw new Exception( $"No compiled map at {path} - build it with hammer_build_map" );

		var files = VpkDirectory.Read( path );
		var mapFile = Hammer.MapAsset?.AbsolutePath;

		return new
		{
			Vpk = path,
			Built = File.GetLastWriteTime( path ).ToString( "yyyy-MM-dd HH:mm:ss" ),
			OutOfDate = mapFile is not null && File.Exists( mapFile ) && File.GetLastWriteTime( mapFile ) > File.GetLastWriteTime( path ),
			TotalBytes = new FileInfo( path ).Length,
			Files = files.Count,
			ByKind = files.GroupBy( x => CompiledKindOf( x.Path ) )
				.Select( x => new { Kind = x.Key, Files = x.Count(), Bytes = x.Sum( y => y.Size ) } )
				.OrderByDescending( x => x.Bytes )
				.ToArray(),
			// World geometry is batched per material, and the batch files are named after it - "..._mt_concrete_wall.vmesh_c"
			WorldGeometryByMaterial = files.Where( x => x.Path.Contains( "/worldnodes/", StringComparison.OrdinalIgnoreCase ) && x.Path.Contains( "_mt_" ) )
				.GroupBy( x => Path.GetFileNameWithoutExtension( x.Path )[(Path.GetFileNameWithoutExtension( x.Path ).IndexOf( "_mt_" ) + 4)..] )
				.Select( x => new { Material = x.Key, Batches = x.Count(), Bytes = x.Sum( y => y.Size ) } )
				.OrderByDescending( x => x.Bytes )
				.ToArray(),
			Largest = files.OrderByDescending( x => x.Size ).Take( largest ).Select( x => new { x.Path, Bytes = x.Size } ).ToArray()
		};
	}

	/// <summary>
	/// One asset's usage, for hammer_asset_usage.
	/// </summary>
	public class AssetUse
	{
		public string Asset { get; set; }
		public string Type { get; set; }

		/// <summary>False when the map references it but it isn't installed - usually a cloud package that hasn't been downloaded.</summary>
		public bool Installed { get; set; }

		/// <summary>How many times it's placed - a prefab's contents count once per placement of the prefab.</summary>
		public int Nodes { get; set; }

		/// <summary>For materials, how many mesh faces use it.</summary>
		public int Faces { get; set; }

		/// <summary>For materials, how many triangles those faces render as.</summary>
		public int Triangles { get; set; }

		/// <summary>For materials, the surface area those faces cover in square units.</summary>
		public float Area { get; set; }
	}

	/// <summary>
	/// Every asset a node uses, with face counts for mesh materials.
	/// </summary>
	static IEnumerable<(MapNode Node, string Path, int Faces, int Triangles, float Area)> UsesOf( MapNode node )
	{
		if ( node is MapMesh mesh )
		{
			return mesh.GetFaces()
				.GroupBy( x => x.Material ?? "" )
				.Where( x => x.Key.Length > 0 )
				.Select( x => (node, NormalizeAssetPath( x.Key ), x.Count(), x.Sum( y => y.TriangleCount ), x.Sum( y => y.Area )) );
		}

		return AssetsUsedBy( node ).Select( x => (node, x, 0, 0, 0f) );
	}

	/// <summary>
	/// One node as it ends up in the world. A node inside a prefab gets one of these per placement of
	/// that prefab. Owner is the node placed in the map itself that brought it in - the only kind of node
	/// the other tools can address - and Prefab the prefab map it came from, null for the map's own nodes.
	/// </summary>
	internal readonly record struct Placement( MapNode Node, MapNode Owner, string Prefab );

	/// <summary>
	/// Walk the map as placed. Hammer loads each prefab map once and shares its world between every
	/// prefab node pointing at it, so walking children alone sees a prefab's contents once however many
	/// times it's placed. Instead, expand every instance through its target each time it's placed.
	/// </summary>
	internal static (List<Placement> Placements, Dictionary<string, int> PrefabPlacements) PlacedNodes( MapDocument map )
	{
		var root = map.World;
		var placements = new List<Placement>();
		var prefabCounts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

		// Instance targets are templates - they appear through their instances, never on their own
		var targets = root.Children.OfType<MapInstance>().Select( x => x.Target ).Where( x => x.IsValid() ).ToHashSet();

		foreach ( var node in OwnNodes( root, targets ) )
		{
			Place( node, node, null, 0 );
		}

		return (placements, prefabCounts);

		void Place( MapNode node, MapNode owner, string prefab, int depth )
		{
			placements.Add( new Placement( node, owner, prefab ) );

			if ( node is not MapInstance instance || depth >= 16 || !instance.Target.IsValid() )
				return;

			var target = instance.Target;

			if ( target is MapWorld world )
			{
				// A prefab world's own MapPathName resolves through the root document - the map placing it
				var path = instance.TargetMapPath?.Replace( '\\', '/' ).ToLowerInvariant() ?? $"world {world.NodeId}";
				prefabCounts[path] = prefabCounts.GetValueOrDefault( path ) + 1;

				foreach ( var child in OwnNodes( world, targets ) )
				{
					Place( child, owner, path, depth + 1 );
				}
			}
			else
			{
				// A local instance copies a group in the same map
				foreach ( var child in SubtreeOf( target ) )
				{
					Place( child, owner, prefab, depth + 1 );
				}
			}
		}
	}

	/// <summary>
	/// A world's own nodes - not those of the prefab worlds inside it, and not instance templates.
	/// </summary>
	static IEnumerable<MapNode> OwnNodes( MapWorld world, HashSet<MapNode> targets )
	{
		return world.Children.Where( x => x is not MapWorld && x.World == world && !IsInsideTarget( x, world, targets ) );
	}

	static bool IsInsideTarget( MapNode node, MapWorld world, HashSet<MapNode> targets )
	{
		for ( var current = node; current.IsValid() && current != world; current = current.Parent )
		{
			if ( targets.Contains( current ) ) return true;
		}

		return false;
	}

	static IEnumerable<MapNode> SubtreeOf( MapNode node )
	{
		yield return node;

		foreach ( var child in node.Children )
		{
			foreach ( var descendant in SubtreeOf( child ) )
				yield return descendant;
		}
	}

	/// <summary>
	/// A type name for an asset that isn't installed, from its extension.
	/// </summary>
	static string TypeFromExtension( string path ) => Path.GetExtension( path ).ToLowerInvariant() switch
	{
		".vmat" => "Material",
		".vmdl" => "Model",
		".prefab" => "Prefab",
		".vpcf" => "Particle System",
		".vsnd" or ".sound" => "Sound",
		".vtex" or ".png" or ".jpg" or ".tga" or ".psd" => "Texture",
		".vmap" => "Map",
		var extension => extension.TrimStart( '.' )
	};

	static MapMesh[] MeshesOf( MapDocument map, int[] ids )
	{
		if ( ids is { Length: > 0 } )
		{
			return ResolveNodes( map, ids ).Select( x => x as MapMesh
				?? throw new Exception( $"Node {x.NodeId} is a {KindOf( x )}, not a mesh" ) ).ToArray();
		}

		return Nodes( map ).OfType<MapMesh>().ToArray();
	}

	/// <summary>
	/// A face is hidden when every sample on it - its center and each corner pulled slightly inwards -
	/// sits inside solid geometry, checked in two directions so a lone one sided plane is less likely
	/// to count as solid.
	/// </summary>
	static bool IsFaceHidden( MeshOccluders occluders, MapMeshFace face )
	{
		if ( face.Vertices.Count < 3 || face.Area < 0.01f )
			return false;

		var normal = face.Normal.Normal;
		var tangent = (face.Vertices[1] - face.Vertices[0]).Normal;
		var tilted = (normal + tangent * 0.7f).Normal;

		var samples = face.Vertices.Select( x => Vector3.Lerp( x, face.Center, 0.1f ) ).Prepend( face.Center );

		foreach ( var sample in samples )
		{
			var start = sample + normal * 0.5f;

			if ( !occluders.StartsInside( start, normal ) || !occluders.StartsInside( start, tilted ) )
				return false;
		}

		return true;
	}

	static bool IsOverlay( MapNode node ) => node is MapStaticOverlay || (node is MapEntity e && string.Equals( e.ClassName, "info_overlay", StringComparison.OrdinalIgnoreCase ));

	/// <summary>
	/// Where a decal replacing an overlay goes - center, facing and size - or why it can't be made.
	/// </summary>
	record DecalPlan( MapNode Overlay, int OverlayId, Material Material, Vector3 Center, Rotation Rotation, Vector2 Size, string Error );

	static DecalPlan PlanDecal( MapNode overlay, MeshOccluders surfaces )
	{
		DecalPlan Fail( string error ) => new( overlay, overlay.NodeId, null, default, default, default, error );

		var frame = Rotation.From( overlay.Angles );
		string materialPath;
		Vector3 normal, center;
		Vector2 size;

		if ( overlay is MapStaticOverlay staticOverlay )
		{
			var faces = staticOverlay.GetFaces();
			if ( faces.Count == 0 ) return Fail( "The overlay has no faces" );

			materialPath = faces.GroupBy( x => x.Material ).OrderByDescending( x => x.Sum( y => y.Area ) ).First().Key;

			// Area weighted normal - overlays projected over corners bend, the dominant surface wins
			normal = faces.Aggregate( Vector3.Zero, ( sum, x ) => sum + x.Normal * x.Area ).Normal;
			if ( normal.IsNearZeroLength ) normal = frame.Up;

			var (u, v) = PlaneAxes( normal, frame );
			var points = faces.SelectMany( x => x.Vertices ).ToArray();
			var origin = points.Aggregate( Vector3.Zero, ( a, b ) => a + b ) / points.Length;

			float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;

			foreach ( var p in points )
			{
				var du = Vector3.Dot( p - origin, u );
				var dv = Vector3.Dot( p - origin, v );
				minU = MathF.Min( minU, du ); maxU = MathF.Max( maxU, du );
				minV = MathF.Min( minV, dv ); maxV = MathF.Max( maxV, dv );
			}

			center = origin + u * ((minU + maxU) * 0.5f) + v * ((minV + maxV) * 0.5f);
			size = new Vector2( maxU - minU, maxV - minV );

			return Finish( u, v );
		}

		var entity = (MapEntity)overlay;
		materialPath = entity.GetKeyValue( "material" );
		normal = frame.Up;
		center = entity.Position;

		var material = string.IsNullOrWhiteSpace( materialPath ) ? null : Material.Load( materialPath );
		if ( material is null ) return Fail( $"Couldn't load its material '{materialPath}'" );

		var width = entity.GetKeyValue( "width" ).ToFloat( -1 );
		var height = entity.GetKeyValue( "height" ).ToFloat( -1 );

		size = new Vector2(
			width > 0 ? width : NativeHammer.Global.MaterialGetMappingWidth( material.native ),
			height > 0 ? height : NativeHammer.Global.MaterialGetMappingHeight( material.native ) );

		return Finish( frame.Forward, frame.Left );

		DecalPlan Finish( Vector3 u, Vector3 v )
		{
			var mat = Material.Load( materialPath );
			if ( mat is null ) return Fail( $"Couldn't load its material '{materialPath}'" );
			if ( size.x < 0.1f || size.y < 0.1f ) return Fail( "It has no size" );

			// Drop onto the surface the overlay covers - from a little in front, in case it's flush
			if ( surfaces.Raycast( center + normal, -normal, 256, out var distance ) )
				center = center + normal - normal * distance;

			// The decal projects along its forward axis into the surface, its left and up axes are the texture's width and height
			var rotation = Rotation.LookAt( -normal, v );

			return new DecalPlan( overlay, overlay.NodeId, mat, center, rotation, size, null );
		}
	}

	/// <summary>
	/// Two axes in the plane with this normal, lined up with the overlay's own rotation where possible.
	/// </summary>
	static (Vector3 U, Vector3 V) PlaneAxes( Vector3 normal, Rotation frame )
	{
		var u = frame.Forward - normal * Vector3.Dot( frame.Forward, normal );

		if ( u.Length < 0.01f )
			u = frame.Left - normal * Vector3.Dot( frame.Left, normal );

		u = u.Normal;
		var v = Vector3.Cross( normal, u ).Normal;

		return (u, v);
	}

	/// <summary>
	/// The .decal asset for a material - one already in the folder, or a new one made from the material's
	/// textures. A new one is compiled but only loads a few frames later.
	/// </summary>
	static Asset EnsureDecalAsset( string folder, Material material, List<string> created )
	{
		var name = Path.GetFileNameWithoutExtension( material.ResourcePath );
		var relative = $"{(folder ?? "").Replace( '\\', '/' ).Trim().Trim( '/' )}/{name}.decal".TrimStart( '/' );

		var existing = AssetSystem.FindByPath( relative );
		if ( existing is not null )
			return existing;

		var assetsPath = Project.Current?.GetAssetsPath()
			?? throw new Exception( "No project is open to create .decal assets in" );

		var absolute = Path.Combine( assetsPath, relative );
		Directory.CreateDirectory( Path.GetDirectoryName( absolute ) );

		var asset = AssetSystem.CreateResource( "decal", absolute )
			?? throw new Exception( $"Couldn't create {relative}" );

		var materialAsset = AssetSystem.FindByPath( material.ResourcePath );

		var json = new JsonObject
		{
			["ColorTexture"] = TexturePath( material, materialAsset, "g_tColor" ),
			["NormalTexture"] = TexturePath( material, materialAsset, "g_tNormal" ),
			["RoughMetalOcclusionTexture"] = TexturePath( material, materialAsset, "g_tRma" ),
			["Width"] = NativeHammer.Global.MaterialGetMappingWidth( material.native ),
			["Height"] = NativeHammer.Global.MaterialGetMappingHeight( material.native )
		};

		File.WriteAllText( asset.GetSourceFile( true ), json.ToJsonString() );
		asset.Compile( false );

		created.Add( asset.Path );
		return asset;
	}

	/// <summary>
	/// The colour an overlay material tints its texture with, so the decal keeps the look. White when it has none.
	/// </summary>
	static Color TintOf( Material material )
	{
		var tint = material.GetVector4( "g_vColorTint" );

		// Unset parameters read back as zero - a black tint is never what an overlay meant
		return tint.x + tint.y + tint.z <= 0.0001f ? Color.White : new Color( tint.x, tint.y, tint.z, 1 );
	}

	/// <summary>
	/// The asset path of the texture a material binds to a shader parameter. Textures a material
	/// generates from its source images have no path of their own, so match the bound texture
	/// against the textures the material asset references.
	/// </summary>
	static string TexturePath( Material material, Asset materialAsset, string parameter )
	{
		var texture = material.GetTexture( parameter );
		if ( texture is null || texture.IsError )
			return null;

		if ( !string.IsNullOrEmpty( texture.ResourcePath ) )
			return texture.ResourcePath;

		var guid = texture.native.GetGuid();

		foreach ( var reference in materialAsset?.GetReferences( false ) ?? [] )
		{
			if ( !reference.Path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var candidate = Texture.Load( reference.Path, false );

			if ( candidate is not null && (ReferenceEquals( candidate, texture ) || candidate.native.GetGuid() == guid) )
				return reference.Path;
		}

		return null;
	}

	/// <summary>
	/// What a file in a compiled map vpk is, by extension and folder.
	/// </summary>
	static string CompiledKindOf( string path )
	{
		var extension = Path.GetExtension( path ).ToLowerInvariant();

		if ( path.Contains( "lightmap", StringComparison.OrdinalIgnoreCase ) ) return "Lightmaps";
		if ( path.Contains( "/worldnodes/", StringComparison.OrdinalIgnoreCase ) ) return "World geometry";

		return extension switch
		{
			".vwnod_c" => "World geometry",
			".vwrld_c" => "World",
			".vmdl_c" or ".vmesh_c" or ".vmblk_c" => "Models",
			".vphys_c" => "Physics",
			".vvis_c" => "Visibility",
			".vents_c" => "Entities",
			".vtex_c" => "Textures",
			".vmat_c" => "Materials",
			".vnmap_c" or ".nav" => "Navigation",
			".scene_c" => "Game objects",
			".vmap_c" => "Map",
			".vrman_c" => "Resource manifests",
			_ => extension.TrimStart( '.' )
		};
	}
}
