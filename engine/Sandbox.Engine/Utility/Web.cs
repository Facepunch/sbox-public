using Sentry;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace Sandbox.Utility;

internal static class Web
{
	static HttpClient client = new HttpClient()
	{
		Timeout = TimeSpan.FromMinutes( 120 )
	};

	/// <summary>
	/// Download a file to a target filename (todo - progress)
	/// </summary>
	public static async Task<bool> DownloadFile( string url, string targetFileName, CancellationToken cancelToken, Sandbox.Utility.DataProgress.Callback progress = null )
	{
		Assert.NotNull( targetFileName );

		var tempName = targetFileName + $".{Random.Shared.Int( 10000, 99999 )}.tmp";

		int tries = 0;
		retry:

		try
		{
			tries++;
			var response = await client.GetAsync( url, HttpCompletionOption.ResponseContentRead );

			if ( !response.IsSuccessStatusCode )
			{
				if ( tries < 3 )
				{
					Log.Warning( $"Error downloading {url} (status was {response.StatusCode}) - retrying" );
					await Task.Delay( 500 * tries );
					goto retry;
				}

				Log.Warning( $"Error downloading {url} (status was {response.StatusCode})" );
				SentrySdk.AddBreadcrumb( $"Error downloading {url} (status was {response.StatusCode})", "download.failed" );
				return false;
			}

			DataProgress p = new DataProgress();
			p.TotalBytes = response.Content.Headers.ContentLength ?? 0;
			progress?.Invoke( p );

			// Make sure the directory exists
			var path = new DirectoryInfo( System.IO.Path.GetDirectoryName( tempName ) );
			if ( !path.Exists ) path.Create();

			using var fileStream = new FileStream( tempName, FileMode.Create );
			using var bodyStream = await response.Content.ReadAsStreamAsync( cancelToken );
			using var content = new DataProgress.HttpContentStream( bodyStream );

			if ( progress is not null )
			{
				content.Progress = p => MainThread.Queue( () => progress.Invoke( p ) );
			}

			await content.CopyToAsync( fileStream, cancelToken );
		}
		catch ( TaskCanceledException )
		{
			throw;
		}
		catch ( System.Net.Http.HttpRequestException e )
		{
			if ( tries <= 3 )
			{
				Log.Warning( $"Error downloading {url} ({e.Message}) - retrying" );
				await Task.Delay( 500 * tries );
				goto retry;
			}

			Log.Warning( e, $"Http Error downloading {url}" );
			SentrySdk.CaptureException( e, scope => scope.SetExtra( "url", url ) );
			return false;
		}
		catch ( System.IO.IOException ioe )
		{
			if ( tries <= 3 )
			{
				Log.Warning( $"Error downloading {url} ({ioe.Message}) - retrying" );
				await Task.Delay( 500 * tries );
				goto retry;
			}

			Log.Warning( ioe, $"IO Error downloading {url}" );
			SentrySdk.CaptureException( ioe, scope => scope.SetExtra( "url", url ) );
			return false;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"Error downloading {url}" );
			SentrySdk.CaptureException( ex, scope => scope.SetExtra( "url", url ) );
			return false;
		}

		cancelToken.ThrowIfCancellationRequested();

		int imoveTries = 0;
		while ( true )
		{
			imoveTries++;

			try
			{
				System.IO.File.Move( tempName, targetFileName, true );
				return true;
			}
			catch ( System.Exception )
			{
				if ( imoveTries > 10 )
					return false;

				await Task.Delay( 100 * tries );
			}
		}
	}

	/// <summary>
	/// Download a file to a byte array
	/// </summary>
	public static async Task<byte[]> GrabFile( string url, CancellationToken cancelToken, Action<int> loader = null )
	{
		try
		{
			using ( var stream = await client.GetStreamAsync( url, cancelToken ) )
			{
				using ( var fileStream = new MemoryStream() )
				{
					var t = stream.CopyToAsync( fileStream, cancelToken );

					while ( !t.IsCompleted )
					{
						loader?.Invoke( (int)fileStream.Position );
						await Task.Delay( 16, cancelToken );
						cancelToken.ThrowIfCancellationRequested();
					}

					//await Task.Delay( 100 );

					loader?.Invoke( (int)fileStream.Position );
					return fileStream.ToArray();
				}
			}
		}
		catch ( TaskCanceledException )
		{
			throw;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"Error downloading {url}" );
			return null;
		}
	}

	/// <summary>
	/// Download a url to a string
	/// </summary>
	public static async Task<string> DownloadString( string url, CancellationToken cancelToken )
	{
		for ( int i = 0; i <= 3; i++ )
		{
			try
			{
				return await client.GetStringAsync( url, cancelToken );
			}
			catch ( HttpRequestException e )
			{
				if ( e.StatusCode == System.Net.HttpStatusCode.NotFound )
				{
					Log.Warning( e, $"Error downloading string '{url}' (not found)" );
					return null;
				}

				Log.Warning( e, $"Error downloading string '{url}' ({e.StatusCode}) ({i}/3)" );
				await Task.Delay( 500 );
				continue;
			}
			catch ( TaskCanceledException )
			{
				throw;
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Error downloading string '{url}'" );
				return null;
			}
		}

		return null;
	}

	/// <summary>
	/// Download a url to a string
	/// </summary>
	public static async Task<T> DownloadJson<T>( string url, CancellationToken cancelToken = default )
	{
		var json = await DownloadString( url, cancelToken );

		try
		{
			return JsonSerializer.Deserialize<T>( json, new JsonSerializerOptions( JsonSerializerOptions.Default ) { PropertyNameCaseInsensitive = true } );
		}
		catch ( TaskCanceledException )
		{
			throw;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"Error downloading json {url}" );
			return default;
		}
	}

	/// <summary>
	/// Download a file to a target filename (todo - progress)
	/// </summary>
	public static async Task<bool> PutAsync( Stream fileStream, string endpoint, CancellationToken cancelationToken, Sandbox.Utility.DataProgress.Callback progress )
	{
		DataProgress p = new DataProgress { TotalBytes = fileStream.Length };
		progress?.Invoke( p );

		using var content = new DataProgress.HttpContentStream( fileStream );
		content.Progress = p => MainThread.Queue( () => progress?.Invoke( p ) );

		// Assume azure
		content.Headers.Add( "x-ms-blob-type", "BlockBlob" );

		HttpResponseMessage r = default;

		await Task.Run( async () => r = await client.PutAsync( endpoint, content, cancelationToken ), cancelationToken );

		if ( r.StatusCode != System.Net.HttpStatusCode.Created )
		{
			Log.Warning( $"PutAsync failed status code: {r}" );
			return false;
		}

		p.ProgressBytes = p.TotalBytes;
		progress?.Invoke( p );

		cancelationToken.ThrowIfCancellationRequested();
		return true;
	}
}
