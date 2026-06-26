using System.IO.Compression;
using Amazon.S3;
using Amazon.S3.Transfer;
using Microsoft.Extensions.FileSystemGlobbing;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Packages a complete, ready-to-run build of the game into a single zip and
/// uploads it to R2 so it can be downloaded and launched.
/// </summary>
internal class UploadBuildArtifacts
{
	// What to package, mirrors the Steam depot mappings mostly
	// This doesn't have to be perfect
	private static readonly string[] IncludeGlobs =
	{
		"*.exe",
		"*.dll",
		"*.json",
		".version",
		"thirdpartylegalnotices.txt",
		"thirdpartylegalnotices/**",
		"bin/win64/**",
		"bin/managed/**",
		"bin/assettypes.txt",
		"bin/enginetools.txt",
		"addons/**",
		"core/**",
		"config/**",
		"editor/**",
		"mount/**",
		"samples/**",
		"templates/**"
	};

	// What to strip,  debug symbols, uncompiled files, etc.
	private static readonly string[] ExcludeGlobs =
	{
		"**/*.pdb",
		"**/*.dbg",
		"**/*.psd",
		"**/*.exr",
		"**/*.tif",
		"**/*.tiff",
		"**/*.vtex",
		"**/*.fbx",
		"**/*.dmx",
		"**/*.ma",
		"**/*.max",
		"**/*.lxo",
		"**/*.vmdl",
		"**/*.vmat",
		"**/obj/**",
		"**/*.sln",
		"**/*.csproj",
		"**/*.codegen",
		"**/.intermediate/**",
		"**/*.code-workspace"
	};

	internal ExitCode Run()
	{
		try
		{
			var connection = R2.CreateS3Client();
			if ( connection is null )
				return ExitCode.Failure;

			using var s3 = connection.Value.Client;
			var bucket = connection.Value.Bucket;

			var repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Directory.GetCurrentDirectory() ) );

			// (zip entry path, absolute source path)
			var files = CollectRunnableFiles( repoRoot );
			if ( files.Count == 0 )
			{
				Log.Error( "No runnable build files were found to package. Did the build/content steps run?" );
				return ExitCode.Failure;
			}

			var commit = GetCommit();
			if ( string.IsNullOrEmpty( commit ) )
			{
				Log.Error( "Unable to determine the commit hash to key the build artifact." );
				return ExitCode.Failure;
			}

			var zipPath = Path.Combine( Path.GetTempPath(), $"sbox-build-{commit}-{Guid.NewGuid():N}.zip" );

			try
			{
				CreateArchive( files, zipPath );

				var zipSize = new FileInfo( zipPath ).Length;
				Log.Info( $"Packaged {files.Count} file(s) into build archive ({Utility.FormatSize( zipSize )})" );

				// Immutable, commit-keyed object.
				if ( !UploadZip( s3, bucket, zipPath, $"builds/{commit}.zip" ) )
					return ExitCode.Failure;

				// Hand the size to the later report-build step so it can record the artifact's byte size.
				Utility.SetGitHubEnv( "BUILD_ARTIFACT_BYTES", zipSize.ToString() );

				Log.Info( $"Build artifact available at {R2.PublicBaseUrl}/builds/{commit}.zip" );

				return ExitCode.Success;
			}
			finally
			{
				try { if ( File.Exists( zipPath ) ) File.Delete( zipPath ); } catch { }
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build artifact upload failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	/// <summary>
	/// Gathers the files that make up a runnable build by applying the depot-style
	/// include/exclude globs (see <see cref="IncludeGlobs"/> / <see cref="ExcludeGlobs"/>)
	/// to the <c>game/</c> folder.
	/// </summary>
	private static List<(string EntryPath, string AbsolutePath)> CollectRunnableFiles( string repoRoot )
	{
		var gameRoot = Path.Combine( repoRoot, "game" );
		if ( !Directory.Exists( gameRoot ) )
			return new List<(string, string)>();

		var matcher = new Matcher( StringComparison.OrdinalIgnoreCase );
		matcher.AddIncludePatterns( IncludeGlobs );
		matcher.AddExcludePatterns( ExcludeGlobs );

		var files = new List<(string EntryPath, string AbsolutePath)>();
		foreach ( var absolutePath in matcher.GetResultsInFullPath( gameRoot ) )
		{
			// Entry paths stay relative to the repo root so extracting yields a game/ folder.
			var entry = ToForwardSlash( Path.GetRelativePath( repoRoot, absolutePath ) );
			files.Add( (entry, absolutePath) );
		}

		return files;
	}

	private static void CreateArchive( IReadOnlyCollection<(string EntryPath, string AbsolutePath)> files, string zipPath )
	{
		if ( File.Exists( zipPath ) )
			File.Delete( zipPath );

		Log.Info( $"Creating build archive ({files.Count} file(s))..." );

		using var archive = ZipFile.Open( zipPath, ZipArchiveMode.Create );
		foreach ( var (entryPath, absolutePath) in files )
		{
			archive.CreateEntryFromFile( absolutePath, entryPath, CompressionLevel.Fastest );
		}
	}

	private static bool UploadZip( IAmazonS3 s3, string bucket, string zipPath, string key )
	{
		Log.Info( $"Uploading build artifact to {key}..." );

		try
		{
			// TransferUtility handles multipart upload + retries for large archives.
			using var transfer = new TransferUtility( s3 );
			var request = new TransferUtilityUploadRequest
			{
				BucketName = bucket,
				Key = key,
				FilePath = zipPath,
				ContentType = "application/zip",
				// R2 doesn't implement Streaming SigV4 (the SDK's default for upload bodies):
				// "STREAMING-AWS4-HMAC-SHA256-PAYLOAD not implemented". Disable payload signing
				// (and the default checksum) - TransferUtility propagates both to each part upload.
				DisablePayloadSigning = true,
				DisableDefaultChecksumValidation = true
			};

			transfer.UploadAsync( request ).GetAwaiter().GetResult();
			return true;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to upload build artifact to {key}: {ex.Message}" );
			return false;
		}
	}

	/// <summary>
	/// The immutable commit this build is keyed by. Prefers the CI-provided
	/// commit, falling back to the local git HEAD.
	/// </summary>
	private static string GetCommit()
	{
		var sha = Environment.GetEnvironmentVariable( "GITHUB_SHA" );
		if ( !string.IsNullOrWhiteSpace( sha ) )
			return sha.Trim();

		string head = null;
		Utility.RunProcess( "git", "rev-parse HEAD", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
				head ??= e.Data.Trim();
		} );

		return head;
	}

	private static string ToForwardSlash( string path ) => path.Replace( '\\', '/' );
}
