using NativeEngine;
using Sandbox.Engine;
using System.Reflection;
using System.Runtime.InteropServices;
using static Sandbox.ResourceLibrary;

namespace Sandbox;

public class ResourceSystem
{
	// Index of Json based, GameResources PrefabFile, DecalDefintions etc.
	private Dictionary<int, Resource> ResourceIndex { get; } = new();
	private Dictionary<ulong, Resource> ResourceIndexLong { get; } = new();
	private Dictionary<Guid, Resource> GuidIndex { get; } = new();

	// Weak references to native resources (Model, Material, Texture, Shader, etc.) — GC-friendly.
	private Dictionary<int, WeakReference<Resource>> WeakIndex { get; } = new();
	private Dictionary<ulong, WeakReference<Resource>> WeakIndexLong { get; } = new();
	private Dictionary<Guid, WeakReference<Resource>> WeakGuidIndex { get; } = new();

	/// Maps ResourceIdLong → path for all resources that exist on disk.
	/// Populated at startup without loading anything; used as on-demand load fallback during
	/// network deserialization when a resource hasn't been loaded on the client yet.
	private Dictionary<ulong, string> PathIndex { get; } = new();

	// Used to register resources path for use in networking
	internal void RegisterPath( string path )
	{
		path = Resource.FixPath( path );
		if ( string.IsNullOrEmpty( path ) ) return;
		PathIndex[path.FastHash64()] = path;
	}

	internal void Register( Resource resource )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !string.IsNullOrEmpty( resource.ResourcePath ) )
		{
#pragma warning disable CS0618 // Type or member is obsolete
			ResourceIndex[resource.ResourceId] = resource;
#pragma warning restore CS0618 // Type or member is obsolete
			ResourceIndexLong[resource.ResourceIdLong] = resource;
		}

		if ( resource.Guid != Guid.Empty )
		{
			if ( GuidIndex.TryGetValue( resource.Guid, out var existing )
			&& !ReferenceEquals( existing, resource )
			&& resource.ResourceIdLong != existing.ResourceIdLong // Asset.SaveToDisk recreates?
			&& !existing.IsError )
			{
				Log.Warning( $"GUID not unique! Assigning temp new GUID to: {resource.ResourcePath}" );
				resource.Guid = Guid.NewGuid();
			}

			GuidIndex[resource.Guid] = resource;
		}

		if ( resource is GameResource gameResource && !gameResource.IsPromise )
		{
			IToolsDll.Current?.RunEvent<IEventListener>( i => i.OnRegister( gameResource ) );
		}
	}

	/// <summary>
	/// Register a resource with a weak reference, allowing GC to collect it when no longer in use.
	/// </summary>
	internal void RegisterWeak( Resource resource )
	{
		if ( !string.IsNullOrEmpty( resource.ResourcePath ) )
		{
#pragma warning disable CS0618 // Type or member is obsolete
			WeakIndex[resource.ResourceId] = new WeakReference<Resource>( resource );
#pragma warning restore CS0618 // Type or member is obsolete
			WeakIndexLong[resource.ResourceIdLong] = new WeakReference<Resource>( resource );
		}

		if ( resource.Guid != Guid.Empty )
		{
			WeakGuidIndex[resource.Guid] = new WeakReference<Resource>( resource );
		}
	}

	internal void Unregister( Resource resource )
	{
		ThreadSafe.AssertIsMainThread();

		// Only remove entries that index this exact resource. A stale instance whose index
		// entry was replaced (e.g. re-registered under the same path) must not unregister
		// the live resource when it gets finalized.
		var removed = RemoveIndexed( ResourceIndexLong, resource.ResourceIdLong, resource );
		removed |= RemoveIndexedWeak( WeakIndexLong, resource.ResourceIdLong, resource );

#pragma warning disable CS0618 // Type or member is obsolete
		removed |= RemoveIndexed( ResourceIndex, resource.ResourceId, resource );
		removed |= RemoveIndexedWeak( WeakIndex, resource.ResourceId, resource );
#pragma warning restore CS0618 // Type or member is obsolete

		removed |= RemoveIndexed( GuidIndex, resource.Guid, resource );
		removed |= RemoveIndexedWeak( WeakGuidIndex, resource.Guid, resource );

		if ( removed && resource is GameResource gameResource && !gameResource.IsPromise )
		{
			IToolsDll.Current?.RunEvent<IEventListener>( i => i.OnUnregister( gameResource ) );
		}
	}

	private static bool RemoveIndexed<TKey>( Dictionary<TKey, Resource> index, TKey key, Resource resource )
	{
		if ( !index.TryGetValue( key, out var indexed ) || indexed != resource )
			return false;

		return index.Remove( key );
	}

	private static bool RemoveIndexedWeak<TKey>( Dictionary<TKey, WeakReference<Resource>> index, TKey key, Resource resource )
	{
		if ( !index.TryGetValue( key, out var weakRef ) )
			return false;

		if ( !weakRef.TryGetTarget( out var target ) )
		{
			// A dead entry can't be this resource - we hold a live reference to it.
			// Prune it, but it doesn't count as unregistering this resource.
			index.Remove( key );
			return false;
		}

		if ( target != resource )
			return false;

		return index.Remove( key );
	}

	/// <summary>
	/// Update the path of a resource, updating the index and re-registering it.
	/// </summary>
	internal void MoveResource( Resource resource, string newPath )
	{
		ThreadSafe.AssertIsMainThread();

		newPath = Resource.FixPath( newPath );

		if ( string.Equals( resource.ResourcePath, newPath ) )
			return; // already known at this path, nothing to do

		ThreadSafe.AssertIsMainThread();

		if ( !string.IsNullOrEmpty( resource.ResourcePath ) )
		{
			Log.Info( $"Resource moved: {resource.ResourcePath} -> {newPath} ({resource.Guid})" );
		}

		Unregister( resource );

		if ( resource is GameResource gr )
			gr.Register( newPath );
		else
			resource.RegisterWeakResourceId( newPath );
	}

	/// <summary>
	/// Grant a new GUID to a resource, updating the index.
	/// </summary>
	internal void AssignGuid( Resource resource, Guid newGuid )
	{
		if ( newGuid == Guid.Empty || newGuid == resource.Guid )
			return;

		if ( resource is GameResource )
		{
			RemoveIndexed( GuidIndex, resource.Guid, resource );
		}
		else
		{
			RemoveIndexedWeak( WeakGuidIndex, resource.Guid, resource );
		}

		resource.Guid = newGuid;

		if ( resource.Guid == Guid.Empty )
			return;

		Resource existing;
		if ( (GuidIndex.TryGetValue( newGuid, out existing ) || WeakGuidIndex.TryGetValue( newGuid, out var existingRef ) && existingRef.TryGetTarget( out existing ))
			&& !ReferenceEquals( existing, resource ) )
		{
			// this should not happen - it would mean we're not updating an existing instance from whatever path, and instead creating a new one with the same ID.
			Log.Error( $"GUID collision! ({newGuid}) incoming: {resource.ResourcePath}, existing: {existing.ResourcePath}" );
		}

		if ( resource is GameResource )
		{
			GuidIndex[newGuid] = resource;
		}
		else
		{
			WeakGuidIndex[resource.Guid] = new WeakReference<Resource>( resource );
		}
	}


	internal void OnHotload()
	{
		TypeCache.Clear();
	}

	/// <summary>
	/// Prune dead entries from the WeakIndex to prevent unbounded growth.
	/// Only runs every 30 seconds.
	/// </summary>
	TimeSince _lastWeakPrune;

	internal void PruneWeakIndex()
	{
		if ( _lastWeakPrune < 30 )
			return;

		_lastWeakPrune = 0;

		var dead = WeakIndex.Where( kvp => !kvp.Value.TryGetTarget( out _ ) ).Select( kvp => kvp.Key ).ToList();
		foreach ( var key in dead )
		{
			WeakIndex.Remove( key );
		}

		var deadNative = WeakIndexLong.Where( kvp => !kvp.Value.TryGetTarget( out _ ) ).Select( kvp => kvp.Key ).ToList();
		foreach ( var key in deadNative )
		{
			WeakIndexLong.Remove( key );
		}
	}

	internal void Clear()
	{
		// TODO: remove from native too?

		var toDispose = ResourceIndex.Values.ToArray();

		foreach ( var resource in toDispose.OfType<GameResource>() )
		{
			resource.DestroyInternal();
		}

		foreach ( var resource in toDispose )
		{
			// Don't wait/rely for finalizer get rid of this immediately
			resource?.Destroy();
		}

		GuidIndex.Clear();
		WeakGuidIndex.Clear();

		ResourceIndex.Clear();
		WeakIndex.Clear();

		ResourceIndexLong.Clear();
		WeakIndexLong.Clear();
		PathIndex.Clear();

		TypeCache.Clear();
	}

	/// <summary>
	/// Find all alive weak resources of a given type whose ResourcePath starts with the given prefix.
	/// Used for hotload scenarios like SVG textures with query parameters.
	/// </summary>
	internal IEnumerable<T> FindWeakByPathPrefix<T>( string pathPrefix ) where T : Resource
	{
		foreach ( var kvp in WeakIndex )
		{
			if ( !kvp.Value.TryGetTarget( out var resource ) )
				continue;

			if ( resource is not T typed )
				continue;

			if ( resource.ResourcePath is not null && resource.ResourcePath.TrimStart( '/' ).StartsWith( pathPrefix, StringComparison.OrdinalIgnoreCase ) )
				yield return typed;
		}
	}

	internal Resource Get( System.Type t, ResourceId id )
	{
		// try to load by GUID first
		if ( id.Guid is Guid guid && guid != default )
		{
			Resource resource;

			if ( GuidIndex.TryGetValue( guid, out resource ) )
			{
				if ( resource.GetType().IsAssignableTo( t ) )
					return resource;

				return null;
			}

			if ( WeakGuidIndex.TryGetValue( guid, out var weakRef ) && weakRef.TryGetTarget( out resource ) )
			{
				if ( resource.GetType().IsAssignableTo( t ) )
					return resource;

				return null;
			}
		}

		// no GUID match, try to load by path
		if ( !string.IsNullOrEmpty( id.Path ) )
		{
			string filepath = Resource.FixPath( id.Path );
			ulong identifier = filepath.FastHash64();

			if ( ResourceIndexLong.TryGetValue( identifier, out var resource ) )
			{
				if ( resource.GetType().IsAssignableTo( t ) )
					return resource;

				return null;
			}

			if ( WeakIndexLong.TryGetValue( identifier, out var weakRef ) && weakRef.TryGetTarget( out var weakResource ) )
			{
				if ( weakResource.GetType().IsAssignableTo( t ) )
					return weakResource;
			}
		}

		return null;
	}

	/// <summary>
	/// Get a cached resource by its hash.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="identifier">Resource hash to look up.</param>
	[Obsolete( "Use Get<T>(path) instead. Identifier based access will be removed in a future update." )]
	public T Get<T>( int identifier ) where T : Resource
	{
		if ( ResourceIndex.TryGetValue( identifier, out var resource ) )
			return resource as T;

		if ( WeakIndex.TryGetValue( identifier, out var weakRef ) && weakRef.TryGetTarget( out var weakResource ) )
			return weakResource as T;

		return default;
	}

	// Internal use only — do not expose ulong IDs publicly.
	internal T GetByIdLong<T>( ulong idLong ) where T : Resource
	{
		if ( ResourceIndexLong.TryGetValue( idLong, out var resource ) )
			return resource as T;

		if ( WeakIndexLong.TryGetValue( idLong, out var weakRef ) && weakRef.TryGetTarget( out var weakResource ) )
			return weakResource as T;

		return default;
	}

	/// <summary>
	/// Get a cached resource by GUID.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="id">GUID of the resource.</param>
	public T Get<T>( Guid id ) where T : Resource
	{
		if ( GuidIndex.TryGetValue( id, out var resource ) )
			return resource as T;

		if ( WeakGuidIndex.TryGetValue( id, out var weakRef ) && weakRef.TryGetTarget( out var weakResource ) )
			return weakResource as T;

		return default;
	}

	internal string LookupPath( ulong idLong )
	{
		return PathIndex.GetValueOrDefault( idLong );
	}

	/// <summary>
	/// Get a cached resource by its file path.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">File path to the resource.</param>
	public T Get<T>( string filepath ) where T : Resource
	{
		filepath = Resource.FixPath( filepath );

		return GetByIdLong<T>( filepath.FastHash64() );
	}


	/// <summary>
	/// Get a cached resource
	/// </summary>
	internal T Get<T>( ResourceId id ) where T : Resource
	{
		return Get( typeof( T ), id ) as T;
	}

	/// <summary>
	/// Try to get a cached resource by its file path.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">File path to the resource.</param>
	/// <param name="resource">The retrieved resource, if any.</param>
	/// <returns>True if resource was retrieved successfully.</returns>
	public bool TryGet<T>( string filepath, out T resource ) where T : Resource
	{
		resource = Get<T>( filepath );
		return resource != null;
	}

	/// <summary>
	/// Try to get a cached resource by its GUID.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="id">GUID of the resource.</param>
	/// <param name="resource">The retrieved resource, if any.</param>
	/// <returns>True if resource was retrieved successfully.</returns>
	public bool TryGet<T>( Guid id, out T resource ) where T : Resource
	{
		resource = Get<T>( id );
		return resource != null;
	}

	/// <summary>
	/// Get all cached resources of given type.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	public IEnumerable<T> GetAll<T>()
	{
		return ResourceIndex.Values.OfType<T>().Distinct();
	}

	/// <summary>
	/// Get all cached resources of given type in a specific folder.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">The path of the folder to check.</param>
	/// <param name="recursive">Whether or not to check folders within the specified folder.</param>
	public IEnumerable<T> GetAll<T>( string filepath, bool recursive = true ) where T : Resource
	{
		filepath = filepath.Replace( '\\', '/' );
		if ( !filepath.EndsWith( "/" ) ) filepath += "/";
		return ResourceIndex.Values.OfType<T>().Distinct().Where( x =>
		{
			if ( x.ResourcePath.StartsWith( filepath ) )
			{
				if ( recursive ) return true;
				if ( !x.ResourcePath.Substring( filepath.Length ).Contains( "/" ) ) return true;
			}
			return false;
		} );
	}

	/// <summary>
	/// Returns stats about the ResourceIndex and WeakIndex for debug overlays.
	/// </summary>
	internal ResourceStats GetResourceStats()
	{
		var stats = new ResourceStats();

		foreach ( var resource in ResourceIndex.Values )
		{
			var typeName = resource.GetType().Name;
			stats.StrongIndex.TryGetValue( typeName, out var count );
			stats.StrongIndex[typeName] = count + 1;
		}

		foreach ( var kvp in WeakIndex )
		{
			var alive = kvp.Value.TryGetTarget( out var resource );
			var typeName = alive ? resource.GetType().Name : "(dead)";
			stats.WeakIndexEntries.TryGetValue( typeName, out var count );
			stats.WeakIndexEntries[typeName] = count + 1;
		}

		stats.StrongTotal = ResourceIndex.Count;
		stats.WeakTotal = WeakIndex.Count;

		return stats;
	}

	internal struct ResourceStats
	{
		public Dictionary<string, int> StrongIndex;
		public Dictionary<string, int> WeakIndexEntries;
		public int StrongTotal;
		public int WeakTotal;

		public ResourceStats()
		{
			StrongIndex = new();
			WeakIndexEntries = new();
		}
	}

	/// <summary>
	/// Read compiled resource as JSON from the provided buffer.
	/// </summary>
	internal unsafe string ReadCompiledResourceJson( Span<byte> data )
	{
		fixed ( byte* ptr = data )
		{
			return EngineGlue.ReadCompiledResourceFileJson( (IntPtr)ptr );
		}
	}

	/// <summary>
	/// Read compiled resource as JSON from the provided file path.
	/// </summary>
	internal unsafe string ReadCompiledResourceJson( BaseFileSystem fs, string fileName )
	{
		if ( !fs.FileExists( fileName ) )
			return string.Empty;

		var data = fs.ReadAllBytes( fileName );

		fixed ( byte* ptr = data )
		{
			return EngineGlue.ReadCompiledResourceFileJson( (IntPtr)ptr );
		}
	}

	internal unsafe byte[] ReadCompiledResourceBlock( string blockName, Span<byte> data )
	{
		fixed ( byte* ptr = data )
		{
			IntPtr blockData = EngineGlue.ReadCompiledResourceFileBlock( blockName, (IntPtr)ptr, out var size );
			if ( blockData == IntPtr.Zero || size <= 0 )
				return null;

			var result = new byte[size];
			Marshal.Copy( blockData, result, 0, size );
			return result;
		}
	}

	private Dictionary<string, AssetTypeAttribute> TypeCache { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Get the <see cref="AssetTypeAttribute"/> for a given extension.
	/// </summary>
	internal bool TryGetType( string extension, out AssetTypeAttribute resourceAttribute )
	{
		if ( extension.StartsWith( '.' ) ) extension = extension[1..];
		if ( extension.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) extension = extension[..^2];

		if ( TypeCache.TryGetValue( extension, out resourceAttribute ) )
			return true;

		resourceAttribute = Game.TypeLibrary.GetAttributes<AssetTypeAttribute>()
			.FirstOrDefault( x => string.Equals( x.Extension, extension, StringComparison.OrdinalIgnoreCase ) );

		if ( resourceAttribute != null )
		{
			TypeCache[extension] = resourceAttribute;
			return true;
		}

		return false;
	}

	/// <summary>
	/// garry: why the fuck does this exist
	/// garry: fuck me why the fuck does this exist
	/// </summary>
	internal GameResource LoadRawGameResource( string path )
	{
		var extension = System.IO.Path.GetExtension( path );
		if ( !TryGetType( extension, out var type ) )
		{
			Log.Warning( $"Could not find GameResource for extension '{extension}'" );
			return null;
		}

		var json = EngineGlue.ReadCompiledResourceFileJsonFromFilesystem( path );
		if ( string.IsNullOrEmpty( json ) )
		{
			Log.Warning( $"Failed to load {path}" );
			return null;
		}

		try
		{
			var se = GameResource.GetPromise( type.TargetType, path );
			if ( se is null )
				return null;

			se.LoadFromJson( json );
			// se.LoadFromResource( data );

			Register( se );

			se.PostLoadInternal();
			return se;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, $"		Error when deserializing {path} ({ex.Message})" );
		}

		return null;
	}

	internal T LoadGameResource<T>( string file, BaseFileSystem fs, bool deferPostload = false, Package sourcePackage = null ) where T : GameResource
	{
		var attr = typeof( T ).GetCustomAttribute<AssetTypeAttribute>();
		if ( attr == null ) return default;

		// this is filled in automatically when accessed via TypeLibrary
		// but this ain't TypeLibrary kiddo
		attr.TargetType = typeof( T );

		return LoadGameResource( attr, file, fs, deferPostload, sourcePackage ) as T;
	}

	/// <summary>
	/// Loads a Gameresource from disk. Doesn't look at cache. Registers the resource if successful.
	/// </summary>
	internal GameResource LoadGameResource( AssetTypeAttribute type, string file, BaseFileSystem fs, bool deferPostload = false, Package sourcePackage = null )
	{
		Assert.NotNull( type );
		Assert.NotNull( file );

		if ( !file.EndsWith( "_c" ) ) file += "_c";

		Span<byte> data = null;

		try
		{
			if ( fs.FileExists( file ) )
			{
				data = fs.ReadAllBytes( file );
			}

			if ( data.Length <= 3 )
			{
				Log.Warning( $"		Skipping {file} (is null)" );
				return null;
			}

			ResourceId resourceId = file;
			if ( g_pResourceSystem.ResolvePathToGuid( file, out Guid resolvedGuid ) )
			{
				resourceId.Guid = resolvedGuid;
			}

			var se = GameResource.GetPromise( type.TargetType, resourceId );
			if ( se is null ) return null;

			// Attribute the resource to the package it was loaded from (null = local project).
			// Only overwrite when a package is supplied so aggregate reloads don't clobber a
			// previously attributed cloud package.
			if ( sourcePackage is not null )
				se.Package = sourcePackage;

			se.TryLoadFromData( data );

			if ( Application.IsEditor )
			{
				var sourceFilePath = file.Substring( 0, file.Length - 2 );
				if ( fs.FileExists( sourceFilePath ) )
				{
					var jsonBlob = fs.ReadAllText( sourceFilePath );
					se.LastSavedSourceHash = jsonBlob.FastHash();
				}
			}

			//
			// garry: wtf is this for? maps?
			//
			if ( Application.IsDedicatedServer )
			{
				InstallReferences( se );
			}

			if ( resourceId.Guid is Guid guid && guid != default )
			{
				AssignGuid( se, guid );
			}

			if ( se.ResourcePath != file )
			{
				// if the resource was loaded from a different path than it's now on, move it to the new path
				MoveResource( se, file );
			}
			else
			{
				Register( se );
			}

			if ( !deferPostload )
				se.PostLoadInternal();

			return se;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, $"		Error when deserializing {file} ({ex.Message})" );
			return null;
		}
	}

	/// <summary>
	/// Installs all references for a GameResource 
	/// </summary>
	private void InstallReferences( GameResource se )
	{
		var references = se.GetReferencedPackages();

		foreach ( var r in references )
		{
			_ = PackageManager.InstallAsync( new PackageLoadOptions() { PackageIdent = r, ContextTag = "server" } );
		}
	}
}

/// <summary>
/// Keeps a library of all available <see cref="Resource"/>.
/// </summary>
public static class ResourceLibrary
{
	/// <summary>
	/// Get a cached resource by its hash.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="identifier">Resource hash to look up.</param>
	[Obsolete( "Use Get<T>(filepath) instead. Identifier based access will be removed in a future update." )]
	public static T Get<T>( int identifier ) where T : Resource => Game.Resources.Get<T>( identifier );

	/// <summary>
	/// Get a cached resource by its file path.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">File path to the resource.</param>
	public static T Get<T>( string filepath ) where T : Resource => Game.Resources.Get<T>( filepath );

	/// <summary>
	/// Get a cached resource
	/// </summary>
	internal static T Get<T>( ResourceId id ) where T : Resource => Game.Resources.Get<T>( id );

	/// <summary>
	/// Try to get a cached resource by its file path.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">File path to the resource.</param>
	/// <param name="resource">The retrieved resource, if any.</param>
	/// <returns>True if resource was retrieved successfully.</returns>
	public static bool TryGet<T>( string filepath, out T resource ) where T : Resource => Game.Resources.TryGet<T>( filepath, out resource );

	/// <summary>
	/// Get all cached resources of given type.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	public static IEnumerable<T> GetAll<T>() => Game.Resources.GetAll<T>();

	/// <summary>
	/// Get all cached resources of given type in a specific folder.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">The path of the folder to check.</param>
	/// <param name="recursive">Whether or not to check folders within the specified folder.</param>
	public static IEnumerable<T> GetAll<T>( string filepath, bool recursive = true ) where T : Resource => Game.Resources.GetAll<T>( filepath, recursive );

	/// <summary>
	/// Load a resource by its file path.
	/// </summary>
	public static async Task<T> LoadAsync<T>( string path ) where T : Resource
	{
		// try to load cached version first
		if ( TryGet<T>( path, out var cached ) )
			return cached;

		// Check if the type is a GameResource, and handle it accordingly
		var type = typeof( T );
		if ( type.IsSubclassOf( typeof( GameResource ) ) )
		{
			if ( type == typeof( PrefabFile ) )
			{
				return (T)(object)PrefabFile.Load( path );
			}

			// Really should be loaded already I think?
			return Get<T>( path );
		}

		if ( type == typeof( Model ) )
		{
			return (T)(object)(await Sandbox.Model.LoadAsync( path ));
		}

		if ( type == typeof( Material ) )
		{
			return (T)(object)(await Sandbox.Material.LoadAsync( path ));
		}

		if ( type == typeof( Shader ) )
		{
			return (T)(object)(Sandbox.Shader.Load( path ));
		}

		return default;
	}

	/// <summary>
	/// Render a thumbnail for this resource
	/// </summary>
	public static async Task<Bitmap> GetThumbnail( string path, int width = 256, int height = 256 )
	{
		var resource = await ResourceLibrary.LoadAsync<Resource>( path );
		if ( resource is null ) return default;

		// try to render it
		return resource.RenderThumbnail( new() { Width = width, Height = height } );
	}

	public interface IEventListener
	{
		/// <summary>
		/// Called when a new resource has been registered
		/// </summary>
		void OnRegister( GameResource resource ) { }

		/// <summary>
		/// Called when a previously known resource has been unregistered
		/// </summary>
		void OnUnregister( GameResource resource ) { }

		/// <summary>
		/// Called when a resource has been saved
		/// </summary>
		void OnSave( GameResource resource ) { }

		/// <summary>
		/// Called when the source file of a known resource has been externally modified on disk
		/// </summary>
		void OnExternalChanges( GameResource resource ) { }

		/// <summary>
		/// Called when the source file of a known resource has been externally modified on disk
		/// and after it has been fully loaded (after post load is called)
		/// </summary>
		void OnExternalChangesPostLoad( GameResource resource ) { }
	}
}
