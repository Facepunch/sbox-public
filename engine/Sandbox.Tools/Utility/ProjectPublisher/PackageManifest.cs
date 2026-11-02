using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Editor;

public interface IProgress
{
	public void SetProgressMessage( string message ) { }
	public void SetProgress( float total, float current ) { }
}

public partial class ProjectPublisher
{

	public partial class PackageManifest
	{
		public string Summary { get; set; }
		public string Description { get; set; }
		public bool IncludeSourceFiles { get; set; }

		/// <summary>
		/// List of packages that the code references
		/// </summary>
		public HashSet<string> CodePackageReferences { get; } = new();

		public List<string> Errors = new List<string>();

		/// <summary>Snapshot of publishing settings, including unsaved edits used by the settings preview.</summary>
		public sealed record PublishOptions( bool PublishUsedAssetsOnly, string Resources );

		/// <summary>Preview files have sizes but no upload hashes. They must be rebuilt before uploading.</summary>
		[System.Text.Json.Serialization.JsonIgnore]
		public bool IsPreview { get; private set; }

		PublishOptions publishingOptions;
		IProgress progress;
		readonly HashSet<string> previewFileNames = new( StringComparer.OrdinalIgnoreCase );

		bool ContainsPublishedFile( string path )
		{
			if ( !IsPreview ) return Assets.Any( x => string.Equals( x.Name, path, StringComparison.OrdinalIgnoreCase ) );
			// Publisher customization can add/remove entries through the public manifest list.
			if ( previewFileNames.Count != Assets.Count )
			{
				previewFileNames.Clear();
				previewFileNames.UnionWith( Assets.Select( x => ReviewPath( x.Name ) ) );
			}
			return previewFileNames.Contains( ReviewPath( path ) );
		}

		public ProjectFile FindAsset( string relativePath )
		{
			return Assets.FirstOrDefault( x => string.Equals( x.Name, relativePath, StringComparison.OrdinalIgnoreCase ) );
		}


		public List<ProjectFile> Assets { get; set; } = new();


		void ClearAssets()
		{
			Assets.Clear();
			previewFileNames.Clear();
			SceneWarnings.Clear();
			ExcludedAssets.Clear();
			ExcludedAssetSizes.Clear();
			CodeAssetWarnings.Clear();
			RecommendedRules = [];
			ExclusionOverrides.Clear();
			ExcludedByRules.Clear();
			IncludedExclusionReasons.Clear();
			inclusionReasons.Clear();
			configuredPublishPaths.Clear();
			reviewSizeGraph.Clear();
			reviewIncludedFiles.Clear();
			reviewImpactCache.Clear();
			reviewFootprintCache.Clear();
			reviewDependencyCache.Clear();
			reviewAssetAliases.Clear();
		}

		public async Task BuildFromAssets( Project project, IProgress progress = null, CancellationToken cancel = default, PublishOptions options = null, bool previewOnly = false )
		{
			ClearAssets();

			var rootFolder = project.RootDirectory.FullName;


			this.progress = progress;
			IsPreview = previewOnly;
			publishingOptions = options ?? new( project.Config.PublishUsedAssetsOnly, project.Config.Resources );

			//
			// Collect localization files
			//
			await IncludeFiles( rootFolder, "Localization/*.json", cancel );

			await Task.Delay( 10 );
			cancel.ThrowIfCancellationRequested();

			//
			// Collect font files
			//
			foreach ( var path in AllAssetPaths( project ) )
			{
				await IncludeFiles( path, "fonts/*", cancel );
			}

			await Task.Delay( 10 );
			cancel.ThrowIfCancellationRequested();

			//
			// Include all project config
			//
			if ( project.Config.Type == "game" )
			{
				await IncludeFiles( rootFolder, "ProjectSettings/*", cancel );
				await IncludeCursorImages();
			}

			await Task.Delay( 10 );
			cancel.ThrowIfCancellationRequested();


			//
			// Search the code path for files
			//
			foreach ( var path in AllCodePaths( project ) )
			{
				progress?.SetProgressMessage( "Collecting code assets" );
				await IncludeFiles( path, "*", cancel );
			}

			await Task.Delay( 10 );
			cancel.ThrowIfCancellationRequested();

			//
			// Search in assets
			//
			{
				progress?.SetProgressMessage( "Collecting assets" );
				await CollectAssets( project, cancel );
			}

			this.progress = null;
		}

		IEnumerable<string> AllCodePaths( Project project )
		{
			if ( project.HasCodePath() )
				yield return project.GetCodePath();

			// each library
			foreach ( var library in LibrarySystem.All )
			{
				if ( library.Project.HasCodePath() )
					yield return library.Project.GetCodePath();
			}
		}

		IEnumerable<string> AllAssetPaths( Project project )
		{
			if ( project.HasAssetsPath() )
				yield return project.GetAssetsPath();

			// each library
			foreach ( var library in LibrarySystem.All )
			{
				if ( library.Project.HasAssetsPath() )
					yield return library.Project.GetAssetsPath();
			}
		}

		internal async Task BuildFrom( Asset singleAsset, Project project = null, CancellationToken cancel = default )
		{
			IsPreview = false;
			ClearAssets();

			var assetList = new List<Asset>
			{
				singleAsset
			};

			await CollectAssets( assetList, cancel );

			var includeCode = singleAsset.Publishing.BuildPublishContext().IncludeCode;

			if ( project is not null && includeCode )
			{
				foreach ( var path in AllCodePaths( project ) )
				{
					await IncludeFiles( path, "*", cancel, IncludeSourceFiles );
				}
			}

			progress = null;

			if ( Assets.Count == 0 )
				Errors.Add( "No files found" );
		}

		public async Task BuildFromSource( Project addon, IProgress progress = null, CancellationToken cancel = default )
		{
			IsPreview = false;
			ClearAssets();

			var rootFolder = addon.RootDirectory.FullName;

			this.progress = progress;

			//
			// Collect localization files
			//
			await IncludeFiles( rootFolder, "*", cancel, true );

			cancel.ThrowIfCancellationRequested();

			this.progress = null;
		}

		public string ToJson() => JsonSerializer.Serialize( this, new JsonSerializerOptions( JsonSerializerOptions.Default ) { WriteIndented = true } );

		private async Task CollectAssets( Project project, CancellationToken cancel )
		{
			var assets = new List<Asset>();

			foreach ( var path in AllAssetPaths( project ) )
			{
				var assetPath = path.Replace( '\\', '/' );
				assetPath = assetPath.TrimEnd( '/' ) + '/';

				progress?.SetProgressMessage( "Finding Assets.." );
				assets.AddRange( AssetSystem.All.Where( x => x.AbsolutePath.StartsWith( assetPath, StringComparison.OrdinalIgnoreCase ) ) );
			}

			assets = assets.Distinct().ToList();
			var candidates = assets;

			if ( project.Config.Type == "game" )
				assets = await FindAssetsToPublish( project, assets, cancel );

			await CollectAssets( assets, cancel, referencesCollected: project.Config.Type == "game" && publishingOptions.PublishUsedAssetsOnly );

			// Compile selected assets before copying loose resources, so a clean build cannot ship their sources.
			if ( !string.IsNullOrWhiteSpace( publishingOptions.Resources ) )
			{
				cancel.ThrowIfCancellationRequested();

				foreach ( var path in AllAssetPaths( project ) )
				{
					await IncludeFiles( path, publishingOptions.Resources, cancel, allowSourceFiles: IncludeSourceFiles, matchAssetAliases: true, legacyRules: !publishingOptions.PublishUsedAssetsOnly );
				}
			}

			if ( project.Config.Type == "game" )
			{
				if ( publishingOptions.PublishUsedAssetsOnly ) await IncludeResourceDirectories( project, cancel );
				if ( IsPreview || publishingOptions.PublishUsedAssetsOnly ) await BuildAssetReview( project, candidates, cancel, assets );
			}
		}

		/// <summary>
		/// Collect and add input dependencies to the manifest.
		/// These are files that were involved in compile but don't cause a recompile - usually only present for child resources (ie. the tga that a vtex_c came from)
		/// </summary>
		/// <param name="asset"></param>
		/// <returns></returns>
		private async Task CollectInputDependencies( Asset asset )
		{
			if ( !IncludeSourceFiles ) return;

			foreach ( var file in asset.GetAdditionalContentFiles() )
			{
				var ast = AssetSystem.FindByPath( file );
				if ( ast == null ) continue;

				await AddFile( ast.AbsolutePath, ast.RelativePath );
			}

			foreach ( var a in asset.GetInputDependencies() )
			{
				var ast = AssetSystem.FindByPath( a );
				if ( ast == null ) continue;

				await AddFile( ast.AbsolutePath, ast.RelativePath );
			}
		}

		private async Task CollectAssets( List<Asset> assets, CancellationToken cancel, bool referencesCollected = false )
		{
			HashSet<Asset> AddedAssets = new();

			foreach ( var asset in assets )
			{
				cancel.ThrowIfCancellationRequested();
				await YieldPreview( cancel );

				AddedAssets.Add( asset );

				if ( referencesCollected )
				{
					await CollectInputDependencies( asset );
				}
				else
				{
					foreach ( var a in asset.GetReferences( true ) )
					{
						AddedAssets.Add( a );
						await CollectInputDependencies( a );
					}
				}

				foreach ( var file in asset.GetAdditionalContentFiles() )
				{
					if ( !IncludeSourceFiles && !file.EndsWith( ".rect" ) )
						continue;

					var ast = AssetSystem.FindByPath( file );

					if ( ast == null ) continue;

					await CollectInputDependencies( ast );
					await AddFile( ast.AbsolutePath, ast.RelativePath );
				}

				// Collect game-side files (data files to be packaged like navdata)
				foreach ( var file in asset.GetAdditionalGameFiles() )
				{
					var absPath = FileSystem.Mounted.GetFullPath( file );
					if ( !string.IsNullOrEmpty( absPath ) && System.IO.File.Exists( absPath ) )
					{
						await AddFile( absPath, file );
					}
				}

				progress?.SetProgressMessage( $"Found {AddedAssets.Count:n0}" );
			}

			List<Task> tasks = new List<Task>();
			int i = 0;
			foreach ( var a in AddedAssets )
			{
				cancel.ThrowIfCancellationRequested();

				progress?.SetProgress( i++, AddedAssets.Count );
				progress?.SetProgressMessage( $"Adding Asset {i:n0} of {AddedAssets.Count:n0} - {a.RelativePath}" );


				tasks.Add( AddAsset( a ) );

				while ( tasks.Count > 8 )
				{
					var completed = await Task.WhenAny( tasks );
					await completed;
					tasks.Remove( completed );
				}
			}

			await Task.WhenAll( tasks.ToArray() );
		}

		async Task<bool> AddAsset( Asset asset )
		{
			if ( !CanPublishFile( asset ) )
				return false;

			await asset.CompileIfNeededAsync();

			if ( IncludeSourceFiles )
			{
				var abs = asset.GetSourceFile( true );
				var rel = asset.GetSourceFile( false );

				await AddFile( abs, rel );
			}

			{
				var abs = asset.GetCompiledFile( true );
				var rel = asset.GetCompiledFile( false );

				if ( asset.IsCompileFailed )
				{
					Errors.Add( $"Asset failed to compile: {asset.Path}" );
					return false;
				}

				//
				// This is fine, some shit doesn't get compiled.
				// There's probably a way to find this out proper though.
				//
				if ( string.IsNullOrEmpty( abs ) )
				{
					//Log.Warning( $"Compiled file missing: {asset.Path}" );
					return false;
				}

				// Add this file
				await AddFile( abs, rel );

				// Should we add the thumbnail?
				await TryAddThumbnail( asset );
			}

			return true;
		}

		async Task TryAddThumbnail( Asset asset )
		{
			if ( asset.AssetType == null ) return;

			// don't do thumbs for built in assets, except models
			if ( !asset.AssetType.IsGameResource && (asset.AssetType != AssetType.Model) )
				return;

			// they should explicitly opt into this
			if ( asset.AssetType.IsGameResource && !asset.AssetType.Flags.Contains( AssetTypeFlags.IncludeThumbnails ) )
				return;

			var rel = asset.GetCompiledFile( false );

			var thumbName = $"{rel}.t.png";

			//
			// already added
			//
			if ( ContainsPublishedFile( thumbName ) )
				return;

			if ( IsPreview )
			{
				// Cached PNG metadata is enough for preview; do not render or re-encode images per refresh.
				var cached = AssetThumbnail.GetThumbnailFile( asset, false );
				if ( File.Exists( cached ) ) await AddFile( cached, thumbName );
				return;
			}

			var thumb = asset.GetAssetThumb( true );

			if ( thumb is null ) return;

			var png = thumb.GetPng();
			await AddFile( png, thumbName );
		}

		private async Task AddFile( string absPath, string relativePath )
		{
			await YieldPreview();
			if ( !System.IO.File.Exists( absPath ) )
			{
				Errors.Add( $"File not found \"{absPath}\" ({relativePath})" );
				return;
			}

			relativePath = relativePath.NormalizeFilename( false, false ).TrimStart( '/' );

			//
			// already added
			//
			if ( ContainsPublishedFile( relativePath ) )
				return;

			var e = await ReadPublishFile( absPath, relativePath, IsPreview );

			if ( ContainsPublishedFile( relativePath ) )
				return;


			Assets.Add( e );
			if ( IsPreview ) previewFileNames.Add( ReviewPath( e.Name ) );
		}

		/// <summary>Preview needs metadata only; reading and hashing payloads is reserved for upload.</summary>
		internal static async Task<ProjectFile> ReadPublishFile( string absolutePath, string relativePath, bool previewOnly )
		{
			var info = new FileInfo( absolutePath );
			var file = new ProjectFile { Name = relativePath, Size = (int)info.Length, AbsolutePath = absolutePath };
			if ( !previewOnly )
			{
				file.Hash = await Task.Run( async () =>
				{
					using var stream = info.OpenRead();
					return (await Sandbox.Utility.Crc64.FromStreamAsync( stream )).ToString( "x" );
				} );
			}
			return file;
		}

		internal void RequireUploadManifest()
		{
			if ( IsPreview ) throw new InvalidOperationException( "Build a full publish manifest before uploading a preview." );
		}

		internal async Task IncludeCursorImages()
		{
			if ( FileSystem.ProjectSettings is null || FileSystem.Content is null ) return;

			var settings = EditorUtility.LoadProjectSettings<CursorSettings>( "Cursors.config" );
			var cursors = settings?.Cursors;
			if ( cursors is null ) return;

			foreach ( var cursor in cursors.Values )
			{
				if ( string.IsNullOrWhiteSpace( cursor.Image ) ) continue;

				var relative = cursor.Image.NormalizeFilename( true, false );
				if ( !LooseFileAllowed( relative, false ) ) continue;

				var absPath = FileSystem.Content.GetFullPath( cursor.Image );
				if ( !string.IsNullOrWhiteSpace( absPath ) && File.Exists( absPath ) )
					await AddFile( absPath, relative );
			}
		}

		readonly System.Diagnostics.Stopwatch previewSlice = System.Diagnostics.Stopwatch.StartNew();

		async Task YieldPreview( CancellationToken cancel = default )
		{
			if ( !IsPreview || previewSlice.ElapsedMilliseconds < 4 ) return;
			await Task.Delay( 1, cancel );
			previewSlice.Restart();
		}

		internal async Task IncludeFiles( string root, string wildcardScript, CancellationToken cancel, bool allowSourceFiles = false, bool matchAssetAliases = false, bool legacyRules = false )
		{
			if ( !System.IO.Directory.Exists( root ) )
				return;

			var matches = new ResourceRuleMatcher( wildcardScript, legacyRules );
			// Disk enumeration and matching are managed work; do them away from the editor thread.
			var files = await Task.Run( () =>
			{
				var result = new List<(string Absolute, string Relative)>();
				foreach ( var file in Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ) )
				{
					cancel.ThrowIfCancellationRequested();
					var relative = Path.GetRelativePath( root, file ).NormalizeFilename( true, false );
					if ( !LooseFileAllowed( relative, allowSourceFiles ) || !matches.Matches( relative ) ) continue;
					if ( new FileInfo( file ).Length < 1 ) continue;
					result.Add( (file, relative) );
				}
				return result;
			}, cancel );
			foreach ( var (absolute, relative) in files )
			{
				cancel.ThrowIfCancellationRequested();
				await YieldPreview( cancel );
				if ( matchAssetAliases && AssetSystem.FindByPath( absolute ) is { } asset )
				{
					// A compiled sidecar must not bypass an exclusion matching its source alias.
					if ( matches.HasExclusions && !matches.Matches( AssetPaths( asset ).Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( NormalizePublishPath ) ) ) continue;
					// A matching standalone file (e.g. .ogg compiled to .vsnd_c) must also ship as-is.
					// Other compilable assets still require the source option to publish their source files.
					var compiled = asset.GetCompiledFile();
					if ( !IncludeSourceFiles && !IsStandaloneSourceFile( asset ) && (asset.IsCompileFailed || !string.IsNullOrEmpty( compiled ))
						&& !string.Equals( NormalizePublishPath( relative ), string.IsNullOrEmpty( compiled ) ? null : NormalizePublishPath( compiled ), StringComparison.OrdinalIgnoreCase ) ) continue;
				}
				await AddFile( absolute, relative );
			}
		}

		/// <summary>
		/// This really exists only to dissallow dangerous extensions like .exe etc.
		/// So feel free to add anything non dangerous to this list.
		/// </summary>
		public static string[] DissallowedExtensions = [".dll", ".exe", ".csproj", ".sln", ".user", ".slnx", ".pdb"];

		public static bool LooseFileAllowed( string file, bool allowSourceFiles )
		{
			if ( file.Contains( "/obj/", StringComparison.OrdinalIgnoreCase ) ) return false;

			// Ignore all dot folders and dot files (.git, .vs, .idea, .editorconfig, .addon, etc)
			if ( file.Contains( "/.", StringComparison.OrdinalIgnoreCase ) ) return false;
			if ( file.Contains( "_bakeresourcecache", StringComparison.OrdinalIgnoreCase ) ) return false;
			if ( file.Contains( "launchsettings.json", StringComparison.OrdinalIgnoreCase ) ) return false;

			if ( !allowSourceFiles )
			{
				if ( file.Contains( ".sbproj", StringComparison.OrdinalIgnoreCase ) ) return false;
				if ( file.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) ) return false;
				if ( file.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) ) return false;
				if ( file.EndsWith( ".fbx", StringComparison.OrdinalIgnoreCase ) ) return false;
				// Editor sidecars can map a compiled resource GUID back to its raw source path.
				if ( file.EndsWith( ".meta", StringComparison.OrdinalIgnoreCase ) ) return false;
			}

			if ( DissallowedExtensions.Any( x => file.EndsWith( x, StringComparison.OrdinalIgnoreCase ) ) )
				return false;

			return true;
		}


		public async Task AddTextFile( string contents, string relativePath )
		{
			var bytes = Encoding.UTF8.GetBytes( contents );
			await AddFile( bytes, relativePath );
		}

		internal async Task AddFile( byte[] contents, string relativePath )
		{
			//
			// already added
			//
			if ( ContainsPublishedFile( relativePath ) )
				return;

			var e = new ProjectFile
			{
				Name = relativePath,
				Size = (int)contents.Length,
				Contents = contents
			};

			// Preview rows do not need a hash for generated content either.
			if ( !IsPreview ) await Task.Run( async () =>
			{
				using ( var stream = new MemoryStream( contents ) )
				{
					e.Hash = (await Sandbox.Utility.Crc64.FromStreamAsync( stream )).ToString( "x" );
				}
			} );

			if ( ContainsPublishedFile( relativePath ) )
				return;

			Assets.Add( e );
			if ( IsPreview ) previewFileNames.Add( ReviewPath( e.Name ) );
		}

		/// <summary>
		/// We're referencing this asset package, so add it as an EditorReference and
		/// include its asset.
		/// </summary>
		internal async Task AddCodePackageReference( string package )
		{
			var asset = await AssetSystem.InstallAsync( package );
			if ( asset == null )
			{
				Log.Warning( $"Couldn't find asset for package {package}" );
				return;
			}

			CodePackageReferences.Add( package.ToLower() );

			await AddAsset( asset );

			foreach ( var a in asset.GetReferences( true ) )
			{
				await AddAsset( a );
			}
		}
	}
}
