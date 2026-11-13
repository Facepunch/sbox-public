using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// Tracks and loads resources for a game or menu context.
/// </summary>
public partial class ResourceSystem
{
	/// <summary>
	/// Resolves uncompiled assets through their declaration and the normal resource cache.
	/// A true result means this is an uncompiled asset, even if the requested file or type is unavailable.
	/// </summary>
	internal bool TryLoadUncompiledResource( Type targetType, ResourceId id, BaseFileSystem fileSystem, out Resource resource )
	{
		resource = null;
		var cached = Get( targetType, id );
		var path = id.Path ?? cached?.ResourcePath;

		// A cached GUID match is authoritative, even when a serialized path is stale.
		if ( cached is not null && cached.Guid != Guid.Empty && id.Guid == cached.Guid )
			path = cached.ResourcePath;

		if ( string.IsNullOrWhiteSpace( path ) ||
			!TryGetType( System.IO.Path.GetExtension( path ), out var assetType ) || !assetType.IsUncompiled )
			return false;

		if ( !assetType.TargetType.IsAssignableTo( targetType ) || path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			return true;

		resource = cached ?? LoadUncompiledResource( assetType, path, fileSystem );
		return true;
	}

	/// <summary>
	/// Loads source bytes and invokes the resource's load hook. Registration happens first so
	/// references made during loading resolve to the same instance. Failed loads are unregistered,
	/// logged and returned as null so a bad source file does not abort its referring resource's load.
	/// Missing files and failed reloads leave an already loaded resource available for the context's lifetime.
	/// </summary>
	internal Resource LoadUncompiledResource( AssetTypeAttribute assetType, string path, BaseFileSystem fileSystem, bool reloadExisting = false )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !assetType.IsUncompiled )
			throw new ArgumentException( "The asset type must be uncompiled.", nameof( assetType ) );

		path = Resource.FixPath( path );
		var cached = Get( assetType.TargetType, path );
		if ( cached is not null && !reloadExisting )
			return cached;

		if ( !fileSystem.FileExists( path ) )
			return cached;

		Resource resource = null;
		try
		{
			var data = fileSystem.ReadAllBytes( path );
			resource = cached;
			if ( resource is null )
			{
				resource = GlobalContext.Current.TypeLibrary.GetType( assetType.TargetType ).Create<Resource>();
				NativeEngine.g_pResourceSystem.ResolvePathToGuid( path, out var guid );
				resource.SetResourceIdentity( path, guid );
				Register( resource );
				RegisterPath( path );
			}

			resource.OnLoaded( new ResourceLoadContext( path, data ) );
			return resource;
		}
		catch ( Exception exception )
		{
			if ( resource is not null && cached is null )
				Unregister( resource );

			Log.Warning( exception, $"Failed to load uncompiled resource '{path}'." );
			return cached;
		}
	}
}
