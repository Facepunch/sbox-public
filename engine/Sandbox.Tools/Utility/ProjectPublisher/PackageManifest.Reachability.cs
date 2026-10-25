using Sandbox.DataModel;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		async Task<List<Asset>> FindAssetsToPublish( Project project, List<Asset> assets, CancellationToken cancel )
		{
			// Existing projects keep publishing all assets until they opt in.
			if ( !publishingOptions.PublishUsedAssetsOnly ) return assets;

			progress?.SetProgressMessage( "Finding publish roots.." );
			var configuredPaths = GetConfiguredScenePaths( project.Config ).Where( IsReferencePath ).ToList();
			foreach ( var file in Assets.Where( x => x.Name.StartsWith( "ProjectSettings/", StringComparison.OrdinalIgnoreCase )
				&& (x.Name.EndsWith( ".config", StringComparison.OrdinalIgnoreCase ) || x.Name.EndsWith( ".json", StringComparison.OrdinalIgnoreCase )) ) )
			{
				configuredPaths.AddRange( GetSettingsPaths( file ) );
			}

			configuredPublishPaths.UnionWith( configuredPaths.Select( NormalizePublishPath ) );
			var roots = FindPublishRoots( assets, AssetPaths, configuredPaths, publishingOptions.Resources );
			// A configured scene can also belong to an installed package outside the project's asset directories.
			foreach ( var path in configuredPaths )
			{
				if ( AssetSystem.FindByPath( path ) is { } asset ) roots.Add( asset );
			}

			var used = await FindReachableAssets( roots, async asset =>
			{
				await YieldPreview( cancel );
				// Compile before reading references so edited or newly created assets have an up-to-date graph.
				await asset.CompileIfNeededAsync();
				return asset.GetReferences( false );
			}, cancel );

			foreach ( var path in configuredPaths )
				await IncludeLooseReference( path );

			// GameResource JSON records raw image paths as compile inputs. Preserve those runtime files,
			// without publishing the source textures/meshes used to compile native materials and models.
			foreach ( var asset in used.Where( x => x.AssetType.IsGameResource ).ToArray() )
			{
				cancel.ThrowIfCancellationRequested();
				foreach ( var path in asset.GetInputDependencies() )
				{
					if ( AssetSystem.FindByPath( path ) is { } image && image.AssetType == AssetType.ImageFile )
					{
						used.Add( image );
						await IncludeLooseReference( path );
					}
				}
			}

			var omitted = assets.Where( x => !used.Contains( x ) ).ToList();
			if ( omitted.Count > 0 )
			{
				var names = string.Join( ", ", omitted.Take( 20 ).Select( x => x.Path ) );
				if ( omitted.Count > 20 ) names += $", and {omitted.Count - 20} more";
				Log.Info( $"Leaving out {omitted.Count} assets outside the publish roots and their dependencies: {names}. Put assets loaded from code in Resources or list them under Resource Files." );
			}

			// Collect sidecar game files (such as navdata) for dependencies as well as roots.
			return used.ToList();
		}

		static IEnumerable<string> AssetPaths( Asset asset ) => [asset.Path, asset.RelativePath, asset.GetSourceFile(), asset.GetCompiledFile()];

		internal static IEnumerable<string> GetConfiguredScenePaths( ProjectConfig config )
		{
			yield return config.GetMetaOrDefault( "StartupScene", "start.scene" );
			yield return config.GetMetaOrDefault( "MapStartupScene", "" );
			yield return config.GetMetaOrDefault( "DedicatedServerStartupScene", "" );
			yield return config.GetMetaOrDefault( "SystemScene", "" );
		}

		internal IEnumerable<string> GetSettingsPaths( ProjectFile file )
		{
			try
			{
				var text = file.Contents is not null ? System.Text.Encoding.UTF8.GetString( file.Contents ) : File.ReadAllText( file.AbsolutePath );
				return GetSettingsPaths( text );
			}
			catch ( Exception exception ) when ( exception is JsonException or IOException or UnauthorizedAccessException )
			{
				Errors.Add( $"Cannot read resource references from {file.Name}: {exception.Message}" );
				return [];
			}
		}

		/// <summary>
		/// Project settings contain serialized resource/file references. Only complete paths are considered;
		/// property names, wildcards and code are never interpreted as references.
		/// </summary>
		internal static IEnumerable<string> GetSettingsPaths( string json )
		{
			using var document = JsonDocument.Parse( json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip } );
			return Strings( document.RootElement ).Where( IsReferencePath ).ToArray();

			static IEnumerable<string> Strings( JsonElement element )
			{
				if ( element.ValueKind == JsonValueKind.String ) yield return element.GetString();
				else if ( element.ValueKind == JsonValueKind.Object )
				{
					foreach ( var property in element.EnumerateObject() )
						foreach ( var value in Strings( property.Value ) ) yield return value;
				}
				else if ( element.ValueKind == JsonValueKind.Array )
				{
					foreach ( var item in element.EnumerateArray() )
						foreach ( var value in Strings( item ) ) yield return value;
				}
			}
		}

		internal static HashSet<T> FindPublishRoots<T>( IEnumerable<T> assets, Func<T, IEnumerable<string>> pathsOf,
			IEnumerable<string> configuredPaths, string resourcePatterns )
		{
			var configured = configuredPaths.Where( IsReferencePath ).Select( NormalizePublishPath ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var patterns = new ResourceRuleMatcher( resourcePatterns );
			return assets.Where( asset =>
			{
				var paths = pathsOf( asset ).Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( NormalizePublishPath ).ToArray();
				// Rules apply to the asset as a whole, across source and compiled aliases.
				return paths.Any( path => IsResourcesPath( path ) || configured.Contains( path ) ) || patterns.Matches( paths );
			} ).ToHashSet();
		}

		internal static async Task<HashSet<T>> FindReachableAssets<T>( IEnumerable<T> roots, Func<T, Task<IEnumerable<T>>> referencesOf, CancellationToken cancel = default )
		{
			var used = new HashSet<T>();
			var pending = new Queue<T>( roots );
			while ( pending.TryDequeue( out var asset ) )
			{
				cancel.ThrowIfCancellationRequested();
				if ( !used.Add( asset ) ) continue;
				foreach ( var reference in await referencesOf( asset ) ) pending.Enqueue( reference );
			}
			return used;
		}

		internal static bool IsResourcesPath( string path ) => NormalizePublishPath( path ).StartsWith( "/resources/", StringComparison.OrdinalIgnoreCase );

		static string NormalizePublishPath( string path ) => path.Trim().NormalizeFilename( true, false );

		static bool IsReferencePath( string path ) => !string.IsNullOrWhiteSpace( path ) && Path.HasExtension( path )
			&& path.IndexOfAny( [':', '*', '?', '"', '\r', '\n'] ) < 0 && !path.Replace( '\\', '/' ).Split( '/' ).Contains( ".." );

		async Task IncludeLooseReference( string path )
		{
			if ( !IsReferencePath( path ) || !LooseFileAllowed( path, false ) ) return;
			if ( AssetSystem.FindByPath( path ) is { } asset && !string.IsNullOrEmpty( asset.GetCompiledFile() ) ) return;
			var absolute = FileSystem.Mounted.GetFullPath( path );
			if ( !string.IsNullOrEmpty( absolute ) && File.Exists( absolute ) ) await AddFile( absolute, path );
		}

		async Task IncludeResourceDirectories( Project project, CancellationToken cancel )
		{
			foreach ( var root in AllAssetPaths( project ).Where( Directory.Exists ) )
			{
				foreach ( var directory in Directory.EnumerateDirectories( root ).Where( x => string.Equals( Path.GetFileName( x ), "Resources", StringComparison.OrdinalIgnoreCase ) ) )
				{
					foreach ( var file in Directory.EnumerateFiles( directory, "*", SearchOption.AllDirectories ) )
					{
						cancel.ThrowIfCancellationRequested();
						var relative = Path.GetRelativePath( root, file ).NormalizeFilename( true, false );
						if ( !ResourceFileAllowed( relative ) ) continue;
						// Compilable assets have already been collected. Don't also ship their sources.
						if ( AssetSystem.FindByPath( file ) is { } asset && !string.IsNullOrEmpty( asset.GetCompiledFile() ) ) continue;
						await AddFile( file, relative );
					}
				}
			}
		}

		internal static bool ResourceFileAllowed( string path ) => LooseFileAllowed( path, false )
			&& !path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) && !path.EndsWith( "_d", StringComparison.OrdinalIgnoreCase )
			&& !path.EndsWith( ".meta", StringComparison.OrdinalIgnoreCase );
	}
}
