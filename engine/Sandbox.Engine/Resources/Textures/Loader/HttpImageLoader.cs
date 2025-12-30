using Microsoft.Extensions.Caching.Memory;
using System.IO;
using System.Net.Http;

namespace Sandbox.TextureLoader;

internal static class ImageUrl
{
	/// <summary>
	/// Entries are cached on a sliding window, they will be released if not used for 10 minutes
	/// </summary>
	static readonly MemoryCache _cache = new( new MemoryCacheOptions() );

	internal static bool IsAppropriate( string url )
	{
		return url.StartsWith( "https://" ) || url.StartsWith( "http://" );
	}

	internal static Texture Load( string filename, bool warnOnMissing )
	{
		try
		{
			return _cache.GetOrCreate<Texture>( filename, entry =>
			{
				//
				// Create a 1x1 placeholder texture
				//
				var placeholder = Texture.Create( 1, 1 ).WithName( "httpimg-placeholder" ).WithData( new byte[4] { 0, 0, 0, 0 } ).Finish();
				_ = placeholder.ReplacementAsync( LoadFromUrl( filename ) );

				entry.SlidingExpiration = TimeSpan.FromMinutes( 10 );
				return placeholder;
			} );
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Couldn't Load from Url {filename} ({e.Message})" );
			return null;
		}
	}

	static HttpClient HttpClient;

	internal static async Task<Texture> LoadFromUrl( string url )
	{
		HttpClient ??= new HttpClient();
		var filename = url;

		try
		{
			// I'd love to retry this multiple times, if it's a weird error that seems recoverable

			var bytes = await HttpClient.GetByteArrayAsync( url );
			Texture texture = null;
			// decode in a thread
			await Task.Run( () =>
			{
				using var ms = new MemoryStream( bytes );
				texture = Image.Load( ms, url );
			} );

			return texture;
		}
		catch ( System.Security.Authentication.AuthenticationException )
		{
			Log.Warning( $"AuthenticationException when downloading {url}" );
		}
		catch ( HttpRequestException e )
		{
			Log.Warning( e, $"HttpRequestException when downloading {url}" );
		}

		return default;
	}
}
