using Sandbox.Engine;
using System;
using System.Text.Json.Nodes;
using System.Threading;

namespace Editor;

public static partial class AssetSystem
{
	internal static CloudAssetDirectory CloudDirectory { get; set; }

	/// <inheritdoc cref="InstallAsync(Package, bool, Action{float}, CancellationToken)"/>
	public static async Task<Asset> InstallAsync( string packageIdent, bool skipIfInstalled = true, Action<float> loading = null, CancellationToken token = default )
	{
		loading?.Invoke( 0.0f );
		var package = await Package.FetchAsync( packageIdent, false, false );
		return await InstallAsync( package, skipIfInstalled, loading, token );
	}

	/// <summary>
	/// Install a cloud package. Will return the primary asset on completion (if it has one)
	/// </summary>
	/// <param name="package"></param>
	/// <param name="skipIfInstalled">Skip downloading the remote package if any version of this package is already installed.</param>
	/// <param name="loading"></param>
	/// <param name="token"></param>
	public static async Task<Asset> InstallAsync( Package package, bool skipIfInstalled = true, Action<float> loading = null, CancellationToken token = default )
	{
		ThreadSafe.AssertIsMainThread();

		if ( package == null )
			return null;

		if ( package.Revision == null )
			return null;

		if ( !CanCloudInstall( package ) )
			return null;

		if ( !skipIfInstalled || !IsCloudInstalled( package ) )
		{
			// download the manifest
			await package.Revision.DownloadManifestAsync( token );

			using var suppressWatchers = FileWatch.Suppress();
			var downloaded = await DownloadCloudFiles( package, loading, token );

			foreach ( var file in package.Revision.Manifest.Files )
			{
				var fullPath = FileSystem.Cloud.GetFullPath( file.Path );

				// A file that was already on disk and is already an asset was registered by the asset system's
				// own scan, and hasn't changed since - registering it again is a slow native call for nothing.
				// Anything just written, or not known yet, is registered as before.
				var asset = downloaded.Contains( file.Path ) ? null : FindByPath( fullPath );
				if ( asset is not null )
				{
					asset.TryLoadGameResource( typeof( GameResource ), out _, true );
				}
				else
				{
					asset = RegisterFile( fullPath );
				}

				if ( asset is null )
					continue;

				asset.Package = package;

				// update ref version on any assets that depend on this
				ReplaceReferences( asset.GetDependants( false ), package, package );
			}

			CloudDirectory.AddPackage( package );
			EditorEvent.Run( "package.changed.installed", package );
		}

		var primaryAssetName = package.PrimaryAsset;
		return FindByPath( primaryAssetName );
	}

	internal static void UninstallPackage( Package package )
	{
		if ( !IsCloudInstalled( package ) )
		{
			return;
		}

		var contents = GetPackageFiles( package );
		CloudDirectory.RemovePackage( package );

		foreach ( var file in contents )
		{
			var fullPath = FileSystem.Cloud.GetFullPath( file );
			var asset = FindByPath( fullPath );

			if ( asset is null || asset.Package != package )
				continue;

			var requiredBy = CloudDirectory.FindPackages( fullPath, file ).FirstOrDefault();
			if ( requiredBy == null )
			{
				// no other package needs this, we can delete it
				asset.Delete();
			}
			else
			{
				// update any assets that depend on this to point to the package that remains
				ReplaceReferences( asset.GetDependants( false ), package, requiredBy );
				asset.Package = requiredBy;
			}
		}
	}

	/// <summary>
	/// Replaces any references in <paramref name="assets"/> to <paramref name="fromPackage"/> with a reference to <paramref name="toPackage"/>
	/// (does a replace by ident to avoid having to open the map etc)
	/// </summary>
	private static void ReplaceReferences( IEnumerable<Asset> assets, Package fromPackage, Package toPackage )
	{
		if ( assets.Count() < 1 ) return;

		string newIdent = toPackage?.GetIdent( false, true );
		string projectPath = Project.Current.GetAssetsPath().Replace( '\\', '/' );

		foreach ( var asset in assets )
		{
			// more bs
			if ( !asset.AbsolutePath.StartsWith( projectPath, StringComparison.OrdinalIgnoreCase ) )
				continue;

			if ( !asset.AssetType.IsGameResource )
			{
				var config = asset?.Publishing?.ProjectConfig;
				if ( config is null || config.EditorReferences is null )
					continue;

				var references = config.EditorReferences.Where( x => !fromPackage.IsNamed( x ) ).ToList();
				if ( references.Count == config.EditorReferences.Count )
					continue;

				if ( newIdent != null && !references.Contains( newIdent ) )
					references.Add( newIdent );

				references = references.OrderBy( x => x ).ToList();

				// Reinstalling the same revision swaps a reference for itself. Rewriting the meta then changes nothing
				// but its write time, which everything watching or caching meta files has to treat as a change.
				if ( references.SequenceEqual( config.EditorReferences ) )
					continue;

				config.EditorReferences = references;
				asset.MetaData.Set( "publish", asset.Publishing );
			}
			else if ( typeof( GameResource ).IsAssignableFrom( asset.AssetType.ResourceType ) && asset.LoadResource() is GameResource gameResource )
			{
				string filename = asset.GetSourceFile( true );
				if ( string.IsNullOrWhiteSpace( filename ) )
					continue;

				var json = System.IO.File.ReadAllText( filename );

				if ( JsonNode.Parse( json ) is not JsonObject jso )
					continue;

				if ( jso["__references"] is not JsonArray references )
					continue;

				var oldReferences = references.Select( x => x.ToString() ).ToList();
				var newReferences = oldReferences.Where( x => !fromPackage.IsNamed( x ) ).ToList();
				if ( newReferences.Count == oldReferences.Count )
					continue;

				if ( newIdent != null && !newReferences.Contains( newIdent ) )
					newReferences.Add( newIdent );

				newReferences = newReferences.OrderBy( x => x ).ToList();
				if ( newReferences.SequenceEqual( oldReferences ) )
					continue;

				jso["__references"] = JsonValue.Create( newReferences );

				gameResource.SaveToDisk( filename, jso.ToJsonString( Json.options ) );
			}
		}
	}

	/// <summary>
	/// Gets the locally installed package revision by ident
	/// </summary>
	public static Package.IRevision GetInstalledRevision( string packageIdent )
	{
		return CloudDirectory.FindPackage( packageIdent )?.Revision;
	}

	/// <summary>
	/// Is this package installed in our cloud directory?
	/// </summary>
	public static bool IsCloudInstalled( string packageIdent )
	{
		return CloudDirectory.FindPackage( packageIdent ) != null;
	}

	/// <summary>
	/// Is a version this package installed in our cloud directory?
	/// </summary>
	public static bool IsCloudInstalled( Package package, bool exactVersion = false )
	{
		if ( package is null ) return false;
		if ( package.Org is null )
		{
			Log.Trace( $"{package.FullIdent} no org" );
			return false;
		}
		if ( !package.IsRemote )
		{
			Log.Trace( $"{package.FullIdent} not remote" );
			return false;
		}

		var local = CloudDirectory.FindPackage( package.FullIdent );
		if ( local is null )
		{
			Log.Trace( $"{package.FullIdent} wasn't found in CloudDirectory" );
			return false;
		}

		if ( exactVersion && package.Revision is not null && package.Revision.VersionId != local.Revision.VersionId )
		{
			Log.Trace( $"{package.Revision.VersionId} != {local.Revision.VersionId} version is different" );
			return false; // version is different
		}

		return true;
	}

	/// <summary>
	/// Get all packages in the download cache
	/// </summary>
	public static IReadOnlyCollection<Package> GetInstalledPackages()
	{
		return CloudDirectory.GetPackages();
	}

	/// <summary>
	/// Get all packages, referenced by assets in the current project, in the download cache
	/// </summary>
	public static IReadOnlyCollection<Package> GetReferencedPackages()
	{
		return CloudAsset.GetAssetReferences( true )
			.Select( CloudDirectory.FindPackage )
			.Where( p => p != null ).ToList();
	}

	public static IReadOnlyCollection<string> GetPackageFiles( Package package )
	{
		return CloudDirectory.GetPackageFiles( package ).ToList();
	}

	/// <summary>
	/// Is this package type something we can install?
	/// </summary>
	public static bool CanCloudInstall( Package package )
	{
		if ( package.TypeName == "map" ) return false;
		if ( package.TypeName == "collection" ) return false;

		return true;
	}

	/// <summary>
	/// Initialize the files from the package manifest. Returns the manifest paths of the files that were written.
	/// </summary>
	private static async Task<HashSet<string>> DownloadCloudFiles( Package package, Action<float> progress, CancellationToken token )
	{
		long totalSize = package.Revision.Manifest.Files.Sum( x => x.Size );
		long downloaded = 0;
		var written = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		IToolsDll.Current?.RunEvent( "package.download.start", package, token );

		await package.Revision.Manifest.Files.ForEachTaskAsync( async ( e ) =>
		{
			if ( await DownloadFile( package, e, token ) )
				written.Add( e.Path );

			downloaded += e.Size;

			float frac = (float)((double)downloaded / (double)totalSize);
			progress?.Invoke( frac );
			IToolsDll.Current?.RunEvent( "package.download.update", package, frac );
		}, Package.MaxParallelDownloads );

		IToolsDll.Current?.RunEvent( "package.download.complete", package );
		return written;
	}

	/// <summary>
	/// Download a manifest file into the cloud folder. False if it didn't need writing.
	/// </summary>
	static async Task<bool> DownloadFile( Package package, ManifestSchema.File entry, CancellationToken token )
	{
		ThreadSafe.AssertIsMainThread();

		//
		// Ignore thumbnails
		//
		string path = entry.Path.StartsWith( "/" ) ? entry.Path : $"/{entry.Path}";
		if ( path == $"/{package.FullIdent}/thumb.png" )
			return false;

		//
		// Ignore assemblies
		//
		if ( entry.Path.EndsWith( ".dll" ) )
			return false;

		//
		// This file exists in core, no need to download it (at this moment)
		//
		if ( EngineFileSystem.CoreContent.FileExists( entry.Path ) )
			return false;

		if ( !CloudDirectory.AddFile( entry.Path, entry.Crc, entry.Size, package ) )
			return false; // unwanted - newer revision already registered

		//
		// File exists - we should probably check crcs and shit
		//
		if ( FileSystem.Cloud.FileExists( entry.Path ) && FileSystem.Cloud.FileSize( entry.Path ) == entry.Size )
			return false;

		var targetFile = FileSystem.Cloud.GetFullPath( entry.Path );
		var targetPath = System.IO.Path.GetDirectoryName( entry.Path );
		FileSystem.Cloud.CreateDirectory( targetPath );

		if ( System.IO.File.Exists( targetFile ) )
		{
			System.IO.File.Delete( targetFile );
		}

		if ( entry.Size == 0 )
		{
			await System.IO.File.WriteAllTextAsync( targetFile, "" );
		}
		else
		{
			var url = $"{entry.Url}";
			await Sandbox.Utility.Web.DownloadFile( url, targetFile, token );
		}

		return true;
	}
}

