using System.Text;
using System.Threading;

namespace Sandbox.Services;

public static partial class Ai
{
	public sealed partial class Query
	{
		const int MaxImageDimension = 2048;
		const int MaxImageBytes = 3 * 1024 * 1024;
		internal const int MaxImagePayloadBytes = 16 * 1024 * 1024;

		readonly List<Image> images = [];

		sealed class Image( Func<Task<string>> prepare )
		{
			Task<string> data;

			public Task<string> Prepare( CancellationToken cancellationToken )
			{
				cancellationToken.ThrowIfCancellationRequested();
				return (data ??= prepare()).WaitAsync( cancellationToken );
			}
		}

		/// <summary>
		/// Adds an HTTP(S) image URL or a base64 image data URL in JPEG, PNG, WebP or GIF format.
		/// HTTP(S) URLs are forwarded to the AI provider without downloading them locally.
		/// </summary>
		public Query AddImage( string url )
		{
			ArgumentException.ThrowIfNullOrWhiteSpace( url );
			if ( url.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) )
			{
				var separator = url.IndexOf( ',' );
				var header = separator < 0 ? "" : url[..separator].ToLowerInvariant();
				if ( header is not ("data:image/jpeg;base64" or "data:image/png;base64" or "data:image/webp;base64" or "data:image/gif;base64") )
					throw new ArgumentException( "Supply a JPEG, PNG, WebP or GIF base64 data URL.", nameof( url ) );
				var base64 = url.AsSpan( separator + 1 );
				if ( base64.Length > ((MaxImageBytes + 2) / 3) * 4 )
					throw new ArgumentException( $"Inline images cannot exceed {MaxImageBytes} bytes before base64 encoding.", nameof( url ) );
				var bytes = new byte[base64.Length / 4 * 3];
				if ( !Convert.TryFromBase64Chars( base64, bytes, out var written ) || written == 0 || written > MaxImageBytes )
					throw new ArgumentException( "The inline image has invalid or oversized base64 data.", nameof( url ) );
			}
			else if ( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) || (uri.Scheme != "https" && uri.Scheme != "http") )
			{
				throw new ArgumentException( "Supply an HTTP(S) image URL or a base64 image data URL.", nameof( url ) );
			}
			images.Add( new Image( () => Task.FromResult( url ) ) );
			return this;
		}

		/// <summary>
		/// Copies and encodes a bitmap now, so the caller can dispose it after this call.
		/// Automatically resizes and encodes the image. Use lossless for text, diagrams or transparency.
		/// </summary>
		public Query AddImage( Bitmap bitmap, bool lossless = false )
		{
			ArgumentNullException.ThrowIfNull( bitmap );
			var url = EncodeImage( bitmap, lossless );
			images.Add( new Image( () => Task.FromResult( url ) ) );
			return this;
		}

		/// <summary>
		/// Adds a 2D texture. Keep it alive until submission reads it asynchronously from the GPU.
		/// Its first encoded image is cached for reuse and retries. Uses the same sizing and encoding as bitmaps.
		/// </summary>
		public Query AddImage( Texture texture, bool lossless = false )
		{
			ArgumentNullException.ThrowIfNull( texture );
			images.Add( new Image( () => ReadTexture( texture, lossless ) ) );
			return this;
		}

		static Task<string> ReadTexture( Texture texture, bool lossless )
		{
			if ( !texture.IsValid ) throw new ObjectDisposedException( nameof( texture ) );
			if ( texture.Depth != 1 ) throw new ArgumentException( "AI images require a 2D texture.", nameof( texture ) );
			var result = new TaskCompletionSource<string>( TaskCreationOptions.RunContinuationsAsynchronously );
			texture.GetBitmapAsync( bitmap =>
			{
				using ( bitmap )
				{
					try { result.SetResult( EncodeImage( bitmap, lossless ) ); }
					catch ( Exception exception ) { result.SetException( exception ); }
				}
			} );
			return result.Task;
		}

		static string EncodeImage( Bitmap bitmap, bool lossless )
		{
			if ( !bitmap.IsValid ) throw new ObjectDisposedException( nameof( bitmap ) );
			var scale = Math.Min( 1.0, (double)MaxImageDimension / Math.Max( bitmap.Width, bitmap.Height ) );
			using var resized = scale < 1 ? bitmap.Resize( Math.Max( 1, (int)(bitmap.Width * scale) ), Math.Max( 1, (int)(bitmap.Height * scale) ) ) : null;
			var source = resized ?? bitmap;
			var bytes = lossless ? source.ToPng() : source.ToJpg( 85 );
			if ( bytes.Length > MaxImageBytes )
				throw new ArgumentException( $"The encoded image exceeds {MaxImageBytes} bytes. Resize it further or use JPEG.", nameof( bitmap ) );
			return $"data:image/{(lossless ? "png" : "jpeg")};base64,{Convert.ToBase64String( bytes )}";
		}

		internal async Task<List<string>> PrepareImages( CancellationToken cancellationToken )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var snapshot = images.ToArray();
			var result = new List<string>( snapshot.Length );
			long size = 0;
			foreach ( var image in snapshot )
			{
				var url = await image.Prepare( cancellationToken );
				size += Encoding.UTF8.GetByteCount( url );
				if ( size > MaxImagePayloadBytes )
					throw new InvalidOperationException( $"The combined image payload exceeds {MaxImagePayloadBytes} bytes." );
				result.Add( url );
			}
			cancellationToken.ThrowIfCancellationRequested();
			return result;
		}
	}
}
