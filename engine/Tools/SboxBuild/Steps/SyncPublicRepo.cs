using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal record ArtifactFileInfo
{
	[JsonPropertyName( "path" )]
	public string Path { get; init; }

	[JsonPropertyName( "sha256" )]
	public string Sha256 { get; init; }

	[JsonPropertyName( "size" )]
	public long Size { get; init; }
}

internal record ArtifactManifest
{
	[JsonPropertyName( "commit" )]
	public string Commit { get; init; }

	[JsonPropertyName( "timestamp" )]
	public string Timestamp { get; init; }

	[JsonPropertyName( "files" )]
	public List<ArtifactFileInfo> Files { get; init; }
}

internal record PublicSyncResult( string CommitHash, List<ArtifactFileInfo> Artifacts );

/// <summary>
/// Syncs the master branch to the public repository by filtering specific paths
/// </summary>
internal class SyncPublicRepo( string name ) : Step( name )
{
	private const string PUBLIC_REPO = "Facepunch/sbox-public";
	private const string PUBLIC_BRANCH = "master";
	private const int MAX_PARALLEL_UPLOADS = 32;
	protected override ExitCode RunInternal()
	{
		try
		{
			if ( !TryGetRemoteBase( out var remoteBase ) )
			{
				return ExitCode.Failure;
			}

			var syncResult = SyncToPublicRepository( remoteBase );

			if ( syncResult is null || string.IsNullOrEmpty( syncResult.CommitHash ) )
			{
				Log.Error( "Failed to sync to public repository" );
				return ExitCode.Failure;
			}

			if ( !UploadManifest( syncResult.CommitHash, syncResult.Artifacts, remoteBase ) )
			{
				Log.Error( "Failed to upload manifest" );
				return ExitCode.Failure;
			}

			Environment.SetEnvironmentVariable( "PUBLIC_COMMIT_HASH", syncResult.CommitHash );

			Log.Info( $"Successfully synced to public repository. Public commit: {syncResult.CommitHash}" );

			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Public repo sync failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private static bool TryGetRemoteBase( out string remoteBase )
	{
		remoteBase = null;

		var r2AccessKeyId = Environment.GetEnvironmentVariable( "SYNC_R2_ACCESS_KEY_ID" );
		var r2SecretAccessKey = Environment.GetEnvironmentVariable( "SYNC_R2_SECRET_ACCESS_KEY" );
		var r2Bucket = Environment.GetEnvironmentVariable( "SYNC_R2_BUCKET" );
		var r2Endpoint = Environment.GetEnvironmentVariable( "SYNC_R2_ENDPOINT" );

		if ( string.IsNullOrEmpty( r2AccessKeyId ) || string.IsNullOrEmpty( r2SecretAccessKey ) ||
			 string.IsNullOrEmpty( r2Bucket ) || string.IsNullOrEmpty( r2Endpoint ) )
		{
			Log.Error( "R2 credentials not properly configured in environment variables" );
			return false;
		}

		remoteBase = BuildRcloneRemoteBase( r2Bucket, r2AccessKeyId, r2SecretAccessKey, r2Endpoint );
		return true;
	}

	private PublicSyncResult SyncToPublicRepository( string remoteBase )
	{
		var repositoryRoot = Path.GetFullPath( "." );
		var localFilePath = new Uri( repositoryRoot ).AbsoluteUri;
		var filteredRepoPath = Path.Combine( Path.GetTempPath(), $"sbox-filtered-{Guid.NewGuid()}" );

		try
		{
			Log.Info( "Creating clone for filtering..." );

			if ( !Utility.RunProcess( "git", $"clone --shallow-exclude public-history-root \"{localFilePath}\" \"{filteredRepoPath}\"" ) )
			{
				Log.Error( "Failed to create clone" );
				return null;
			}

			Log.Info( "Running git-filter-repo to filter paths..." );

			var filterArgs = BuildFilterRepoArguments();

			var relativeFilteredPath = GetRelativeWorkingDirectory( filteredRepoPath );

			if ( !Utility.RunProcess( "git", filterArgs, relativeFilteredPath ) )
			{
				Log.Error( "Failed to filter repository" );
				return null;
			}

			var lfsTrackedFiles = GetLfsTrackedFiles( relativeFilteredPath );
			if ( lfsTrackedFiles is null )
			{
				return null;
			}

			var uploadedArtifacts = new List<ArtifactFileInfo>();

			if ( lfsTrackedFiles.Count > 0 )
			{
				Log.Info( $"Found {lfsTrackedFiles.Count} LFS tracked files to upload" );
				if ( !TryUploadLfsArtifacts( filteredRepoPath, lfsTrackedFiles, remoteBase, uploadedArtifacts ) )
				{
					return null;
				}

				if ( !RemovePathsFromRepo( lfsTrackedFiles, relativeFilteredPath ) )
				{
					return null;
				}

				Log.Info( $"Removed {lfsTrackedFiles.Count} LFS tracked files from filtered repository" );
			}
			else
			{
				Log.Info( "No LFS tracked files found in filtered repository" );
			}

			if ( !TryUploadBuildArtifacts( repositoryRoot, remoteBase, uploadedArtifacts ) )
			{
				return null;
			}

			Log.Info( "Pushing filtered repository to public..." );

			var token = Environment.GetEnvironmentVariable( "SYNC_GITHUB_TOKEN" );
			if ( string.IsNullOrEmpty( token ) )
			{
				Log.Error( "SYNC_GITHUB_TOKEN environment variable not set" );
				return null;
			}

			var publicUrl = $"https://x-access-token:{token}@github.com/{PUBLIC_REPO}.git";
			if ( !Utility.RunProcess( "git", $"remote add public \"{publicUrl}\"", relativeFilteredPath ) )
			{
				Log.Warning( "Failed to add remote (may already exist), attempting to update URL" );
				if ( !Utility.RunProcess( "git", $"remote set-url public \"{publicUrl}\"", relativeFilteredPath ) )
				{
					Log.Error( "Failed to configure public remote" );
					return null;
				}
			}

			if ( !Utility.RunProcess( "git", $"push public {PUBLIC_BRANCH} --force", relativeFilteredPath ) )
			{
				Log.Error( "Failed to push to public repository" );
				return null;
			}

			string publicCommitHash = null;
			if ( !Utility.RunProcess( "git", "rev-parse HEAD", relativeFilteredPath, onDataReceived: ( _, e ) =>
			{
				if ( !string.IsNullOrWhiteSpace( e.Data ) )
				{
					publicCommitHash ??= e.Data.Trim();
				}
			} ) )
			{
				Log.Error( "Failed to retrieve public commit hash" );
				return null;
			}

			if ( string.IsNullOrWhiteSpace( publicCommitHash ) )
			{
				Log.Error( "Public commit hash was empty" );
				return null;
			}

			Log.Info( $"Public repository commit hash: {publicCommitHash}" );

			return new PublicSyncResult( publicCommitHash, uploadedArtifacts );
		}
		finally
		{
			TryDeleteDirectory( filteredRepoPath );
		}
	}

	private static string BuildFilterRepoArguments()
	{
		var filterArgs = new StringBuilder();
		filterArgs.Append( "filter-repo --force " );

		var pathsToKeep = new[]
		{
			"engine",
			"game",
			".editorconfig",
			".gitattributes",
			"public/"
		};

		foreach ( var path in pathsToKeep )
		{
			filterArgs.Append( $"--path {path} " );
		}

		var renames = new Dictionary<string, string>
		{
			{ "public/.gitignore", ".gitignore" },
			{ "public/README.md", "README.md" },
			{ "public/LICENSE.md", "LICENSE.md" },
			{ "public/CONTRIBUTING.md", "CONTRIBUTING.md" },
			{ "public/Bootstrap.bat", "Bootstrap.bat" }
		};

		foreach ( var rename in renames )
		{
			filterArgs.Append( $"--path-rename {rename.Key}:{rename.Value} " );
		}

		// Reference the original commit, and mark our baseline commit
		var commitCallback = """
			if not commit.parents:
				commit.message = b'Open source release\n\nThis commit imports the C# engine code and game files, excluding C++ source code.'
				commit.author_name = b's&box team'
				commit.author_email = b'sboxbot@facepunch.com'
				commit.committer_name = b's&box team'
				commit.committer_email = b'sboxbot@facepunch.com'
				commit.message += b'\n\n[Source-Commit: ' + commit.original_id + b']\n'
			""";

		const string filenameCallback = "return filename if (filename is None or (not filename.endswith(b'.pdb') and (b'game/core/shaders/' not in filename or (lambda base: base.startswith(b'vr_') or base.endswith(b'shader_c') or base in {b'common.fxc', b'common_samplers.fxc', b'descriptor_set_support.fxc', b'system.fxc', b'tiled_culling.hlsl'})(filename.split(b'/')[-1])))) else None";
		filterArgs.Append( $"--filename-callback \"{filenameCallback}\" " );
		filterArgs.Append( $"--commit-callback \"{commitCallback}\"" );

		return filterArgs.ToString();
	}

	private static List<string> GetLfsTrackedFiles( string relativeRepoPath )
	{
		var trackedFiles = new List<string>();
		var uniqueFiles = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		if ( !Utility.RunProcess( "git", "lfs ls-files --name-only", relativeRepoPath, onDataReceived: ( _, e ) =>
		{
			if ( string.IsNullOrWhiteSpace( e.Data ) )
			{
				return;
			}

			var path = e.Data.Trim();
			if ( uniqueFiles.Add( path ) )
			{
				trackedFiles.Add( path );
			}
		} ) )
		{
			Log.Error( "Failed to list LFS tracked files" );
			return null;
		}

		trackedFiles.Sort( StringComparer.OrdinalIgnoreCase );
		return trackedFiles;
	}

	private static bool TryUploadLfsArtifacts( string repoRoot, IReadOnlyCollection<string> lfsPaths, string remoteBase, List<ArtifactFileInfo> artifacts )
	{
		if ( artifacts is null )
		{
			throw new ArgumentNullException( nameof( artifacts ) );
		}

		var uniqueUploads = new Dictionary<string, (string AbsolutePath, ArtifactFileInfo Artifact)>( StringComparer.OrdinalIgnoreCase );

		foreach ( var lfsPath in lfsPaths )
		{
			var normalizedPath = NormalizeRepoPath( lfsPath );

			if ( string.Equals( Path.GetExtension( normalizedPath ), ".pdb", StringComparison.OrdinalIgnoreCase ) )
			{
				Log.Info( $"Skipping LFS artifact with excluded extension: {lfsPath}" );
				continue;
			}

			var absolutePath = Path.Combine( repoRoot, normalizedPath );

			if ( !File.Exists( absolutePath ) )
			{
				Log.Error( $"LFS file not found on disk: {lfsPath}" );
				return false;
			}

			var fileInfo = new FileInfo( absolutePath );
			var sha256 = Utility.CalculateSha256( absolutePath );

			var artifact = new ArtifactFileInfo
			{
				Path = lfsPath.Replace( '\\', '/' ),
				Sha256 = sha256,
				Size = fileInfo.Length
			};

			artifacts.Add( artifact );

			if ( !uniqueUploads.TryAdd( sha256, (absolutePath, artifact) ) )
			{
				Log.Info( $"Skipping upload for {lfsPath} (already uploaded hash {sha256})" );
			}
		}

		if ( uniqueUploads.Count == 0 )
		{
			Log.Info( "No unique LFS artifacts to upload" );
			return true;
		}

		var maxParallelUploads = Math.Max( 1, Math.Min( MAX_PARALLEL_UPLOADS, Environment.ProcessorCount ) );
		Log.Info( $"Uploading {uniqueUploads.Count} unique LFS artifacts (up to {maxParallelUploads} concurrent uploads)..." );

		var failedUploads = new ConcurrentBag<string>();
		Parallel.ForEach( uniqueUploads, new ParallelOptions { MaxDegreeOfParallelism = maxParallelUploads }, kvp =>
		{
			var (absolutePath, artifact) = kvp.Value;
			if ( !UploadArtifactFile( absolutePath, artifact, remoteBase ) )
			{
				Log.Error( $"Failed to upload LFS artifact: {artifact.Path}" );
				failedUploads.Add( artifact.Path );
			}
		} );

		if ( !failedUploads.IsEmpty )
		{
			Log.Error( $"Failed to upload {failedUploads.Count} artifact(s)" );
			return false;
		}

		Log.Info( $"Uploaded {uniqueUploads.Count} unique LFS artifacts" );
		return true;
	}

	private static bool TryUploadBuildArtifacts( string repositoryRoot, string remoteBase, List<ArtifactFileInfo> artifacts )
	{
		var buildArtifactsRoot = Path.Combine( repositoryRoot, "game", "bin", "win64" );
		if ( !Directory.Exists( buildArtifactsRoot ) )
		{
			Log.Info( $"Build artifacts directory not found, skipping upload: {buildArtifactsRoot}" );
			return true;
		}

		var filesToUpload = new List<string>();
		try
		{
			foreach ( var filePath in Directory.EnumerateFiles( buildArtifactsRoot, "*", SearchOption.AllDirectories ) )
			{
				if ( string.Equals( Path.GetExtension( filePath ), ".pdb", StringComparison.OrdinalIgnoreCase ) )
				{
					continue;
				}

				var relativeToBuild = Path.GetRelativePath( buildArtifactsRoot, filePath );
				var segments = relativeToBuild.Split( new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, System.StringSplitOptions.RemoveEmptyEntries );
				if ( segments.Length > 0 && string.Equals( segments[0], "managed", StringComparison.OrdinalIgnoreCase ) )
				{
					continue;
				}

				filesToUpload.Add( filePath );
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to enumerate build artifacts: {ex.Message}" );
			return false;
		}

		if ( filesToUpload.Count == 0 )
		{
			Log.Info( "No build artifacts found to upload" );
			return true;
		}

		Log.Info( $"Found {filesToUpload.Count} build artifact(s) to upload" );

		var uniqueUploads = new Dictionary<string, (string AbsolutePath, ArtifactFileInfo Artifact)>( StringComparer.OrdinalIgnoreCase );

		foreach ( var absolutePath in filesToUpload )
		{
			var fileInfo = new FileInfo( absolutePath );
			var sha256 = Utility.CalculateSha256( absolutePath );
			var repoRelativePath = Path.GetRelativePath( repositoryRoot, absolutePath ).Replace( '\\', '/' );

			var artifact = new ArtifactFileInfo
			{
				Path = repoRelativePath,
				Sha256 = sha256,
				Size = fileInfo.Length
			};

			artifacts.Add( artifact );

			if ( !uniqueUploads.TryAdd( sha256, (absolutePath, artifact) ) )
			{
				Log.Info( $"Skipping upload for {repoRelativePath} (already uploaded hash {sha256})" );
			}
		}

		var maxParallelUploads = Math.Max( 1, Math.Min( MAX_PARALLEL_UPLOADS, Environment.ProcessorCount ) );
		Log.Info( $"Uploading {uniqueUploads.Count} unique build artifacts (up to {maxParallelUploads} concurrent uploads)..." );

		var failedUploads = new ConcurrentBag<string>();
		Parallel.ForEach( uniqueUploads, new ParallelOptions { MaxDegreeOfParallelism = maxParallelUploads }, kvp =>
		{
			var (absolutePath, artifact) = kvp.Value;
			if ( !UploadArtifactFile( absolutePath, artifact, remoteBase ) )
			{
				Log.Error( $"Failed to upload build artifact: {artifact.Path}" );
				failedUploads.Add( artifact.Path );
			}
		} );

		if ( !failedUploads.IsEmpty )
		{
			Log.Error( $"Failed to upload {failedUploads.Count} build artifact(s)" );
			return false;
		}

		Log.Info( $"Uploaded {uniqueUploads.Count} unique build artifacts" );
		return true;
	}

	private static bool RemovePathsFromRepo( IReadOnlyCollection<string> pathsToRemove, string relativeRepoPath )
	{
		if ( pathsToRemove.Count == 0 )
		{
			return true;
		}

		var tempFile = Path.GetTempFileName();

		try
		{
			var normalizedPaths = new List<string>( pathsToRemove.Count );
			foreach ( var path in pathsToRemove )
			{
				normalizedPaths.Add( path.Replace( '\\', '/' ) );
			}

			File.WriteAllLines( tempFile, normalizedPaths );
			var args = $"filter-repo --force --paths-from-file \"{tempFile}\" --invert-paths";
			if ( !Utility.RunProcess( "git", args, relativeRepoPath ) )
			{
				Log.Error( "Failed to remove LFS tracked files from repository" );
				return false;
			}

			return true;
		}
		finally
		{
			if ( File.Exists( tempFile ) )
			{
				File.Delete( tempFile );
			}
		}
	}

	private static string NormalizeRepoPath( string repoRelativePath )
	{
		return repoRelativePath.Replace( '/', Path.DirectorySeparatorChar );
	}

	private static string BuildRcloneRemoteBase( string bucket, string accessKeyId, string secretAccessKey, string endpoint )
	{
		return $":s3,bucket={bucket},provider=Cloudflare,access_key_id={accessKeyId},secret_access_key={secretAccessKey},endpoint='{endpoint}':";
	}

	private static bool UploadArtifactFile( string localPath, ArtifactFileInfo artifact, string remoteBase )
	{
		var remotePath = $"{remoteBase}/artifacts/{artifact.Sha256}";
		var sizeLabel = $" ({Utility.FormatSize( artifact.Size )})";
		Log.Info( $"Uploading {artifact.Sha256}{sizeLabel}..." );
		return Utility.RunProcess( "rclone", $"copyto \"{localPath}\" \"{remotePath}\" --ignore-existing -q", timeoutMs: 600000 );
	}

	private static bool UploadManifest( string commitHash, IReadOnlyList<ArtifactFileInfo> artifacts, string remoteBase )
	{
		var files = artifacts is null ? new List<ArtifactFileInfo>() : new List<ArtifactFileInfo>( artifacts );
		var manifest = new ArtifactManifest
		{
			Commit = commitHash,
			Timestamp = DateTime.UtcNow.ToString( "o" ),
			Files = files
		};

		var manifestJson = JsonSerializer.Serialize( manifest, new JsonSerializerOptions
		{
			WriteIndented = true
		} );

		var manifestPath = Path.Combine( Path.GetTempPath(), $"{commitHash}.json" );
		File.WriteAllText( manifestPath, manifestJson );

		try
		{
			Log.Info( $"Uploading manifest: {commitHash}.json with {manifest.Files.Count} files" );
			var remotePath = $"{remoteBase}/manifests/{commitHash}.json";
			if ( !Utility.RunProcess( "rclone", $"copyto \"{manifestPath}\" \"{remotePath}\"", timeoutMs: 60000 ) )
			{
				return false;
			}
		}
		finally
		{
			if ( File.Exists( manifestPath ) )
			{
				File.Delete( manifestPath );
			}
		}

		return true;
	}

	private static string GetRelativeWorkingDirectory( string absolutePath )
	{
		var repoRoot = Directory.GetCurrentDirectory();
		var relativePath = Path.GetRelativePath( repoRoot, absolutePath );
		return string.IsNullOrEmpty( relativePath ) ? "." : relativePath;
	}

	private static void TryDeleteDirectory( string path )
	{
		if ( !Directory.Exists( path ) )
		{
			return;
		}

		try
		{
			Log.Info( "Cleaning up temporary filtered repository..." );
			Directory.Delete( path, true );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to clean up temporary directory: {ex.Message}" );
		}
	}
}
