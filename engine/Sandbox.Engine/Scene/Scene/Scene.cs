using Sandbox.Utility;
using Sandbox.Volumes;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class Scene : GameObject
{
	public bool IsEditor { get; private set; }

	public SceneWorld SceneWorld { get; private set; }
	public SceneWorld DebugSceneWorld => gizmoInstance?.World;

	[System.Obsolete( "Use Scene.Editor.HasUnsavedChanges" )]
	public bool HasUnsavedChanges { get; private set; }

	public GameResource Source { get; internal set; }

	/// <summary>
	/// For scenes within a hammer MapWorld, for action graph stack traces,
	/// and so the editor knows the map must be saved when editing graphs from it.
	/// </summary>
	internal Facepunch.ActionGraphs.ISourceLocation OverrideSourceLocation { get; set; }

	public GameObjectDirectory Directory { get; private set; }

	Gizmo.Instance gizmoInstance = new();

	[JsonIgnore]
	[System.Obsolete( "please use the SceneInformation component" )]
	public string Title { get; set; }

	[JsonIgnore]
	[System.Obsolete( "please use the SceneInformation component" )]
	public string Description { get; set; }

	/// <summary>
	/// If true we'll additive load the system scene when this scene is loaded. Defaults
	/// to true. You might want to disable this for specific scenes, like menu scenes etc.
	/// </summary>
	[Property]
	public bool WantsSystemScene { get; set; } = true;

	/// <summary>
	/// Global render attributes accessible on any renderable in this Scene.
	/// </summary>
	public RenderAttributes RenderAttributes { get; }

	private PhysicsWorld _physicsWorld;

	public PhysicsWorld PhysicsWorld => _physicsWorld ??= CreatePhysicsWorld();

	private PhysicsWorld CreatePhysicsWorld()
	{
		return new PhysicsWorld
		{
			DebugSceneWorld = DebugSceneWorld,
			Gravity = Vector3.Down * 850,
			SimulationMode = PhysicsSimulationMode.Continuous,
			CollisionRules = ProjectSettings.Collision,
			Scene = this
		};
	}

	protected Scene( bool isEditor ) : base( true, "Scene" )
	{
		_all.Add( this );

		SceneWorld = new SceneWorld();
		Directory = new GameObjectDirectory( this );

		RenderAttributes = new();
		IsEditor = isEditor;

		if ( this is not PrefabCacheScene )
		{
			InitSystems();
		}
	}

	public Scene() : this( false ) { }

	~Scene()
	{
		MainThread.Queue( DestroyInternal );
	}

	/// <summary>
	/// Returns true if this scene has not been destroyed
	/// </summary>
	public override bool IsValid => SceneWorld is not null;

	/// <summary>
	/// Destroy this scene. After this you should never use it again.
	/// </summary>
	public override void Destroy()
	{
		DestroyInternal();
	}

	internal virtual void DestroyInternal()
	{
		_all.Remove( this );

		// Clearing the object index now means we can save time
		// because we don't have to do it for each object.
		// Note that we can't do this in Clear because we don't want to
		// remove any GameObjectSystem's from the index - because they're
		// persistent between the clears.
		ClearObjectIndex();

		Clear();
		ProcessDeletes();
		ShutdownSystems();

		GC.SuppressFinalize( this );

		_physicsWorld?.Delete();
		_physicsWorld = default;

		SceneWorld?.Delete();
		SceneWorld = default;

		gizmoInstance?.Dispose();
		gizmoInstance = default;

		NavMesh?.Dispose();
		NavMesh = default;

		DisposeListeners();
	}

	public static Scene CreateEditorScene()
	{
		return new Scene( true );
	}

	/// <summary>
	/// Create a GameObject on this scene. This doesn't require the scene to be the active scene.
	/// </summary>
	public GameObject CreateObject( bool enabled = true )
	{
		using ( Push() )
		{
			var go = new GameObject( enabled );
			go.Enabled = enabled;
			go.Parent = this;
			return go;
		}
	}


	/// <summary>
	/// Push this scene as the active scene, for a scope
	/// </summary>
	public IDisposable Push()
	{
		ThreadSafe.AssertIsMainThread();
		var old = Game.ActiveScene;

		Game.ActiveScene = this;

		var timeScope = Time.Scope( TimeNow, TimeDelta );

		return DisposeAction.Create( () =>
		{
			ThreadSafe.AssertIsMainThread();

			if ( Game.ActiveScene == this )
			{
				Game.ActiveScene = old;
			}

			timeScope?.Dispose();
		} );
	}

	[System.Obsolete]
	public void ClearUnsavedChanges()
	{
	}

	internal override void OnHotload()
	{
		base.OnHotload();

		SceneNetworkSystem.Instance?.OnHotload();

		foreach ( var obj in Directory.AllGameObjects )
		{
			if ( obj == this ) continue;

			obj.OnHotload();
		}

		foreach ( var obj in networkedObjects )
		{
			obj.OnHotload();
		}

		foreach ( var obj in systems )
		{
			obj.OnHotload();
		}

		HotloadObjectIndex();
	}

	internal void Render( SwapChainHandle_t swapChain, Vector2? size )
	{
		PreCameraRender();

		// Get all cameras sorted by render priority
		var cameras = Cameras.OrderBy( x => x.Priority );
		foreach ( var cc in cameras )
		{
			if ( cc.Active == false ) continue;
			if ( cc.IsSceneEditorCamera ) continue;

			cc.AddToRenderList( swapChain, size );
		}
	}

	internal void RenderEnvmaps()
	{
		// Don't update all at once to not overflow transform buffer in large scenes
		const int maxSimultaniousUpdates = 5;
		foreach ( var envmap in GetAllComponents<EnvmapProbe>().Where( x => x.Dirty ).Take( maxSimultaniousUpdates ) )
		{
			envmap.RenderCubemap();
		}
	}

	/// <summary>
	/// Cache all enabled and disabled cameras in the scene.
	/// Registered/Deregistered in CameraComponent.OnAwake and OnDestroy.
	/// </summary>
	internal HashSet<CameraComponent> Cameras { get; } = new HashSet<CameraComponent>();

	/// <summary>
	/// Should be called before rendering. This allows things like reflections to render.
	/// </summary>
	internal void PreCameraRender()
	{
		// We want to initialize all cameras (enabled & disabled) incase they're used to render manually
		// we need to make sure the SceneCamera is created etc.
		var cameras = Cameras.OrderBy( x => x.Priority );
		foreach ( var cc in cameras )
		{
			cc.InitializeRendering();
		}

		RenderEnvmaps();

		// Alpha is used to lerp between IBL and fixed ambient light
		Color ambientLight = Color.Transparent;

		foreach ( var light in GetAllComponents<DirectionalLight>() )
		{
			if ( Camera.IsValid() && light.Tags.HasAny( Camera.RenderExcludeTags ) )
				continue;

			ambientLight += light.SkyColor;
		}

		foreach ( var light in GetAllComponents<AmbientLight>() )
		{
			if ( Camera.IsValid() && light.Tags.HasAny( Camera.RenderExcludeTags ) )
				continue;

			ambientLight += light.Color;
		}

		if ( SceneWorld.IsValid() )
		{
			SceneWorld.AmbientLightColor = ambientLight;
		}
	}

	private VolumeSystem _volumeSystem;

	/// <summary>
	/// Allows quickly finding components that have a volume
	/// </summary>
	public VolumeSystem Volumes => _volumeSystem ??= VolumeSystem.Get( this );

	/// <summary>
	/// Adds the "system" scene, which is defined in the project settings.
	/// </summary>
	internal void AddSystemScene()
	{
		if ( !WantsSystemScene )
			return;

		var systemScene = Application.GamePackage?.GetMeta( "SystemScene", "" ) ?? null;

		if ( string.IsNullOrWhiteSpace( systemScene ) )
			return;

		// additive load this scene
		var slo = new SceneLoadOptions();
		slo.IsSystemScene = true;
		slo.IsAdditive = true;
		slo.SetScene( systemScene );
		Load( slo );
	}

	/// <summary>
	/// Find objects with all tags
	/// </summary>
	public IEnumerable<GameObject> FindAllWithTags( IEnumerable<string> tags )
	{
		foreach ( var go in Directory.AllGameObjects )
		{
			if ( go.Tags.HasAll( tags ) )
			{
				yield return go;
			}
		}
	}

	/// <summary>
	/// Find objects with tag
	/// </summary>
	public IEnumerable<GameObject> FindAllWithTag( string tag )
	{
		return Directory.AllGameObjects.Where( x => x.Tags.Has( tag ) );
	}
}
