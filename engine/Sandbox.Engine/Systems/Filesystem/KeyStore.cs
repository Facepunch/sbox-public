namespace Sandbox;

/// <summary>
/// Allows storing files by hashed keys, rather than by actual filename. This is sometimes useful.
/// </summary>
[Expose]
public sealed class KeyStore
{
	private BaseFileSystem _fs { get; set; }

	private KeyStore()
	{
	}

	/// <summary>
	/// Creates a keystore which is in a global cache position. The folder can be 
	/// deleted at any time, and it's all fine and no-one cares.
	/// </summary>
	public static KeyStore CreateGlobalCache()
	{
		var ks = new KeyStore();

		// make sure this folder exists
		EngineFileSystem.Root.CreateDirectory( "/.source2/cache" );

		ks._fs = EngineFileSystem.Root.CreateSubSystem( "/.source2/cache" );

		return ks;
	}

	private string GetPath( string key )
	{
		key ??= "null";
		return $"{key.Md5()}.bin";
	}

	/// <summary>
	/// Store a bunch of bytes
	/// </summary>
	public void Set( string key, byte[] data )
	{
		if ( key is null ) throw new ArgumentNullException( nameof( key ) );
		if ( data is null ) throw new ArgumentNullException( nameof( data ) );

		_fs.WriteAllBytes( GetPath( key ), data );
	}

	/// <summary>
	/// Get stored bytes, or return null
	/// </summary>
	public byte[] Get( string key )
	{
		var path = GetPath( key );
		return _fs.FileExists( path ) ? _fs.ReadAllBytes( path ).ToArray() : null;
	}

	/// <summary>
	/// Get stored bytes, or return false
	/// </summary>
	public bool TryGet( string key, out byte[] data )
	{
		var path = GetPath( key );
		if ( _fs.FileExists( path ) )
		{
			data = _fs.ReadAllBytes( path ).ToArray();
			return true;
		}

		data = Array.Empty<byte>();
		return false;
	}

	/// <summary>
	/// Check if a key exists
	/// </summary>
	public bool Exists( string key )
	{
		return _fs.FileExists( GetPath( key ) );
	}

	/// <summary>
	/// Remove a key
	/// </summary>
	public void Remove( string key )
	{
		var path = GetPath( key );
		if ( _fs.FileExists( path ) )
			_fs.DeleteFile( path );
	}
}
