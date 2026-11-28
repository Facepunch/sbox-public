using NativeEngine;
using Sandbox.SteamApi;
using System.Globalization;
using System.IO;

namespace Sandbox.TextureLoader;

internal static class WorkshopThumbnail
{
	const string Prefix = "workshop://";
	const string Suffix = "/thumb";

	static bool TryParse( string path, out ulong workshopItemId )
	{
		workshopItemId = 0;
		return path.StartsWith( Prefix, StringComparison.Ordinal ) && path.EndsWith( Suffix, StringComparison.Ordinal )
			&& path.Length > Prefix.Length + Suffix.Length
			&& ulong.TryParse( path.AsSpan( Prefix.Length, path.Length - Prefix.Length - Suffix.Length ), NumberStyles.None, CultureInfo.InvariantCulture, out workshopItemId )
			&& workshopItemId is not (0 or ulong.MaxValue);
	}

	internal static Texture Load( string path, bool warnOnMissing )
	{
		if ( !TryParse( path, out var workshopItemId ) ) return null;

		var texture = Texture.Create( 1, 1 ).WithData( new byte[4] ).Finish();
		_ = texture.ReplacementAsync( LoadAsync( workshopItemId, warnOnMissing ) );
		return texture;
	}

	static async Task<Texture> LoadAsync( ulong workshopItemId, bool warnOnMissing )
	{
		try
		{
			var item = await Workshop.FetchAsync( workshopItemId );
			if ( item is null || item.PreviewHandle is 0 or ulong.MaxValue ) return null;

			var previewFileHandle = item.PreviewHandle;

			var folder = EngineFileSystem.Root.GetFullPath( "/.source2/cache/wsthumb" );
			var path = Path.Combine( folder, previewFileHandle.ToString( CultureInfo.InvariantCulture ) );
			if ( File.Exists( path ) )
			{
				return await Task.Run( () =>
				{
					using var stream = File.OpenRead( path );
					return Image.Load( stream, path );
				} );
			}

			var bytes = await DownloadAsync( previewFileHandle );
			if ( bytes is null ) return null;

			return await Task.Run( () =>
			{
				using var stream = new MemoryStream( bytes, writable: false );
				var image = Image.Load( stream, path );
				if ( image is not null ) CacheImage( folder, path, bytes );
				return image;
			} );
		}
		catch ( Exception e )
		{
			if ( warnOnMissing ) Log.Warning( $"Couldn't load Workshop thumbnail {workshopItemId}: {e.Message}" );
			return null;
		}
	}

	static void CacheImage( string folder, string path, byte[] bytes )
	{
		// Publish the complete file so another running game can read it immediately.
		var temp = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			Directory.CreateDirectory( folder );
			File.WriteAllBytes( temp, bytes );
			File.Move( temp, path );
		}
		catch ( IOException ) when ( File.Exists( path ) )
		{
			// Another instance already saved this handle.
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't cache Workshop thumbnail {path}: {e.Message}" );
		}
		finally
		{
			if ( File.Exists( temp ) ) File.Delete( temp );
		}
	}

	static async Task<byte[]> DownloadAsync( ulong previewFileHandle )
	{
		if ( !SteamClient.IsValid ) return null;

		var call = SteamNative.RemoteStorage.DownloadPreview( previewFileHandle );

		using var buffer = await SteamCalls.GetResultAsync( call,
			handle => SteamNative.RemoteStorage.ReadDownloadedPreviewResult( handle, previewFileHandle, Workshop.MaxPreviewBytes ) );

		return buffer.IsNull ? null : buffer.ToArray();
	}
}
