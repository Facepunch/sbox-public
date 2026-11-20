using System;
using System.IO;
using Editor.MapDoc;
using System.Text.Json.Nodes;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	internal static HashSet<Element> ReadHiddenSelectionNodes( Element root )
	{
		// "visbility" is the spelling in the VMAP schema. Hammer restores user and auto
		// selection-set visibility on load. Quick Hide is transient.
		if ( root.GetValueOrDefault( "visbility" ) is not Element visibility ||
			visibility.GetValueOrDefault( "nodes" ) is not Element[] nodes || !visibility.ContainsKey( "hiddenFlags" ) ) return null;

		var flags = visibility.Numbers<int>( "hiddenFlags" );
		HashSet<Element> hidden = null;
		for ( int i = 0; i < Math.Min( nodes.Length, flags.Length ); i++ )
			if ( (flags[i] & 3) != 0 && nodes[i] is { } node ) (hidden ??= []).Add( node );
		return hidden;
	}

	sealed partial class VmapReader
	{
		readonly HashSet<SourceNode> instanceDefinitions = [];
		readonly HashSet<SourceNode> worldsWithOverlays = [];
		readonly Dictionary<string, Element[]> dependencyFiles = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, SourceNode> worlds = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, SourceNode> prefabWorlds = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, (JsonObject Effect, string Warning)> postProcessing = new( StringComparer.OrdinalIgnoreCase );

		static string EntityValue( object value ) => value switch
		{
			string s => s,
			bool b => b ? "1" : "0",
			Color32 c => $"{c.r} {c.g} {c.b} {c.a}",
			System.Numerics.Vector2 v => FormattableString.Invariant( $"{v.X} {v.Y}" ),
			System.Numerics.Vector3 v => FormattableString.Invariant( $"{v.X} {v.Y} {v.Z}" ),
			System.Numerics.Vector4 v => FormattableString.Invariant( $"{v.X} {v.Y} {v.Z} {v.W}" ),
			IFormattable number => number.ToString( null, System.Globalization.CultureInfo.InvariantCulture ),
			_ => null
		};

		static Dictionary<string, string> ReadProperties( Element element, params ReadOnlySpan<string> names )
		{
			var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var key in names )
				if ( element.TryGetValue( key, out var value ) && EntityValue( value ) is { } text ) result.Add( key, text );
			return result;
		}

		static void ReadEntityKeys( Element properties, ref Dictionary<string, string> keys )
		{
			if ( properties is null ) return;
			foreach ( var pair in properties )
			{
				if ( EntityValue( pair.Value ) is not { } text || (pair.Key == "classname" && text.Length == 0) ) continue;
				(keys ??= new( StringComparer.OrdinalIgnoreCase ))[pair.Key] = text;
			}
		}

		// Separate definitions can select overlapping subtrees of the same prefab.
		// Give each its own hierarchy while sharing the immutable mesh data.
		static SourceNode CopyInstanceDefinition( SourceNode node, Dictionary<SourceNode, SourceNode> copies )
		{
			if ( copies.TryGetValue( node, out var copy ) ) return copy;
			copy = node.ShallowCopy();
			copies.Add( node, copy );
			copy.Children = Array.ConvertAll( node.Children, child => CopyInstanceDefinition( child, copies ) );
			if ( node.Target is not null && string.IsNullOrEmpty( node.TargetMapPath ) )
				copy.Target = CopyInstanceDefinition( node.Target, copies );
			if ( node.ProjectedOverlays is not null )
				copy.ProjectedOverlays = node.ProjectedOverlays.ConvertAll( overlay => CopyInstanceDefinition( overlay, copies ) );
			return copy;
		}

		static string PrefabMapPath( Element prefab )
		{
			return prefab.Get( "targetMapPath", "" ).Replace( '\\', '/' ).Trim().TrimStart( '/' );
		}

		static string ReferencePath( string filename, string asset )
		{
			if ( LocalReferencePath( filename, asset ) is { } local ) return local;
			var path = FileSystem.Content.GetFullPath( asset );
			if ( path is not null && Path.IsPathFullyQualified( path ) && File.Exists( path ) ) return path;
			return FileSystem.Content.FileExists( asset ) ? asset.Replace( '\\', '/' ).TrimStart( '/' ) : null;
		}

		public HashSet<string> GetPackageReferences( string filename, Asset source )
		{
			var packages = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var pending = new Stack<(string Path, Asset Asset)>();
			pending.Push( (filename, source) );
			while ( pending.TryPop( out var entry ) )
			{
				var (path, asset) = entry;
				path = path.Replace( '\\', '/' );
				if ( !visited.Add( path ) ) continue;

				bool indexed = asset is not null && path.Equals( SourcePath( asset ).Replace( '\\', '/' ), StringComparison.OrdinalIgnoreCase );
				var settings = indexed || !Path.IsPathFullyQualified( path )
					? asset?.GetPublishSettings( false )
					: new MetaData( path + ".meta" ).Get<Asset.PublishSettings>( "publish" );
				if ( settings?.ProjectConfig?.EditorReferences is { } references ) packages.UnionWith( references );

				// The asset index can point to a built-in VMAP with the same name as a project prefab.
				// Follow the file the geometry reader uses, including its own nested references.
				var document = FindOpenDocument( path );
				var children = indexed && !document.IsValid() && asset is NativeAsset native && native.native.IsSourceUnchangedSinceCompile()
					? asset.GetReferences( false ).Where( child => child?.AssetType?.FileExtension == "vmap" ).Select( child => child.Path )
					: SourceMapReferences( ReadMapFile( path, document, retainForConversion: true ) ).Distinct( StringComparer.OrdinalIgnoreCase );
				foreach ( var child in children )
					if ( ReferencePath( path, child ) is { } resolved )
						pending.Push( (resolved, AssetSystem.FindByPath( child )) );
			}
			return packages;
		}

		static IEnumerable<string> SourceMapReferences( Element[] elements )
		{
			foreach ( var element in elements )
			{
				switch ( element.ClassName )
				{
					case "CMapPrefab":
						if ( PrefabMapPath( element ) is { Length: > 0 } prefab ) yield return prefab;
						foreach ( var path in element.Get<string[]>( "variableOverrideValues", [] ) )
							if ( path.EndsWith( ".vmap", StringComparison.OrdinalIgnoreCase ) ) yield return path;
						break;
					case "CMapDotaTileGrid":
						foreach ( var (key, value) in element )
							if ( key is "targetMapPath" or "targetMapPath1" or "targetMapPath2" or "targetMapPath3" or "baseMapPath" &&
								value is string { Length: > 0 } map ) yield return map;
						break;
					case "CMapEntity" when element.GetValueOrDefault( "entity_properties" ) is Element properties:
						foreach ( var (key, value) in properties )
							if ( value is string { Length: > 0 } target && key.Equals( "targetMapName", StringComparison.OrdinalIgnoreCase ) )
								yield return target;
						break;
				}
			}
		}

		unsafe Element[] ReadMapFile( string path, MapDocument document, bool retainForConversion = false )
		{
			if ( document.IsValid() )
			{
				using var buffer = CUtlBuffer.Create();
				if ( !document.native.SerializeVmap( buffer ) ) throw new InvalidDataException( $"Could not snapshot open VMAP '{path}'." );
				using var snapshot = new UnmanagedMemoryStream( (byte*)buffer.Base(), buffer.TellMaxPut() );
				return BinaryVmap.Read( snapshot, path );
			}
			bool physical = Path.IsPathFullyQualified( path );
			if ( physical ) path = Path.GetFullPath( path );
			if ( dependencyFiles.Remove( path, out var cached ) ) return cached;
			using var stream = physical ? File.OpenRead( path ) : FileSystem.Content.OpenRead( path );
			var elements = BinaryVmap.Read( stream, path );
			if ( physical && retainForConversion ) dependencyFiles.Add( path, elements );
			return elements;
		}

		public static string LocalReferencePath( string filename, string asset )
		{
			// A VMAP may be imported directly from a different project.
			if ( !Path.IsPathFullyQualified( filename ) ) return null;
			DirectoryInfo root = null;
			for ( var folder = new FileInfo( filename ).Directory; folder is not null; folder = folder.Parent )
			{
				if ( folder.Name.Equals( "Assets", StringComparison.OrdinalIgnoreCase ) )
				{
					root = folder;
					break;
				}

				// Standalone Hammer content roots have a maps folder without an s&box Assets folder.
				if ( folder.Name.Equals( "maps", StringComparison.OrdinalIgnoreCase ) ) root = folder.Parent;
			}
			if ( root is null ) return null;
			var local = Path.GetFullPath( Path.Combine( root.FullName, asset ) );
			return File.Exists( local ) ? local : null;
		}

		SourceNode ReadReferencedMap( string filename, string asset, bool asPrefab )
		{
			var path = ReferencePath( filename, asset );
			if ( path is null ) return null;
			return ReadWorld( path, FindOpenDocument( path ), asPrefab );
		}

		Func<string, SourceNode> SkyboxResolver( string filename ) =>
			asset => ReadReferencedMap( filename, asset, false );

		public SourceNode ReadWorld( string filename, MapDocument document = null, bool insidePrefab = false )
		{
			if ( Path.IsPathFullyQualified( filename ) ) filename = Path.GetFullPath( filename );
			var cache = insidePrefab ? prefabWorlds : worlds;
			if ( cache.TryGetValue( filename, out var existing ) )
				return existing ?? throw new InvalidDataException( $"Recursive VMAP reference to '{filename}'." );
			cache.Add( filename, null );
			var file = ReadMapFile( filename, document );
			if ( file.Length == 0 ) throw new InvalidDataException( $"Empty VMAP '{filename}'." );
			SourceNode ReadReference( string asset ) => ReadReferencedMap( filename, asset, true );

			var nodes = new Dictionary<Element, SourceNode>();
			bool hasOverlays = false;
			bool hasPropCullBoxes = false;
			bool hasEntityParents = false;
			List<(SourceNode Node, string Name)> layers = null;
			Dictionary<SourceNode, SourceNode> subinstanceDefinitions = null;
			var instances = new Queue<(SourceNode Instance, Element Target)>();
			List<SourceNode> proxies = null;
			List<GridBake> grids = null;
			var root = file[0].Get<Element>( "world" );
			var hiddenSelection = ReadHiddenSelectionNodes( file[0] );
			bool tileLibrary = file.Any( e => e.ClassName == "CMapTileSet" );
			SourceNode ReadNode( Element element, string entityClass, bool worldGeometry = true, bool visible = true )
			{
				var node = new SourceNode();
				if ( !nodes.TryAdd( element, node ) ) throw new InvalidDataException( $"Repeated child '{element.Name}' in VMAP hierarchy '{filename}'." );
				node.Name = element.Name;
				node.TypeString = element.ClassName;
				node.NodeId = element.Get( "nodeID", 0 );
				node.EditorOnly = element.Get( "editorOnly", false );
				node.ForceHidden = element.Get( "force_hidden", false );
				node.SelectionHidden = hiddenSelection?.Contains( element ) == true;
				visible &= !node.EditorOnly && !node.Hidden;
				var angles = element.Get( "angles", System.Numerics.Vector3.Zero );
				// Hammer stores scaled mesh vertices directly and bakes tile grids using only their origin and angles.
				var scale = element.ClassName is "CMapMesh" or "CMapTileMesh" or "CMapStaticOverlay" or "CMapDotaTileGrid"
					? System.Numerics.Vector3.One : element.Get( "scales", System.Numerics.Vector3.One );
				node.Transform = new Transform( element.Get( "origin", System.Numerics.Vector3.Zero ), new Angles( angles.X, angles.Y, angles.Z ).ToRotation(), scale );
				node.Meshes = [];
				if ( element.GetValueOrDefault( "connectionsData" ) is Element[] { Length: > 0 } connections ) node.Connections = connections;
				if ( element.TryGetValue( "variableNames", out var names ) && names is string[] { Length: > 0 } variables )
				{
					var keys = element.GetArray<string>( "variableTargetKeys" );
					if ( variables.Length != keys.Length ) throw new InvalidDataException( $"Invalid variable bindings on node {node.NodeId}." );
					node.VariableBindings = variables.Zip( keys ).ToArray();
				}
				switch ( element.ClassName )
				{
					case "CMapWorld":
						node.Kind = NodeKind.World;
						node.IsSkyboxWorld = string.Equals( element.Get<string>( "mapUsageType", null ), "skybox", StringComparison.OrdinalIgnoreCase );
						if ( element.TryGetValue( "SerializedScene", out var scene ) && scene is string { Length: > 0 } json && JsonNode.Parse( json )?["GameObjects"] is JsonArray objects )
							node.EmbeddedObjects = objects.OfType<JsonObject>();
						break;
					case "CMapEntity":
						node.Kind = NodeKind.Entity;
						var properties = element.Get<Element>( "entity_properties" );
						var keys = new Dictionary<string, string>( properties.Count, StringComparer.OrdinalIgnoreCase );
						foreach ( var pair in properties )
							if ( EntityValue( pair.Value ) is { } value ) keys[pair.Key] = value;
						node.EntityKeys = keys;
						entityClass = keys["classname"];
						// Hammer matches entity class names case-insensitively.
						if ( entityClass.AsSpan().ContainsAnyInRange( 'A', 'Z' ) )
							entityClass = keys["classname"] = entityClass.ToLowerInvariant();
						if ( entityClass == "prop_static" && element.GetValueOrDefault( "boneNames" ) is string[] { Length: > 0 } boneNames )
						{
							var positions = element.Numbers<System.Numerics.Vector3>( "bonePositions", optional: true );
							var rotations = element.Numbers<System.Numerics.Vector4>( "boneRotations", optional: true );
							node.BonePose = new( boneNames.Length, StringComparer.OrdinalIgnoreCase );
							for ( int i = 0; i < boneNames.Length; i++ )
							{
								var q = i < rotations.Length ? rotations[i] : System.Numerics.Vector4.UnitW;
								node.BonePose[boneNames[i]] = new Transform( i < positions.Length ? (Vector3)positions[i] : Vector3.Zero, new Rotation( q.X, q.Y, q.Z, q.W ) );
							}
						}
						node.Entity = CreateEntityEntry( keys, node.Transform );
						if ( entityClass == "info_cull_triangles" && !node.Hidden && !node.EditorOnly && CullsProps( node.Entity ) )
							hasPropCullBoxes = true;
						if ( entityClass is ("post_processing_entity" or "post_processing_volume") && keys.TryGetValue( "postprocessing", out var effect ) && !string.IsNullOrWhiteSpace( effect ) )
						{
							(node.PostProcessing, node.ImportWarning) = ReadPostProcessing( filename, effect );
						}
						hasEntityParents |= !string.IsNullOrWhiteSpace( node.Entity.ParentName );
						if ( entityClass == "skybox_reference" )
						{
							node.SkyboxPath = keys.GetValueOrDefault( "targetMapName" );
							if ( !string.IsNullOrWhiteSpace( node.SkyboxPath ) ) node.SkyboxWorld = ReadReferencedMap( filename, node.SkyboxPath, false );
							if ( node.VariableBindings?.Any( v => v.Key.Equals( "targetMapName", StringComparison.OrdinalIgnoreCase ) ) == true )
								node.ResolveSkyboxOverride = SkyboxResolver( filename );
						}
						break;
					case "CMapGameObject":
						node.Kind = NodeKind.GameObject;
						Guid.TryParse( element.Get<string>( "m_GUID" ), out node.ObjectId );
						break;
					case "CMapProxyInstance":
					case "CMapInstance":
						if ( element.GetValueOrDefault( "target" ) is not Element target ) goto case "CMapGroup";
						node.Kind = NodeKind.Instance;
						instances.Enqueue( (node, target) );
						break;
					case "CMapPrefabSubInstance":
						int id = element.Get( "targetNodeID", -1 );
						if ( id is <= 0 or int.MaxValue || element.GetValueOrDefault( "targetPrefab" ) is not Element { ClassName: "CMapPrefab" } prefab )
							goto case "CMapGroup";
						if ( insidePrefab && !prefab.Get( "loadIfNested", true ) ) goto case "CMapGroup";
						var asset = PrefabMapPath( prefab );
						if ( string.IsNullOrEmpty( asset ) || ReadReference( asset ) is not { } library ||
							!GetPrefabNodes( library ).TryGetValue( id, out var definition ) ) goto case "CMapGroup";

						subinstanceDefinitions ??= new();
						if ( !subinstanceDefinitions.TryGetValue( definition, out node.Target ) )
							subinstanceDefinitions.Add( definition, node.Target = CopyInstanceDefinition( definition, new() ) );
						node.Kind = NodeKind.Instance;
						break;
					case "CMapPrefab":
						if ( insidePrefab && !element.Get( "loadIfNested", true ) ) goto case "CMapGroup";
						node.TargetMapPath = PrefabMapPath( element );
						if ( string.IsNullOrEmpty( node.TargetMapPath ) ) goto case "CMapGroup";
						node.Kind = NodeKind.Instance;
						node.PrefabTargetName = element.Get( "targetName", "" );
						node.FixupEntityNames = element.Get( "fixupEntityNames", true );
						if ( element.Get( "loadAtRuntime", false ) )
						{
							var path = node.TargetMapPath;
							if ( path.StartsWith( "maps/", StringComparison.OrdinalIgnoreCase ) ) path = path[5..];
							node.EntityKeys = new( StringComparer.OrdinalIgnoreCase )
							{
								["classname"] = "point_prefab",
								["targetname"] = node.PrefabTargetName,
								["fixupNames"] = node.FixupEntityNames ? "1" : "0",
								["targetMapName"] = Path.ChangeExtension( path, null )
							};
						}
						var overrides = element.Get<string[]>( "variableOverrideNames", [] );
						if ( overrides.Length > 0 )
						{
							var values = element.GetArray<string>( "variableOverrideValues" );
							if ( overrides.Length != values.Length ) throw new InvalidDataException( $"Invalid prefab overrides on node {node.NodeId}." );
							node.VariableOverrides = new( StringComparer.OrdinalIgnoreCase );
							for ( int i = 0; i < overrides.Length; i++ ) node.VariableOverrides[overrides[i]] = values[i];
						}
						node.Target = ReadReference( node.TargetMapPath );
						if ( node.Target is null ) throw new FileNotFoundException( $"Cannot resolve prefab '{node.TargetMapPath}' referenced by '{filename}'." );
						break;
					case "CMapTileMesh":
					case "CMapMesh":
						node.Kind = NodeKind.GroupOrMesh;
						node.StaticWorldMesh = worldGeometry;
						var mesh = ReadVmapMesh( element, entityClass, tileLibrary );
						if ( mesh is null ) throw new NotSupportedException( $"Cannot convert mesh {node.NodeId} in '{filename}'." );
						if ( element.GetValueOrDefault( "dmemesh" ) is Element )
						{
							var transform = node.Transform.WithPosition( Vector3.Zero );
							var center = mesh.Mesh.CalculateBounds( transform ).Center;
							var offset = transform.PointToLocal( center );
							foreach ( var vertex in mesh.Mesh.VertexHandles )
								mesh.Mesh.SetVertexPositionPreserveNormals( vertex, mesh.Mesh.GetVertexPosition( vertex ) - offset );
							node.Transform = transform.WithPosition( center );
						}
						node.Meshes = [mesh];
						if ( element.ClassName == "CMapTileMesh" )
							node.Children = ReadTiles( element, node, mesh.Mesh, ReadReference );
						break;
					case "CMapTile":
						node.Kind = NodeKind.GroupOrMesh;
						tileData.Add( node, element );
						break;
					case "CMapDotaTileGrid":
						var gridLayers = GridLayers( element, filename, out bool inherited, out var gridOrigin );
						node.Transform = node.Transform.WithPosition( gridOrigin );
						node.Children = ReadDotaGrid( element, gridLayers, inherited, node, ReadReference );
						(grids ??= []).Add( new( element, node.Children, gridLayers.Length, visible ) );
						for ( int i = 1; i < gridLayers.Length; i++ ) (layers ??= []).Add( (node.Children[i], node.Children[i].Name) );
						goto case "CMapGroup";
					case "CMapDotaGridnavMarkup":
						// Hammer keeps baked navigation cells in memory. Only the node transform and children survive a VMAP save.
						goto case "CMapGroup";
					case "CMapGlobalTileGridMaps":
						goto case "CMapGroup";
					case "CMapTileSet":
						tileData.Add( node, element );
						goto case "CMapGroup";
					case "CMapIsoSurface":
						// Hammer previews the voxel surface, but the map compiler emits its child meshes.
						node.EntityKeys = ReadProperties( element, "resolution", "smoothingAngle", "material" );
						goto case "CMapGroup";
					case "CMapOpaqueDataBlob":
						goto case "CMapGroup";
					case "CMapWorldLayer":
						var layerName = element.Get( "worldLayerName", "" );
						(layers ??= []).Add( (node, layerName) );
						if ( !string.IsNullOrEmpty( layerName ) ) node.Name = layerName;
						goto case "CMapGroup";
					case "CMapSpawnGroup":
					case "CMapTileMeshGroup":
					case "CMapGroup":
						node.Kind = NodeKind.GroupOrMesh;
						break;
					case "CMapWeightedGroup":
						tileData.Add( node, element );
						goto case "CMapGroup";
					case "CMapStaticOverlay":
						node.Kind = NodeKind.GroupOrMesh;
						node.Overlay = element;
						hasOverlays = true;
						break;
					case "CMapCable":
						node.Kind = NodeKind.Other;
						node.Meshes = ReadCable( element, node.Transform );
						break;
					case "CTrajectoryPath":
						node.EntityKeys = ReadProperties( element, "modelPath", "finalTime", "numTimeSteps", "minThrust", "maxThrust", "specificImpulse", "initialMass", "gravity" );
						goto case "CMapPath";
					case "CTrajectoryPathNode":
						node.EntityKeys = ReadProperties( element, "autoTime", "time", "posTolerance", "constrainVelocity", "velocity", "velTolerance" );
						goto case "CMapPathNode";
					case "CModelPath":
						node.Children = ReadModelPath( element, node.Transform );
						goto case "CMapPath";
					case "CMapPath":
						var pathKeys = element.Get<Element>( "entity_properties", null );
						ReadEntityKeys( pathKeys, ref node.EntityKeys );
						if ( pathKeys?.Get( "classname", "" ) == "path_particle_rope" )
						{
							node.Meshes = ReadCable( element, node.Transform );
							node.ImportWarning = $"Particle rope '{node.Name}' was baked to a static cable. Its particle simulation requires a scene equivalent.";
						}
						if ( pathKeys?.GetValueOrDefault( "targetname" ) is string { Length: > 0 } pathName ) node.Name = pathName;
						goto case "CMapGroup";
					case "CMapPathNode":
						ReadEntityKeys( element.Get<Element>( "entity_properties", null ), ref node.EntityKeys );
						if ( element.GetValueOrDefault( "pathNodeName" ) is string { Length: > 0 } name ) node.Name = name;
						goto case "CMapGroup";
					case "CMapGroupProxy":
					case "CMapTileProxy":
						(proxies ??= []).Add( node );
						tileData.Add( node, element );
						node.ProxyOrientUp = element.Get( "alwaysOrientUp", false );
						goto case "CMapGroup";
					// Hammer uses tile objects as editor links to the tile library.
					// They never emit the referenced map into the compiled world.
					case "CMapTileObject":
						goto case "CMapGroup";
					case "CMapCordon":
						if ( !element.ContainsKey( "scales" ) ) node.Transform = node.Transform.WithScale( new Vector3( 512 ) );
						goto case "CMapGroup";
					case "CImagePlaneNode":
						node.ImagePlane = (filename, element.Get( "m_ImageFileRelativePath", "" ), element.Get( "m_flOpacityScale", 1f ));
						if ( !element.ContainsKey( "scales" ) ) node.Transform = node.Transform.WithScale( new Vector3( 256, 256, 0 ) );
						goto case "CMapGroup";
					case "CMapPhysicsPin":
						// Pins constrain the Hammer physics posing tool. The compiled map uses
						// the resulting pose, so adding a runtime joint would constrain it twice.
						node.EntityKeys = ReadProperties( element, "targetBodyName", "targetOffset", "constrainRotation", "rigidConstraint" );
						goto case "CMapGroup";
					default:
						throw new NotSupportedException( $"Cannot convert {element.ClassName} in '{filename}' (node {node.NodeId})." );
				}
				var children = element.SceneChildren;
				if ( children.Length > 0 )
				{
					var generated = node.Children;
					node.Children = new SourceNode[children.Length + (generated?.Length ?? 0)];
					bool worldChildren = worldGeometry && node.Kind is not (NodeKind.Entity or NodeKind.GameObject);
					for ( int i = 0; i < children.Length; i++ ) node.Children[i] = ReadNode( children[i], entityClass, worldChildren, visible );
					generated?.CopyTo( node.Children, children.Length );
				}
				else node.Children ??= [];
				if ( node.Kind == NodeKind.Entity && entityClass == "func_shatterglass" ) BuildGlassPanel( node );
				return node;
			}
			var world = ReadNode( root, null );
			world.ChoiceGroups = file[0].Get<Element>( "mapVariables", null )?.Get<Element[]>( "m_ChoiceGroups", null );
			if ( grids is not null ) AssignGridSkyboxIds( grids, nodes.Values );
			// Resolve placements after their definitions have inherited properties from their actual parents.
			while ( instances.TryDequeue( out var placement ) )
				placement.Instance.Target = nodes.GetValueOrDefault( placement.Target ) ?? ReadNode( placement.Target, null );
			if ( proxies is not null ) BindTileProxies( proxies, nodes );
			instanceDefinitions.UnionWith( nodes.Values.Where( n => n.OwnsTarget ).Select( n => n.Target ) );
			if ( layers is not null )
			{
				// Hammer starts a world layer hidden unless a placed controller enables it.
				var visible = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
				var seen = new HashSet<SourceNode>();
				void FindControllers( SourceNode n )
				{
					if ( n.Hidden || n.EditorOnly || !seen.Add( n ) ) return;
					if ( n.Kind == NodeKind.Entity && (n.Entity.GetValue<int>( "spawnflags" ) & 1) != 0 &&
						n.Entity.GetString( "layerName" ) is { Length: > 0 } name ) visible.Add( name );
					foreach ( var child in n.Children )
						if ( !instanceDefinitions.Contains( child ) ) FindControllers( child );
					if ( n.Target is { Children: not null } target ) FindControllers( target );
				}
				FindControllers( world );
				foreach ( var (node, name) in layers ) node.HiddenWorldLayer = !visible.Contains( name );
			}
			if ( hasEntityParents )
			{
				var templates = nodes.Values.Where( n => n.Target is not null && string.IsNullOrEmpty( n.TargetMapPath ) ).Select( n => n.Target ).ToHashSet();
				ResolveEntityParents( world, templates );
				foreach ( var template in templates ) ResolveEntityParents( template, templates );
			}
			if ( hasOverlays || nodes.Values.Any( n => n.Target is not null && worldsWithOverlays.Contains( n.Target ) ) )
			{
				worldsWithOverlays.Add( world );
				BakeOverlays( world, instanceDefinitions );
			}
			if ( hasPropCullBoxes ) CullPlacedProps( world, instanceDefinitions );
			cache[filename] = world;
			return world;
		}
	}
}
