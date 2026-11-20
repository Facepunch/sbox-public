using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// A saving/loading system that captures the differences between the current scene state and the original scene.
/// </summary>
internal sealed class SaveSystem : GameObjectSystem<SaveSystem>, ISceneLoadingEvents
{
	private const int CurrentSaveVersion = 2;

	/// <summary>
	/// The current save format version. Saves with a different version are incompatible.
	/// </summary>
	public static int SaveVersion => CurrentSaveVersion;
	private Dictionary<string, string> _metadata = new();
	private readonly List<LoadedSceneEntry> _loadedScenes = new();
	private bool _suppressSystemScene;

	/// <summary>
	/// The path of the save file that was most recently loaded, if any.
	/// </summary>
	public string LoadedSavePath { get; private set; }

	/// <summary>
	/// Whether a save file is currently loaded.
	/// </summary>
	public bool HasLoadedSave => LoadedSavePath is not null;

	public SaveSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Set metadata on the current session's save.
	/// This will be written on the next <see cref="Save"/> call.
	/// </summary>
	public void SetMetadata( string key, string value )
	{
		if ( string.IsNullOrWhiteSpace( key ) )
			throw new ArgumentException( "Metadata key cannot be null or empty.", nameof( key ) );

		_metadata[key] = value;
	}

	/// <summary>
	/// Get a metadata value from the current session, falling back to a default value.
	/// </summary>
	public string GetMetadata( string key, string defaultValue = null )
	{
		if ( key is null ) return defaultValue;

		return _metadata.TryGetValue( key, out var value ) ? value : defaultValue;
	}

	/// <summary>
	/// Get a copy of all metadata for the current session.
	/// </summary>
	public IReadOnlyDictionary<string, string> GetAllMetadata()
	{
		return new Dictionary<string, string>( _metadata );
	}

	/// <summary>
	/// Read metadata from a save file on disk without loading the full save.
	/// Returns null if the file doesn't exist or is invalid.
	/// </summary>
	public static IReadOnlyDictionary<string, string> GetFileMetadata( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		if ( !FileSystem.Data.FileExists( path ) )
			return null;

		try
		{
			var text = FileSystem.Data.ReadAllText( path );
			using var doc = JsonDocument.Parse( text );

			if ( doc.RootElement.TryGetProperty( "Metadata", out var metaElement ) )
			{
				return JsonSerializer.Deserialize<Dictionary<string, string>>( metaElement.GetRawText() );
			}

			return new Dictionary<string, string>();
		}
		catch ( Exception e )
		{
			Log.Warning( $"SaveSystem: Failed to read metadata from '{path}': {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Read the save format version from a save file without loading the full save.
	/// Returns 0 if the file doesn't exist, is invalid, or has no version field.
	/// </summary>
	public static int GetFileSaveVersion( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return 0;

		if ( !FileSystem.Data.FileExists( path ) )
			return 0;

		try
		{
			var text = FileSystem.Data.ReadAllText( path );
			using var doc = JsonDocument.Parse( text );

			if ( doc.RootElement.TryGetProperty( "Version", out var versionElement ) )
				return versionElement.GetInt32();

			return 0;
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>
	/// Saves the current world to the given path. This means any changes made to the those original
	/// scenes are preserved when loading an older save.
	/// </summary>
	/// <returns>True if the save was successful.</returns>
	public bool Save( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
		{
			Log.Warning( "SaveSystem: Cannot save — path is null or empty." );
			return false;
		}

		if ( !Scene.IsValid() )
		{
			Log.Warning( "SaveSystem: Cannot save — no valid scene." );
			return false;
		}

		if ( _loadedScenes.Count == 0 )
		{
			Log.Warning( "SaveSystem: Cannot save — no tracked scene sources. The scene must be loaded from a SceneFile." );
			return false;
		}

		Scene.RunEvent<Global.ISaveEvents>( x => x.BeforeSave( path ) );

		var baseline = BuildCompositeBaseline();
		if ( baseline is null )
		{
			Log.Warning( "SaveSystem: Failed to build baseline from loaded scene sources." );
			return false;
		}

		var current = BuildCurrentSceneJson( Scene );
		if ( current is null )
		{
			Log.Warning( "SaveSystem: Failed to serialize current scene state." );
			return false;
		}

		// Calculate the diff between baseline and current state
		var patch = Json.CalculateDifferences( baseline, current, GameObject.DiffObjectDefinitions );
		var sceneSources = new JsonArray();
		foreach ( var entry in _loadedScenes )
		{
			sceneSources.Add( JsonValue.Create( entry.ResourcePath ) );
		}

		var primarySceneFile = GetPrimarySceneFile();
		var networkOwnership = CollectNetworkOwnership( Scene );
		var syncState = CollectSyncState( Scene );
		var requiredPackages = CollectRequiredPackages( _loadedScenes, current );
		var saveData = new JsonObject
		{
			["Version"] = CurrentSaveVersion,
			["SceneId"] = Scene.Id.ToString(),
			["SceneSources"] = sceneSources,
			["SceneProperties"] = primarySceneFile is not null ? SerializeScenePropertyDiffs( Scene, primarySceneFile ) : null,
			["Metadata"] = JsonSerializer.SerializeToNode( _metadata ),
			["Patch"] = Json.ToNode( patch ),
			["NetworkOwnership"] = networkOwnership,
			["SyncState"] = syncState,
			["RequiredPackages"] = requiredPackages,
		};

		try
		{
			// Make sure any parent directories exist first
			var dir = Path.GetDirectoryName( path );
			if ( !string.IsNullOrEmpty( dir ) )
				FileSystem.Data.CreateDirectory( dir );

			FileSystem.Data.WriteAllText( path, saveData.ToJsonString() );
			LoadedSavePath = path;
		}
		catch ( Exception e )
		{
			Log.Warning( $"SaveSystem: Failed to write save file '{path}': {e.Message}" );
			return false;
		}

		Scene.RunEvent<Global.ISaveEvents>( x => x.AfterSave( path ) );
		return true;
	}

	/// <summary>
	/// Load a previously saved game state from a file, applying the differences to the original scene file(s) to reconstruct the saved scene.
	/// </summary>
	/// <returns>True if the load was successful.</returns>
	public async Task<bool> Load( string path )
	{
		//
		// Host only
		//
		if ( !Networking.IsHost ) return false;

		if ( string.IsNullOrWhiteSpace( path ) )
		{
			Log.Warning( "SaveSystem: Cannot load — path is null or empty." );
			return false;
		}

		if ( !Scene.IsValid() )
		{
			Log.Warning( "SaveSystem: Cannot load — no valid scene." );
			return false;
		}

		if ( !FileSystem.Data.FileExists( path ) )
		{
			Log.Warning( $"SaveSystem: Save file '{path}' does not exist." );
			return false;
		}

		JsonObject saveRoot;

		try
		{
			var text = FileSystem.Data.ReadAllText( path );
			saveRoot = JsonNode.Parse( text )?.AsObject();
		}
		catch ( Exception e )
		{
			Log.Warning( $"SaveSystem: Failed to read save file '{path}': {e.Message}" );
			return false;
		}

		if ( saveRoot is null )
		{
			Log.Warning( $"SaveSystem: Save file '{path}' is empty or invalid." );
			return false;
		}

		// Validate that the save format version is compatible
		var saveVersion = saveRoot["Version"]?.GetValue<int>() ?? 0;
		if ( saveVersion != CurrentSaveVersion )
		{
			Log.Warning( $"SaveSystem: Save file '{path}' uses version {saveVersion}, but this build requires version {CurrentSaveVersion}. The save is incompatible." );
			return false;
		}

		var sceneSources = new List<string>();
		if ( saveRoot["SceneSources"] is JsonArray sourcesArray )
		{
			foreach ( var source in sourcesArray )
			{
				var val = source?.GetValue<string>();
				if ( !string.IsNullOrEmpty( val ) )
					sceneSources.Add( val );
			}
		}

		if ( sceneSources.Count == 0 )
		{
			Log.Warning( $"SaveSystem: Save file '{path}' has no scene sources." );
			return false;
		}

		// Resolve all scene files from disk, the first being the primary scene
		var sceneFiles = new List<SceneFile>();
		foreach ( var source in sceneSources )
		{
			var sf = ResourceLibrary.Get<SceneFile>( source );
			if ( sf is null )
			{
				Log.Warning( $"SaveSystem: Scene source '{source}' not found. Skipping." );
				continue;
			}
			sceneFiles.Add( sf );
		}

		if ( sceneFiles.Count == 0 )
		{
			Log.Warning( $"SaveSystem: None of the scene sources from save '{path}' could be found." );
			return false;
		}

		// Mount any cloud packages the save references before loading the scene
		if ( saveRoot["RequiredPackages"] is JsonArray pkgArray )
		{
			await MountRequiredPackages( pkgArray );
		}

		Scene.RunEvent<Global.ISaveEvents>( x => x.BeforeLoad( path ) );

		Json.Patch savedPatch = null;
		if ( saveRoot["Patch"] is JsonObject patchNode )
		{
			savedPatch = Json.FromNode<Json.Patch>( patchNode );
		}

		savedPatch ??= new Json.Patch();

		// Use the saved scene ID for the baseline root so the patch applies correctly
		var savedSceneId = Guid.TryParse( saveRoot["SceneId"]?.GetValue<string>(), out var parsedId )
			? parsedId
			: sceneFiles[0].Id;

		var baseline = BuildCompositeBaselineFromFiles( sceneFiles, savedSceneId );
		if ( baseline is null )
		{
			Log.Warning( "SaveSystem: Failed to build baseline from scene sources." );
			return false;
		}

		// Create a new SceneFile by applying the saved patch to the baseline, then load that
		var patched = Json.ApplyPatch( baseline, savedPatch, GameObject.DiffObjectDefinitions );
		var primarySceneFile = sceneFiles[0];
		var savedSceneProperties = saveRoot["SceneProperties"];
		var patchedSceneFile = BuildPatchedSceneFile( primarySceneFile, patched, savedSceneProperties );

		// Show loading screen first
		BroadcastShowLoadingScreen();
		await Task.Delay( 50 );

		_suppressSystemScene = true; // Make sure we don't load two system scenes.....

		// Make sure the launch map is set to the one saved in the save file, if any (crap)
		var savedMap = FindLaunchMapName( patchedSceneFile.GameObjects );
		if ( !string.IsNullOrWhiteSpace( savedMap ) )
		{
			LaunchArguments.Map = savedMap;
		}

		CleanupSystem.PreserveBaselineForSaveLoad();

		var options = new SceneLoadOptions();
		options.SetScene( patchedSceneFile );
		Game.ChangeScene( options );

		var newSystem = SaveSystem.Current;
		if ( newSystem is null )
		{
			Log.Warning( "SaveSystem: Could not find new SaveSystem instance after ChangeScene." );
			return false;
		}

		// Keep track of the original scene sources so any subsequent re-save diffs correctly.
		newSystem._loadedScenes.Clear();
		foreach ( var sf in sceneFiles )
		{
			if ( string.IsNullOrEmpty( sf.ResourcePath ) ) continue;
			newSystem._loadedScenes.Add( new LoadedSceneEntry
			{
				ResourcePath = sf.ResourcePath,
				SceneFileId = sf.Id
			} );
		}

		// Restore metadata onto the new instance so AfterLoad event handlers can read it.
		newSystem._metadata = saveRoot["Metadata"] is JsonObject metaNode
			? JsonSerializer.Deserialize<Dictionary<string, string>>( metaNode ) ?? new Dictionary<string, string>()
			: new Dictionary<string, string>();
		newSystem.LoadedSavePath = path;

		// Restore [Sync] property values before network ownership so everything is populated BEFORE any ownership-change callbacks fire.
		if ( saveRoot["SyncState"] is JsonObject syncNode )
		{
			RestoreSyncState( newSystem.Scene, syncNode );
		}

		if ( saveRoot["NetworkOwnership"] is JsonObject ownershipNode )
		{
			RestoreNetworkOwnership( newSystem.Scene, ownershipNode );
		}

		newSystem.Scene.RunEvent<Global.ISaveEvents>( x => x.AfterLoad( path ) );
		return true;
	}

	// Before we load any scene, we keep track of the source scene file so we can diff against it later when saving
	void ISceneLoadingEvents.BeforeLoad( Scene scene, SceneLoadOptions options )
	{
		var sceneFile = options.GetSceneFile();
		if ( sceneFile is null ) return;

		if ( !options.IsAdditive )
		{
			_loadedScenes.Clear();
			_metadata.Clear();
			LoadedSavePath = null;
		}

		var resourcePath = sceneFile.ResourcePath;

		// The scene file might be in-memory (e.g. editor Play creates one via
		// CreateSceneFile). Try to resolve the original on-disk scene file by
		// checking Scene.Source, then by matching the scene file's Id against
		// all loaded SceneFile resources.
		if ( string.IsNullOrEmpty( resourcePath ) && scene.Source is SceneFile sourceFile )
		{
			resourcePath = sourceFile.ResourcePath;
		}

		if ( string.IsNullOrEmpty( resourcePath ) && sceneFile.Id != Guid.Empty )
		{
			var match = ResourceLibrary.GetAll<SceneFile>()
				.FirstOrDefault( x => x.Id == sceneFile.Id && !string.IsNullOrEmpty( x.ResourcePath ) );

			if ( match is not null )
				resourcePath = match.ResourcePath;
		}

		if ( string.IsNullOrEmpty( resourcePath ) )
			return;
		if ( _loadedScenes.Any( e => e.ResourcePath == resourcePath ) )
			return;

		_loadedScenes.Add( new LoadedSceneEntry
		{
			ResourcePath = resourcePath,
			SceneFileId = sceneFile.Id
		} );
	}

	// When loading a scene, we hook into it and make sure WantsSystemScene is false when we need to prevent duplicates
	Task ISceneLoadingEvents.OnLoad( Scene scene, SceneLoadOptions options, LoadingContext context )
	{
		if ( _suppressSystemScene )
		{
			scene.WantsSystemScene = false;
			_suppressSystemScene = false;
		}

		return Task.CompletedTask;
	}

	private sealed class LoadedSceneEntry
	{
		public string ResourcePath { get; init; }
		public Guid SceneFileId { get; init; }
	}

	private SceneFile GetPrimarySceneFile()
	{
		if ( _loadedScenes.Count == 0 ) return null;
		return ResourceLibrary.Get<SceneFile>( _loadedScenes[0].ResourcePath );
	}

	private static JsonArray CollectRequiredPackages( List<LoadedSceneEntry> loadedScenes, JsonObject currentSceneJson )
	{
		var packages = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var entry in loadedScenes )
		{
			var sf = ResourceLibrary.Get<SceneFile>( entry.ResourcePath );
			if ( sf is null ) continue;

			foreach ( var pkg in sf.GetReferencedPackages() )
			{
				if ( !string.IsNullOrEmpty( pkg ) )
					packages.Add( pkg );
			}
		}

		if ( currentSceneJson is not null )
		{
			foreach ( var pkg in Cloud.ResolvePrimaryAssetsFromJson( currentSceneJson ) )
			{
				if ( !string.IsNullOrEmpty( pkg.FullIdent ) )
					packages.Add( pkg.FullIdent );
			}
		}

		var result = new JsonArray();
		foreach ( var ident in packages )
		{
			result.Add( JsonValue.Create( ident ) );
		}

		return result;
	}

	private static async Task MountRequiredPackages( JsonArray packageArray )
	{
		foreach ( var node in packageArray )
		{
			var ident = node?.GetValue<string>();
			if ( string.IsNullOrEmpty( ident ) ) continue;

			// Skip packages that are already mounted
			if ( Package.TryGetCached( ident, out var _ ) )
				continue;

			await Package.MountAsync( ident, false );
		}
	}

	/// <summary>
	/// This builds a baseline JSON from all the loaded scene files that we've tracked in BeforeLoad.
	/// The final scene gets diffed against this.
	/// </summary>
	private JsonObject BuildCompositeBaseline()
	{
		var sceneFiles = new List<SceneFile>();
		foreach ( var entry in _loadedScenes )
		{
			var sf = ResourceLibrary.Get<SceneFile>( entry.ResourcePath );
			if ( sf is null )
			{
				Log.Warning( $"SaveSystem: Tracked scene '{entry.ResourcePath}' could not be found." );
				continue;
			}
			sceneFiles.Add( sf );
		}

		return BuildCompositeBaselineFromFiles( sceneFiles, Scene.Id );
	}

	/// <summary>
	/// Builds a composite baseline JSON from a list of scene files by merging
	/// all their GameObjects into a single root node.
	/// </summary>
	/// <param name="sceneFiles">The scene files to merge.</param>
	/// <param name="rootId">The GUID to use for the root node. Match the scene's root ID to make sure diffs dont get fucked.</param>
	private static JsonObject BuildCompositeBaselineFromFiles( List<SceneFile> sceneFiles, Guid rootId )
	{
		if ( sceneFiles.Count == 0 ) return null;

		var children = new JsonArray();

		foreach ( var sceneFile in sceneFiles )
		{
			if ( sceneFile?.GameObjects is null ) continue;

			foreach ( var go in sceneFile.GameObjects )
			{
				if ( go is null ) continue;
				children.Add( go.DeepClone() );
			}
		}

		// The root needs to look like a valid GameObject for the diff to actually work
		var root = new JsonObject
		{
			["__guid"] = rootId.ToString(),
			["Flags"] = 0,
			["Components"] = new JsonArray(),
			["Children"] = children,
		};

		return root;
	}

	/// <summary>
	/// Serializes the current live scene as a JSON object in the same format as <see cref="BuildCompositeBaseline"/> so the two can be diffed.
	/// </summary>
	private static JsonObject BuildCurrentSceneJson( Scene scene )
	{
		using var sceneScope = scene.Push();

		var children = new JsonArray();

		foreach ( var child in scene.Children )
		{
			// Skip DontDestroyOnLoad objects — they persist across scene loads and probably shouldn't be part of the save diff (?)
			if ( child.Flags.Contains( GameObjectFlags.DontDestroyOnLoad ) )
				continue;

			var jso = child.Serialize();
			if ( jso is null ) continue;

			DrivenProp.RemoveDriveTags( child, jso );
			children.Add( jso );
		}

		// Needs to look like a GameObject for diffs to work
		var root = new JsonObject
		{
			["__guid"] = scene.Id.ToString(),
			["Flags"] = 0,
			["Components"] = new JsonArray(),
			["Children"] = children,
		};

		return root;
	}

	/// <summary>
	/// Get any changes made to scene-level properties (like gravity, nav mesh settings, etc)
	/// </summary>
	private static JsonNode SerializeScenePropertyDiffs( Scene scene, SceneFile sceneFile )
	{
		var currentProps = scene.SerializeProperties();
		var baseProps = sceneFile.SceneProperties;

		if ( baseProps is null )
			return currentProps?.DeepClone();

		// Only store properties that ACTUALLY differ from the baseline
		var diffs = new JsonObject();
		var hasChanges = false;

		foreach ( var prop in currentProps )
		{
			if ( baseProps.TryGetPropertyValue( prop.Key, out var baseValue ) )
			{
				if ( !JsonNode.DeepEquals( baseValue, prop.Value ) )
				{
					diffs[prop.Key] = prop.Value?.DeepClone();
					hasChanges = true;
				}
			}
			else
			{
				// New property not in baseline
				diffs[prop.Key] = prop.Value?.DeepClone();
				hasChanges = true;
			}
		}

		return hasChanges ? diffs : null;
	}

	/// <summary>
	/// Snapshots network ownership for all owned GameObjects in the scene, storing the owner's SteamID.
	/// </summary>
	private static JsonObject CollectNetworkOwnership( Scene scene )
	{
		var result = new JsonObject();

		foreach ( var go in scene.GetAllObjects( true ) )
		{
			if ( !go.Network.Active ) continue;

			var owner = go.Network.Owner;
			if ( owner is null ) continue;

			result[go.Id.ToString()] = owner.SteamId.Value;
		}

		return result;
	}

	/// <summary>
	/// Restores network ownership by matching saved SteamIDs to connected players.
	/// </summary>
	private static void RestoreNetworkOwnership( Scene scene, JsonObject ownershipData )
	{
		var steamIdToConnection = new Dictionary<long, Connection>();
		foreach ( var conn in Connection.All )
		{
			steamIdToConnection.TryAdd( conn.SteamId.Value, conn );
		}

		// Anything we spawn here, let's batch it
		using var _ = scene.BatchGroup();

		foreach ( var (goGuidStr, node) in ownershipData )
		{
			if ( !Guid.TryParse( goGuidStr, out var goGuid ) ) continue;

			var go = scene.Directory.FindByGuid( goGuid ) as GameObject;
			if ( go is null || !go.IsValid() ) continue;

			Connection target = null;

			if ( node?.GetValue<long>() is long steamIdValue && steamIdValue != 0 )
			{
				steamIdToConnection.TryGetValue( steamIdValue, out target );
			}

			if ( target is null ) continue;

			// Only the host can reassign ownership, and the object must be networked
			if ( !go.Network.Active )
			{
				go.NetworkSpawn( target );
			}
			else
			{
				go.Network.AssignOwnership( target );
			}
		}
	}

	/// <summary>
	/// Collects all [Sync]-only property values from every component in the scene, so the networked state can be restored
	/// </summary>
	private static JsonObject CollectSyncState( Scene scene )
	{
		var result = new JsonObject();

		foreach ( var go in scene.GetAllObjects( true ) )
		{
			if ( go.Flags.Contains( GameObjectFlags.DontDestroyOnLoad ) )
				continue;

			foreach ( var component in go.Components.GetAll() )
			{
				var typeDesc = TypeLibrary.GetType( component.GetType() );
				if ( typeDesc is null ) continue;

				var syncProps = typeDesc.Properties.Where( p => p.HasAttribute<SyncAttribute>() );
				JsonObject componentData = null;

				foreach ( var syncProp in syncProps )
				{
					// Skip properties that also have [Property] since those are already captured by the main diff
					if ( syncProp.HasAttribute<PropertyAttribute>() )
						continue;

					try
					{
						var value = syncProp.GetValue( component );

						// Try JSON first, then fallback to bytepack (mostly for the types that don't serialize well)
						JsonNode node;
						try
						{
							node = Json.ToNode( value, syncProp.PropertyType );
						}
						catch
						{
							var bs = ByteStream.Create( 256 );
							try
							{
								Game.TypeLibrary.ToBytes( value, ref bs );
								var base64 = Convert.ToBase64String( bs.ToArray() );
								node = new JsonObject { ["__bytepack"] = base64 };
							}
							finally
							{
								bs.Dispose();
							}
						}

						componentData ??= new JsonObject();
						componentData[syncProp.Name] = node;
					}
					catch ( Exception e )
					{
						Log.Warning( $"SaveSystem: Failed to serialize [Sync] property {component.GetType().Name}.{syncProp.Name}: {e.Message}" );
					}
				}

				if ( componentData is not null )
				{
					result[component.Id.ToString()] = componentData;
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Restore the [Sync]-only properties
	/// </summary>
	private static void RestoreSyncState( Scene scene, JsonObject syncData )
	{
		foreach ( var (compGuidStr, node) in syncData )
		{
			if ( !Guid.TryParse( compGuidStr, out var compGuid ) ) continue;
			if ( node is not JsonObject propData ) continue;

			var target = scene.Directory.FindComponentByGuid( compGuid );
			if ( target is null ) continue;

			var typeDesc = TypeLibrary.GetType( target.GetType() );
			if ( typeDesc is null ) continue;

			var syncProps = typeDesc.Properties.Where( p => p.HasAttribute<SyncAttribute>() );

			foreach ( var syncProp in syncProps )
			{
				if ( syncProp.HasAttribute<PropertyAttribute>() )
					continue;

				if ( !propData.ContainsKey( syncProp.Name ) )
					continue;

				try
				{
					var jsonValue = propData[syncProp.Name];

					object value;

					// Check if this was serialized via BytePack fallback
					if ( jsonValue is JsonObject wrapper && wrapper.ContainsKey( "__bytepack" ) )
					{
						var base64 = wrapper["__bytepack"]!.GetValue<string>();
						var bytes = Convert.FromBase64String( base64 );
						var reader = ByteStream.CreateReader( bytes );
						try
						{
							value = Game.TypeLibrary.FromBytes<object>( ref reader );
						}
						finally
						{
							reader.Dispose();
						}
					}
					else
					{
						// JSON deserialize as normal
						value = Json.FromNode( jsonValue, syncProp.PropertyType );
					}

					syncProp.SetValue( target, value );
				}
				catch ( Exception e )
				{
					Log.Warning( $"SaveSystem: Failed to restore [Sync] property {target.GetType().Name}.{syncProp.Name}: {e.Message}" );
				}
			}
		}
	}

	/// <summary>
	/// Scan to find the configured launch map from a serialized scene.
	/// </summary>
	private static string FindLaunchMapName( IEnumerable<JsonObject> gameObjects )
	{
		if ( gameObjects is null ) return null;

		foreach ( var go in gameObjects )
		{
			if ( go is null ) continue;

			if ( go["Components"] is JsonArray components )
			{
				foreach ( var component in components.OfType<JsonObject>() )
				{
					var typeName = component["__type"]?.GetValue<string>();
					if ( string.IsNullOrEmpty( typeName ) ) continue;

					var type = TypeLibrary.GetType( typeName );
					if ( type is null || !type.TargetType.IsAssignableTo( typeof( MapInstance ) ) ) continue;

					if ( component["UseMapFromLaunch"]?.GetValue<bool>() != true ) continue;

					var mapName = component["MapName"]?.GetValue<string>();
					if ( !string.IsNullOrWhiteSpace( mapName ) )
						return mapName;
				}
			}

			if ( go["Children"] is JsonArray children )
			{
				var found = FindLaunchMapName( children.OfType<JsonObject>() );
				if ( found is not null ) return found;
			}
		}

		return null;
	}

	/// <summary>
	/// Tells all clients to show the loading screen immediately, before Scene.Load() fires.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void BroadcastShowLoadingScreen()
	{
		LoadingScreen.Title = Game.Language.GetPhrase( "save.loading" );
		LoadingScreen.IsVisible = true;
	}

	/// <summary>
	/// Constructs a <see cref="SceneFile"/> from the patched JSON data so we can load it just like any other scene
	/// </summary>
	private static SceneFile BuildPatchedSceneFile( SceneFile original, JsonObject patchedRoot, JsonNode savedSceneProperties )
	{
		var patchedSceneFile = new SceneFile
		{
			Id = original.Id,
		};

		if ( patchedRoot["Children"] is JsonArray goArray )
		{
			patchedSceneFile.GameObjects = goArray
				.Where( x => x is JsonObject )
				.Select( x => x.DeepClone().AsObject() )
				.ToArray();
		}

		// Start with original scene properties, then apply any saved overrides
		var sceneProperties = original.SceneProperties?.DeepClone()?.AsObject() ?? new JsonObject();

		if ( savedSceneProperties is JsonObject overrides )
		{
			foreach ( var prop in overrides )
			{
				sceneProperties[prop.Key] = prop.Value?.DeepClone();
			}
		}

		patchedSceneFile.SceneProperties = sceneProperties;

		return patchedSceneFile;
	}
}
