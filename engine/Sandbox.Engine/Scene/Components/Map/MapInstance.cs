using Facepunch.ActionGraphs;
using NativeEngine;
using Sentry;
using System.Text.Json.Nodes;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Allows you to load a map into the Scene. This can be either a vpk or a scene map.
/// </summary>
[Expose]
[Title( "Map Instance" )]
[Category( "World" )]
[Icon( "public" )]
[Alias( "MapComponent" )]
public partial class MapInstance : Component, Component.ExecuteInEditor
{
	[Property, Title( "Map" ), MapAssetPath]
	public string MapName { get; set; }

	[Property] public bool UseMapFromLaunch { get; set; }

	[Property, MakeDirty] public bool EnableCollision { get; set; } = true;

	/// <summary>
	/// True if the map is loaded
	/// </summary>
	public bool IsLoaded => loadedMap is not null;

	readonly SemaphoreSlim mapLoadSemaphore = new( 1 );
	readonly HashSet<CancellationTokenSource> tokenSources = new();

	/// <summary>
	/// Called when the map has successfully loaded
	/// </summary>
	[Property] public Action OnMapLoaded { get; set; }

	/// <summary>
	/// Called when the map has been unloaded
	/// </summary>
	[Property] public Action OnMapUnloaded { get; set; }

	SceneMap loadedMap;
	GameObject _mapPhysics;
	string loadedMapName;
	string sceneMapScenePath;

	public MapInstance() : base()
	{
		OnMapLoaded += UpdateDirtyReflections;
		OnMapUnloaded += UpdateDirtyReflections;
	}

	/// <summary>
	/// Get the world bounds of the map
	/// </summary>
	public BBox Bounds
	{
		get
		{
			if ( !loadedMap.IsValid() )
				return default;

			return loadedMap.Bounds;
		}
	}

	internal override void OnEnabledInternal()
	{
		base.OnEnabledInternal();

		Transform.OnTransformChanged += OnTransformChanged;
	}

	internal override void OnDisabledInternal()
	{
		base.OnDisabledInternal();

		Transform.OnTransformChanged -= OnTransformChanged;

		UnloadMap();
	}

	private void OnTransformChanged()
	{
		if ( NoOrigin )
			return;

		var transform = new Transform( WorldPosition );
		WorldTransform = transform;

		if ( loadedMap is not null && loadedMap.IsValid() )
		{
			loadedMap.WorldOrigin = transform.Position;
		}

		foreach ( var body in Bodies )
		{
			if ( !body.IsValid() )
				continue;

			body.Transform = transform;
		}
	}

	protected override Task OnLoad()
	{
		if ( !Active )
			return Task.CompletedTask;

		return LoadMapAsync();
	}

	/// <summary>
	/// Unload the current map.
	/// </summary>
	public void UnloadMap()
	{
		loadedMapName = null;
		sceneMapScenePath = null;

		bool hadMap = loadedMap is not null;

		loadedMap?.Delete();
		loadedMap = null;

		RemoveCollision();

		Physics = null;

		if ( GameObject.IsValid() && GameObject.Children is not null )
		{
			foreach ( var child in GameObject.Children )
			{
				// In editor, don't delete saved child objects
				if ( Scene.IsEditor && !child.Flags.Contains( GameObjectFlags.NotSaved ) )
					continue;

				// If I'm a client and this came from the snapshot.. don't fucking delete me
				if ( Networking.IsClient && child.NetworkMode == NetworkMode.Snapshot )
					continue;

				// If it's a fully networked object and I'm the owner, we can delete
				if ( !child.Network.Active || child.Network.IsOwner )
					child.Destroy();
			}
		}

		if ( hadMap )
		{
			OnMapUnloaded?.InvokeWithWarning();
			g_pWorldRendererMgr.ServiceWorldRequests();
			SceneMap.OnMapUpdated -= OnMapUpdated;
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( loadedMapName != MapName )
		{
			_ = LoadMapAsync( MapName );
		}
	}

	void CancelLoading()
	{
		foreach ( var ts in tokenSources )
		{
			ts.Cancel();
		}

		tokenSources.Clear();
	}

	async Task<bool> LoadMapAsync()
	{
		if ( UseMapFromLaunch && !string.IsNullOrWhiteSpace( LaunchArguments.Map ) )
		{
			MapName = LaunchArguments.Map;
			await LoadMapAsync( MapName );
			return true;
		}

		if ( string.IsNullOrWhiteSpace( MapName ) )
			return false;

		return await LoadMapAsync( MapName );
	}

	async Task<bool> LoadMapAsync( string mapName )
	{
		if ( loadedMapName == mapName )
			return true;

		SentrySdk.AddBreadcrumb( $"LoadMapAsync {mapName}", "map.load" );

		UnloadMap();
		CancelLoading();
		loadedMapName = mapName;

		if ( string.IsNullOrWhiteSpace( mapName ) )
		{
			UnloadMap();
			return true;
		}

		CancellationTokenSource tokenSource = new CancellationTokenSource();
		tokenSources.Add( tokenSource );
		var token = tokenSource.Token;

		try
		{
			// wait for access
			await mapLoadSemaphore.WaitAsync( token );
			GameObject.Flags |= GameObjectFlags.Loading;

			token.ThrowIfCancellationRequested();

			LoadingScreen.Title = $"Loading Map";

			var mapFileName = mapName;

			if ( mapFileName.EndsWith( ".vmap" ) )
				mapFileName = System.IO.Path.ChangeExtension( mapFileName, ".vpk" );

			// If this looks like a package ident, then download it
			if ( !mapFileName.EndsWith( ".vpk" ) && Package.TryParseIdent( mapName, out var parts ) )
			{
				var package = await Package.Fetch( mapName, false );

				if ( package is null || !IsValid )
				{
					Log.Warning( $"No package found: {mapName}" );
					return false;
				}

				if ( package.TypeName != "map" )
				{
					Log.Warning( $"Package {package.FullIdent} is not a map - it's a {package.TypeName}" );
					return false;
				}

				token.ThrowIfCancellationRequested();

				LoadingScreen.Title = $"Loading Map - {package.Title}";

				var fs = await package.MountAsync();

				if ( !IsValid || fs is null )
					return false;

				mapFileName = package.PrimaryAsset;

				if ( string.IsNullOrWhiteSpace( mapFileName ) )
				{
					var maps = fs.FindFile( "/", "*.vpk", true ).ToArray();
					if ( maps.Length == 0 )
					{
						Log.Warning( $"Package '{mapName}' had no map!" );
						return false;
					}

					// use shortest name, just trying to avoid loading the skybox vpk
					mapFileName = maps.OrderBy( x => x.Length ).First();
				}
				else if ( mapFileName.EndsWith( ".scene" ) )
				{
					// Scene maps can be loaded, but we need to do some special work with the GameObjects.
					sceneMapScenePath = mapFileName;
				}
			}

			token.ThrowIfCancellationRequested();

			try
			{
				loadedMapName = mapName;
				SentrySdk.AddBreadcrumb( $"Map Name is {loadedMapName}, filename is {mapFileName}", "map.load" );

				using ( Scene.Push() )
				{
					var loader = new MapComponentMapLoader( this, NoOrigin ? 0 : WorldPosition );
					loadedMap = new SceneMap( loader.World, mapFileName, loader );

					if ( loadedMap.IsValid() )
					{
						var aggregateData = g_pPhysicsSystem.GetAggregateData( $"{loadedMap.MapFolder}/world_physics.vphys" );
						if ( aggregateData.IsValid )
						{
							var objectKey = $"{mapFileName}.World Physics";

							Physics = new PhysicsGroupDescription( aggregateData );
							var go = new GameObject();

							//
							// We don't network this, because it'll be loaded on the client.. but we want the
							// ID to match between all clients - so we set it deterministically.
							//
							go.SetDeterministicId( objectKey.ToGuid() );
							go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
							go.Name = "World Physics";
							go.Tags.Add( "world" );
							go.SetParent( GameObject, NoOrigin );
							Collider = go.Components.Create<MapCollider>();
							Collider.SetDeterministicId( $"{objectKey}.MapCollider".ToGuid() );
							_mapPhysics = go;

							AddCollision();
						}
						else
						{
							Log.Warning( $"Couldn't find map physics: '{loadedMap.MapFolder}/world_physics.vphys'" );
							SentrySdk.AddBreadcrumb( $"Couldn't find map physics: '{loadedMap.MapFolder}/world_physics.vphys'", "map.load" );
						}
					}

					LoadMapSceneGameObjects( mapName );
				}
			}
			catch ( Exception e )
			{
				SentrySdk.AddBreadcrumb( $"Couldn't load map ({e.Message})", "map.load" );
				Log.Warning( e, $"Couldn't load map ({e.Message})" );
				return false;
			}

			OnMapLoaded?.InvokeWithWarning();
		}
		finally
		{
			mapLoadSemaphore.Release();

			if ( GameObject.IsValid() )
			{
				GameObject.Flags &= ~GameObjectFlags.Loading;
			}

			SceneMap.OnMapUpdated += OnMapUpdated;
		}

		tokenSources.Remove( tokenSource );
		return true;
	}

	/// <summary>
	/// Make sure all cubemaps placed on scene are up-to-date when we
	/// load/unload a map instance.
	/// </summary>
	private void UpdateDirtyReflections()
	{
		if ( !IsValid )
			return;

		foreach ( var cubemap in Scene.GetAllComponents<EnvmapProbe>() )
		{
			if ( cubemap.IsValid() )
			{
				cubemap.Dirty = true;
			}
		}
	}

	private void LoadMapSceneGameObjects( string mapName )
	{
		// If this is being loaded from a vpk, load scene contents from world.scene_c.
		// If this is from an actual scene, just use that.
		var path = string.IsNullOrWhiteSpace( sceneMapScenePath ) ? $"{loadedMap?.MapFolder}/world.scene_c" : sceneMapScenePath + "_c";
		var scene = Game.Resources.LoadRawGameResource( path );
		if ( scene is not SceneFile sceneFile )
			return;

		// Wouldn't this be nice? Doesn't make sense within a MapInstance, but when we switch away
		// SceneLoadOptions options = new() { IsAdditive = true };
		// options.SetScene( sceneFile );
		// Scene.Load( options );

		using var optionsScope = ActionGraph.PushSerializationOptions( sceneFile.SerializationOptions with { ForceUpdateCached = Scene.IsEditor } );
		using var sceneScope = Scene.Push();
		using var batchGroup = CallbackBatch.Batch();

		foreach ( var json in sceneFile.GameObjects )
		{
			// Should we ignore this GameObject?
			if ( ShouldIgnoreGameObject( json ) )
				continue;

			var go = new GameObject( false );
			go.Flags |= GameObjectFlags.NotSaved;
			go.SetMapSource( mapName );
			go.SetParent( GameObject, NoOrigin );
			go.Deserialize( json );

			// This is a failsafe for the above check for existing networked objects
			if ( Networking.IsClient && go.NetworkMode != NetworkMode.Never )
			{
				go.DestroyImmediate();
				continue;
			}

			if ( go.NetworkMode == NetworkMode.Object )
			{
				go.NetworkSpawn();
			}
		}
	}

	private bool ShouldIgnoreGameObject( JsonObject json )
	{
		// Don't load another MapInstance if this scene already has one.
		if ( json["Components"] is JsonArray components )
		{
			if ( components.Any( comp => comp["__type"]?.ToString() == "Sandbox.MapInstance" ) )
			{
				return true;
			}
		}

		if ( !Networking.IsClient || !json.TryGetPropertyValue( JsonKeys.Id, out var id ) )
			return false;

		var gameObject = Scene.Directory.FindByGuid( (Guid)id );

		if ( !gameObject.IsValid() || gameObject.IsDestroyed ||
			 gameObject.Flags.HasFlag( GameObjectFlags.NotNetworked ) )
		{
			return false;
		}

		// We already have a GameObject with this id and its networked, so ignore this one
		return gameObject.NetworkMode != NetworkMode.Never;
	}

	private void OnMapUpdated( string mapName )
	{
		UnloadMap();
	}

	internal void OnCreateObjectInternal( GameObject go, MapLoader.ObjectEntry kv )
	{
		try
		{
			OnCreateObject( go, kv );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't load map object {kv.TypeName} ({e.Message})" );
			return;
		}
	}

	/// <summary>
	/// Override this to add components to a map object.
	/// Only called for map objects that are not implemented.
	/// </summary>
	protected virtual void OnCreateObject( GameObject go, MapLoader.ObjectEntry kv )
	{

	}

	[Property, Hide]
	public bool NoOrigin { get; set; }

	public override int ComponentVersion => 1;

	[Expose, JsonUpgrader( typeof( MapInstance ), 1 )]
	private static void Upgrader_v1( JsonObject obj )
	{
		obj["NoOrigin"] = true;
	}

	/// <summary>
	/// Get the PVS of the loaded map
	/// </summary>
	internal IPVS GetNetworkPvs() => loadedMap?.PVS ?? default;
}

file class MapComponentMapLoader : SceneMapLoader
{
	private readonly MapInstance Map;
	private readonly Dictionary<string, GameObject> MapObjects = new();

	public MapComponentMapLoader( MapInstance mapComponent, Vector3 origin ) :
		base( mapComponent.Scene.SceneWorld, mapComponent.Scene.PhysicsWorld, origin )
	{
		Map = mapComponent;
	}

	//
	// Install a function that will create the SceneObjects from SceneMapLoader when enabled
	// (and delete them when disabled). This is a temporary workaround until everything has a
	// working InitializeFromLegacy function.
	//
	void AddMapObjectComponent( GameObject go, ObjectEntry kv )
	{
		var c = go.Components.Create<MapObjectComponent>();

		c.RecreateMapObjects += () =>
		{
			SceneObjects.Clear();
			base.CreateObject( kv );

			if ( SceneObjects.Count > 0 )
			{
				c.AddSceneObjects( SceneObjects );
			}
		};
	}

	void CreateStaticModel( GameObject go, ObjectEntry kv )
	{
		bool isFuncBrush = kv.TypeName == "func_brush";
		//bool solidBsp = kv.GetValue<bool>( "solidbsp", true );
		int BrushSolidities_e = kv.GetValue<int>( "Solidity", 0 );
		int SolidType_t = kv.GetValue<int>( "solid", 0 );

		// Renderer
		{
			var renderer = go.Components.Create<ModelRenderer>();
			renderer.Model = kv.GetResource<Model>( "model" );
			renderer.Tint = kv.GetValue<Color>( "rendercolor", Color.White );
		}

		if ( isFuncBrush )
		{
			bool makeSolid = SolidType_t > 0 && BrushSolidities_e != 1;

			if ( makeSolid )
			{
				var collider = go.Components.Create<ModelCollider>();
				collider.Static = true; // I think func_brush is always static?
				collider.Model = kv.GetResource<Model>( "model" );
			}
		}
	}

	void CreateSoundScapeBox( GameObject go, ObjectEntry kv )
	{
		var soundscape = go.Components.Create<SoundscapeTrigger>();
		soundscape.Soundscape = GameResource.Load<Soundscape>( kv.GetString( "Soundscape" ) );
		soundscape.BoxSize = kv.GetValue<Vector3>( "Extents" ) * 0.5f;
		soundscape.Type = SoundscapeTrigger.TriggerType.Box;
		soundscape.Enabled = kv.GetValue<bool>( "Enabled" );
	}

	void CreateSoundScape( GameObject go, ObjectEntry kv )
	{
		var soundscape = go.Components.Create<SoundscapeTrigger>();
		soundscape.Soundscape = GameResource.Load<Soundscape>( kv.GetString( "Soundscape" ) );
		soundscape.Radius = kv.GetValue<float>( "radius" );
		soundscape.Type = SoundscapeTrigger.TriggerType.Sphere;
		soundscape.Enabled = kv.GetValue<bool>( "Enabled" );
	}

	void CreateGradientFog( GameObject go, ObjectEntry kv )
	{
		var fog = go.Components.Create<GradientFog>();
		fog.Enabled = kv.GetValue<bool>( "fogenabled" );
		fog.Color = kv.GetValue<Color>( "fogcolor" ).WithAlpha( kv.GetValue<float>( "fogmaxopacity" ) );
		fog.StartDistance = kv.GetValue<float>( "FogStart" );
		fog.EndDistance = kv.GetValue<float>( "FogEnd" );
		fog.Height = kv.GetValue<float>( "fogendheight" );
		fog.FalloffExponent = kv.GetValue<float>( "fogfalloffexponent" );
		fog.VerticalFalloffExponent = kv.GetValue<float>( "fogverticalexponent" );
	}

	void CreateCubemapFog( GameObject go, ObjectEntry kv )
	{
		var fog = go.Components.Create<CubemapFog>();
		fog.Sky = kv.GetResource<Material>( "cubemapfogmaterial" );
		fog.Blur = kv.GetValue<float>( "cubemapfoglodbiase" );
		fog.StartDistance = kv.GetValue<float>( "cubemapfogstartdistance" );
		fog.EndDistance = kv.GetValue<float>( "cubemapfogenddistance" );
		fog.FalloffExponent = kv.GetValue<float>( "cubemapfogfalloffexponent" );
		fog.HeightExponent = kv.GetValue<float>( "cubemapfogheightexponent" );
		fog.HeightStart = kv.GetValue<float>( "cubemapfogheightstart" );
		fog.HeightWidth = kv.GetValue<float>( "cubemapfogheightwidth" );
	}

	void CreateProp( GameObject go, ObjectEntry kv )
	{
		var model = kv.GetResource<Model>( "model" );
		if ( model is null || !model.native.IsValid )
			return;

		bool isAnimated = kv.TypeName == "prop_dynamic" || kv.TypeName == "prop_animated";
		bool isStatic = isAnimated || kv.GetValue<bool>( "static" );
		bool isNetworked = !isStatic;

		// Don't spawn networked props, because they will be spawned by
		// the network!
		if ( isNetworked && Networking.IsClient )
		{
			go.Destroy();
			return;
		}

		Prop prop;

		prop = go.Components.Create<Prop>();

		if ( prop.IsValid() )
		{
			prop.Model = model;
			prop.Tint = kv.GetValue( "rendercolor", Color.White );
			prop.WorldScale = kv.GetValue( "scales", Vector3.One );
		}

		if ( model.Physics is null || model.Physics.Parts.Count == 0 )
			return;

		if ( isStatic )
		{
			prop.IsStatic = true;
			go.Tags.Add( "world" );
			return;
		}

		// Map props are fully networked. Their positions and destroys are networked.
		prop.GameObject.Network.SetOrphanedMode( NetworkOrphaned.ClearOwner );
		prop.GameObject.Network.SetOwnerTransfer( OwnerTransfer.Takeover );
		prop.GameObject.NetworkSpawn();
	}

	protected override void CreateObject( ObjectEntry kv )
	{
		var parent = Map.GameObject;

		if ( !string.IsNullOrWhiteSpace( kv.ParentName ) )
		{
			if ( MapObjects.TryGetValue( kv.ParentName, out var outParent ) )
				parent = outParent;
		}

		var targetName = kv.TargetName;
		var prefix = "[PR#]";
		if ( !string.IsNullOrWhiteSpace( targetName ) && targetName.StartsWith( prefix ) )
			targetName = targetName[prefix.Length..];

		var go = new GameObject( false );
		go.SetParent( parent );
		go.Flags |= GameObjectFlags.NotSaved;
		go.Name = string.IsNullOrWhiteSpace( targetName ) ? $"{kv.TypeName}" : $"{kv.TypeName} <{targetName}>";
		go.WorldTransform = kv.Transform;
		go.Tags.Add( kv.Tags );

		if ( !string.IsNullOrWhiteSpace( kv.TargetName ) )
			MapObjects.TryAdd( kv.TargetName, go );

		switch ( kv.TypeName )
		{
			case "func_brush":
				{
					CreateStaticModel( go, kv );
					break;
				}
			case "info_player_start":
				{
					go.Components.Create<SpawnPoint>();
					break;
				}

			case "prop_dynamic":
			case "prop_animated":
			case "prop_physics":
				{
					CreateProp( go, kv );
					break;
				}

			case "env_sky":
				{
					SkyBox2D.InitializeFromLegacy( go, kv );
					break;
				}

			case "skybox_reference":
				{
					MapSkybox3D.InitializeFromLegacy( go, kv );
					break;
				}

			case "env_volumetric_fog_volume":
				{
					VolumetricFogVolume.InitializeFromLegacy( go, kv );
					break;
				}
			case "env_volumetric_fog_controller":
				{
					// We only take the baked fog texture from the legacy component
					VolumetricFogController.InitializeFromLegacy( go, kv );
					break;
				}
			case "env_cubemap":
			case "env_cubemap_box":
				{
					EnvmapProbe.InitializeFromLegacy( go, kv );
					break;
				}

			case "env_combined_light_probe_volume":
				{
					EnvmapProbe.InitializeFromLegacy( go, kv ); // create an envmap component
					AddMapObjectComponent( go, kv ); // create the probe sceneobject (we don't have a component for it)
					break;
				}
			case "snd_soundscape_box":
				{
					CreateSoundScapeBox( go, kv );
					break;
				}
			case "snd_soundscape":
				{
					CreateSoundScape( go, kv );
					break;
				}
			case "env_gradient_fog":
				{
					CreateGradientFog( go, kv );
					break;
				}
			case "env_cubemap_fog":
				{
					CreateCubemapFog( go, kv );
					break;
				}
		}

		// Give users a chance to override functionality
		Map.OnCreateObjectInternal( go, kv );

		// If no components were added, add our default MapObjectComponent
		if ( go.Components.Count == 0 )
		{
			AddMapObjectComponent( go, kv );
		}

		go.Enabled = true;

		using ( CallbackBatch.Batch() )
		{
			go.Components.ForEach( "Loading", true, c => c.OnLoadInternal() );
			go.Components.ForEach( "OnValidate", true, c => c.OnValidateInternal() );
		}
	}
}
