using Facepunch.ActionGraphs;
using NativeEngine;
using Sandbox.Clutter;
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
	/// <summary>
	/// The Workshop map item ID, map package ident, relative map asset path, or mounted scene URI.
	/// </summary>
	[Property, Title( "Map" ), MapAssetPath]
	public string MapName { get; set; }

	/// <summary>
	/// Try the launch map first, falling back to the configured map if it cannot load.
	/// </summary>
	[Property] public bool UseMapFromLaunch { get; set; }

	/// <summary>
	/// Create collision bodies for the loaded Hammer world.
	/// </summary>
	[Property]
	public bool EnableCollision
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			OnEnableCollisionChanged();
		}
	} = true;

	/// <summary>
	/// True if the map is loaded
	/// </summary>
	public bool IsLoaded { get; set; }

	readonly SemaphoreSlim mapLoadSemaphore = new( 1 );
	CancellationTokenSource pendingLoad;

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
	Action releaseMapRequirement;
	IDisposable _systemOverridesScope;

	/// <summary>
	/// The VPK joining clients mount before applying the scene snapshot.
	/// </summary>
	internal string LoadedVpkPath { get; private set; }

	/// <summary>
	/// Refresh reflection probes when the map changes.
	/// </summary>
	public MapInstance()
	{
		OnMapLoaded += UpdateDirtyReflections;
		OnMapUnloaded += UpdateDirtyReflections;
	}

	/// <summary>
	/// Get the world bounds of the map
	/// </summary>
	public BBox Bounds => loadedMap?.Bounds ?? default;

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

	internal override void OnDestroyInternal()
	{
		pendingLoad?.Cancel();
		SceneMap.OnMapUpdated -= OnMapUpdated;

		base.OnDestroyInternal();
	}

	private void OnTransformChanged()
	{
		if ( NoOrigin )
			return;

		var transform = new Transform( WorldPosition );
		WorldTransform = transform;

		if ( loadedMap.IsValid() )
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

	protected override Task OnLoad( LoadingContext context ) => Active ? LoadMapAsync( context ) : Task.CompletedTask;

	/// <summary>
	/// Unload the current map.
	/// </summary>
	public void UnloadMap()
	{
		pendingLoad?.Cancel();
		releaseMapRequirement?.Invoke();

		loadedMapName = null;
		releaseMapRequirement = null;
		LoadedVpkPath = null;

		bool hadMap = loadedMap is not null;

		loadedMap?.Delete();
		loadedMap = null;

		RemoveCollision();

		Physics = null;

		_systemOverridesScope?.Dispose();
		_systemOverridesScope = null;

		if ( GameObject.IsValid() && GameObject.Children is not null )
		{
			foreach ( var child in GameObject.Children )
			{
				// In editor, don't delete saved child objects
				if ( Scene.IsEditor && !child.Flags.Contains( GameObjectFlags.NotSaved ) )
					continue;

				// If I'm a client and this came from the snapshot.. don't fucking delete me
				if ( ContentComesFromSnapshot && child.NetworkMode == NetworkMode.Snapshot )
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
		}

		SceneMap.OnMapUpdated -= OnMapUpdated;

		IsLoaded = false;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( loadedMapName != MapName )
		{
			_ = LoadMapAsync( MapName, null );
		}
	}

	async Task<bool> LoadMapAsync( LoadingContext context )
	{
		var launch = LaunchArguments.Map;
		if ( UseMapFromLaunch && !ContentComesFromSnapshot && !string.IsNullOrWhiteSpace( launch ) && await LoadMapAsync( launch, context ) )
		{
			MapName = launch;
			return true;
		}

		return Active && !string.IsNullOrWhiteSpace( MapName ) && await LoadMapAsync( MapName, context );
	}

	async Task<bool> LoadMapAsync( string mapName, LoadingContext context )
	{
		if ( loadedMapName == mapName )
			return IsLoaded || string.IsNullOrWhiteSpace( mapName );

		SentrySdk.AddBreadcrumb( $"LoadMapAsync {mapName}", "map.load" );

		context?.Title = "Loading Map";

		UnloadMap();
		loadedMapName = mapName;

		if ( string.IsNullOrWhiteSpace( mapName ) ) return true;

		using var request = new CancellationTokenSource();
		pendingLoad = request;
		var token = request.Token;
		var snapshotContent = Networking.IsClient || (Scene?.IsLoadingSnapshot ?? false);
		Action release = null;
		bool acquired = false;

		try
		{
			await mapLoadSemaphore.WaitAsync( token );
			acquired = true;
			_loadingSnapshotContent = snapshotContent;
			GameObject.Flags |= GameObjectFlags.Loading;

			var map = await MapReference.ResolveAsync( mapName );
			token.ThrowIfCancellationRequested();
			var opened = await map.OpenAsync( token );
			release = opened.Release;
			if ( opened.Asset is null || !IsValid ) return false;

			await Task.Yield();
			token.ThrowIfCancellationRequested();

			using var scope = Scene.Push();
			if ( opened.Asset.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( !LoadMapSceneGameObjects( mapName, opened.Asset + "_c" ) )
				{
					Log.Warning( $"Failed to load scenemap: {opened.Asset}" );
					return false;
				}
			}
			else
			{
				var vpk = System.IO.Path.ChangeExtension( opened.Asset, ".vpk" );
				if ( !LoadWorld( vpk ) ) return false;

				LoadMapSceneGameObjects( mapName, $"{loadedMap.MapFolder}/world.scene_c" );
				LoadedVpkPath = vpk;
			}

			token.ThrowIfCancellationRequested();
			releaseMapRequirement = release;
			release = null;
			IsLoaded = true;
			OnMapLoaded?.InvokeWithWarning();
			return true;
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
		{
			return false;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't load map '{mapName}'" );
			return false;
		}
		finally
		{
			release?.Invoke();

			if ( acquired )
			{
				_loadingSnapshotContent = false;
				if ( GameObject.IsValid() ) GameObject.Flags &= ~GameObjectFlags.Loading;
				mapLoadSemaphore.Release();
			}

			if ( pendingLoad == request )
			{
				pendingLoad = null;
				if ( IsValid && !token.IsCancellationRequested ) SceneMap.OnMapUpdated += OnMapUpdated;
			}
		}
	}

	bool LoadWorld( string vpk )
	{
		var loader = new MapComponentMapLoader( this, NoOrigin ? 0 : WorldPosition );
		loadedMap = new SceneMap( loader.World, vpk, loader );
		if ( !loadedMap.IsValid() )
		{
			loadedMap.Delete();
			loadedMap = null;
			Log.Warning( $"Couldn't load map: '{vpk}'" );
			return false;
		}

		var path = $"{loadedMap.MapFolder}/world_physics.vphys";
		Physics = PhysicsGroupDescription.Load( path );
		if ( Physics is null )
		{
			Log.Warning( $"Couldn't find map physics: '{path}'" );
			return true;
		}

		// Each client builds the world physics locally with the same object and component IDs.
		var key = $"{vpk}.World Physics";
		_mapPhysics = new GameObject();
		_mapPhysics.SetDeterministicId( key.ToGuid() );
		_mapPhysics.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_mapPhysics.Name = "World Physics";
		_mapPhysics.Tags.Add( "world" );
		_mapPhysics.SetParent( GameObject, NoOrigin );
		Collider = _mapPhysics.Components.Create<MapCollider>();
		Collider.SetDeterministicId( $"{key}.MapCollider".ToGuid() );
		AddCollision();
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

	private bool LoadMapSceneGameObjects( string mapName, string path )
	{
		var sceneFile = SceneFile.Load( path ) ?? Game.Resources.LoadRawGameResource( path ) as SceneFile;
		if ( sceneFile is null ) return false;

		using var optionsScope = ActionGraph.PushSerializationOptions( sceneFile.SerializationOptions with { ForceUpdateCached = Scene.IsEditor } );
		using var sceneScope = Scene.Push();
		// Set up a blob context to resolve binary data in the map scene.
		using var blobs = BlobDataSerializer.Load( sceneFile.BinaryData, path );
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
			if ( ContentComesFromSnapshot && go.NetworkMode != NetworkMode.Never )
			{
				go.DestroyImmediate();
				continue;
			}

			if ( go.NetworkMode == NetworkMode.Object )
			{
				go.NetworkSpawn();
			}
		}

		// Apply scene-level GameObjectSystems data (e.g. painted clutter) to the host scene.
		if ( sceneFile.SceneProperties?["GameObjectSystems"] is { } systemsNode )
		{
			_systemOverridesScope = Scene.ApplyTransientGameObjectSystemOverrides( systemsNode );

			if ( !NoOrigin
				&& systemsNode is JsonObject systems
				&& systems[typeof( ClutterGridSystem ).FullName] is JsonObject clutter
				&& clutter.ContainsKey( nameof( ClutterGridSystem.Storage ) ) )
			{
				Scene.GetSystem<ClutterGridSystem>()?.Storage.ApplyWorldTransform( WorldTransform );
			}
		}

		return true;
	}

	bool _loadingSnapshotContent;

	/// <summary>
	/// This map load is restoring content supplied by the network snapshot.
	/// </summary>
	internal bool ContentComesFromSnapshot => Networking.IsClient || _loadingSnapshotContent || (Scene?.IsLoadingSnapshot ?? false);

	private bool ShouldIgnoreGameObject( JsonObject json )
	{
		// Don't load another MapInstance if this scene already has one.
		if ( json["Components"] is JsonArray components && components.Any( comp => comp["__type"]?.ToString() == "Sandbox.MapInstance" ) )
			return true;

		if ( !ContentComesFromSnapshot || !json.TryGetPropertyValue( JsonKeys.Id, out var id ) )
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

	/// <summary>
	/// Load map objects in world space. Older maps use this to preserve their original placement.
	/// </summary>
	[Property, Hide]
	public bool NoOrigin { get; set; }

	/// <inheritdoc/>
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

file class MapComponentMapLoader( MapInstance map, Vector3 origin ) : SceneMapLoader( map.Scene.SceneWorld, map.Scene.PhysicsWorld, origin )
{
	private readonly Dictionary<string, GameObject> MapObjects = new();

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

	void CreateBrush( GameObject go, ObjectEntry kv )
	{
		var model = kv.GetResource<Model>( "model" );
		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;
		renderer.Tint = kv.GetValue<Color>( "rendercolor", Color.White );

		if ( kv.GetValue<int>( "solid" ) <= 0 || kv.GetValue<int>( "Solidity" ) == 1 ) return;

		var collider = go.Components.Create<ModelCollider>();
		collider.Static = true;
		collider.Model = model;
	}

	void CreateSoundscape( GameObject go, ObjectEntry kv )
	{
		var soundscape = go.Components.Create<SoundscapeTrigger>();
		soundscape.Soundscape = GameResource.Load<Soundscape>( kv.GetString( "Soundscape" ) );
		if ( kv.TypeName == "snd_soundscape_box" )
		{
			soundscape.Type = SoundscapeTrigger.TriggerType.Box;
			soundscape.BoxSize = kv.GetValue<Vector3>( "Extents" ) * 0.5f;
		}
		else
		{
			soundscape.Type = SoundscapeTrigger.TriggerType.Sphere;
			soundscape.Radius = kv.GetValue<float>( "radius" );
		}

		soundscape.Enabled = kv.GetValue<bool>( "Enabled" );
	}

	void CreateGradientFog( GameObject go, ObjectEntry kv )
	{
#pragma warning disable CS0618 // Retain legacy fog support for existing content.
		var fog = go.Components.Create<GradientFog>();
#pragma warning restore CS0618
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
#pragma warning disable CS0618 // Retain legacy fog support for existing content.
		var fog = go.Components.Create<CubemapFog>();
#pragma warning restore CS0618
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

		// The snapshot supplies networked props.
		if ( !isStatic && map.ContentComesFromSnapshot )
		{
			go.Destroy();
			return;
		}

		var prop = go.Components.Create<Prop>();

		if ( prop.IsValid() )
		{
			prop.Model = model;
			prop.Tint = kv.GetValue( "rendercolor", Color.White );
			prop.WorldScale = kv.GetValue( "scales", Vector3.One );
			prop.MaterialGroup = kv.GetValue( "skin", "default" );
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

	//
	// Create a real Light component on the GameObject instead of a raw SceneLight. Exposed settings
	// map to the component properties; everything else rides along in the internal LegacyData backend.
	//
	void CreateLightComponent( GameObject go, ObjectEntry kv, LightType type )
	{
		// Never network these — LegacyData is internal (not serialized), so a snapshot copy would
		// arrive half-configured. Every client builds an identical light from the map VPK anyway.
		go.NetworkMode = NetworkMode.Never;

		var data = LightData.Parse( kv, type );
		if ( !data.Enabled )
			return;

		var legacy = data.ToLegacyData();

		Light light;

		if ( type == LightType.Spot )
		{
			var spot = go.Components.Create<SpotLight>();
			spot.Radius = data.Range;
			spot.ConeInner = data.InnerConeAngle;
			spot.ConeOuter = data.OuterConeAngle;
			spot.Cookie = data.LightCookie;

			// Hammer lights have attenuation and cone mask from the lightmap itself
			// We can take advantage of that and tighten the cone for sharper shadows
			spot.ConeInner = MathF.Min( data.InnerConeAngle, 80.0f );
			spot.ConeOuter = MathF.Min( data.OuterConeAngle, 80.0f );

			// Native quadratic is the Hammer coefficient; SceneLight.QuadraticAttenuation scales by 10000.
			legacy.ConstantAttenuation = data.Attenuation0;
			legacy.LinearAttenuation = data.Attenuation1;
			legacy.QuadraticAttenuation = data.Attenuation2;
			legacy.FallOff = data.FallOff;

			light = spot;
		}
		else if ( type == LightType.Omni )
		{
			var point = go.Components.Create<PointLight>();
			point.Radius = data.Range;

			legacy.ConstantAttenuation = data.Attenuation0;
			legacy.LinearAttenuation = data.Attenuation1;
			legacy.QuadraticAttenuation = data.Attenuation2;
			legacy.Cookie = data.LightCookie; // PointLight doesn't expose a cookie property

			light = point;
		}
		else
		{
			var directional = go.Components.Create<DirectionalLight>();
			var skyColor = kv.GetValue( "SkyColor", Color.White );
			directional.SkyColor = (skyColor * kv.GetValue( "SkyIntensity", 1.0f )).WithAlpha( skyColor.a );

			light = directional;
		}

		// Hammer lights have no concept of "hardness" — let's make them reasonably sharp by default.
		light.ShadowHardness = 0.5f;

		light.LightColor = data.FinalColor;
		light.Shadows = data.CastShadows;
		// Fog mode / render diffuse-specular / attenuation ride in LegacyData — applied when the
		// scene light is created (OnEnabled). Component FogMode can't express Hammer's "Baked" fog.
		light.LegacyData = legacy;
	}

	protected override void CreateObject( ObjectEntry kv )
	{
		var parent = map.GameObject;

		if ( !string.IsNullOrWhiteSpace( kv.ParentName ) && MapObjects.TryGetValue( kv.ParentName, out var mapParent ) )
			parent = mapParent;

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
				CreateBrush( go, kv );
				break;
			case "info_player_start":
				go.Components.Create<SpawnPoint>();
				break;

			// Lights that have a matching component become a GameObject + Light component.
			// Rect/Capsule/Ortho have no component yet, so they fall through to the raw path.
			case "light_environment":
			case "light_directional":
				CreateLightComponent( go, kv, LightType.Directional );
				break;
			case "light_spot":
				CreateLightComponent( go, kv, LightType.Spot );
				break;
			case "light_omni":
				CreateLightComponent( go, kv, LightType.Omni );
				break;

			case "prop_dynamic":
			case "prop_animated":
			case "prop_physics":
				CreateProp( go, kv );
				break;

			case "env_sky":
				SkyBox2D.InitializeFromLegacy( go, kv );
				break;

			case "skybox_reference":
				MapSkybox3D.InitializeFromLegacy( go, kv );
				break;

			case "env_volumetric_fog_volume":
				VolumetricFogVolume.InitializeFromLegacy( go, kv );
				break;
			case "env_volumetric_fog_controller":
				// We only take the baked fog texture from the legacy component
				VolumetricFogController.InitializeFromLegacy( go, kv );
				break;
			case "env_cubemap":
			case "env_cubemap_box":
				EnvmapProbe.InitializeFromLegacy( go, kv );
				break;

			// Never network probe volumes. Their textures live in the map VPK, so a snapshot copy can
			// arrive before the VPK is mounted and render black. Every client builds them from the VPK.
			case "env_light_probe_volume":
				go.NetworkMode = NetworkMode.Never;
				LightProbeVolume.InitializeFromLegacy( go, kv );
				break;
			case "env_combined_light_probe_volume":
				go.NetworkMode = NetworkMode.Never;
				EnvmapProbe.InitializeFromLegacy( go, kv ); // create an envmap component
				LightProbeVolume.InitializeFromLegacy( go, kv );
				break;
			case "snd_soundscape_box":
			case "snd_soundscape":
				CreateSoundscape( go, kv );
				break;
			case "env_gradient_fog":
				CreateGradientFog( go, kv );
				break;
			case "env_cubemap_fog":
				CreateCubemapFog( go, kv );
				break;
		}

		// Give users a chance to override functionality
		map.OnCreateObjectInternal( go, kv );

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
