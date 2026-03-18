using Microsoft.Extensions.Caching.Memory;
using Sandbox.Engine;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Sandbox.TextureLoader;

internal static class ImageUrl
{
	/// <summary>
	/// Caches raw downloaded bytes on a sliding window, so repeated loads
	/// don't re-download while keeping VRAM free when textures aren't in use.
	/// </summary>
	static readonly MemoryCache _byteCache = new( new MemoryCacheOptions() );

	internal static bool IsAppropriate( string url )
	{
		if ( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) return false;

		if ( uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps ) return false;

		return true;
	}

	internal static Texture Load( string filename, bool warnOnMissing )
	{
		try
		{
			if ( Game.Resources.Get<Texture>( filename ) is { } cached )
				return cached;

			var placeholder = Texture.Create( 1, 1 ).WithName( "httpimg-placeholder" ).WithData( new byte[4] { 0, 0, 0, 0 } ).Finish();
			_ = placeholder.ReplacementAsync( LoadFromUrl( filename ) );
			placeholder.RegisterWeakResourceId( filename );
			return placeholder;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Couldn't Load from Url {filename} ({e.Message})" );
			return null;
		}
	}

	static HttpClient HttpClient;

	internal static async Task<Texture> LoadFromUrl( string url, CancellationToken ct = default )
	{
		HttpClient ??= new HttpClient();

		try
		{
			if ( !_byteCache.TryGetValue( url, out byte[] bytes ) )
			{
				bytes = await Http.RequestBytesAsync( url, cancellationToken: ct );
				if ( ct.IsCancellationRequested ) return default;

				_byteCache.Set( url, bytes, new MemoryCacheEntryOptions()
					.SetSlidingExpiration( TimeSpan.FromMinutes( 10 ) ) );
			}

			Texture texture = null;
			// decode in a thread
			await Task.Run( () =>
			{
				using var ms = new MemoryStream( bytes );
				texture = Image.Load( ms, url );
			}, ct );

			return texture;
		}
		catch ( OperationCanceledException )
		{
			return default;
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
