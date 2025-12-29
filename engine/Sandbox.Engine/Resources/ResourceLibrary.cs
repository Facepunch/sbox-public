using NativeEngine;
using Sandbox.Engine;
using System.Reflection;
using System.Runtime.InteropServices;
using static Sandbox.ResourceLibrary;

namespace Sandbox;

public class ResourceSystem
{
	private Dictionary<int, Resource> ResourceIndex { get; } = new();

	internal void Register( Resource resource )
	{
		Log.Trace( $"Registering {resource.GetType()} ( {resource.ResourcePath} ) as {resource.ResourceId}" );

		ResourceIndex[resource.ResourceId] = resource;

		if ( resource is GameResource gameResource && !gameResource.IsPromise )
		{
			IToolsDll.Current?.RunEvent<IEventListener>( i => i.OnRegister( gameResource ) );
		}
	}

	internal void Unregister( Resource resource )
	{
		// This isn't thread safe
		ThreadSafe.AssertIsMainThread();

		// Make sure we're unregistering the currently indexed resource

		if ( ResourceIndex.TryGetValue( resource.ResourceId, out var existing ) && existing == resource )
		{
			// native asset system doesn't support asset removal right now,
			// so just remove it from the index to ensure we don't retrieve it anymore

			ResourceIndex.Remove( resource.ResourceId );
		}
		else
		{
			Log.Trace( $"Unregistering \"{resource.ResourcePath}\", but it wasn't registered" );
		}

		if ( resource is GameResource gameResource && !gameResource.IsPromise )
		{
			IToolsDll.Current?.RunEvent<IEventListener>( i => i.OnUnregister( gameResource ) );
		}
	}

	internal void OnHotload()
	{
		TypeCache.Clear();
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
			resource.Destroy();
		}

		ResourceIndex.Clear();

		TypeCache.Clear();
	}

	internal Resource Get( System.Type t, int identifier )
	{
		if ( !ResourceIndex.TryGetValue( identifier, out var resource ) )
			return null;

		if ( resource.GetType().IsAssignableTo( t ) )
			return resource;

		return null;
	}

	internal Resource Get( System.Type t, string filepath )
	{
		filepath = Resource.FixPath( filepath );

		return Get( t, filepath.FastHash() );
	}

	/// <summary>
	/// Get a cached resource by its hash.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="identifier">Resource hash to look up.</param>
	public T Get<T>( int identifier ) where T : Resource
	{
		if ( !ResourceIndex.TryGetValue( identifier, out var resource ) )
			return default;

		return resource as T;
	}

	/// <summary>
	/// Get a cached resource by its file path.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">File path to the resource.</param>
	public T Get<T>( string filepath ) where T : Resource
	{
		filepath = Resource.FixPath( filepath );

		return Get<T>( filepath.FastHash() );
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

	internal T LoadGameResource<T>( string file, BaseFileSystem fs, bool deferPostload = false ) where T : GameResource
	{
		var attr = typeof( T ).GetCustomAttribute<AssetTypeAttribute>();
		if ( attr == null ) return default;

		// this is filled in automatically when accessed via TypeLibrary
		// but this ain't TypeLibrary kiddo
		attr.TargetType = typeof( T );

		return LoadGameResource( attr, file, fs, deferPostload ) as T;
	}

	/// <summary>
	/// Loads a Gameresource from disk. Doesn't look at cache. Registers the resource if successful.
	/// </summary>
	internal GameResource LoadGameResource( AssetTypeAttribute type, string file, BaseFileSystem fs, bool deferPostload = false )
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

			var se = GameResource.GetPromise( type.TargetType, file );
			if ( se is null ) return null;

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

			Register( se );

			if ( !deferPostload )
				se.PostLoadInternal();

			return se;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, $"		Error when deserializing {file} ({ex.Message})" );
		}

		return null;
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
	public static T Get<T>( int identifier ) where T : Resource => Game.Resources.Get<T>( identifier );

	/// <summary>
	/// Get a cached resource by its file path.
	/// </summary>
	/// <typeparam name="T">Resource type to get.</typeparam>
	/// <param name="filepath">File path to the resource.</param>
	public static T Get<T>( string filepath ) where T : Resource => Game.Resources.Get<T>( filepath );

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
