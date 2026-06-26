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
		"core/materials/**/*.png",
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
