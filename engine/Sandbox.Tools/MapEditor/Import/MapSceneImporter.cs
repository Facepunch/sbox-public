using Editor.MapDoc;
using Sandbox.Internal;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Editor.MapEditor;

/// <summary>Converts Hammer source geometry and its authoring hierarchy into editable scene assets.</summary>
internal static partial class MapSceneImporter
{
	public sealed record ImportResult( SceneFile Scene, IReadOnlyList<string> Prefabs, IReadOnlyList<string> Warnings );

	/// <summary>Converts the open document if present, otherwise reads the VMAP source from its project or mounted content.</summary>
	public static async Task<ImportResult> ImportAsync( Asset source, string scenePath )
	{
		ArgumentNullException.ThrowIfNull( source );
		if ( source.AssetType?.FileExtension != "vmap" ) throw new ArgumentException( "Select a VMAP asset.", nameof( source ) );
		ThreadSafe.AssertIsMainThread();
		var path = SourcePath( source );
		if ( string.IsNullOrEmpty( path ) || !Path.IsPathFullyQualified( path ) || !path.EndsWith( ".vmap", StringComparison.OrdinalIgnoreCase ) ||
			(!File.Exists( path ) && !FindOpenDocument( path ).IsValid()) )
		{
			path = Path.ChangeExtension( source.RelativePath, ".vmap" );
			if ( string.IsNullOrEmpty( path ) || !FileSystem.Content.FileExists( path ) )
				throw new FileNotFoundException( $"Cannot find VMAP source for '{source.Path}'.", path );
		}

		scenePath = ValidateScenePath( scenePath );
		var reader = await PrepareReader( path, source );
		return Import( path, FindOpenDocument( path ), scenePath, reader );
	}

	static MapDocument FindOpenDocument( string path ) =>
		Hammer.Open && Path.IsPathFullyQualified( path ) ? Hammer.App.FindMapDoc( path ) : null;

	static string SourcePath( Asset asset )
	{
		if ( !string.IsNullOrEmpty( asset.AbsoluteSourcePath ) ) return asset.AbsoluteSourcePath;
		return string.IsNullOrEmpty( asset.AbsolutePath ) ? asset.Path : asset.AbsolutePath;
	}

	/// <summary>Loads the map's declared dependencies before converting the open document.</summary>
	public static async Task<ImportResult> ImportAsync( MapDocument document, string scenePath )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !document.IsValid() ) throw new ArgumentException( "The map document is not loaded.", nameof( document ) );
		scenePath = ValidateScenePath( scenePath );
		var reader = await PrepareReader( document.PathName, AssetSystem.FindByPath( document.PathName ) );
		if ( !document.IsValid() ) throw new ArgumentException( "The map document is not loaded.", nameof( document ) );
		return Import( Path.GetFullPath( document.PathName ), document, scenePath, reader );
	}

	static async Task<VmapReader> PrepareReader( string path, Asset source )
	{
		HashSet<string> requested = null;
		while ( true )
		{
			var reader = new VmapReader();
			if ( Backend.Package is null ) return reader;
			var packages = reader.GetPackageReferences( path, source );
			if ( requested is not null ) packages.ExceptWith( requested );
			if ( packages.Count == 0 ) return reader;

			var install = CloudAsset.Install( "Loading VMAP dependencies", packages );
			// An edit or a newly mounted prefab can introduce more dependencies while we wait.
			if ( !install.IsCompleted ) reader = null;
			if ( !await install ) throw new OperationCanceledException();
			if ( reader is not null ) return reader;
			(requested ??= new( StringComparer.OrdinalIgnoreCase )).UnionWith( packages );
		}
	}

	static string ValidateScenePath( string scenePath )
	{
		if ( string.IsNullOrWhiteSpace( scenePath ) ) throw new ArgumentException( "Choose a scene filename.", nameof( scenePath ) );
		var assets = Path.GetFullPath( Project.Current.GetAssetsPath() );
		scenePath = Path.GetFullPath( Path.ChangeExtension( Path.IsPathRooted( scenePath ) ? scenePath : Path.Combine( assets, scenePath ), ".scene" ) );
		var relative = Path.GetRelativePath( assets, scenePath );
		if ( relative.StartsWith( ".." + Path.DirectorySeparatorChar ) || Path.IsPathRooted( relative ) )
			throw new ArgumentException( "Save the scene inside the current project's assets.", nameof( scenePath ) );
		CheckOutputPath( scenePath );
		return scenePath;
	}

	static ImportResult Import( string filename, MapDocument document, string scenePath, VmapReader reader )
	{
		var world = reader.ReadWorld( filename, document );
		using var conversion = new Conversion( Path.GetFullPath( Project.Current.GetAssetsPath() ), scenePath );
		var scene = conversion.CreateScene();
		using ( scene.Push() )
			new WorldConversion( conversion, world, scene ).Convert();
		conversion.FinalizeSkyboxes( scene );
		var file = scene.CreateSceneFile();
		conversion.Save( file );
		return new ImportResult( file, conversion.Prefabs.Keys.ToArray(), conversion.Warnings.Distinct().ToArray() );
	}

	static void CheckOutputPath( string path )
	{
		if ( Path.Exists( path ) || Path.Exists( path + "_d" ) )
			throw new IOException( $"The resource or its binary data already exists at '{path}'. Choose a new scene filename." );
	}

	sealed partial class Conversion( string assetsPath, string scenePath ) : IDisposable
	{
		public List<Scene> Scenes { get; } = new();
		public Dictionary<string, PrefabFile> Prefabs { get; } = new( StringComparer.OrdinalIgnoreCase );
		public List<string> Warnings { get; } = new();
		readonly Dictionary<SourceNode, PrefabScene> _prefabSources = new();
		readonly HashSet<SourceNode> _visitingPrefabs = new();

		public Scene CreateScene()
		{
			var scene = Scene.CreateEditorScene();
			// Components build their renderers and physics bodies when the saved scene opens.
			scene.Enabled = false;
			Scenes.Add( scene );
			return scene;
		}

		public PrefabScene ConvertPrefab( SourceNode source )
		{
			var world = source.Target;
			if ( world?.Kind != NodeKind.World ) throw new InvalidDataException( $"VMAP prefab '{source.TargetMapPath}' has not loaded its world." );
			if ( _prefabSources.TryGetValue( world, out var existing ) ) return existing;
			if ( !_visitingPrefabs.Add( world ) ) throw new InvalidDataException( $"Recursive VMAP prefab: {source.TargetMapPath}." );

			var output = DependencyPath( source.TargetMapPath, "prefabs", ".prefab" );
			var scene = PrefabScene.CreateForEditing();
			scene.Enabled = false;
			Scenes.Add( scene );
			var overlays = new List<MeshComponent>();
			using ( scene.Push() )
			{
				var bindings = new List<(Guid Id, SourceNode Node)>();
				new WorldConversion( this, world, scene, prefabOverlayMeshes: overlays ).Convert( bindings );
				// Prefab children are authored in map coordinates. Save them relative to the target world's pivot.
				var pivot = world.Transform.WithScale( 1 );
				if ( pivot != Transform.Zero )
					foreach ( var child in scene.Children ) child.LocalTransform = pivot.ToLocal( child.LocalTransform );
				FinalizeSkyboxes( scene );
				if ( bindings.Count > 0 ) _bindings[world] = bindings;
			}
			var prefab = new PrefabFile();
			using ( var blobs = BlobDataSerializer.Capture() )
			{
				prefab.RootObject = scene.Serialize();
				// Only the temporary scene is inactive. Placements inherit an enabled prefab root.
				prefab.RootObject[GameObject.JsonKeys.Enabled] = true;
				prefab.BinaryData = blobs.ToByteArray();
			}
			// Placements can load this prefab immediately, including its mesh data.
			SaveNewResource( output, prefab );
			var assetPath = Path.GetRelativePath( assetsPath, output ).NormalizeFilename( false, false );
			prefab.RegisterWeakResourceId( assetPath );
			prefab.Register( assetPath );
			Prefabs.Add( output, prefab );
			// Scene instance definitions belong to the prefab asset. A linked placement
			// inherits its contents without forking those definitions into the parent scene.
			scene.InstanceState = SceneInstanceState.Empty;
			scene.Source = prefab;
			// Keep the saved bake in the prefab. Placements project onto their own receivers.
			foreach ( var overlay in overlays ) overlay.Flags |= ComponentFlags.NotCloned;
			_prefabSources.Add( world, scene );
			_visitingPrefabs.Remove( world );
			return scene;
		}

		readonly List<string> createdFiles = [];
		bool saved;

		FileStream CreateOutput( string path )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			var stream = new FileStream( path, FileMode.CreateNew, FileAccess.Write, FileShare.None );
			createdFiles.Add( path );
			return stream;
		}

		IEnumerable<(string Path, GameResource Resource)> Outputs( SceneFile scene )
		{
			foreach ( var (path, prefab) in Prefabs ) yield return (path, prefab);
			foreach ( var (path, skybox) in Skyboxes ) yield return (path, skybox);
			yield return (scenePath, scene);
		}

		public void Save( SceneFile file )
		{
			foreach ( var (path, skybox) in Skyboxes ) SaveNewResource( path, skybox );
			SaveNewResource( scenePath, file );

			// Registration creates asset metadata. Wait until all source files are on disk.
			foreach ( var (path, resource) in Outputs( file ) )
			{
				var asset = AssetSystem.RegisterFile( path ) ?? throw new IOException( $"Could not register {path}." );
				if ( !asset.IsCompiledAndUpToDate && !asset.Compile( false ) )
					throw new IOException( $"Could not compile '{path}'." );
				resource.Register( asset.Path );
			}
			saved = true;
		}

		void SaveNewResource( string path, GameResource resource )
		{
			var json = resource.Serialize().ToJsonString( Json.options );
			if ( File.Exists( path + "_d" ) ) throw new IOException( $"A binary data file already exists for {path}." );
			// Claim the scene or prefab before writing its companion file. A competing import
			// must fail here, before it can write anything belonging to this resource.
			using ( CreateOutput( path ) ) { }
			if ( resource.BinaryData is { Length: > 0 } data )
			{
				using ( var file = CreateOutput( path + "_d" ) ) file.Write( data );
				resource.BinaryData = null;
			}
			resource.SaveToDisk( path, json );
		}

		public void Dispose()
		{
			foreach ( var scene in Scenes ) scene.Destroy();
			if ( saved ) return;
			foreach ( var prefab in Prefabs.Values ) prefab.DestroyInternal();
			foreach ( var skybox in Skyboxes.Values ) skybox.DestroyInternal();
			// A failed import owns only the files it created. Shared grading textures are content-addressed.
			foreach ( var path in createdFiles )
			{
				try { File.Delete( path ); }
				catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
				{
					Log.Warning( $"Could not remove incomplete VMAP output '{path}': {e.Message}" );
				}
			}
		}
	}

	sealed partial class WorldConversion( Conversion conversion, SourceNode source, Scene scene, bool insideSkybox = false, List<MeshComponent> prefabOverlayMeshes = null )
	{
		readonly Dictionary<SourceNode, GameObject> _objects = new();
		readonly HashSet<SourceNode> _targets = new();
		readonly HashSet<SourceNode> _hiddenDefinitions = new();
		readonly HashSet<SourceNode> _visiting = new();
		bool hasReferences;

		public void Convert( List<(Guid Id, SourceNode Node)> bindings = null )
		{
			scene.Name = source.Name;
			CollectTargets( source, new() );
			// Deserialize all embedded objects together so references between separate map nodes resolve.
			using ( CallbackBatch.Isolated() )
			using ( BlobDataSerializer.Suppress() )
			{
				foreach ( var data in source.EmbeddedObjects )
				{
					var copy = new GameObject( scene, false );
					copy.Deserialize( data );
				}
			}
			ConvertChildren( source, scene );
			if ( hasReferences ) ResolveReferences( source );
			// Hammer stores instance definitions in the tree, but only their placements are visible.
			foreach ( var target in _hiddenDefinitions )
			{
				if ( !_objects.TryGetValue( target, out var template ) ) continue;
				scene.UnlinkInstance( template );
				template.DestroyImmediate();
			}
			ImportSelectionSets();
			if ( bindings is not null ) CollectBindings( source, bindings );
		}

		void ImportSelectionSets()
		{
			if ( source.SelectionSets is null ) return;
			var system = scene.GetSystem<SelectionSetsSystem>() ?? throw new InvalidOperationException( "The scene selection set system is unavailable." );
			system.Data ??= new();
			system.Data.SelectionSets ??= [];
			foreach ( var (name, members) in source.SelectionSets )
			{
				var entry = new SelectionSetsSystem.SelectionSetEntry { Name = name };
				foreach ( var member in members )
				{
					if ( !_objects.TryGetValue( member, out var obj ) || !obj.IsValid() ) continue;
					entry.ObjectIds.Add( obj.Id );
					entry.Enabled &= obj.Enabled;
				}
				system.Data.SelectionSets.Add( entry );
			}
		}

		// Hammer visits parents first. A child's transform override is relative to the
		// placement and must be applied after its parent has reached its final transform.
		void CollectBindings( SourceNode node, List<(Guid Id, SourceNode Node)> bindings )
		{
			if ( (node.VariableBindings is not null || node.TargetMapPath is { Length: > 0 } ||
				node.Kind == NodeKind.Entity && !string.IsNullOrEmpty( node.Entity.TargetName )) &&
				_objects.TryGetValue( node, out var obj ) && obj.IsValid() )
				bindings.Add( (obj.Id, node) );

			foreach ( var child in node.Children ) CollectBindings( child, bindings );
		}

		void CollectTargets( SourceNode node, HashSet<SourceNode> visited )
		{
			if ( !visited.Add( node ) ) return;
			hasReferences |= node.Connections is not null || node.Kind == NodeKind.Entity && IsConstraint( node.Entity.TypeName );
			if ( node.Target is not null )
			{
				_targets.Add( node.Target );
				if ( string.IsNullOrEmpty( node.TargetMapPath ) ) CollectTargets( node.Target, visited );
			}
			if ( node.OwnsTarget ) _hiddenDefinitions.Add( node.Target );
			foreach ( var child in node.Children ) CollectTargets( child, visited );
		}

		// An unavailable prefab keeps its saved patch. Record the new parent-local transform
		// there so the placement also survives loading the prefab in its owning project.
		static void UpdateMissingPrefabTransform( GameObject obj )
		{
			if ( !obj.Flags.Contains( GameObjectFlags.Error ) || obj.PrefabInstance is not { Patch: { } patch } instance ||
				!instance.InstanceToPrefabLookup.TryGetValue( obj.Id, out var prefabId ) ) return;

			var root = new Json.ObjectIdentifier { Type = "GameObject", IdValue = prefabId.ToString() };
			patch.PropertyOverrides.RemoveAll( p => p.Target == root && p.Property is ("Position" or "Rotation" or "Scale") );
			patch.PropertyOverrides.Add( new() { Target = root, Property = "Position", Value = Json.ToNode( obj.LocalPosition ) } );
			patch.PropertyOverrides.Add( new() { Target = root, Property = "Rotation", Value = Json.ToNode( obj.LocalRotation ) } );
			patch.PropertyOverrides.Add( new() { Target = root, Property = "Scale", Value = Json.ToNode( obj.LocalScale ) } );
		}

		void ConvertChildren( SourceNode node, GameObject parent )
		{
			foreach ( var child in node.Children )
				if ( !_hiddenDefinitions.Contains( child ) ) ConvertNode( child, parent );
		}

		GameObject ConvertNode( SourceNode node, GameObject parent )
		{
			if ( _objects.TryGetValue( node, out var existing ) )
			{
				existing.SetParent( parent, true );
				return existing;
			}
			if ( !_visiting.Add( node ) ) throw new InvalidDataException( $"Recursive Hammer instance at '{node.Name}' ({node.NodeId})." );
			if ( node.ImportWarning is not null ) conversion.Warnings.Add( node.ImportWarning );
			var transform = node.Transform;
			GameObject result;
			var enabled = true;
			if ( node.Kind == NodeKind.Instance )
			{
				GameObject template;
				if ( !string.IsNullOrEmpty( node.TargetMapPath ) ) template = conversion.ConvertPrefab( node );
				else if ( node.Target is not null ) template = _objects.GetValueOrDefault( node.Target ) ?? ConvertNode( node.Target, scene );
				else throw new InvalidDataException( $"Hammer instance '{node.Name}' has no target." );
				result = template.Clone( new CloneConfig( Transform.Zero, parent, startEnabled: false, name: node.Name ) );
				// Hammer applies a rigid placement delta, retaining the target's scale and state.
				result.WorldTransform = transform.WithScale( template.WorldScale );
				enabled = template is PrefabScene || template.Enabled;
				var overrides = ResolveChoices( node.Target?.ChoiceGroups, node.VariableOverrides );
				if ( node.TypeString == "CMapPrefab" && node.EntityKeys is null )
					conversion.FixupNames( result, node, node.NodeId.ToString(), overrides );
				if ( overrides is not null ) conversion.ApplyOverrides( result, node, overrides );
				// Scene instances share their definition's children. Keep placement-specific
				// children, such as tile proxies, alongside the linked instance.
				if ( node.ProjectedOverlays is not null || (node.Children.Length > 0 && string.IsNullOrEmpty( node.TargetMapPath )) )
				{
					var group = new GameObject( parent, false, result.Name ) { WorldTransform = result.WorldTransform };
					result.SetParent( group, true );
					result.Enabled = true;
					if ( node.ProjectedOverlays is { } overlays )
						foreach ( var overlay in overlays ) ConvertNode( overlay, group );
					result = group;
				}
			}
			else
			{
				result = node.Kind == NodeKind.GameObject ? scene.Directory.FindByGuid( node.ObjectId ) : null;
				if ( result is null ) result = new GameObject( parent, false, string.IsNullOrWhiteSpace( node.Name ) ? node.TypeString : node.Name );
				else result.SetParent( parent, true );
				enabled = !node.HiddenWorldLayer && (node.Kind != NodeKind.GameObject || result.Enabled);
				result.Enabled = false;
				result.WorldTransform = transform;
				if ( node.Kind == NodeKind.GameObject ) UpdateMissingPrefabTransform( result );
				var meshCount = ConvertMeshes( node, result );
				if ( node.ImagePlane is { } image ) conversion.ConvertImagePlane( result, transform, image.Map, image.Path, image.Opacity );
				if ( node.Kind == NodeKind.Entity && node.Entity.TypeName == "skybox_reference" )
				{
					if ( !insideSkybox ) conversion.AttachSkybox( result, node.SkyboxWorld, node.SkyboxPath, node.EntityKeys );
					ConvertEntity( node.Entity, result, conversion.Warnings, node );
				}
				else if ( node.Kind == NodeKind.Entity ) enabled = ConvertEntity( node.Entity, result, conversion.Warnings, node );
				else if ( meshCount == 0 && node.Kind == NodeKind.Other )
					conversion.Warnings.Add( $"'{node.Name}' ({node.TypeString}): imported its hierarchy, but this Hammer node has no scene conversion." );

				foreach ( var helper in result.Components.GetAll<HammerMesh>( FindMode.EverythingInSelf ) ) helper.Destroy();
			}

			ConvertChildren( node, result );

			if ( node.Kind == NodeKind.Entity && node.Entity.TypeName == "func_water" )
				ConvertWater( result, node.Entity, conversion.Warnings );

			if ( node.Kind == NodeKind.Entity && node.Entity.TypeName is ("post_processing_entity" or "post_processing_volume") )
				ConvertPostProcessing( result, node.Entity, node.PostProcessing, conversion );

			if ( node.Kind == NodeKind.Entity && node.Entity.TypeName == "ent_nav_blocker" )
			{
				foreach ( var mesh in result.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ) )
				{
					mesh.HideInGame = true;
					mesh.Collision = MeshComponent.CollisionType.None;
					var area = mesh.GameObject.Components.GetOrCreate<NavMeshArea>();
					area.SceneVolume = new() { Box = mesh.Mesh.CalculateBounds( Transform.Zero ) };
				}
			}

			if ( node.TypeString is "CMapCordon" or "CMapPhysicsPin" )
				result.Flags |= GameObjectFlags.EditorOnly;
			conversion.ApplyVisibility( result, node, enabled );

			if ( _targets.Contains( node ) )
			{
				if ( result.IsPrefabInstance )
				{
					var wrapper = new GameObject( parent, true, result.Name ) { WorldTransform = result.WorldTransform };
					result.SetParent( wrapper, true );
					result = wrapper;
				}
				if ( hasReferences )
				{
					_objects[node] = result;
					ResolveReferences( node );
				}
				scene.CreateInstance( result );
			}
			_objects[node] = result;
			_visiting.Remove( node );
			return result;
		}

		int ConvertMeshes( SourceNode sourceMesh, GameObject target )
		{
			var meshes = conversion.SplitByMaterialSettings( sourceMesh.Meshes );
			foreach ( var data in meshes )
			{
				var owner = meshes.Count == 1 && !data.EditorOnly ? target : new GameObject( target, false, "Surface" );
				if ( data.EditorOnly ) owner.Flags |= GameObjectFlags.EditorOnly;
				if ( sourceMesh.StaticWorldMesh && (owner != target || sourceMesh.Children.Length == 0) ) owner.IsStatic = true;
				var component = owner.Components.Create<MeshComponent>();
				if ( sourceMesh.TypeString == "CMapStaticOverlay" ) prefabOverlayMeshes?.Add( component );
				component.Collision = data.Physics switch
				{
					SceneMeshPhysics.None or SceneMeshPhysics.MultipleHulls or SceneMeshPhysics.ConvexDecomposition => MeshComponent.CollisionType.None,
					SceneMeshPhysics.SingleHull => MeshComponent.CollisionType.Hull,
					SceneMeshPhysics.Capsule => MeshComponent.CollisionType.Capsule,
					SceneMeshPhysics.Sphere => MeshComponent.CollisionType.Sphere,
					_ => MeshComponent.CollisionType.Mesh
				};
				component.SmoothingAngle = data.Smoothing;
				component.Color = data.Tint;
				component.RenderType = data.ShadowsOnly ? ModelRenderer.ShadowRenderType.ShadowsOnly :
					data.Shadows ? ModelRenderer.ShadowRenderType.On : ModelRenderer.ShadowRenderType.Off;
				component.HideInGame = data.HideInGame;
				component.RenderOrder = data.RenderOrder;
				component.DisabledInLowQuality = data.DisabledInLowQuality;
				// Hammer's CSplitString accepts the same delimiters for physics tags on every shape.
				foreach ( var range in data.Tags.AsSpan().SplitAny( " \t\r\n,;/\\" ) )
					if ( range.Start.Value != range.End.Value ) owner.Tags.Add( data.Tags[range] );
				if ( sourceMesh.TypeString is "CMapMesh" or "CMapTileMesh" )
				{
					data.Mesh.TriangulationTransform = sourceMesh.Transform;
					data.Mesh.CollisionTolerance = data.CollisionError;
				}
				component.Surface = data.CollisionSurface;
				component.Mesh = data.Mesh;
				if ( data.Physics is SceneMeshPhysics.MultipleHulls or SceneMeshPhysics.ConvexDecomposition )
					AddCollisionHulls( owner, data, sourceMesh.Transform );
				if ( owner != target ) owner.Enabled = true;
			}
			return meshes.Count;
		}
	}
}
