using Microsoft.Extensions.FileSystemGlobbing;
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
/// Syncs the master branch and tags to the public repository by filtering specific paths
/// </summary>
internal class SyncPublicRepo( bool dryRun = false )
{
	private const string PUBLIC_REPO = "Facepunch/sbox-public";
	private const string PUBLIC_BRANCH = "master";
	private const string SHALLOW_EXCLUDE_TAG = "public-history-root";
	private const int MAX_PARALLEL_UPLOADS = 32;
	private const int PREVIOUS_MANIFEST_SEARCH = 20;

	internal ExitCode Run()
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

		// Private legacy shader sources, matching depot.game.content.vdf and UploadBuildArtifacts.
		"game/core/shaders/ambient_cube.fxc",
		"game/core/shaders/baked_lighting_constants.fxc",
		"game/core/shaders/bump_strength.fxc",
		"game/core/shaders/encoded_normals.fxc",
		"game/core/shaders/ffd.fxc",
		"game/core/shaders/instancing.fxc",
		"game/core/shaders/irradiance_probe_lighting.fxc",
		"game/core/shaders/irradiance_volume.fxc",
		"game/core/shaders/light_probe_volume.fxc",
		"game/core/shaders/math_general.fxc",
		"game/core/shaders/mathlib_base.fxc",
		"game/core/shaders/morph.fxc",
		"game/core/shaders/octohedral_encoding.fxc",
		"game/core/shaders/parallax_occlusion.fxc",
		"game/core/shaders/pcss.fxc",
		"game/core/shaders/post_process_common.fxc",
		"game/core/shaders/quad_overdraw_ps.fxc",
		"game/core/shaders/sheet_sampling.fxc",
		"game/core/shaders/sky.fxc",
		"game/core/shaders/ssbump.fxc",
		"game/core/shaders/transform_buffer.fxc",
		"game/core/shaders/volumetric_fog.fxc",
		"game/core/shaders/vs_decompress.fxc",

		"game/core/shaders/complex.shader",
		"game/core/shaders/copytexture.shader",
		"game/core/shaders/cs_compress_dxt5.shader",
		"game/core/shaders/cs_volumetric_fog.shader",
		"game/core/shaders/debug_show_texture.shader",
		"game/core/shaders/debug_wireframe_2d.shader",
		"game/core/shaders/debugoverlay_wireframe.shader",
		"game/core/shaders/depth_only.shader",
		"game/core/shaders/downsample_depth.shader",
		"game/core/shaders/error.shader",
		"game/core/shaders/eyeball.shader",
		"game/core/shaders/generic.shader",
		"game/core/shaders/morph_composite.shader",
		"game/core/shaders/simple.shader",
		"game/core/shaders/skin.shader",
		"game/core/shaders/sky.shader",
		"game/core/shaders/static_overlay.shader",
		"game/core/shaders/tonemap_query.shader",
		"game/core/shaders/tools_2d_generic.shader",
		"game/core/shaders/tools_generic.shader",
		"game/core/shaders/tools_light_probe.shader",
		"game/core/shaders/tools_selection_outline.shader",
		"game/core/shaders/tools_selection_overlay.shader",
		"game/core/shaders/tools_selection_stencil_copy.shader",
		"game/core/shaders/tools_shading_complexity.shader",
		"game/core/shaders/tools_solid.shader",
		"game/core/shaders/tools_sprite.shader",
		"game/core/shaders/tools_textured_unlit.shader",
		"game/core/shaders/tools_visualize_collision_mesh.shader",
		"game/core/shaders/tools_visualize_tangent_frame.shader",
		"game/core/shaders/tools_wireframe.shader",
		"game/core/shaders/ui.shader",
		"game/core/shaders/unlit.shader",
		"game/core/shaders/visualize_quad_overdraw.shader"
	};

	private static readonly Dictionary<string, string> RepoFilterPathRenames = new()
	{
		{ "public/.gitignore", ".gitignore" },
		{ "public/.gitattributes", ".gitattributes" },
		{ "public/.github/PULL_REQUEST_TEMPLATE.md", ".github/PULL_REQUEST_TEMPLATE.md" },
		{ "public/.github/ISSUE_TEMPLATE/bug_report.yml", ".github/ISSUE_TEMPLATE/bug_report.yml" },
		{ "public/.github/ISSUE_TEMPLATE/config.yml", ".github/ISSUE_TEMPLATE/config.yml" },
		{ "public/.github/ISSUE_TEMPLATE/crash.yml", ".github/ISSUE_TEMPLATE/crash.yml" },
		{ "public/.github/ISSUE_TEMPLATE/feature_request.yml", ".github/ISSUE_TEMPLATE/feature_request.yml" },
		{ "public/.github/ISSUE_TEMPLATE/whitelist.yml", ".github/ISSUE_TEMPLATE/whitelist.yml" },
		{ "public/.github/workflows/pull_request.yml", ".github/workflows/pull_request.yml" },
		{ "public/.github/workflows/pull_request_checks.yml", ".github/workflows/pull_request_checks.yml" },
		{ "public/.github/workflows/pull_request_formatting.yml", ".github/workflows/pull_request_formatting.yml" },
		{ "public/README.md", "README.md" },
		{ "public/AGENTS.md", "AGENTS.md" },
		{ "public/CLAUDE.md", "CLAUDE.md" },
		{ "public/LICENSE.md", "LICENSE.md" },
		{ "public/CONTRIBUTING.md", "CONTRIBUTING.md" },
		{ "public/SECURITY.md", "SECURITY.md" },
		{ "public/Setup.bat", "Setup.bat" },
		{ "public/Setup.sh", "Setup.sh" },
		{ "public/Bootstrap.bat", "Bootstrap.bat" }
	};

	private static Matcher RepoFileFilter()
	{
		if ( _matcher is not null )
		{
			return _matcher;
		}

		_matcher = new Matcher( StringComparison.OrdinalIgnoreCase );

		_matcher.AddIncludePatterns( RepoFilterPathIncludeGlobs );

		_matcher.AddExcludePatterns( RepoFilterPathExcludeGlobs );

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

		// The build output doesn't come from the clone, so hash it while the clone is made
		var buildArtifactsTask = Task.Run( () => HashBuildArtifacts( repositoryRoot ) );

		var filteredRepoPath = CreateShallowClone( repositoryRoot );
		if ( string.IsNullOrEmpty( filteredRepoPath ) )
		{
			buildArtifactsTask.Wait();
			return false;
		}

		try
		{
			var relativeFilteredPath = GetRelativeWorkingDirectory( filteredRepoPath );

			var buildArtifacts = buildArtifactsTask.GetAwaiter().GetResult();
			if ( buildArtifacts is null )
			{
				return false;
			}

			// Make certain we dont have any stray files in our shallow clone
			if ( !CleanIgnoredFiles( relativeFilteredPath ) )
			{
				return false;
			}

			// LFS tracked files in the HEAD of the shallow clone
			var shallowLfsPaths = GetCurrentLfsFiles( relativeFilteredPath );
			if ( shallowLfsPaths is null )
			{
				return false;
			}

			var lfsArtifacts = ReadLfsArtifacts( filteredRepoPath, shallowLfsPaths );
			if ( lfsArtifacts is null )
			{
				return false;
			}

			var uploadedArtifacts = new List<ArtifactFileInfo>();
			var uniqueArtifacts = AddToManifest( buildArtifacts.Concat( lfsArtifacts ), uploadedArtifacts );

			if ( !TryUploadArtifacts( uniqueArtifacts, remoteBase, dryRun ) )
			{
				return false;
			}

			// Run git-filter-repo to filter out unwanted paths.
			// LFS pointer blobs are detected and stripped inline by the Python
			// filter (blob content inspection) so we no longer need to pass a
			// pre-computed LFS path list.
			if ( !RunFilterRepo( relativeFilteredPath ) )
			{
				return false;
			}

			if ( !ValidateFilteredRepository( relativeFilteredPath ) )
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

			// Also upload a copy of the manifest indexed by the private commit hash.
			// This lets CI running in the private repo resolve artifacts directly from
			// its own git history without needing to know the public commit hash.
			var privateCommitHash = GetPrivateCommitHash();
			if ( !string.IsNullOrEmpty( privateCommitHash ) &&
				!string.Equals( privateCommitHash, publicCommitHash, StringComparison.OrdinalIgnoreCase ) )
			{
				if ( !UploadManifest( privateCommitHash, uploadedArtifacts, remoteBase ) )
				{
					return false;
				}
			}

			var manifestTotalBytes = CalculateArtifactTotalSize( uploadedArtifacts );
			Log.Info( $"Total manifest artifact size: {Utility.FormatSize( manifestTotalBytes )}" );

			return true;
		}
		finally
		{
			try
			{
				Thread.Sleep( 500 ); // Give any pending file handles a moment to close
				Log.Info( "Cleaning up temporary filtered repository..." );
				DeleteDirectory( filteredRepoPath );
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

		// LFS content is read from this checkout's object store, the clone only needs the pointers
		var environment = new Dictionary<string, string> { ["GIT_LFS_SKIP_SMUDGE"] = "1" };

		if ( Utility.RunProcess( "git", $"clone --config core.longpaths=true --shallow-exclude {SHALLOW_EXCLUDE_TAG} \"{localFilePath}\" \"{filteredRepoPath}\"", environmentVariables: environment ) )
		{
			return filteredRepoPath;
		}

		Log.Error( "Failed to create clone" );
		return null;
	}

	/// <summary>
	/// Deletes a directory tree, including the read-only files git leaves in .git (packs, commit-graph)
	/// that make Directory.Delete fail on Windows.
	/// </summary>
	private static void DeleteDirectory( string path )
	{
		if ( !Directory.Exists( path ) )
			return;

		var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
		foreach ( var file in Directory.EnumerateFiles( path, "*", options ) )
		{
			var attributes = File.GetAttributes( file );
			if ( attributes.HasFlag( FileAttributes.ReadOnly ) )
				File.SetAttributes( file, attributes & ~FileAttributes.ReadOnly );
		}

		Directory.Delete( path, true );
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

	private static readonly string[] BuildPlatforms = { "win64", "linuxsteamrt64", "osxarm64" };

	/// <summary>
	/// Hashes the binaries for every platform and the compiled assets. Returns null on failure.
	/// </summary>
	private static List<(string SourcePath, ArtifactFileInfo Artifact)> HashBuildArtifacts( string repositoryRoot )
	{
		var files = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var platform in BuildPlatforms )
		{
			var buildArtifactsRoot = Path.Combine( repositoryRoot, "game", "bin", platform );
			if ( !Directory.Exists( buildArtifactsRoot ) )
			{
				Log.Info( $"Build artifacts directory not found, skipping upload: {buildArtifactsRoot}" );
				continue;
			}

			// Inline matcher: include everything, exclude managed root folder, pdbs, and debug symbols
			var matcher = new Matcher( StringComparison.OrdinalIgnoreCase, preserveFilterOrder: true );
			matcher.AddInclude( "**/*" );
			matcher.AddExclude( "managed/**" );
			matcher.AddExclude( "**/*.pdb" );
			matcher.AddExclude( "**/*.dbg" );

			files.UnionWith( matcher.GetResultsInFullPath( buildArtifactsRoot ) );
		}

		var binaries = files.Count;
		files.UnionWith( GetCompiledAssetFiles( repositoryRoot ) );

		Log.Info( $"Found {files.Count} build artifact(s) ({binaries} binaries, {files.Count - binaries} compiled assets)" );

		var candidates = files
			.Select( path => (RepoPath: ToForwardSlash( Path.GetRelativePath( repositoryRoot, path ) ), AbsolutePath: path) )
			.ToList();

		return HashArtifacts( candidates );
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

	/// <summary>
	/// The clone's working tree holds LFS pointers. A pointer's oid is the artifact's sha256, so nothing
	/// needs hashing, and the content comes from this checkout's LFS object store. Returns null on failure.
	/// </summary>
	private static List<(string SourcePath, ArtifactFileInfo Artifact)> ReadLfsArtifacts( string repoRoot, IReadOnlyCollection<string> lfsPaths )
	{
		var objectsDirectory = GetLfsObjectsDirectory();
		if ( objectsDirectory is null )
		{
			return null;
		}

		var paths = lfsPaths.Where( p => RepoFileFilter().Match( p ).HasMatches ).ToList();
		foreach ( var path in paths )
		{
			var extension = Path.GetExtension( path );
			if ( !string.IsNullOrEmpty( extension ) && ForbiddenArtifactExtensions.Contains( extension ) )
			{
				Log.Error( $"Encountered forbidden artifact extension ({extension}): {path}" );
				return null;
			}
		}

		// In parallel, first opens of the freshly checked out pointers are slow (virus scanning)
		var results = new (string SourcePath, ArtifactFileInfo Artifact, string Error)[paths.Count];
		Parallel.For( 0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
		{
			var path = paths[i];
			var pointerPath = Path.Combine( repoRoot, path.Replace( '/', Path.DirectorySeparatorChar ) );
			if ( !TryReadLfsPointer( pointerPath, out var oid, out var size ) )
			{
				results[i].Error = $"Expected an LFS pointer in the clone: {path}";
				return;
			}

			var objectPath = Path.Combine( objectsDirectory, oid[..2], oid[2..4], oid );
			var objectInfo = new FileInfo( objectPath );
			if ( !objectInfo.Exists || objectInfo.Length != size )
			{
				results[i].Error = $"LFS object {oid} for {path} is missing from {objectsDirectory}";
				return;
			}

			results[i] = (objectPath, new ArtifactFileInfo { Path = path, Sha256 = oid, Size = size }, null);
		} );

		var artifacts = new List<(string SourcePath, ArtifactFileInfo Artifact)>( results.Length );
		foreach ( var (sourcePath, artifact, error) in results )
		{
			if ( error is not null )
			{
				Log.Error( error );
				return null;
			}

			artifacts.Add( (sourcePath, artifact) );
		}

		Log.Info( $"Found {artifacts.Count} LFS artifact(s)" );

		return artifacts;
	}

	private static bool TryReadLfsPointer( string path, out string oid, out long size )
	{
		oid = null;
		size = -1;

		// Pointers are around 130 bytes, spec caps them at 1024
		var info = new FileInfo( path );
		if ( !info.Exists || info.Length > 1024 )
		{
			return false;
		}

		foreach ( var line in File.ReadLines( path ) )
		{
			if ( line.StartsWith( "oid sha256:", StringComparison.Ordinal ) )
			{
				oid = line["oid sha256:".Length..].Trim().ToLowerInvariant();
			}
			else if ( line.StartsWith( "size ", StringComparison.Ordinal ) && long.TryParse( line["size ".Length..], out var parsed ) )
			{
				size = parsed;
			}
		}

		return oid is { Length: 64 } && oid.All( char.IsAsciiHexDigit ) && size >= 0;
	}

	private static string GetLfsObjectsDirectory()
	{
		var gitDirectory = GetGitCommonDirectory();
		if ( gitDirectory is null )
		{
			Log.Error( "Failed to find the git directory for LFS objects" );
			return null;
		}

		return Path.Combine( gitDirectory, "lfs", "objects" );
	}

	/// <summary>
	/// The repository's .git directory, shared by all of its worktrees.
	/// </summary>
	private static string GetGitCommonDirectory()
	{
		string gitDirectory = null;
		Utility.RunProcess( "git", "rev-parse --git-common-dir", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
				gitDirectory ??= e.Data.Trim();
		} );

		return string.IsNullOrEmpty( gitDirectory ) ? null : Path.GetFullPath( gitDirectory );
	}

	private bool RunFilterRepo( string relativeRepoPath )
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
			PathRenames = RepoFilterPathRenames.ToDictionary( pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase )
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

	private static readonly HashSet<string> ForbiddenRepoExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".lib", ".exe", ".pdb", ".a", ".dll", ".dylib", ".so",
		".png", ".tga", ".jpg", ".psd", ".pdf", ".bmp", ".gif", ".exr", ".ico", ".svg", ".tif", ".tiff",
		".ttf", ".otf",
		".dmx", ".fbx", ".max",
		".wav", ".ogg", ".mp3", ".mp4", ".webm", ".avi",
		".pyd", ".ppf", ".vsix", ".vcs", ".bin", ".dat", ".jar", ".spv", ".ma", ".lxo"
	};

	private static bool ValidateFilteredRepository( string relativeRepoPath )
	{
		Log.Info( "Validating filtered repository before push..." );

		var renamedTargets = new HashSet<string>( RepoFilterPathRenames.Values, StringComparer.OrdinalIgnoreCase );
		var matcher = RepoFileFilter();
		var violations = new List<string>();

		Utility.RunProcess( "git", "ls-tree -r --name-only HEAD", relativeRepoPath, onDataReceived: ( _, e ) =>
		{
			if ( string.IsNullOrWhiteSpace( e.Data ) )
				return;

			var file = ToForwardSlash( e.Data.Trim() );

			if ( file.StartsWith( "src/", StringComparison.OrdinalIgnoreCase ) )
				violations.Add( $"Private source code: {file}" );

			if ( !renamedTargets.Contains( file ) && !matcher.Match( file ).HasMatches )
				violations.Add( $"Outside include rules: {file}" );

			var ext = Path.GetExtension( file );
			if ( !string.IsNullOrEmpty( ext ) && ForbiddenRepoExtensions.Contains( ext ) )
				violations.Add( $"Forbidden extension ({ext}): {file}" );
		} );

		if ( violations.Count > 0 )
		{
			Log.Error( $"Filtered repository contains {violations.Count} violation(s):" );
			foreach ( var v in violations )
				Log.Error( $"  {v}" );
			return false;
		}

		Log.Info( "Filtered repository validation passed" );
		return true;
	}

	private string PushToPublicRepository( string relativeRepoPath )
	{
		Log.Info( "Pushing filtered branch and tags to public..." );

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

		// git-filter-repo has already rewritten tag targets to their public commits.
		// Push both lightweight and annotated tags atomically with the branch.
		if ( !Utility.RunProcess( "git", $"push --atomic public {PUBLIC_BRANCH} --tags", relativeRepoPath ) )
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

	/// <summary>
	/// Returns the HEAD commit hash of the private (current) repository.
	/// </summary>
	private static string GetPrivateCommitHash()
	{
		string hash = null;
		Utility.RunProcess( "git", "rev-parse HEAD", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
				hash ??= e.Data.Trim();
		} );

		if ( string.IsNullOrEmpty( hash ) )
		{
			Log.Warning( "Failed to resolve private commit hash; private-keyed manifest will not be uploaded." );
		}

		return hash;
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

	/// <summary>
	/// Hashes the candidates in parallel. Returns null if any is missing or forbidden.
	/// </summary>
	private static List<(string SourcePath, ArtifactFileInfo Artifact)> HashArtifacts( IReadOnlyList<(string RepoPath, string AbsolutePath)> candidates )
	{
		foreach ( var (repoPath, absolutePath) in candidates )
		{
			if ( !File.Exists( absolutePath ) )
			{
				Log.Error( $"Artifact not found on disk: {repoPath}" );
				return null;
			}

			var extension = Path.GetExtension( absolutePath );
			if ( !string.IsNullOrEmpty( extension ) && ForbiddenArtifactExtensions.Contains( extension ) )
			{
				Log.Error( $"Encountered forbidden artifact extension ({extension}): {repoPath}" );
				return null;
			}
		}

		// Hashing is CPU+IO bound and benefits from concurrency
		var results = new (string SourcePath, ArtifactFileInfo Artifact)?[candidates.Count];
		Parallel.For( 0, candidates.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
		{
			var (repoPath, absolutePath) = candidates[i];

			var fileInfo = new FileInfo( absolutePath );
			var resolvedPath = absolutePath;
			if ( fileInfo.LinkTarget is not null )
			{
				var resolved = fileInfo.ResolveLinkTarget( returnFinalTarget: true );
				if ( resolved?.Exists != true )
				{
					return; // broken symlink - handled with a warning below
				}

				resolvedPath = resolved.FullName;
				fileInfo = new FileInfo( resolvedPath );
			}

			results[i] = (resolvedPath, new ArtifactFileInfo
			{
				Path = repoPath,
				Sha256 = Utility.CalculateSha256( resolvedPath ),
				Size = fileInfo.Length
			});
		} );

		var artifacts = new List<(string SourcePath, ArtifactFileInfo Artifact)>( candidates.Count );
		for ( var i = 0; i < candidates.Count; i++ )
		{
			if ( results[i] is not { } result )
			{
				Log.Warning( $"Failed to resolve symlink target for {candidates[i].RepoPath}, skipping artifact" );
				continue;
			}

			artifacts.Add( result );
		}

		return artifacts;
	}

	/// <summary>
	/// Adds each artifact to the manifest, first one wins for a path, and returns one artifact per unique blob.
	/// </summary>
	private static List<(string SourcePath, ArtifactFileInfo Artifact)> AddToManifest( IEnumerable<(string SourcePath, ArtifactFileInfo Artifact)> artifacts, List<ArtifactFileInfo> manifest )
	{
		var paths = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var hashes = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var unique = new List<(string SourcePath, ArtifactFileInfo Artifact)>();

		foreach ( var (sourcePath, artifact) in artifacts )
		{
			if ( !paths.Add( artifact.Path ) )
			{
				continue;
			}

			manifest.Add( artifact );

			if ( hashes.Add( artifact.Sha256 ) )
			{
				unique.Add( (sourcePath, artifact) );
			}
		}

		return unique;
	}

	private static bool TryUploadArtifacts( IReadOnlyCollection<(string SourcePath, ArtifactFileInfo Artifact)> artifacts, string remoteBase, bool skipUpload )
	{
		var totalBytes = artifacts.Sum( artifact => artifact.Artifact.Size );
		Log.Info( $"{artifacts.Count} unique artifacts ({Utility.FormatSize( totalBytes )})" );

		var (previousCommit, previousHashes) = GetPreviouslyUploadedHashes();
		var uploads = artifacts.Where( artifact => !previousHashes.Contains( artifact.Artifact.Sha256 ) ).ToList();
		if ( previousCommit is not null )
		{
			Log.Info( $"{artifacts.Count - uploads.Count} already uploaded with {previousCommit}" );
		}

		if ( uploads.Count == 0 )
		{
			Log.Info( "No new artifacts to upload" );
			return true;
		}

		var batchBytes = uploads.Sum( upload => upload.Artifact.Size );

		if ( skipUpload )
		{
			Log.Info( $"Dry run: would upload {uploads.Count} artifacts ({Utility.FormatSize( batchBytes )})" );
			return true;
		}

		Log.Info( $"Uploading {uploads.Count} artifacts ({Utility.FormatSize( batchBytes )})..." );

		if ( !BatchUploadArtifacts( uploads, remoteBase ) )
		{
			return false;
		}

		Log.Info( $"Uploaded {uploads.Count} artifacts ({Utility.FormatSize( batchBytes )})" );

		return true;
	}

	/// <summary>
	/// Artifact hashes from the newest manifest of a recent ancestor. Manifests are only uploaded once all
	/// their artifacts are, so these are already in the bucket. Empty if none is found, uploading everything.
	/// </summary>
	private static (string Commit, HashSet<string> Hashes) GetPreviouslyUploadedHashes()
	{
		var hashes = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		// Skip HEAD itself, its manifest is about to be written and a 404 for it could get cached
		var commits = new List<string>();
		Utility.RunProcess( "git", $"rev-list --first-parent --max-count={PREVIOUS_MANIFEST_SEARCH} HEAD~1", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
				commits.Add( e.Data.Trim() );
		} );

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds( 30 ) };
			foreach ( var commit in commits )
			{
				using var response = httpClient.GetAsync( $"{R2.PublicBaseUrl}/manifests/{commit}.json" ).GetAwaiter().GetResult();
				if ( response.StatusCode == System.Net.HttpStatusCode.NotFound )
				{
					continue;
				}

				response.EnsureSuccessStatusCode();

				using var stream = response.Content.ReadAsStream();
				var manifest = JsonSerializer.Deserialize<ArtifactManifest>( stream );
				foreach ( var file in manifest?.Files ?? [] )
				{
					if ( !string.IsNullOrEmpty( file?.Sha256 ) )
						hashes.Add( file.Sha256 );
				}

				return (commit, hashes);
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to fetch a previous manifest, uploading every artifact: {ex.Message}" );
			return (null, new HashSet<string>( StringComparer.OrdinalIgnoreCase ));
		}

		Log.Info( $"No manifest found for the last {commits.Count} commit(s), uploading every artifact" );
		return (null, hashes);
	}

	private static bool BatchUploadArtifacts( IReadOnlyCollection<(string SourcePath, ArtifactFileInfo Artifact)> uploads, string remoteBase )
	{
		// Next to the repository so staging can hard link rather than copy
		var stagingDir = Path.Combine( GetGitCommonDirectory() ?? Path.GetTempPath(), $"sbox-upload-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( stagingDir );

		Log.Info( $"Staging {uploads.Count} artifact(s) for batch upload..." );

		try
		{
			var copied = 0;
			foreach ( var (sourcePath, artifact) in uploads )
			{
				var destPath = Path.Combine( stagingDir, artifact.Sha256 );

				// Read-only files are copied, deleting a link to one would need the shared attribute cleared
				if ( File.GetAttributes( sourcePath ).HasFlag( FileAttributes.ReadOnly ) || !Utility.TryCreateHardLink( destPath, sourcePath ) )
				{
					File.Copy( sourcePath, destPath, overwrite: true );
					copied++;
				}
			}

			if ( copied > 0 )
			{
				Log.Info( $"Copied {copied} artifact(s) that couldn't be hard linked" );
			}

			// --no-traverse checks each file rather than listing every artifact in the bucket
			var remoteArtifactsPath = $"{remoteBase}/artifacts";
			var args = $"copy \"{stagingDir}\" \"{remoteArtifactsPath}\" --ignore-existing --no-traverse --transfers {MAX_PARALLEL_UPLOADS} --checkers {MAX_PARALLEL_UPLOADS} --stats-one-line --stats 30s -v";
			if ( !Utility.RunProcess( "rclone", args, timeoutMs: 3600000 ) )
			{
				Log.Error( "Failed to batch upload artifacts" );
				return false;
			}

			return true;
		}
		finally
		{
			try
			{
				// Plain delete: clearing read-only on a hard link would change the source file too
				Directory.Delete( stagingDir, true );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to clean up upload staging directory: {ex.Message}" );
			}
		}
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

	private static string GetR2Base() => R2.GetRcloneRemote();

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

		[JsonPropertyName( "path_renames" )]
		public Dictionary<string, string> PathRenames { get; init; }
	}
}
