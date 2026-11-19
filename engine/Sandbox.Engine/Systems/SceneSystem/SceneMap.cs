using Borealis.Source2.World;
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

	/// <summary>
	/// The map's visibility for network culling (<see cref="Scene.IsBBoxVisibleToConnection"/>), read from its compiled
	/// world (<c>world_visibility.vvis</c>) the first time a connection's visibility is checked, so only whoever checks
	/// (the host) holds it; null when the map has none, and then everything is visible. Rendering doesn't read it.
	/// </summary>
	internal WorldVisibility Visibility
	{
		get
		{
			if ( visibilityRead ) return visibility;
			visibility = ReadVisibility();
			visibilityRead = true;
			return visibility;
		}
	}

	/// <summary>Where the map is placed, which <see cref="Visibility"/> is relative to.</summary>
	internal Vector3 Origin { get; private set; }

	WorldVisibility visibility;
	bool visibilityRead;

	// The map's VPK on disk, which its visibility is read from
	string fullVpkPath;

	/// <summary>
	/// The map's world geometry when Borealis.Gpu draws it straight from the compiled world, with no native world
	/// (<see cref="LoadsManaged"/>); null when native's world renderer loaded it.
	/// </summary>
	internal MapGeometry Geometry { get; private set; }

	// The default entity lump's entities, read with the managed world
	IReadOnlyList<MapEntity> entities;

	// The name the map's VPK was mounted under for native resources, while a managed world holds it
	string mountedVpk;

	/// <summary>
	/// Whether maps load without native's world renderer: in a process that draws only on Borealis.Gpu (the game), where
	/// nothing reads native scene objects (<see cref="SceneObject.WritesNative"/>). The editor keeps Source 2's device for
	/// its native tools, captures and bakes, and loads maps natively for them.
	/// </summary>
	internal static bool LoadsManaged => LoadsManagedOverride ?? (!SceneObject.WritesNative && RenderBackend.Default is BorealisGpuRenderBackend);

	/// <summary>Tests choose <see cref="LoadsManaged"/>; null follows the process.</summary>
	internal static bool? LoadsManagedOverride { get; set; }

	/// <summary>
	/// Is the map valid.
	/// </summary>
	public bool IsValid => Geometry is not null || WorldRef.IsValid;

	/// <summary>
	/// Bounds of the map.
	/// </summary>
	public BBox Bounds
	{
		get
		{
			if ( !IsValid ) return default;
			if ( Geometry is not null ) return Geometry.Bounds;
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

			Origin = value;
			if ( Geometry is not null )
			{
				Geometry.Origin = value;
				return;
			}

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
		if ( Geometry is not null ) loader.CreateEntities( entities ?? [] );
		else loader.CreateEntities( WorldRef, map );
	}

	private bool CreateWorld( SceneWorld sceneWorld, string map, bool async, Vector3 origin )
	{
		Assert.IsValid( sceneWorld );

		MapFolder = System.IO.Path.ChangeExtension( map, null );
		var vpk = MapFolder + ".vpk";

		// CWorldRendererMgr::GetLocalMapName just strips maps/ from the start and uses that
		map = System.IO.Path.ChangeExtension( map, null );
		map = map.TrimStart( '\\', '/', ' ' );
		if ( map.StartsWith( "maps/" ) ) map = map[5..];

		if ( string.IsNullOrWhiteSpace( map ) )
			return false;

		MapName = map;

		Origin = origin;
		if ( LoadsManaged )
			return CreateManagedWorld( sceneWorld, vpk, origin );

		// Native doesn't load the world's visibility (owner, 2026-11-18): network culling reads it itself (Visibility)
		const bool loadVis = false;
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

		fullVpkPath = FileSystem.Mounted.GetFullPath( vpk ) ?? FileSystem.Mounted.GetFullPath( MapFolder + ".vpk" );

		// On Borealis.Gpu the world's models, materials and textures are read by path from the map's package and VPK
		if ( RenderBackend.Default is BorealisGpuRenderBackend gpu && FileSystem.Mounted.GetFullPath( vpk ) is { } fullVpk && System.IO.File.Exists( fullVpk ) )
			gpu.AddMapContent( fullVpk );
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
		World.AddSceneMap( this );
	}

	/// <summary>
	/// Load the map without native's world renderer: its VPK mounted for the resources managed code loads by path (its
	/// entities' models, materials and probe textures), its content added to Borealis.Gpu's, its world and its default
	/// entity lump read by Borealis.Source2. Borealis's view of the scene world draws <see cref="Geometry"/>.
	/// </summary>
	private bool CreateManagedWorld( SceneWorld sceneWorld, string vpk, Vector3 origin )
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend gpu )
			return false;

		// As native's CreateWorld: the path as given, then under maps/
		var fullVpk = FileSystem.Mounted.GetFullPath( vpk );
		if ( fullVpk is null || !System.IO.File.Exists( fullVpk ) )
		{
			var fallback = "maps/" + System.IO.Path.GetFileName( vpk );
			fullVpk = FileSystem.Mounted.GetFullPath( fallback );
			if ( fullVpk is not null && System.IO.File.Exists( fullVpk ) )
			{
				vpk = fallback;
				MapFolder = System.IO.Path.ChangeExtension( fallback, null );
			}
		}

		if ( fullVpk is null || !System.IO.File.Exists( fullVpk ) )
		{
			Log.Warning( $"{this}: Unable to find {vpk} for map {MapName}. Has the map been compiled?" );
			return false;
		}

		var mountName = System.IO.Path.GetFileNameWithoutExtension( vpk );
		g_pWorldRendererMgr.MountWorldVPK( mountName, vpk );
		gpu.AddMapContent( fullVpk );

		WorldFile world;
		try
		{
			world = WorldFile.Load( gpu.Services.Content, MapFolder + "/world.vwrld" );
			entities = ReadEntities( gpu.Services.Content, world, "default_ents" );
		}
		catch ( Exception e )
		{
			g_pWorldRendererMgr.UnmountWorldVPK( mountName );
			Log.Warning( e, $"{this}: Unable to read the world of map {MapName}: {e.Message}" );
			return false;
		}

		mountedVpk = mountName;
		fullVpkPath = fullVpk;
		Geometry = new MapGeometry( world, MapFolder, origin );
		World = sceneWorld;
		World.AddSceneMap( this );
		return true;
	}

	/// <summary>
	/// Read <see cref="Visibility"/>: <c>world_visibility.vvis_c</c> from the map's VPK, or loose beside it.
	/// </summary>
	WorldVisibility ReadVisibility()
	{
		if ( !IsValid ) return null;

		var path = MapFolder + "/world_visibility.vvis_c";
		try
		{
			byte[] data = null;
			if ( fullVpkPath is not null && System.IO.File.Exists( fullVpkPath ) )
			{
				using var package = Borealis.Source2.Resources.VpkFile.Open( fullVpkPath );
				data = package.Read( path );
			}

			if ( data is null && FileSystem.Mounted.FileExists( path ) )
				data = FileSystem.Mounted.ReadAllBytes( path ).ToArray();

			return data is null ? null : WorldVisibility.Read( data );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"{this}: Unable to read the visibility of map {MapName}, so everything is visible to every connection: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// The entities of the lump called <paramref name="name"/> (native's <c>CWorld::GetEntityList</c>): the world's root
	/// lumps and the lumps under them, each its own entities only.
	/// </summary>
	static IReadOnlyList<MapEntity> ReadEntities( Borealis.Source2.ContentFiles content, WorldFile world, string name )
	{
		var queue = new Queue<string>( world.EntityLumps );
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		while ( queue.TryDequeue( out var path ) )
		{
			if ( string.IsNullOrEmpty( path ) || !seen.Add( path ) ) continue;
			var lump = EntityLump.Load( content, path );
			var lumpName = string.IsNullOrEmpty( lump.LumpName ) ? System.IO.Path.GetFileNameWithoutExtension( path ) : lump.LumpName;
			if ( string.Equals( lumpName, name, StringComparison.OrdinalIgnoreCase ) ) return lump.Entities;
			foreach ( var child in lump.ChildLumps ) queue.Enqueue( child );
		}

		return [];
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

		// Read already, with nothing native to wait for
		if ( sceneMap.Geometry is not null )
		{
			sceneMap.CreateEntities( "default_ents", loader );
			return sceneMap;
		}

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

			// The release is queued; tear the world down now, while the scene world holding its objects is still alive
			g_pWorldRendererMgr.ServiceWorldRequests();
		}

		// Borealis's views take the objects out when they next see the map gone from its scene world
		Geometry = null;
		entities = null;
		if ( mountedVpk is not null )
		{
			g_pWorldRendererMgr.UnmountWorldVPK( mountedVpk );
			mountedVpk = null;
		}

		visibility = null;
		visibilityRead = true;
		fullVpkPath = null;

		WorldGroup = null;
	}
}
