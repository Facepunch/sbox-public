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

		// R2's S3 API endpoint must be the bare account host
		// (https://<account>.r2.cloudflarestorage.com) with NO path or bucket. If the
		// configured endpoint carries a path component, the SDK bakes the bucket into the
		// object key (objects land at <bucket>/builds/... instead of builds/...). Strip
		// everything but scheme + host.
		if ( Uri.TryCreate( endpoint, UriKind.Absolute, out var endpointUri ) )
			endpoint = $"{endpointUri.Scheme}://{endpointUri.Authority}";

		var config = new AmazonS3Config
		{
			ServiceURL = endpoint,
			// R2 ignores the region but the SigV4 signer needs one.
			AuthenticationRegion = "auto",
			// AWS SDK v4 adds a default CRC checksum on uploads; for multipart it sends
			// a FULL_OBJECT checksum that R2 rejects ("checksum type FULL_OBJECT is not
			// supported"). Only checksum when the operation actually requires it.
			RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
			ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
		};

		return (new AmazonS3Client( accessKeyId, secretAccessKey, config ), bucket);
	}

	/// <summary>
	/// Public base URL the bucket is served from. Used for plain HTTP downloads
	/// (e.g. fetching a build zip) without needing rclone or credentials.
	/// </summary>
	internal const string PublicBaseUrl = "https://artifacts.sbox.game";

	/// <summary>
	/// Builds the rclone remote string for the R2 bucket from the SYNC_R2_*
	/// environment variables. Returns null (and logs) if any are missing.
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

		return $":s3,bucket={bucket},provider=Cloudflare,access_key_id={accessKeyId},secret_access_key={secretAccessKey},endpoint='{endpoint}':";
	}
}
