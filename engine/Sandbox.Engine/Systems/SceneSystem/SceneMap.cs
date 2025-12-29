using NativeEngine;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Map geometry that can be rendered within a <see cref="SceneWorld"/>.
/// </summary>
public sealed partial class SceneMap : IValid
{
	/// <summary>
	/// The scene world this map belongs to.
	/// </summary>
	public SceneWorld World { get; internal set; }

	private IWorldReference WorldRef;
	internal string WorldGroup { get; private set; }
	internal IPVS PVS { get; private set; }

	/// <summary>
	/// Is the map valid.
	/// </summary>
	public bool IsValid => WorldRef.IsValid;

	/// <summary>
	/// Bounds of the map.
	/// </summary>
	public BBox Bounds
	{
		get
		{
			if ( !IsValid ) return default;
			WorldRef.GetWorldBounds( out var min, out var max );
			return new BBox( min, max );
		}
	}

	public Vector3 WorldOrigin
	{
		set
		{
			if ( !IsValid )
				return;

			WorldRef.SetWorldTransform( new Transform( value ) );
		}
	}

	/// <summary>
	/// cs_assault
	/// </summary>
	public string MapName { get; private set; }

	/// <summary>
	/// maps/davej/cs_assault
	/// </summary>
	public string MapFolder { get; private set; }

	private SceneMap()
	{
	}

	/// <summary>
	/// Create a scene map within a scene world.
	/// </summary>
	public SceneMap( SceneWorld sceneWorld, string map ) : this( sceneWorld, map, null )
	{
	}

	/// <summary>
	/// Create a scene map within a scene world.
	/// </summary>
	internal SceneMap( SceneWorld sceneWorld, string map, MapLoader loader )
	{
		if ( !CreateWorld( sceneWorld, map, false, loader is null ? 0 : loader.WorldOrigin ) )
			return;

		CreateEntities( "default_ents", loader );
		sceneWorld.UpdateObjectsForRendering();
	}

	/// <summary>
	/// Invoked when a map file is updated (re-compiled in Hammer.)
	/// </summary>
	internal static Action<string> OnMapUpdated;

	private void CreateEntities( string map, MapLoader loader )
	{
		loader ??= new SceneMapLoader( World, null );
		loader.CreateEntities( WorldRef, map );
	}

	private bool CreateWorld( SceneWorld sceneWorld, string map, bool async, Vector3 origin )
	{
		Assert.IsValid( sceneWorld );

		MapFolder = System.IO.Path.ChangeExtension( map, null );

		// CWorldRendererMgr::GetLocalMapName just strips maps/ from the start and uses that
		map = System.IO.Path.ChangeExtension( map, null );
		map = map.TrimStart( '\\', '/', ' ' );
		if ( map.StartsWith( "maps/" ) ) map = map[5..];

		if ( string.IsNullOrWhiteSpace( map ) )
			return false;

		MapName = map;

		const bool loadVis = true;
		const bool precacheOnly = false;

		var worldGroup = sceneWorld.native.GetWorldDebugName();
		var worldRef = g_pWorldRendererMgr.CreateWorld(
			MapFolder + ".vpk",
			sceneWorld,
			async,
			true,
			loadVis,
			precacheOnly,
			worldGroup, // Worldgroup ID
			new Transform( origin ) );

		if ( !worldRef.IsValid )
		{
			Log.Warning( $"{this}: Unable to create world for map {map}" );
			return false;
		}

		lock ( sceneWorld.InternalSceneMaps )
		{
			Assert.False( sceneWorld.InternalSceneMaps.Any( x => x.WorldRef == worldRef ), "Scene world already contains this scene map" );
		}

		WorldRef = worldRef;
		MapFolder = worldRef.GetFolder();
		WorldGroup = worldGroup;
		World = sceneWorld;

		if ( !async )
		{
			// We're not loading async so the world should be loaded now
			OnWorldLoaded();
		}

		return true;
	}

	private void OnWorldLoaded()
	{
		WorldRef.PrecacheAllWorldNodes( 0x0080 );
		PVS = g_pEnginePVSManager.BuildPvs( WorldRef );
		World.AddSceneMap( this );
	}

	/// <summary>
	/// Create scene map asynchronously for when large maps take time to load.
	/// </summary>
	public static Task<SceneMap> CreateAsync( SceneWorld sceneWorld, string map, CancellationToken cancelToken = default )
	{
		return CreateAsync( sceneWorld, map, null, cancelToken );
	}

	/// <summary>
	/// Create scene map asynchronously for when large maps take time to load.
	/// </summary>
	internal static async Task<SceneMap> CreateAsync( SceneWorld sceneWorld, string map, MapLoader loader, CancellationToken cancelToken = default )
	{
		var sceneMap = new SceneMap();

		if ( !sceneMap.CreateWorld( sceneWorld, map, true, loader.WorldOrigin ) )
			return null;

		if ( !sceneMap.IsValid )
			return null;

		var worldRef = sceneMap.WorldRef;

		while ( !worldRef.IsWorldLoaded() )
		{
			g_pWorldRendererMgr.ServiceWorldRequests();

			await Task.Delay( 1, cancelToken );
			cancelToken.ThrowIfCancellationRequested();
		}

		sceneMap.OnWorldLoaded();
		sceneMap.CreateEntities( "default_ents", loader );

		return sceneMap;
	}

	/// <summary>
	/// Delete this scene map. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{

		if ( World.IsValid() )
		{
			World.RemoveSceneMap( this );

			//World.Delete(); // don#t delete this are you crazy
			World = null;
		}

		if ( WorldRef.IsValid )
		{
			WorldRef.Release();
			WorldRef = IntPtr.Zero;
		}

		if ( PVS.IsValid )
		{
			// don't destroy if another SceneWorld is using it
			if ( !SceneWorld.All.Any( x => x.ActivePVS == PVS ) )
			{
				g_pEnginePVSManager.DestroyPvs( PVS );
			}

			PVS = default;
		}

		WorldGroup = null;
	}
}
