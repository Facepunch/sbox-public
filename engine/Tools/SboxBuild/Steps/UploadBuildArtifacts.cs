using System.IO.Compression;
using Amazon.S3;
using Amazon.S3.Transfer;
using Microsoft.Extensions.FileSystemGlobbing;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Packages a complete, ready-to-run build of the game into a single zip and
/// uploads it to R2 so it can be downloaded and launched. Alongside it goes a much
/// smaller zip with just what PackageBuild and ShaderCompiler need to run.
/// </summary>
internal class UploadBuildArtifacts
{
	// What to package, mirrors the Steam depot mappings mostly
	// This doesn't have to be perfect
	private static readonly string[] IncludeGlobs =
	{
		"*.exe",
		"sbox",
		"sbox-dev",
		"sbox-launcher",
		"sbox-server",
		"benchmark",
		".version",
		"thirdpartylegalnotices.txt",
		"thirdpartylegalnotices/**",
		"bin/win64/**",
		"bin/linuxsteamrt64/**",
		"bin/osxarm64/**",
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

	// What to strip - debug symbols, uncompiled source assets, etc.
	// Mirrors the FileExclusion entries in steamworks/depot.game.content.vdf
	// (Steam's '*' crosses path separators, so those map to '**' globs here).
	private static readonly string[] ExcludeGlobs =
	{
		// Debug symbols (not in the depot, but pointless to ship)
		"**/*.pdb",
		"**/*.dbg",

		// Uncompiled source assets
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

		// Code objects / project files / intermediates
		"**/obj/**",
		"**/*.sln",
		"**/*.csproj",
		"**/*.codegen",
		"**/.intermediate/**",
		"**/.vs/**",
		"**/*.code-workspace",
		"**/unittest/**",
		"addons/*/Properties/**",

		"**/materials/**/*.txt",

		// core/ uncompiled bits not covered by the blanket globs above
		"core/**/*.meta",
		"core/debug/**/*.tga",
		"core/dev/**/*.tga",
		// Keep directly loaded gizmo and editor tool PNGs; exclude material compiler inputs.
		"core/materials/default/**/*.png",
		"core/materials/dev/**/*.png",
		"core/materials/editor/**/*.png",
		"core/materials/models/**/*.png",
		"core/materials/postprocess/**/*.png",
		"core/materials/tools/new/**/*.png",
		"core/textures/**/*.png",

		// core/sounds - allow core/sounds/editor/*.wav, strip the rest of the raws
		"core/sounds/**/*.sound",
		"core/sounds/ambience/**/*.wav",
		"core/sounds/footsteps/**/*.wav",
		"core/sounds/impacts/**/*.wav",
		"core/sounds/physics/**/*.wav",
		"core/sounds/water/**/*.wav",

		// Shader source - explicitly not allowed to ship
		"core/shaders/ambient_cube.fxc",
		"core/shaders/baked_lighting_constants.fxc",
		"core/shaders/bump_strength.fxc",
		"core/shaders/encoded_normals.fxc",
		"core/shaders/ffd.fxc",
		"core/shaders/instancing.fxc",
		"core/shaders/irradiance_probe_lighting.fxc",
		"core/shaders/irradiance_volume.fxc",
		"core/shaders/light_probe_volume.fxc",
		"core/shaders/math_general.fxc",
		"core/shaders/mathlib_base.fxc",
		"core/shaders/morph.fxc",
		"core/shaders/octohedral_encoding.fxc",
		"core/shaders/parallax_occlusion.fxc",
		"core/shaders/pcss.fxc",
		"core/shaders/post_process_common.fxc",
		"core/shaders/quad_overdraw_ps.fxc",
		"core/shaders/sheet_sampling.fxc",
		"core/shaders/sky.fxc",
		"core/shaders/ssbump.fxc",
		"core/shaders/transform_buffer.fxc",
		"core/shaders/volumetric_fog.fxc",
		"core/shaders/vs_decompress.fxc",

		"core/shaders/complex.shader",
		"core/shaders/copytexture.shader",
		"core/shaders/cs_compress_dxt5.shader",
		"core/shaders/cs_volumetric_fog.shader",
		"core/shaders/debug_show_texture.shader",
		"core/shaders/debug_wireframe_2d.shader",
		"core/shaders/debugoverlay_wireframe.shader",
		"core/shaders/depth_only.shader",
		"core/shaders/downsample_depth.shader",
		"core/shaders/error.shader",
		"core/shaders/eyeball.shader",
		"core/shaders/generic.shader",
		"core/shaders/morph_composite.shader",
		"core/shaders/simple.shader",
		"core/shaders/skin.shader",
		"core/shaders/sky.shader",
		"core/shaders/static_overlay.shader",
		"core/shaders/tonemap_query.shader",
		"core/shaders/tools_2d_generic.shader",
		"core/shaders/tools_generic.shader",
		"core/shaders/tools_light_probe.shader",
		"core/shaders/tools_selection_outline.shader",
		"core/shaders/tools_selection_overlay.shader",
		"core/shaders/tools_selection_stencil_copy.shader",
		"core/shaders/tools_shading_complexity.shader",
		"core/shaders/tools_solid.shader",
		"core/shaders/tools_sprite.shader",
		"core/shaders/tools_textured_unlit.shader",
		"core/shaders/tools_visualize_collision_mesh.shader",
		"core/shaders/tools_visualize_tangent_frame.shader",
		"core/shaders/tools_wireframe.shader",
		"core/shaders/ui.shader",
		"core/shaders/unlit.shader",
		"core/shaders/visualize_quad_overdraw.shader",

		// addons raw assets
		"addons/**/*.tga",
		"addons/citizen/Assets/models/**/*_color.png",
		"addons/citizen/Assets/models/**/*_normal.png",
		"addons/citizen/Assets/models/**/*_bentnormal.png",
		"addons/citizen/Assets/models/**/*_depth.png",
		"addons/citizen/Assets/models/**/*_translucency.png",
		"addons/citizen/Assets/models/**/*_alpha.png",
		"addons/citizen/Assets/models/**/*_opacity.png",
		"addons/citizen/Assets/models/**/*_specular.png",
		"addons/citizen/Assets/models/**/*_direction.png",
		"addons/citizen/Assets/models/**/*_rough.png",
		"addons/citizen/Assets/models/**/*_roughness.png",
		"addons/citizen/Assets/models/**/*_ao.png",
		"addons/citizen/Assets/models/**/*_ambient.png",
		"addons/citizen/Assets/models/**/*_metal.png"
	};

	// The compilers build is the slice of the full build that PackageBuild and ShaderCompiler need,
	// so the backend's compile workers don't have to download everything. It's picked from the full
	// build's files, so anything stripped above stays stripped here.
	private static readonly string[] CompilerIncludeGlobs =
	{
		// PackageBuild compiles against bin/managed, ShaderCompiler runs from it
		"bin/managed/**",

		// ShaderCompiler boots the engine with the resource compiler, which needs these
		"bin/assettypes.txt",
		"bin/win64/animationsystem.dll",
		"bin/win64/dxcompiler.dll",
		"bin/win64/engine2.dll",
		"bin/win64/filesystem_stdio.dll",
		"bin/win64/materialsystem2.dll",
		"bin/win64/meshsystem.dll",
		"bin/win64/modeldoc_utils.dll",
		"bin/win64/openxr_loader.dll",
		"bin/win64/rendersystemempty.dll",
		"bin/win64/resourcecompiler.dll",
		"bin/win64/schemasystem.dll",
		"bin/win64/SDL3.dll",
		"bin/win64/sentry.dll",
		"bin/win64/slang-compiler.dll",
		"bin/win64/slang.dll",
		"bin/win64/steam_api64.dll",
		"bin/win64/tier0.dll",
		"bin/win64/vfx_vulkan.dll",

		// Shader includes, plus the error assets the engine loads at startup
		"core/shaders/**/*.fxc",
		"core/shaders/**/*.hlsl",
		"core/shaders/**/*.h",
		"core/shaders/**/*.cpp",
		"core/shaders/error.shader_c",
		"core/materials/error.vmat_c"
	};

	private static readonly string[] CompilerExcludeGlobs =
	{
		"bin/managed/**/*.xml"
	};

	// The engine mounts these at startup and throws if they're missing, but nothing in them is needed
	private static readonly string[] CompilerEmptyFolders =
	{
		"game/addons/citizen/Assets/",
		"game/mount/"
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
			var compilersZipPath = Path.Combine( Path.GetTempPath(), $"sbox-compilers-{commit}-{Guid.NewGuid():N}.zip" );

			try
			{
				var zipSize = CreateArchive( files, zipPath );

				// R2 takes one write at a time per object, but separate objects can go up together,
				// so the full build uploads while the compilers zip is made and uploaded.
				var buildUpload = Task.Run( () => UploadZip( s3, bucket, zipPath, $"builds/{commit}.zip" ) );

				var compilersSize = CreateArchive( SelectCompilerFiles( repoRoot, files ), compilersZipPath, CompilerEmptyFolders );
				var compilersUploaded = UploadZip( s3, bucket, compilersZipPath, $"builds/{commit}-compilers.zip" );

				// Immutable, commit-keyed objects.
				if ( !buildUpload.GetAwaiter().GetResult() || !compilersUploaded )
					return ExitCode.Failure;

				// Hand the size to the later report-build step so it can record the artifact's byte size.
				Utility.SetGitHubEnv( "BUILD_ARTIFACT_BYTES", zipSize.ToString() );

				Log.Info( $"Build artifact available at {R2.PublicBaseUrl}/builds/{commit}.zip" );
				Log.Info( $"Compilers artifact available at {R2.PublicBaseUrl}/builds/{commit}-compilers.zip" );
				GitHubActions.AddLink( "Build", $"{R2.PublicBaseUrl}/builds/{commit}.zip" );
				GitHubActions.AddLink( "Compilers", $"{R2.PublicBaseUrl}/builds/{commit}-compilers.zip" );
				GitHubActions.Note( $"{Utility.FormatSize( zipSize )}, compilers {Utility.FormatSize( compilersSize )}" );

				return ExitCode.Success;
			}
			finally
			{
				try { if ( File.Exists( zipPath ) ) File.Delete( zipPath ); } catch { }
				try { if ( File.Exists( compilersZipPath ) ) File.Delete( compilersZipPath ); } catch { }
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

	/// <summary>
	/// Picks the files PackageBuild and ShaderCompiler need out of the full build
	/// (see <see cref="CompilerIncludeGlobs"/>).
	/// </summary>
	private static List<(string EntryPath, string AbsolutePath)> SelectCompilerFiles( string repoRoot, IReadOnlyCollection<(string EntryPath, string AbsolutePath)> files )
	{
		var matcher = new Matcher( StringComparison.OrdinalIgnoreCase );
		matcher.AddIncludePatterns( CompilerIncludeGlobs );
		matcher.AddExcludePatterns( CompilerExcludeGlobs );

		var gameRoot = Path.Combine( repoRoot, "game" );
		var selected = matcher.Match( gameRoot, files.Select( x => x.AbsolutePath ) ).Files
			.Select( x => ToForwardSlash( Path.Combine( "game", x.Path ) ) )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );

		return files.Where( x => selected.Contains( x.EntryPath ) ).ToList();
	}

	/// <summary>
	/// Zips the files and returns the zip's size.
	/// </summary>
	private static long CreateArchive( IReadOnlyCollection<(string EntryPath, string AbsolutePath)> files, string zipPath, IEnumerable<string> emptyFolders = null )
	{
		Log.Info( $"Creating {Path.GetFileName( zipPath )} ({files.Count} file(s))..." );

		ParallelZip.Create( zipPath, files, emptyFolders );

		var size = new FileInfo( zipPath ).Length;
		Log.Info( $"Packaged {files.Count} file(s) into {Path.GetFileName( zipPath )} ({Utility.FormatSize( size )})" );

		return size;
	}

	/// <summary>
	/// One part at a time only managed ~13MB/s. R2 rate limits writes to the same object
	/// (https://developers.cloudflare.com/r2/platform/limits/), and the SDK default of 10
	/// parallel 5MiB parts got rejected with "Reduce your concurrent request rate for the
	/// same object" - but a few big parts at once is a much lower request rate than that.
	/// Retries go back to one at a time in case R2 still doesn't like it.
	/// </summary>
	private const int UploadConcurrency = 4;

	/// <summary>
	/// Big parts keep the request rate down: a ~5GB archive is ~40 requests at 128MiB
	/// rather than ~1000 at the SDK's 5MiB default.
	/// </summary>
	private const long UploadPartSize = 128 * 1024 * 1024;

	private static bool UploadZip( IAmazonS3 s3, string bucket, string zipPath, string key )
	{
		const int attempts = 3;

		for ( var attempt = 1; attempt <= attempts; attempt++ )
		{
			var concurrency = attempt == 1 ? UploadConcurrency : 1;
			Log.Info( $"Uploading build artifact to {key} ({concurrency} part(s) at a time)..." );

			try
			{
				var timer = System.Diagnostics.Stopwatch.StartNew();

				// TransferUtility handles multipart upload + retries for large archives.
				using var transfer = new TransferUtility( s3, new TransferUtilityConfig
				{
					ConcurrentServiceRequests = concurrency
				} );

				var request = new TransferUtilityUploadRequest
				{
					BucketName = bucket,
					Key = key,
					FilePath = zipPath,
					ContentType = "application/zip",
					PartSize = UploadPartSize,
					// R2 doesn't implement Streaming SigV4 (the SDK's default for upload bodies):
					// "STREAMING-AWS4-HMAC-SHA256-PAYLOAD not implemented". Disable payload signing
					// (and the default checksum) - TransferUtility propagates both to each part upload.
					DisablePayloadSigning = true,
					DisableDefaultChecksumValidation = true
				};

				transfer.UploadAsync( request ).GetAwaiter().GetResult();

				var megabytes = new FileInfo( zipPath ).Length / 1024.0 / 1024.0;
				Log.Info( $"Uploaded {key} in {timer.Elapsed.TotalSeconds:0}s ({megabytes / timer.Elapsed.TotalSeconds:0} MB/s)" );
				return true;
			}
			catch ( Exception ex )
			{
				var detail = ex is AmazonS3Exception s3Ex
					? $"{s3Ex.Message} (code {s3Ex.ErrorCode}, http {(int)s3Ex.StatusCode})"
					: ex.Message;

				if ( attempt == attempts )
				{
					Log.Error( $"Failed to upload build artifact to {key}: {detail}" );
					return false;
				}

				// Throttling and transient R2 errors are worth another go - the archive is
				// already on disk, so the only cost is the re-upload.
				var backoff = TimeSpan.FromSeconds( 15 * attempt );
				Log.Warning( $"Upload attempt {attempt}/{attempts} failed: {detail} - retrying in {backoff.TotalSeconds:0}s" );
				Thread.Sleep( backoff );
			}
		}

		return false;
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
