using Microsoft.Extensions.FileSystemGlobbing;
using System.Collections.Concurrent;
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

/// <summary>
/// Syncs the master branch to the public repository by filtering specific paths
/// </summary>
internal class SyncPublicRepo( string name, bool dryRun = false ) : Step( name )
{
	private const string PUBLIC_REPO = "Facepunch/sbox-public";
	private const string PUBLIC_BRANCH = "master";
	private const string SHALLOW_EXCLUDE_TAG = "public-history-root";
	private const int MAX_PARALLEL_UPLOADS = 32;

	protected override ExitCode RunInternal()
	{
		try
		{
			var success = SyncToPublicRepository();
			if ( !success )
			{
				return ExitCode.Failure;
			}

			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Public repo sync failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private static readonly string[] RepoFilterPathIncludeGlobs =
	{
		"engine/**",
		"game/**",
		".editorconfig",
		"public/**"
	};

	private static readonly string[] RepoFilterPathExcludeGlobs =
	{
		"**/*.pdb",
		"game/core/shaders/**"
	};

	private static readonly string[] RepoFilterShaderWhitelistGlobs =
	{
		"game/core/shaders/**/vr_*",
		"game/core/shaders/**/*.shader_c",
		"game/core/shaders/common.fxc",
		"game/core/shaders/common_samplers.fxc",
		"game/core/shaders/descriptor_set_support.fxc",
		"game/core/shaders/system.fxc",
		"game/core/shaders/tiled_culling.hlsl",
		"game/core/shaders/skinning_cs.shader",
		"game/core/shaders/yuv_resolve.shader",
		"game/core/shaders/sbox_pixel.fxc",
		"game/core/shaders/sbox_shared.fxc",
		"game/core/shaders/sbox_vertex.fxc"
	};

	private static readonly Dictionary<string, string> RepoFilterPathRenames = new()
	{
		{ "public/.gitignore", ".gitignore" },
		{ "public/.gitattributes", ".gitattributes" },
		{ "public/.github/workflows/pull_request.yml", ".github/workflows/pull_request.yml" },
		{ "public/.github/workflows/pull_request_checks.yml", ".github/workflows/pull_request_checks.yml" },
		{ "public/.github/workflows/pull_request_formatting.yml", ".github/workflows/pull_request_formatting.yml" },
		{ "public/README.md", "README.md" },
		{ "public/LICENSE.md", "LICENSE.md" },
		{ "public/CONTRIBUTING.md", "CONTRIBUTING.md" },
		{ "public/Bootstrap.bat", "Bootstrap.bat" }
	};

	private static Matcher RepoFileFilter()
	{
		if ( _matcher is not null )
		{
			return _matcher;
		}

		// Ordered since we first include everything, then exclude, then re-include specific files
		_matcher = new Matcher( StringComparison.OrdinalIgnoreCase, preserveFilterOrder: true );

		_matcher.AddIncludePatterns( RepoFilterPathIncludeGlobs );

		_matcher.AddExcludePatterns( RepoFilterPathExcludeGlobs );

		_matcher.AddIncludePatterns( RepoFilterShaderWhitelistGlobs );

		return _matcher;
	}

	private static Matcher _matcher = null;

	private bool SyncToPublicRepository()
	{
		string remoteBase = null;
		if ( !dryRun )
		{
			remoteBase = GetR2Base();
			if ( string.IsNullOrEmpty( remoteBase ) )
			{
				return false;
			}
		}
		else
		{
			Log.Info( "Dry run enabled: skipping R2 uploads and public push" );
		}

		var repositoryRoot = Path.GetFullPath( "." );
		var filteredRepoPath = CreateShallowClone( repositoryRoot );
		if ( string.IsNullOrEmpty( filteredRepoPath ) )
		{
			return false;
		}

		try
		{
			var relativeFilteredPath = GetRelativeWorkingDirectory( filteredRepoPath );
			var uploadedArtifacts = new HashSet<ArtifactFileInfo>();
			var uploadedArtifactHashes = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

			// Upload windows binaries
			if ( !TryUploadBuildArtifacts( repositoryRoot, remoteBase, "win64", dryRun, ref uploadedArtifacts, uploadedArtifactHashes ) )
			{
				return false;
			}

			// Upload linux binaries
			if ( !TryUploadBuildArtifacts( repositoryRoot, remoteBase, "linuxsteamrt64", dryRun, ref uploadedArtifacts, uploadedArtifactHashes ) )
			{
				return false;
			}

			//
			// Start working with the shallow clone
			//

			// Make certain we dont have any stray files in our shallow clone
			if ( !CleanIgnoredFiles( relativeFilteredPath ) )
			{
				return false;
			}

			// Upload LFS tracked files in the HEAD of the shallow clone
			var shallowLfsPaths = GetCurrentLfsFiles( relativeFilteredPath );
			if ( shallowLfsPaths is null )
			{
				return false;
			}

			if ( !TryUploadLfsArtifacts( filteredRepoPath, shallowLfsPaths, remoteBase, dryRun, ref uploadedArtifacts, uploadedArtifactHashes ) )
			{
				return false;
			}

			// Make sure we filter out lfs files that are in the history as well
			var allLfsPaths = GetAllPublicLfsFiles( relativeFilteredPath );
			if ( allLfsPaths is null )
			{
				return false;
			}

			// Run git-filter-repo to filter out unwanted paths
			if ( !RunFilterRepo( relativeFilteredPath, allLfsPaths ) )
			{
				return false;
			}

			var publicCommitHash = dryRun
				? "000000"
				: PushToPublicRepository( relativeFilteredPath );
			if ( string.IsNullOrEmpty( publicCommitHash ) )
			{
				return false;
			}

			if ( dryRun )
			{
				var dryRunTotalBytes = CalculateArtifactTotalSize( uploadedArtifacts );
				Log.Info( $"Dry run filtered repository commit hash: {publicCommitHash}" );
				Log.Info( $"Dry run: total artifact payload {Utility.FormatSize( dryRunTotalBytes )}" );
				WriteDryRunOutputs( publicCommitHash, uploadedArtifacts );
				return true;
			}

			if ( !UploadManifest( publicCommitHash, uploadedArtifacts, remoteBase ) )
			{
				return false;
			}

			var uploadedTotalBytes = CalculateArtifactTotalSize( uploadedArtifacts );
			Log.Info( $"Total artifact data uploaded: {Utility.FormatSize( uploadedTotalBytes )}" );

			return true;
		}
		finally
		{
			try
			{
				Thread.Sleep( 500 ); // Give any pending file handles a moment to close
				Log.Info( "Cleaning up temporary filtered repository..." );
				Directory.Delete( filteredRepoPath, true );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to clean up temporary directory: {ex.Message}" );
			}
		}
	}

	private static string CreateShallowClone( string repositoryRoot )
	{
		var localFilePath = new Uri( repositoryRoot ).AbsoluteUri;
		var filteredRepoPath = Path.Combine( Path.GetTempPath(), $"sbox-filtered-{Guid.NewGuid()}" );

		Log.Info( "Creating clone for filtering..." );

		if ( Utility.RunProcess( "git", $"clone --shallow-exclude {SHALLOW_EXCLUDE_TAG} \"{localFilePath}\" \"{filteredRepoPath}\"" ) )
		{
			return filteredRepoPath;
		}

		Log.Error( "Failed to create clone" );
		return null;
	}

	private static bool CleanIgnoredFiles( string relativeRepoPath )
	{
		Log.Info( "Removing ignored files from filtered repository..." );
		if ( Utility.RunProcess( "git", "clean -f -x -d", relativeRepoPath ) )
		{
			return true;
		}

		Log.Error( "Failed to remove ignored files from filtered repository" );
		return false;
	}

	private static bool TryUploadBuildArtifacts( string repositoryRoot, string remoteBase, string platform, bool skipUpload, ref HashSet<ArtifactFileInfo> artifacts, HashSet<string> uploadedHashes )
	{
		var buildArtifactsRoot = Path.Combine( repositoryRoot, "game", "bin", platform );
		if ( !Directory.Exists( buildArtifactsRoot ) )
		{
			Log.Info( $"Build artifacts directory not found, skipping upload: {buildArtifactsRoot}" );
			return true;
		}

		// Inline matcher: include everything, exclude managed root folder and pdbs
		var matcher = new Matcher( StringComparison.OrdinalIgnoreCase, preserveFilterOrder: true );
		matcher.AddInclude( "**/*" );
		matcher.AddExclude( "managed/**" );
		matcher.AddExclude( "**/*.pdb" );

		var filesToUpload = matcher
			.GetResultsInFullPath( buildArtifactsRoot )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );

		var compiledAssets = GetCompiledAssetFiles( repositoryRoot );
		var newCompiledAssets = 0;
		foreach ( var asset in compiledAssets )
		{
			if ( filesToUpload.Add( asset ) )
			{
				newCompiledAssets++;
			}
		}

		if ( filesToUpload.Count == 0 )
		{
			Log.Info( "No build artifacts found to upload" );
			return true;
		}

		var compiledAssetSuffix = newCompiledAssets > 0 ? $" (including {newCompiledAssets} compiled asset(s))" : string.Empty;
		Log.Info( $"Found {filesToUpload.Count} build artifact(s) to upload{compiledAssetSuffix}" );

		var candidates = filesToUpload
			.Select( path =>
			{
				var repoRelativePath = Path.GetRelativePath( repositoryRoot, path );
				return (RepoPath: ToForwardSlash( repoRelativePath ), AbsolutePath: path);
			} )
			.ToList();

		return TryUploadArtifacts( candidates, remoteBase, artifacts, uploadedHashes, "build", skipUpload );
	}

	private static IReadOnlyCollection<string> GetCompiledAssetFiles( string repositoryRoot )
	{
		var gameRoot = Path.Combine( repositoryRoot, "game" );
		if ( !Directory.Exists( gameRoot ) )
		{
			return Array.Empty<string>();
		}

		var searchRoots = new[]
		{
			"addons",
			"core",
			"config",
			"editor",
			"mount",
			"samples",
			"templates"
		};

		var compiledAssets = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var directory in searchRoots )
		{
			var fullPath = Path.Combine( gameRoot, directory );
			if ( !Directory.Exists( fullPath ) )
			{
				continue;
			}

			foreach ( var path in Directory.EnumerateFiles( fullPath, "*.*_c", SearchOption.AllDirectories ) )
			{
				compiledAssets.Add( Path.GetFullPath( path ) );
			}
		}

		return compiledAssets;
	}

	private static bool TryUploadLfsArtifacts( string repoRoot, IReadOnlyCollection<string> lfsPaths, string remoteBase, bool skipUpload, ref HashSet<ArtifactFileInfo> artifacts, HashSet<string> uploadedHashes )
	{
		if ( lfsPaths.Count == 0 )
		{
			return true;
		}

		var candidates = lfsPaths
			.Where( p => RepoFileFilter().Match( p ).HasMatches )
			.Select( path => (RepoPath: path, AbsolutePath: Path.Combine( repoRoot, path.Replace( '/', Path.DirectorySeparatorChar ) )) )
			.ToList();

		return TryUploadArtifacts( candidates, remoteBase, artifacts, uploadedHashes, "LFS", skipUpload );
	}

	private bool RunFilterRepo( string relativeRepoPath, IReadOnlyCollection<string> lfsPaths )
	{
		Log.Info( "Running git-filter-repo to filter paths..." );

		var scriptPath = Path.Combine( Directory.GetCurrentDirectory(), "engine", "Tools", "SboxBuild", "Steps", "SyncPublicRepoFilter.py" );
		if ( !File.Exists( scriptPath ) )
		{
			Log.Error( $"Filter script not found: {scriptPath}" );
			return false;
		}

		var config = new FilterConfigData
		{
			IncludeGlobs = RepoFilterPathIncludeGlobs,
			ExcludeGlobs = RepoFilterPathExcludeGlobs,
			WhitelistedShaders = RepoFilterShaderWhitelistGlobs,
			PathRenames = RepoFilterPathRenames.ToDictionary( pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase ),
			LfsPaths = lfsPaths
				.Select( ToForwardSlash )
				.Distinct( StringComparer.OrdinalIgnoreCase )
				.ToList()
		};

		string configPath = null;
		try
		{
			configPath = Path.Combine( Path.GetTempPath(), $"sbox-filter-config-{Guid.NewGuid():N}.json" );
			var configJson = JsonSerializer.Serialize( config );
			File.WriteAllText( configPath, configJson );

			var pythonExecutable = GetPythonExecutable();
			var arguments = $"\"{scriptPath}\" --config \"{configPath}\"";

			if ( Utility.RunProcess( pythonExecutable, arguments, relativeRepoPath ) )
			{
				return true;
			}

			Log.Error( "Failed to filter repository" );
			return false;
		}
		finally
		{
			if ( configPath is not null && File.Exists( configPath ) )
			{
				File.Delete( configPath );
			}
		}
	}

	private string PushToPublicRepository( string relativeRepoPath )
	{
		Log.Info( "Pushing filtered repository to public..." );

		var token = Environment.GetEnvironmentVariable( "SYNC_GITHUB_TOKEN" );
		if ( string.IsNullOrEmpty( token ) )
		{
			Log.Error( "SYNC_GITHUB_TOKEN environment variable not set" );
			return null;
		}

		var publicUrl = $"https://x-access-token:{token}@github.com/{PUBLIC_REPO}.git";
		if ( !Utility.RunProcess( "git", $"remote add public \"{publicUrl}\"", relativeRepoPath ) )
		{
			Log.Warning( "Failed to add remote (may already exist), attempting to update URL" );
			if ( !Utility.RunProcess( "git", $"remote set-url public \"{publicUrl}\"", relativeRepoPath ) )
			{
				Log.Error( "Failed to configure public remote" );
				return null;
			}
		}

		if ( !Utility.RunProcess( "git", $"push public {PUBLIC_BRANCH}", relativeRepoPath ) )
		{
			Log.Error( "Failed to push to public repository" );
			return null;
		}

		string publicCommitHash = null;
		if ( !Utility.RunProcess( "git", "rev-parse HEAD", relativeRepoPath, onDataReceived: ( _, e ) =>
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

		return publicCommitHash;
	}

	private static HashSet<string> GetCurrentLfsFiles( string relativeRepoPath )
	{
		var trackedFiles = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		if ( !Utility.RunProcess( "git", "lfs ls-files --name-only", relativeRepoPath, onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
			{
				trackedFiles.Add( ToForwardSlash( e.Data.Trim() ) );
			}
		} ) )
		{
			Log.Error( "Failed to list LFS tracked files" );
			return null;
		}

		return trackedFiles;
	}

	private static HashSet<string> GetAllPublicLfsFiles( string relativeRepoPath )
	{
		var trackedFiles = GetCurrentLfsFiles( relativeRepoPath );

		if ( !Utility.RunProcess( "git", "lfs ls-files --all --deleted --name-only", relativeRepoPath, onDataReceived: ( _, e ) =>
		{
			if ( string.IsNullOrWhiteSpace( e.Data ) )
			{
				return;
			}

			trackedFiles.Add( ToForwardSlash( e.Data.Trim() ) );
		} ) )
		{
			Log.Error( "Failed to list historical LFS tracked files" );
			return null;
		}

		return trackedFiles;
	}

	private void WriteDryRunOutputs( string commitHash, IEnumerable<ArtifactFileInfo> artifacts )
	{
		var workingDirectory = Directory.GetCurrentDirectory();
		var manifestPath = Path.Combine( workingDirectory, "public-sync-manifest.dryrun.json" );
		var manifest = new ArtifactManifest
		{
			Commit = commitHash,
			Timestamp = DateTime.UtcNow.ToString( "o" ),
			Files = artifacts is null ? new List<ArtifactFileInfo>() : new List<ArtifactFileInfo>( artifacts )
		};

		var manifestJson = JsonSerializer.Serialize( manifest, new JsonSerializerOptions { WriteIndented = true } );
		File.WriteAllText( manifestPath, manifestJson );

		Log.Info( $"Dry run manifest written to {manifestPath}" );
	}

	// Additional safe guard, stuff we absolutely do not want to upload
	private static readonly HashSet<string> ForbiddenArtifactExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".pdb",
		".cpp",
		".h"
	};

	private static bool TryUploadArtifacts( IReadOnlyCollection<(string RepoPath, string AbsolutePath)> candidates, string remoteBase, HashSet<ArtifactFileInfo> artifacts, HashSet<string> uploadedHashes, string artifactLabel, bool skipUpload )
	{
		if ( candidates.Count == 0 )
		{
			Log.Info( $"No {artifactLabel} artifacts found to upload" );
			return true;
		}

		var uniqueUploads = new List<(string AbsolutePath, ArtifactFileInfo Artifact)>();
		var duplicateManifestCount = 0;
		var duplicateUploadCount = 0;

		foreach ( var (repoPath, absolutePath) in candidates )
		{
			var repoPathNormalized = ToForwardSlash( repoPath );

			if ( !File.Exists( absolutePath ) )
			{
				Log.Error( $"Artifact not found on disk: {repoPathNormalized}" );
				return false;
			}

			var extension = Path.GetExtension( absolutePath );
			if ( !string.IsNullOrEmpty( extension ) && ForbiddenArtifactExtensions.Contains( extension ) )
			{
				Log.Error( $"Encountered forbidden artifact extension ({extension}): {repoPathNormalized}" );
				return false;
			}

			var fileInfo = new FileInfo( absolutePath );
			var sha256 = Utility.CalculateSha256( absolutePath );
			var artifact = new ArtifactFileInfo
			{
				Path = repoPathNormalized,
				Sha256 = sha256,
				Size = fileInfo.Length
			};

			if ( !artifacts.Add( artifact ) )
			{
				duplicateManifestCount++;
				continue;
			}

			if ( !uploadedHashes.Add( sha256 ) )
			{
				duplicateUploadCount++;
				continue;
			}

			uniqueUploads.Add( (absolutePath, artifact) );
		}

		if ( uniqueUploads.Count == 0 )
		{
			Log.Info( $"No unique {artifactLabel} artifacts to upload" );
			return true;
		}

		if ( duplicateManifestCount > 0 )
		{
			Log.Info( $"Skipped {duplicateManifestCount} duplicate manifest entries for {artifactLabel} artifacts" );
		}

		if ( duplicateUploadCount > 0 )
		{
			Log.Info( $"Skipped {duplicateUploadCount} duplicate upload candidates for {artifactLabel} artifacts" );
		}

		long batchBytes = 0;
		foreach ( var upload in uniqueUploads )
		{
			batchBytes += upload.Artifact.Size;
		}

		if ( skipUpload )
		{
			Log.Info( $"Dry run skipping upload for {uniqueUploads.Count} unique {artifactLabel} artifacts ({Utility.FormatSize( batchBytes )})" );
			return true;
		}

		var maxParallelUploads = Math.Max( 1, Math.Min( MAX_PARALLEL_UPLOADS, Environment.ProcessorCount ) );
		Log.Info( $"Uploading {uniqueUploads.Count} unique {artifactLabel} artifacts (up to {maxParallelUploads} concurrent uploads)..." );

		var failedUploads = new ConcurrentBag<string>();
		Parallel.ForEach( uniqueUploads, new ParallelOptions { MaxDegreeOfParallelism = maxParallelUploads }, item =>
		{
			var (absolutePath, artifact) = item;
			if ( !UploadArtifactFile( absolutePath, artifact, remoteBase ) )
			{
				Log.Error( $"Failed to upload {artifactLabel} artifact: {artifact.Path}" );
				failedUploads.Add( artifact.Path );
			}
		} );

		if ( !failedUploads.IsEmpty )
		{
			Log.Error( $"Failed to upload {failedUploads.Count} {artifactLabel} artifact(s)" );
			return false;
		}

		Log.Info( $"Uploaded {uniqueUploads.Count} unique {artifactLabel} artifacts ({Utility.FormatSize( batchBytes )})" );

		return true;
	}

	private static bool UploadArtifactFile( string localPath, ArtifactFileInfo artifact, string remoteBase )
	{
		var remotePath = $"{remoteBase}/artifacts/{artifact.Sha256}";
		var sizeLabel = $" ({Utility.FormatSize( artifact.Size )})";
		Log.Info( $"Uploading {artifact.Sha256}{sizeLabel}..." );
		return Utility.RunProcess( "rclone", $"copyto \"{localPath}\" \"{remotePath}\" --ignore-existing -q", timeoutMs: 600000 );
	}

	private static bool UploadManifest( string commitHash, IEnumerable<ArtifactFileInfo> artifacts, string remoteBase )
	{
		var files = artifacts is null ? new List<ArtifactFileInfo>() : new List<ArtifactFileInfo>( artifacts );
		var manifest = new ArtifactManifest
		{
			Commit = commitHash,
			Timestamp = DateTime.UtcNow.ToString( "o" ),
			Files = files
		};

		var manifestJson = JsonSerializer.Serialize( manifest, new JsonSerializerOptions { WriteIndented = true } );

		var manifestPath = Path.Combine( Path.GetTempPath(), $"{commitHash}.json" );
		File.WriteAllText( manifestPath, manifestJson );

		try
		{
			Log.Info( $"Uploading manifest: {commitHash}.json with {manifest.Files.Count} files" );
			var remotePath = $"{remoteBase}/manifests/{commitHash}.json";
			if ( !Utility.RunProcess( "rclone", $"copyto \"{manifestPath}\" \"{remotePath}\"", timeoutMs: 60000 ) )
			{
				Log.Error( "Failed to upload manifest file" );
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

	private static long CalculateArtifactTotalSize( IEnumerable<ArtifactFileInfo> artifacts )
	{
		if ( artifacts is null )
		{
			return 0;
		}

		long total = 0;
		foreach ( var artifact in artifacts )
		{
			if ( artifact is null )
			{
				continue;
			}

			total += artifact.Size;
		}

		return total;
	}

	private static string GetRelativeWorkingDirectory( string absolutePath )
	{
		var repoRoot = Directory.GetCurrentDirectory();
		var relativePath = Path.GetRelativePath( repoRoot, absolutePath );
		return string.IsNullOrEmpty( relativePath ) ? "." : relativePath;
	}

	private static string GetR2Base()
	{
		var r2AccessKeyId = Environment.GetEnvironmentVariable( "SYNC_R2_ACCESS_KEY_ID" );
		var r2SecretAccessKey = Environment.GetEnvironmentVariable( "SYNC_R2_SECRET_ACCESS_KEY" );
		var r2Bucket = Environment.GetEnvironmentVariable( "SYNC_R2_BUCKET" );
		var r2Endpoint = Environment.GetEnvironmentVariable( "SYNC_R2_ENDPOINT" );

		if ( string.IsNullOrEmpty( r2AccessKeyId ) || string.IsNullOrEmpty( r2SecretAccessKey ) ||
			 string.IsNullOrEmpty( r2Bucket ) || string.IsNullOrEmpty( r2Endpoint ) )
		{
			Log.Error( "R2 credentials not properly configured in environment variables" );
			return null;
		}

		return $":s3,bucket={r2Bucket},provider=Cloudflare,access_key_id={r2AccessKeyId},secret_access_key={r2SecretAccessKey},endpoint='{r2Endpoint}':";
	}

	private static string ToForwardSlash( string path )
	{
		return path.Replace( '\\', '/' );
	}

	private static string GetPythonExecutable()
	{
		var overridePath = Environment.GetEnvironmentVariable( "PYTHON" );
		if ( !string.IsNullOrWhiteSpace( overridePath ) )
		{
			return overridePath;
		}

		if ( OperatingSystem.IsWindows() )
		{
			return "python";
		}

		return "python3";
	}

	private sealed class FilterConfigData
	{
		[JsonPropertyName( "include_globs" )]
		public string[] IncludeGlobs { get; init; }

		[JsonPropertyName( "exclude_globs" )]
		public string[] ExcludeGlobs { get; init; }

		[JsonPropertyName( "whitelisted_shaders" )]
		public string[] WhitelistedShaders { get; init; }

		[JsonPropertyName( "path_renames" )]
		public Dictionary<string, string> PathRenames { get; init; }

		[JsonPropertyName( "lfs_paths" )]
		public List<string> LfsPaths { get; init; }
	}
}
