using Amazon.Runtime;
using Amazon.S3;

namespace Facepunch;

/// <summary>
/// Shared helpers for talking to our Cloudflare R2 bucket.
/// </summary>
internal static class R2
{
	/// <summary>
	/// Creates an S3 client pointed at the R2 bucket from the SYNC_R2_*
	/// environment variables, along with the bucket name. Returns null (and logs)
	/// if any are missing. The caller owns the client and must dispose it.
	///
	/// R2 is S3-compatible, so this is the simplest way to push a single object -
	/// no rclone (and no rclone-on-the-runner dependency) required.
	/// </summary>
	internal static (IAmazonS3 Client, string Bucket)? CreateS3Client()
	{
		var accessKeyId = Environment.GetEnvironmentVariable( "SYNC_R2_ACCESS_KEY_ID" );
		var secretAccessKey = Environment.GetEnvironmentVariable( "SYNC_R2_SECRET_ACCESS_KEY" );
		var bucket = Environment.GetEnvironmentVariable( "SYNC_R2_BUCKET" );
		var endpoint = Environment.GetEnvironmentVariable( "SYNC_R2_ENDPOINT" );

		if ( string.IsNullOrEmpty( accessKeyId ) || string.IsNullOrEmpty( secretAccessKey ) ||
			 string.IsNullOrEmpty( bucket ) || string.IsNullOrEmpty( endpoint ) )
		{
			Log.Error( "R2 credentials not properly configured in environment variables (SYNC_R2_ACCESS_KEY_ID / SYNC_R2_SECRET_ACCESS_KEY / SYNC_R2_BUCKET / SYNC_R2_ENDPOINT)" );
			return null;
		}

		var config = new AmazonS3Config
		{
			ServiceURL = GetAccountEndpoint( endpoint ),
			// R2 ignores the region but the SigV4 signer needs one.
			AuthenticationRegion = "auto",
			// AWS SDK v4 adds a default CRC checksum on uploads; for multipart it sends
			// a FULL_OBJECT checksum that R2 rejects ("checksum type FULL_OBJECT is not
			// supported"). Only checksum when the operation actually requires it.
			RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
			ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
			// R2 throttles per-object writes (HTTP 429 "Reduce your concurrent request rate
			// for the same object"), so give the SDK plenty of room to back off and retry an
			// individual request rather than failing the whole transfer.
			RetryMode = RequestRetryMode.Adaptive,
			MaxErrorRetry = 8,
			// Upload bodies are copied to the request through this buffer, 8KB by default
			BufferSize = 1024 * 1024
		};

		return (new AmazonS3Client( accessKeyId, secretAccessKey, config ), bucket);
	}

	/// <summary>
	/// Public base URL the bucket is served from. Used for plain HTTP downloads
	/// (e.g. fetching a build zip) without needing rclone or credentials.
	/// </summary>
	internal const string PublicBaseUrl = "https://artifacts.sbox.game";

	/// <summary>
	/// R2's S3 API endpoint must be the bare account host
	/// (https://&lt;account&gt;.r2.cloudflarestorage.com) with NO path or bucket. If the
	/// configured endpoint carries a path component, the SDK bakes the bucket into the
	/// object key (objects land at &lt;bucket&gt;/builds/... instead of builds/...), and rclone
	/// lists the wrong place. Strip everything but scheme + host.
	/// </summary>
	private static string GetAccountEndpoint( string endpoint )
	{
		if ( Uri.TryCreate( endpoint, UriKind.Absolute, out var endpointUri ) )
			return $"{endpointUri.Scheme}://{endpointUri.Authority}";

		return endpoint;
	}

	/// <summary>
	/// Builds the rclone remote string for the root of the R2 bucket from the SYNC_R2_*
	/// environment variables, e.g. <c>:s3,...:bucket</c>. Append <c>/path</c> to address keys
	/// inside it. Returns null (and logs) if any are missing.
	/// </summary>
	internal static string GetRcloneRemote()
	{
		var accessKeyId = Environment.GetEnvironmentVariable( "SYNC_R2_ACCESS_KEY_ID" );
		var secretAccessKey = Environment.GetEnvironmentVariable( "SYNC_R2_SECRET_ACCESS_KEY" );
		var bucket = Environment.GetEnvironmentVariable( "SYNC_R2_BUCKET" );
		var endpoint = Environment.GetEnvironmentVariable( "SYNC_R2_ENDPOINT" );

		if ( string.IsNullOrEmpty( accessKeyId ) || string.IsNullOrEmpty( secretAccessKey ) ||
			 string.IsNullOrEmpty( bucket ) || string.IsNullOrEmpty( endpoint ) )
		{
			Log.Error( "R2 credentials not properly configured in environment variables (SYNC_R2_ACCESS_KEY_ID / SYNC_R2_SECRET_ACCESS_KEY / SYNC_R2_BUCKET / SYNC_R2_ENDPOINT)" );
			return null;
		}

		// rclone has no bucket option, the first path segment is the bucket. A bucket in the
		// endpoint path still lands uploads in the right place but sends listings elsewhere.
		// no_check_bucket: the bucket exists, and object-scoped R2 tokens can't create buckets.
		return $":s3,provider=Cloudflare,no_check_bucket=true,access_key_id={accessKeyId},secret_access_key={secretAccessKey},endpoint='{GetAccountEndpoint( endpoint )}':{bucket}";
	}
}
