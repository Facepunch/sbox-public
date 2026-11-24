using System.IO;

namespace Sandbox;

/// <summary>
/// A generated thumbnail in the global disk cache. Use a variant for previews with
/// different framing.
/// </summary>
internal sealed class ThumbnailCache
{
	readonly string key;
	readonly string bakedModelPath;

	/// <summary>
	/// Identifies a preview by resource path and optional variant. Local resources are scoped
	/// to the game and the modification time of the mounted asset.
	/// </summary>
	public ThumbnailCache( string resourcePath, string variant = null )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( resourcePath );

		var path = Resource.FixPath( resourcePath );
		if ( variant is null ) bakedModelPath = path;

		var identity = resourcePath;
		if ( !Mounting.MountUtility.IsMountPath( resourcePath ) )
		{
			var files = FileSystem.Mounted;
			var source = files.FileExists( $"{path}_c" ) ? $"{path}_c" : path;
			var modified = files.FileExists( source ) ? files.GetLastWriteTime( source ).ToUniversalTime().Ticks : 0;
			identity = $"{Game.Ident}\n{path}\n{modified}";
		}

		if ( variant is not null ) identity += $"\n{variant}";

		// Keep the original mount preview keys so existing entries stay warm.
		key = $"preview/icons/{identity.Md5()}.png";
	}

	/// <summary>
	/// Stable across games and rebuilds. Hash the entire path, including the mount ident.
	/// </summary>
	internal static string GetBakedModelKey( string resourcePath )
		=> GetBakedKeyForPath( Resource.FixPath( resourcePath ) );

	static string GetBakedKeyForPath( string path )
	{
		if ( path?.EndsWith( ".vmdl", StringComparison.Ordinal ) != true ) return null;

		var source = Mounting.MountUtility.IsMountPath( path ) ? "mount" : "local";
		return $"spawnicon/model/{source}/{path.Md5()}";
	}

	/// <summary>
	/// Loads the cached texture, or returns null when the thumbnail needs rendering.
	/// </summary>
	public Texture Load()
	{
		using var bitmap = LoadBitmap();
		return bitmap?.ToTexture();
	}

	/// <summary>
	/// Read the loose thumbnail first, then the shipped fallback for a model.
	/// </summary>
	internal Bitmap LoadBitmap() => LoadLooseBitmap() ?? LoadBakedBitmap();

	Bitmap LoadBakedBitmap()
	{
		var bakedKey = GetBakedKeyForPath( bakedModelPath );
		if ( bakedKey is null || !BakeCache.TryGet( bakedKey, out var baked ) ) return null;

		try
		{
			return Bitmap.CreateFromBytes( baked );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read thumbnail {bakedKey}: {e.Message}" );
			return null;
		}
	}

	Bitmap LoadLooseBitmap()
	{
		try
		{
			return FileSystem.Cache.TryGet( key, out var bytes ) ? Bitmap.CreateFromBytes( bytes ) : null;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read thumbnail {key}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Stores the finished bitmap as a PNG for subsequent sessions.
	/// </summary>
	public void Save( Bitmap bitmap )
	{
		try
		{
			FileSystem.Cache.Set( key, bitmap.ToPng() );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Couldn't save thumbnail {key}: {e.Message}" );
		}
	}
}
