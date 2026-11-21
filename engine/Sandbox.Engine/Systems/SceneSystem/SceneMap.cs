using Borealis.Source2;
using Borealis.Source2.Models;
using Borealis.Source2.Resources;
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

	/// <summary>
	/// The map's visibility for network culling (<see cref="Scene.IsBBoxVisibleToConnection"/>), read from its compiled
	/// world (<c>world_visibility.vvis</c>) only by whoever checks connections (the host): in the background once it hosts
	/// (<see cref="PrefetchVisibility"/>), or else the first time it's asked for. Null when the map has none, and then
	/// everything is visible. Rendering doesn't read it.
	/// </summary>
	internal WorldVisibility Visibility
	{
		get
		{
			if ( visibilityRead ) return visibility;
			visibility = visibilityTask is { } task ? task.GetAwaiter().GetResult() : ReadVisibility();
			visibilityTask = null;
			visibilityRead = true;
			return visibility;
		}
	}

	/// <summary>Start reading <see cref="Visibility"/> on a worker, if nothing has yet, so the first check doesn't wait for it.</summary>
	internal void PrefetchVisibility()
	{
		if ( visibilityRead || visibilityTask is not null ) return;
		visibilityTask = Task.Run( ReadVisibility );
	}

	/// <summary>Where the map is placed, which <see cref="Visibility"/> is relative to.</summary>
	internal Vector3 Origin { get; private set; }

	WorldVisibility visibility;
	bool visibilityRead;
	Task<WorldVisibility> visibilityTask;

	// The map's VPK on disk, which its visibility is read from
	string fullVpkPath;

	/// <summary>
	/// The map's world geometry, read from its compiled world by Borealis.Source2: Borealis.Gpu draws it, and traces hit it.
	/// </summary>
	internal MapGeometry Geometry { get; private set; }

	// The default entity lump's entities, read with the world
	IReadOnlyList<MapEntity> entities;

	// The name the map's VPK was mounted under for native resources, while the map holds it
	string mountedVpk;

	// The map's VPK, opened to read its world in a process without a Borealis.Gpu device (whose content keeps its own)
	VpkFile package;

	/// <summary>
	/// Is the map valid.
	/// </summary>
	public bool IsValid => Geometry is not null;

	/// <summary>
	/// Bounds of the map.
	/// </summary>
	public BBox Bounds => IsValid ? Geometry.Bounds : default;

	public Vector3 WorldOrigin
	{
		set
		{
			if ( !IsValid )
				return;

			Origin = value;
			Geometry.Origin = value;
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
		if ( !CreateWorld( sceneWorld, map, loader is null ? 0 : loader.WorldOrigin ) )
			return;

		CreateEntities( loader );
	}

	/// <summary>
	/// Invoked when a map file is updated (re-compiled in Hammer.)
	/// </summary>
	internal static Action<string> OnMapUpdated;

	private void CreateEntities( MapLoader loader )
	{
		loader ??= new SceneMapLoader( World, null );
		loader.CreateEntities( entities ?? [] );
	}

	/// <summary>
	/// Load the map: its VPK mounted for the resources loaded by path (its entities' models, materials and probe textures),
	/// its world and its default entity lump read by Borealis.Source2. With a Borealis.Gpu device the map's content is
	/// added to the device's and Borealis's view of the scene world draws <see cref="Geometry"/>; without one (a server,
	/// tests) the world is read from the map's VPK, for its entities, bounds and traces.
	/// </summary>
	private bool CreateWorld( SceneWorld sceneWorld, string map, Vector3 origin )
	{
		Assert.IsValid( sceneWorld );

		MapFolder = System.IO.Path.ChangeExtension( map, null );
		var vpk = MapFolder + ".vpk";

		// The map's name is its path without maps/ at the start
		map = System.IO.Path.ChangeExtension( map, null );
		map = map.TrimStart( '\\', '/', ' ' );
		if ( map.StartsWith( "maps/" ) ) map = map[5..];

		if ( string.IsNullOrWhiteSpace( map ) )
			return false;

		MapName = map;
		Origin = origin;

		// The path as given, then under maps/
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
		EngineGlue.MountWorldVPK( mountName, vpk );

		ContentFiles content;
		Func<string, ModelFile> readModel;
		if ( RenderBackend.Default is BorealisGpuRenderBackend gpu )
		{
			gpu.AddMapContent( fullVpk );
			content = gpu.Services.Content;
			readModel = path => gpu.Services.ReadModel( path ).GetAwaiter().GetResult();
		}
		else
		{
			// The map's package folder (<package>/maps/<map>.vpk) and VPK, then wherever the process has mounted files
			var root = System.IO.Path.GetDirectoryName( System.IO.Path.GetDirectoryName( fullVpk ) );
			var files = new ContentFiles( root ) { Resolve = BorealisGpuRenderBackend.MountedFile };
			package = VpkFile.Open( fullVpk );
			files.AddPackage( package );
			content = files;
			readModel = path => ModelFile.Load( files, path );
		}

		WorldFile world;
		try
		{
			world = WorldFile.Load( content, MapFolder + "/world.vwrld" );
			entities = ReadEntities( content, world, "default_ents" );
		}
		catch ( Exception e )
		{
			EngineGlue.UnmountWorldVPK( mountName );
			package?.Dispose();
			package = null;
			Log.Warning( e, $"{this}: Unable to read the world of map {MapName}: {e.Message}" );
			return false;
		}

		mountedVpk = mountName;
		fullVpkPath = fullVpk;
		Geometry = new MapGeometry( world, MapFolder, origin, readModel );
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
				using var vpk = VpkFile.Open( fullVpkPath );
				data = vpk.Read( path );
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
	/// The entities of the lump called <paramref name="name"/>: the world's root lumps and the lumps under them, each its
	/// own entities only.
	/// </summary>
	static IReadOnlyList<MapEntity> ReadEntities( ContentFiles content, WorldFile world, string name )
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
	/// Create scene map asynchronously for when large maps take time to load. The world is read at once: there's nothing
	/// to wait for.
	/// </summary>
	internal static Task<SceneMap> CreateAsync( SceneWorld sceneWorld, string map, MapLoader loader, CancellationToken cancelToken = default )
	{
		cancelToken.ThrowIfCancellationRequested();

		var sceneMap = new SceneMap();
		if ( !sceneMap.CreateWorld( sceneWorld, map, loader?.WorldOrigin ?? default ) )
			return Task.FromResult<SceneMap>( null );

		sceneMap.CreateEntities( loader );
		return Task.FromResult( sceneMap );
	}

	/// <summary>
	/// Delete this scene map. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{
		if ( World.IsValid() )
		{
			World.RemoveSceneMap( this );
			World = null;
		}

		// Borealis's views take the objects out when they next see the map gone from its scene world
		Geometry = null;
		entities = null;
		if ( mountedVpk is not null )
		{
			EngineGlue.UnmountWorldVPK( mountedVpk );
			mountedVpk = null;
		}

		package?.Dispose();
		package = null;

		// A read still going finishes on its own, and nothing keeps what it reads
		visibility = null;
		visibilityRead = true;
		visibilityTask = null;
		fullVpkPath = null;
	}
}
