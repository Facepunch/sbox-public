namespace Sandbox.Services;

/// <summary>
/// On-disk JSON cache for service-API responses, keyed by a stable string. Lets callers
/// keep working when the backend is unreachable, and gives a fast warm start by loading
/// last session's data immediately on launch.
///
/// Files are written under <c>/.source2/cache/services/</c> in the engine root filesystem.
/// </summary>
internal static class ServiceCache
{
	const string CacheFolder = "/.source2/cache/services";

	// Override point for tests; production code resolves to EngineFileSystem.Root.
	internal static BaseFileSystem FilesystemOverride;

	static BaseFileSystem GetFilesystem() => FilesystemOverride ?? EngineFileSystem.Root;

	/// <summary>
	/// Load <paramref name="cacheKey"/> from disk (if present) and apply it, then fetch from
	/// the backend, apply the fresh result, and refresh the disk copy. Backend failures are
	/// swallowed — whatever came off disk stays applied. Use when you want a warm-start UI
	/// that then transparently updates once the backend responds.
	/// </summary>
	public static async Task LoadAsync<T>( string cacheKey, Func<Task<T>> fetch, Action<T> apply )
	{
		if ( TryReadDisk<T>( cacheKey, out var diskValue ) )
			apply( diskValue );

		try
		{
			var value = await fetch();
			if ( value is null ) return;

			apply( value );
			WriteDisk( cacheKey, value );
		}
		catch ( Exception )
		{
		}
	}

	/// <summary>
	/// Fetch <paramref name="cacheKey"/> from the backend. On success, refreshes the disk
	/// copy and returns the fresh value. On failure, returns the last-known disk copy if
	/// one exists, otherwise <c>default</c>. Use when callers want a single return value
	/// and don't care whether it came from the backend or disk.
	/// </summary>
	public static async Task<T> TryFetchAsync<T>( string cacheKey, Func<Task<T>> fetch )
	{
		try
		{
			var value = await fetch();
			if ( value is not null )
			{
				WriteDisk( cacheKey, value );
				return value;
			}
		}
		catch ( Exception )
		{
		}

		return TryReadDisk<T>( cacheKey, out var diskValue ) ? diskValue : default;
	}

	static bool TryReadDisk<T>( string cacheKey, out T value )
	{
		value = default;

		try
		{
			var fs = GetFilesystem();
			if ( fs is null ) return false;

			var path = GetPath( cacheKey );
			if ( !fs.FileExists( path ) ) return false;

			var json = fs.ReadAllText( path );
			if ( string.IsNullOrEmpty( json ) ) return false;

			value = System.Text.Json.JsonSerializer.Deserialize<T>( json );
			return value is not null;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"ServiceCache: failed to read '{cacheKey}' from disk" );
			return false;
		}
	}

	static void WriteDisk<T>( string cacheKey, T value )
	{
		try
		{
			var fs = GetFilesystem();
			if ( fs is null ) return;

			fs.CreateDirectory( CacheFolder );
			var json = System.Text.Json.JsonSerializer.Serialize( value );
			fs.WriteAllText( GetPath( cacheKey ), json );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"ServiceCache: failed to write '{cacheKey}' to disk" );
		}
	}

	static string GetPath( string cacheKey ) => $"{CacheFolder}/{Sanitize( cacheKey )}.json";

	// Keys are lowercased so disk lookups behave the same way as the in-memory caches
	// that feed them, and so we don't end up with org_facepunch.json and org_FACEPUNCH.json
	// living as separate files on case-sensitive filesystems.
	static string Sanitize( string key )
	{
		if ( string.IsNullOrEmpty( key ) ) return "_";

		var chars = key.ToLowerInvariant().ToCharArray();
		for ( int i = 0; i < chars.Length; i++ )
		{
			var c = chars[i];
			if ( !char.IsLetterOrDigit( c ) && c != '_' && c != '-' && c != '.' )
				chars[i] = '_';
		}
		return new string( chars );
	}
}
