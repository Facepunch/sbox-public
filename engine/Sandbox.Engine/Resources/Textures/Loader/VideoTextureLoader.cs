using Sandbox.Utility;

namespace Sandbox.TextureLoader;

internal static class VideoTextureLoader
{
	static readonly CaseInsensitiveDictionary<WeakReference<Texture>> ActivePlayers = new();

	internal static readonly HashSet<string> Extensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".mp4",
		".webm",
		".mov",
		".avi",
		".wmv",
		".mvk",
		".m4v",
	};

	internal static bool IsAppropriate( string url )
	{
		// Check if it's a web URL
		if ( Uri.TryCreate( url, UriKind.Absolute, out var uri ) )
		{
			if ( uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps )
			{
				var split = url.Split( '?' )[0];
				var extension = System.IO.Path.GetExtension( split );
				return Extensions.Contains( extension );
			}

			return false;
		}

		// Treat as local file path
		var ext = System.IO.Path.GetExtension( url );
		return Extensions.Contains( ext );
	}

	internal static Texture Load( BaseFileSystem filesystem, string filename, bool warnOnMissing )
	{
		if ( ActivePlayers.TryGetValue( filename, out var playerref ) && playerref.TryGetTarget( out var texture ) )
		{
			return texture;
		}

		bool isUrl = filename.Contains( "://" );

		if ( !isUrl && !filesystem.FileExists( filename ) )
		{
			if ( warnOnMissing )
			{
				Log.Warning( $"Image.Load: '{filename}' not found" );
			}

			return null;
		}

#pragma warning disable CA2000 // Dispose objects before losing scope
		// TOOD this sucks, right now we rely on the VideoPlayer and its texture to be GC'd to free up resources
		// we should make this explicit, but i don't know how, since VideoPlayer and texture form a circular reference,
		// so we can't keep a hard reference to either of them
		var player = new VideoPlayer();
#pragma warning restore CA2000 // Dispose objects before losing scope
		player.SetVideoOnly();

		ActivePlayers[filename] = new WeakReference<Texture>( player.Texture );

		player.Repeat = true;

		player.Play( filesystem, filename );

		return player.Texture;
	}

	static readonly Superluminal superluminal = new( "TickVideoPlayers", "#2c3541" );

	public static void TickVideoPlayers()
	{
		using var _ = superluminal.Start();

		foreach ( var entry in ActivePlayers )
		{
			if ( !entry.Value.TryGetTarget( out var texture ) )
			{
				Log.Trace( $"Removing dead video player {entry.Key}" );
				ActivePlayers.Remove( entry.Key );
				continue;
			}

			if ( texture.LastUsed > 2 )
				continue;

			if ( texture.ParentObject is VideoPlayer player )
			{
				player.Present();
			}
		}
	}
}
