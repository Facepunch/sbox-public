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

	public class PackageManifest
	{
		public string Summary { get; set; }
		public string Description { get; set; }
		public bool IncludeSourceFiles { get; set; }

		/// <summary>
		/// List of packages that the code references
		/// </summary>
		public HashSet<string> CodePackageReferences { get; } = new();

		public List<string> Errors = new List<string>();

		IProgress progress;
		ulong scannedBytes;

		public ProjectFile FindAsset( string relativePath )
		{
			return Assets.FirstOrDefault( x => string.Equals( x.Name, relativePath, StringComparison.OrdinalIgnoreCase ) );
		}


		public List<ProjectFile> Assets { get; set; } = new();


		public async Task BuildFromAssets( Project project, IProgress progress = null, CancellationToken cancel = default )
		{
			Assets.Clear();

			var rootFolder = project.RootDirectory.FullName;


			this.progress = progress;

			if ( !string.IsNullOrWhiteSpace( project.Config.Resources ) )
			{
				cancel.ThrowIfCancellationRequested();

				foreach ( var path in AllAssetPaths( project ) )
				{
					await IncludeFiles( path, project.Config.Resources, cancel );
				}
			}

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
			}

			await Task.Delay( 10 );
			cancel.ThrowIfCancellationRequested();

			//
			// If we're a game, include content from /addons/base/code/
			// This versions things like the styles and dev ui, which is only
			// going to work with the shipped base code.
			//
			if ( project.Config.Type == "game" )
			{
				progress?.SetProgressMessage( "Collecting base code assets" );
				await IncludeFiles( FileSystem.Root.GetFullPath( "/addons/base/code/" ), "*", cancel );
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

		internal async Task BuildFrom( Asset singleAsset, CancellationToken cancel = default )
		{
			Assets.Clear();

			var assetList = new List<Asset>();
			assetList.Add( singleAsset );

			await CollectAssets( assetList, cancel );

			progress = null;

			if ( Assets.Count == 0 )
				Errors.Add( "No files found" );
		}

		public async Task BuildFromSource( Project addon, IProgress progress = null, CancellationToken cancel = default )
		{
			Assets.Clear();

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
			foreach ( var path in AllAssetPaths( project ) )
			{
				var assetPath = path.Replace( '\\', '/' );
				assetPath = assetPath.TrimEnd( '/' ) + '/';

				progress?.SetProgressMessage( "Finding Assets.." );
				var assets = AssetSystem.All.Where( x => x.AbsolutePath.StartsWith( assetPath, StringComparison.OrdinalIgnoreCase ) ).ToList();

				await CollectAssets( assets, cancel );
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

			foreach ( var file in asset.GetAdditionalRelatedFiles() )
			{
				if ( !IncludeSourceFiles && !file.EndsWith( ".rect" ) )
					continue;

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

		private async Task CollectAssets( List<Asset> assets, CancellationToken cancel )
		{
			HashSet<Asset> AddedAssets = new();

			foreach ( var asset in assets )
			{
				cancel.ThrowIfCancellationRequested();

				AddedAssets.Add( asset );

				foreach ( var a in asset.GetReferences( true ) )
				{
					AddedAssets.Add( a );
					await CollectInputDependencies( a );
				}

				foreach ( var file in asset.GetAdditionalRelatedFiles() )
				{
					if ( !IncludeSourceFiles && !file.EndsWith( ".rect" ) )
						continue;

					var ast = AssetSystem.FindByPath( file );

					if ( ast == null ) continue;

					await CollectInputDependencies( ast );
					await AddFile( ast.AbsolutePath, ast.RelativePath );
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
					await Task.WhenAny( tasks.ToArray() );
					tasks.RemoveAll( x => x.IsCompleted );
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
			var thumb = asset.GetAssetThumb( true );

			if ( thumb is null ) return;

			var thumbName = $"{rel}.t.png";

			var png = thumb.GetPng();
			await AddFile( png, thumbName );
		}

		private async Task AddFile( string absPath, string relativePath )
		{
			if ( !System.IO.File.Exists( absPath ) )
			{
				Errors.Add( $"File not found \"{absPath}\" ({relativePath})" );
				return;
			}

			relativePath = relativePath.NormalizeFilename( false, false ).TrimStart( '/' );

			//
			// already added
			//
			if ( Assets.Any( x => string.Equals( x.Name, relativePath, StringComparison.OrdinalIgnoreCase ) ) )
				return;

			var info = new System.IO.FileInfo( absPath );

			var e = new ProjectFile
			{
				Name = relativePath,
				Size = (int)info.Length,
				AbsolutePath = absPath
			};

			// run in a thread to make it happen in the background
			await Task.Run( async () =>
			{
				using ( var stream = info.OpenRead() )
				{
					e.Hash = (await Sandbox.Utility.Crc64.FromStreamAsync( stream )).ToString( "x" );
				}
			} );

			scannedBytes += (ulong)e.Size;

			Assets.Add( e );
		}

		internal async Task IncludeFiles( string root, string wildcardScript, CancellationToken cancel, bool allowSourceFiles = false )
		{
			if ( !System.IO.Directory.Exists( root ) )
				return;

			var wildcards = wildcardScript.Split( "\n", StringSplitOptions.RemoveEmptyEntries )
				.Select( x => x.Trim() )
				.Where( x => !x.StartsWith( "//" ) )
				.Select( x => x.NormalizeFilename( true, false ) )
				.ToArray();

			foreach ( var file in System.IO.Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ) )
			{
				var relative = System.IO.Path.GetRelativePath( root, file ).NormalizeFilename( true, false );

				if ( !LooseFileAllowed( relative, allowSourceFiles ) )
					continue;

				if ( !wildcards.Any( x => relative.WildcardMatch( x ) ) )
					continue;

				if ( new System.IO.FileInfo( file ).Length < 1 )
					continue;

				await AddFile( file, relative );
				cancel.ThrowIfCancellationRequested();
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
			if ( file.Contains( "/.git", StringComparison.OrdinalIgnoreCase ) ) return false;
			if ( file.Contains( "/.addon", StringComparison.OrdinalIgnoreCase ) ) return false;

			if ( file.Contains( "/.editorconfig", StringComparison.OrdinalIgnoreCase ) ) return false;
			if ( file.Contains( "/.vs/", StringComparison.OrdinalIgnoreCase ) ) return false;
			if ( file.Contains( "_bakeresourcecache", StringComparison.OrdinalIgnoreCase ) ) return false;
			if ( file.Contains( "launchsettings.json", StringComparison.OrdinalIgnoreCase ) ) return false;

			if ( !allowSourceFiles )
			{
				if ( file.Contains( ".sbproj", StringComparison.OrdinalIgnoreCase ) ) return false;
				if ( file.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) ) return false;
				if ( file.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) ) return false;
				if ( file.EndsWith( ".fbx", StringComparison.OrdinalIgnoreCase ) ) return false;
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
			var e = new ProjectFile
			{
				Name = relativePath,
				Size = (int)contents.Length,
				Contents = contents
			};

			// run in a thread to make it super fast
			await Task.Run( async () =>
			{
				using ( var stream = new MemoryStream( contents ) )
				{
					e.Hash = (await Sandbox.Utility.Crc64.FromStreamAsync( stream )).ToString( "x" );
				}
			} );

			scannedBytes += (ulong)e.Size;
			Assets.Add( e );
		}

		/// <summary>
		/// Test our wildcards and make sure to pull in any assets that the assets that
		/// we're whitelisting are referencing. If they're not already included in the wildcard
		/// then we'll add them by full relative path to the end of the list.
		/// </summary>
		internal string[] GrabWildcardReferences( string wildcard )
		{
			var script = wildcard ?? "";

			var parts = script
							.Split( "\n", StringSplitOptions.RemoveEmptyEntries )
							.Where( x => !x.StartsWith( "//" ) )
							.ToHashSet( StringComparer.OrdinalIgnoreCase );

			// get a list of included assets that match this wildcard system
			var assets = Assets.Where( x => parts.Any( y => x.Name.WildcardMatch( y ) ) ).ToArray();

			// loop each hit asset and get references.
			foreach ( var a in assets )
			{
				var asset = AssetSystem.FindByPath( a.AbsolutePath );
				if ( asset == null ) continue;

				foreach ( var d in asset.GetReferences( true ) )
				{
					// aleady have this reference
					if ( assets.Any( x => x.AbsolutePath == d.AbsolutePath ) )
						continue;

					// add it to the end
					parts.Add( d.Path.Replace( "\\", "/" ).TrimStart( '/' ) );
				}
			}

			// return collapsed version
			return parts.ToArray();
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
