using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Diagnostics;
using Sandbox.Utility;

namespace Sandbox;

/// <summary>
/// Mounts verified texture prefixes separately from the complete asset cache. Native
/// resource reads fetch the remaining mips on demand using the adjacent .webstream metadata.
/// </summary>
internal static class TextureWebStreaming
{
	static readonly HttpClient http = new( new HttpClientHandler { AllowAutoRedirect = false } ) { Timeout = TimeSpan.FromSeconds( 30 ) };
	// Stripes bound lock storage and keep overlapping package downloads from replacing mounted files.
	static readonly SemaphoreSlim[] locks = Enumerable.Range( 0, 64 ).Select( _ => new SemaphoreSlim( 1 ) ).ToArray();
	static readonly object downloadLock = new();
	static int downloads;
	static long networkBytes;
	static long downloadStarted;
	static double measuredBytesPerSecond;
	static long measurementBytes;
	static double measurementSeconds;
	static float maximumMbps;
	static readonly Queue<(string Path, string Url, int Size)> prefetchQueue = new();
	static readonly HashSet<string> prefetchPaths = new( StringComparer.OrdinalIgnoreCase );
	static CancellationTokenSource prefetchCancellation;
	static Task[] prefetchWorkers = [];
	static int prefetchGeneration;

	[ConVar( "r_texture_web_streaming", ConVarFlags.Saved | ConVarFlags.Protected, Help = "Stream higher-quality textures during gameplay. Set to 0 before joining to download full textures up front. Rejoin to apply to already mounted textures." )]
	public static bool Enabled { get; set; } = true;

	internal static void QueuePrefetch( string path, ManifestSchema.File file )
	{
		if ( !IsSupported( file ) || !File.Exists( path + ".webstream" ) ) return;
		lock ( downloadLock )
		{
			if ( !prefetchPaths.Add( path ) ) return;
			prefetchQueue.Enqueue( (path, file.Url, (int)file.Size) );
			if ( prefetchCancellation is not null ) return;
			prefetchCancellation = new();
			var token = prefetchCancellation.Token;
			var generation = prefetchGeneration;
			// Only two speculative transfers; renderer requests have their own native workers.
			prefetchWorkers = Enumerable.Range( 0, 2 ).Select( _ => Task.Run( () => PrefetchAsync( token, generation ) ) ).ToArray();
		}
	}

	internal static void ClearPrefetch()
	{
		Task[] workers;
		lock ( downloadLock )
		{
			prefetchCancellation?.Cancel();
			prefetchCancellation?.Dispose();
			prefetchCancellation = null;
			prefetchQueue.Clear();
			prefetchPaths.Clear();
			prefetchGeneration = NativeGlue.Resources.CancelTextureWebStreamingPrefetch();
			workers = prefetchWorkers;
			prefetchWorkers = [];
		}
		// Finish native cache access before the game filesystem can be torn down.
		Task.WaitAll( workers );
	}

	static async Task PrefetchAsync( CancellationToken token, int generation )
	{
		bool warned = false;
		while ( !token.IsCancellationRequested )
		{
			try
			{
				await Task.Delay( 250, token );
				if ( LoadingScreen.IsVisible || Volatile.Read( ref downloads ) > 0 || Application.GamePackage is null
					|| NativeGlue.Resources.HasTextureWebStreamingRequests() ) continue;
				(string Path, string Url, int Size) item;
				lock ( downloadLock )
				{
					token.ThrowIfCancellationRequested();
					if ( !prefetchQueue.TryDequeue( out item ) ) continue;
				}
				// Native code uses the same cache locks and bandwidth budget, and yields on new renderer demand.
				var finished = NativeGlue.Resources.PrefetchTextureWebStreaming( item.Path, item.Url, item.Size, generation );
				lock ( downloadLock )
					if ( !finished && !token.IsCancellationRequested ) prefetchQueue.Enqueue( item );
			}
			catch ( OperationCanceledException ) when ( token.IsCancellationRequested ) { return; }
			catch ( Exception e )
			{
				// Keep processing other textures, but avoid flooding the log on repeated faults.
				if ( !warned ) Log.Warning( $"Texture web prefetch failed: {e.Message}. Continuing with remaining textures." );
				warned = true;
			}
		}
	}

	[ConVar( "r_texture_web_streaming_max_mbps", ConVarFlags.Protected, Min = 0, Help = "Extra Mbps cap for background web texture downloads, for demos. 0 uses the automatic budget. Does not limit initial package downloads." )]
	public static float MaximumMbps
	{
		get => maximumMbps;
		set
		{
			lock ( downloadLock )
			{
				maximumMbps = float.IsFinite( value ) && value > 0 ? value : 0;
				UpdateDownloadState();
			}
		}
	}

	internal static int GetBackgroundLimit( double measuredBytesPerSecond, float maximumMbps = 0 )
	{
		const double reserve = 20_000_000 / 8;
		// At or below 20 Mbps, share equally instead of reserving the entire connection.
		var limit = measuredBytesPerSecond <= reserve ? measuredBytesPerSecond * 0.5
			: Math.Min( measuredBytesPerSecond * 0.8, measuredBytesPerSecond - reserve );
		if ( maximumMbps > 0 ) limit = Math.Min( limit, maximumMbps * 1_000_000.0 / 8 );
		return (int)Math.Clamp( limit, 1, int.MaxValue );
	}

	internal static double AverageDownloadSpeed( double previous, long bytes, double seconds )
	{
		var speed = bytes / seconds;
		if ( previous <= 0 ) return speed;
		// A rolling, byte-weighted average: small packages barely move it, while roughly
		// 32 MiB of new downloads gives a changed connection speed meaningful influence.
		var weight = 1 - Math.Exp( -bytes / (32.0 * 1024 * 1024) );
		return previous + (speed - previous) * weight;
	}

	internal static IDisposable BeginDownload()
	{
		lock ( downloadLock )
		{
			if ( downloads++ == 0 )
			{
				Interlocked.Exchange( ref networkBytes, 0 );
				downloadStarted = Stopwatch.GetTimestamp();
			}
			UpdateDownloadState();
		}
		return new DisposeAction( () =>
		{
			lock ( downloadLock )
			{
				if ( --downloads == 0 )
				{
					var seconds = Stopwatch.GetElapsedTime( downloadStarted ).TotalSeconds;
					var bytes = Interlocked.Read( ref networkBytes );
					// Combine short/small downloads too, excluding entirely cached packages.
					if ( bytes > 0 )
					{
						measurementBytes += bytes;
						measurementSeconds += seconds;
					}
					if ( measurementSeconds >= 1 && measurementBytes >= 256 * 1024 )
					{
						measuredBytesPerSecond = AverageDownloadSpeed( measuredBytesPerSecond, measurementBytes, measurementSeconds );
						measurementBytes = 0;
						measurementSeconds = 0;
						Log.Info( $"Texture web streaming: background limit {GetBackgroundLimit( measuredBytesPerSecond, maximumMbps ) * 8.0 / 1_000_000:0.##} Mbps (rolling average {measuredBytesPerSecond * 8.0 / 1_000_000:0.##} Mbps)." );
					}
				}
				UpdateDownloadState();
			}
		} );
	}

	internal static void RecordNetworkBytes( int bytes )
	{
		if ( Volatile.Read( ref downloads ) > 0 ) Interlocked.Add( ref networkBytes, bytes );
	}

	internal static void UpdateDownloadState()
	{
		lock ( downloadLock )
			NativeGlue.Resources.SetTextureWebStreamingState( LoadingScreen.IsVisible || downloads > 0,
				GetBackgroundLimit( measuredBytesPerSecond > 0 ? measuredBytesPerSecond : 256 * 1024, maximumMbps ) );
	}

	internal static Services.TextureWebStreamingInfo.Mip GetInitialMip( ManifestSchema.File file )
	{
		if ( !Enabled || file.Path?.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) != true
			|| file.Size is <= 0 or > 128 * 1024 * 1024
			|| file.Texture is not
			{
				Version: Services.TextureWebStreamingInfo.CurrentVersion,
				Width: > 0 and <= ushort.MaxValue, Height: > 0 and <= ushort.MaxValue, Mips.Length: > 0 and <= 15
			} texture
			|| !Uri.TryCreate( file.Url, UriKind.Absolute, out var uri ) || uri.Scheme != Uri.UriSchemeHttps
			|| !string.IsNullOrEmpty( uri.UserInfo ) || file.Url.Length > 2048 || file.Url.Any( char.IsWhiteSpace ) )
			return null;

		var dimension = Math.Max( texture.Width, texture.Height );
		// Small textures aren't worth a separate background download.
		if ( dimension <= 256 ) return null;
		// Drop two mips, keeping the longer dimension between 256 and 1K.
		var level = 0;
		while ( (level < 2 || (dimension >> level) > 1024) && (dimension >> (level + 1)) >= 256 ) level++;
		if ( level == 0 ) return null;
		Services.TextureWebStreamingInfo.Mip selected = null;
		long previousSize = file.Size;
		for ( int i = 0; i < texture.Mips.Length; i++ )
		{
			var mip = texture.Mips[i];
			if ( mip is not { PrefixSize: > 16, PrefixHash.Length: 64 } || mip.Level != i + 1
				|| (dimension >> mip.Level) == 0 || mip.PrefixSize >= previousSize || !mip.PrefixHash.All( char.IsAsciiHexDigit ) )
				return null;
			previousSize = mip.PrefixSize;
			if ( mip.Level == level ) selected = mip;
		}
		// Bound prefix allocations, including the resource header.
		return selected is { PrefixSize: <= 8 * 1024 * 1024 } ? selected : null;
	}

	internal static bool IsSupported( ManifestSchema.File file ) => GetInitialMip( file ) is not null;

	internal static long DownloadSize( ManifestSchema.File file ) => GetInitialMip( file )?.PrefixSize ?? file.Size;

	internal static async Task<string> TryPrepareAsync( ManifestSchema.File file, CancellationToken token )
	{
		var mip = GetInitialMip( file );
		if ( mip is null ) return null;
		var key = Convert.ToHexString( SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( $"{file.Url}\n{file.Size}\n{mip.PrefixHash}" ) ) ).ToLowerInvariant();
		var path = EngineFileSystem.DownloadedFiles.GetFullPath( $"/web-textures/{key}.vtex_c" );
		var gate = locks[(uint)key.GetHashCode() % locks.Length];
		await gate.WaitAsync( token );
		try
		{
			if ( !File.Exists( path ) || new FileInfo( path ).Length != mip.PrefixSize || !Valid( await File.ReadAllBytesAsync( path, token ), mip ) )
			{
				var bytes = await DownloadPrefixAsync( http, file, token );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				await File.WriteAllBytesAsync( path + ".tmp", bytes, token );
				File.Move( path + ".tmp", path, true );
			}

			// The physical target owns its metadata, so another package's logical path cannot override it.
			var metadata = $"{file.Size}\n{file.Url}\n";
			if ( !File.Exists( path + ".webstream" ) || await File.ReadAllTextAsync( path + ".webstream", token ) != metadata )
			{
				await File.WriteAllTextAsync( path + ".webstream.tmp", metadata, token );
				File.Move( path + ".webstream.tmp", path + ".webstream", true );
			}
			return path;
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested ) { throw; }
		catch ( Exception e )
		{
			Log.Warning( $"Texture streaming unavailable for {file.Path}: {e.Message}" );
			return null;
		}
		finally { gate.Release(); }
	}

	static bool Valid( byte[] bytes, Services.TextureWebStreamingInfo.Mip mip ) => bytes.Length == mip.PrefixSize
		&& Convert.ToHexString( SHA256.HashData( bytes ) ).Equals( mip.PrefixHash, StringComparison.OrdinalIgnoreCase );

	internal static async Task<byte[]> DownloadPrefixAsync( HttpClient client, ManifestSchema.File file, CancellationToken token )
	{
		var mip = GetInitialMip( file ) ?? throw new InvalidDataException( "No usable initial texture mip" );
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource( token );
		timeout.CancelAfter( TimeSpan.FromSeconds( 30 ) );
		token = timeout.Token;
		using var request = new HttpRequestMessage( HttpMethod.Get, file.Url );
		request.Headers.Range = new RangeHeaderValue( 0, mip.PrefixSize - 1 );
		using var response = await client.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, token );
		var range = response.Content.Headers.ContentRange;
		if ( response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes"
			|| range.From != 0 || range.To != mip.PrefixSize - 1 || range.Length != file.Size )
			throw new InvalidDataException( "Server did not return the requested texture range" );

		await using var stream = await response.Content.ReadAsStreamAsync( token );
		var bytes = new byte[mip.PrefixSize];
		await stream.ReadExactlyAsync( bytes, token );
		RecordNetworkBytes( bytes.Length );
		if ( await stream.ReadAsync( new byte[1], token ) != 0 || !Valid( bytes, mip ) )
			throw new InvalidDataException( "Texture prefix hash or length mismatch" );
		return bytes;
	}
}
