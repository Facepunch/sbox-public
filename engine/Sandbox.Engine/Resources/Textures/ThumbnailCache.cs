using System.IO;

namespace Sandbox;

/// <summary>
/// A generated thumbnail in the global disk cache. Use a variant for previews with
/// different framing.
/// </summary>
internal sealed class ThumbnailCache
{
	readonly string key;

	/// <summary>
	/// Identifies a preview by resource path and optional variant. Local resources are scoped
	/// to the game and the modification time of the mounted asset.
	/// </summary>
	public ThumbnailCache( string resourcePath, string variant = null )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( resourcePath );

		var identity = resourcePath;
		if ( !Mounting.MountUtility.IsMountPath( resourcePath ) )
		{
			var path = resourcePath.NormalizeFilename( false ).TrimStart( '/' );
			if ( path.EndsWith( "_c" ) ) path = path[..^2];

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
	/// Loads the cached texture, or returns null when the thumbnail needs rendering.
	/// </summary>
	public Texture Load()
	{
		try
		{
			if ( !FileSystem.Cache.TryGet( key, out var bytes ) ) return null;

			using var bitmap = Bitmap.CreateFromBytes( bytes );
			return bitmap?.ToTexture();
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
