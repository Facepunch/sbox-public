
/// <summary>
/// Quick data folder file storage, good for saving local data
/// </summary>
internal static class LocalData
{
	/// <summary>
	/// Serialize <paramref name="value"/> and write it to <c>{key}.json</c> under <see cref="FileSystem.Data"/>.
	/// The directory hierarchy is created automatically.
	/// </summary>
	public static void Set<T>( string key, T value )
	{
		var path = KeyToPath( key );
		var dir = System.IO.Path.GetDirectoryName( path );

		if ( !string.IsNullOrEmpty( dir ) && !FileSystem.Data.DirectoryExists( dir ) )
			FileSystem.Data.CreateDirectory( dir );

		FileSystem.Data.WriteJson( path, value );
	}

	/// <summary>
	/// Read and deserialize the value stored at <paramref name="key"/>.
	/// Returns <paramref name="fallback"/> if the file doesn't exist or deserialization fails.
	/// </summary>
	public static T Get<T>( string key, T fallback = default )
	{
		var path = KeyToPath( key );

		if ( !FileSystem.Data.FileExists( path ) )
			return fallback;

		try
		{
			return FileSystem.Data.ReadJson<T>( path );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[LocalData] Failed to read '{path}'" );
			return fallback;
		}
	}

	/// <summary>
	/// Returns true if a value has been stored at <paramref name="key"/>.
	/// </summary>
	public static bool Has( string key ) => FileSystem.Data.FileExists( KeyToPath( key ) );

	/// <summary>
	/// Delete the value stored at <paramref name="key"/>. No-op if it doesn't exist.
	/// </summary>
	public static void Delete( string key )
	{
		var path = KeyToPath( key );
		if ( FileSystem.Data.FileExists( path ) )
			FileSystem.Data.DeleteFile( path );
	}

	static string KeyToPath( string key ) => $"{key}.json";
}
