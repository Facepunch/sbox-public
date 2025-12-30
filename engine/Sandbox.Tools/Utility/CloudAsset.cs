using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Editor;

public class CloudAsset
{
	/// <summary>
	/// Checks if a package is installed on disk, including checking the version if it's present in the ident.
	/// </summary>
	/// <param name="ident"></param>
	/// <returns></returns>
	private static bool IsInstalled( string ident )
	{
		// this shouldn't really happen, but I guess just in case someone fudges a file
		if ( !Package.TryParseIdent( ident, out var parts ) )
			return true;

		var packageIdentNoVersion = $"{parts.org}.{parts.package}";

		// Putting this here while I look into why vmaps are referencing their own game projects 
		if ( packageIdentNoVersion == Game.Ident )
			return true;

		// Do we have this package on disk already?
		var localPackage = AssetSystem.CloudDirectory.FindPackage( packageIdentNoVersion );
		if ( localPackage is null )
			return false;

		// If it's pinned to a version, check we're on that
		if ( parts.version is not null && localPackage.Revision.VersionId != parts.version )
		{
			Log.Info( $"Package '{packageIdentNoVersion}' version mismatch, updating.. (current: {localPackage.Revision.VersionId}, required: {parts.version})" );
			return false;
		}

		return true;
	}

	/// <summary>
	/// Install a cloud asset by ident
	/// </summary>
	[ConCmd( "install", ConVarFlags.Protected )]
	public static async Task InstallSingle( string ident )
	{
		var asset = await AssetSystem.InstallAsync( ident );
		EditorUtility.InspectorObject = asset;
	}


	/// <summary>
	/// Install multiple packages, skipping what's already installed. Does progress window.
	/// </summary>
	public static async Task<bool> Install( string windowTitle, IEnumerable<string> packages )
	{
		if ( packages.Count() == 0 ) return true;

		if ( Backend.Package is null )
		{
			Log.Warning( $"Unable to install cloud assets, backend not available." );
			return false;
		}

		using var progress = Progress.Start( windowTitle );
		var cancel = Progress.GetCancel();

		// Sol: for whatever reason some projects were being saved with multiple refs to different version of the same package?
		// make sure we're only trying one ref of any package (prefer the newest, version-pinned one) otherwise stuff gets confusing
		Dictionary<string, int?> versions = new();
		foreach ( var ident in packages )
		{
			if ( !Package.TryParseIdent( ident, out var parts ) )
				continue;

			string fullIdent = Package.FormatIdent( parts.org, parts.package );
			var newVer = parts.version;

			if ( !versions.TryGetValue( fullIdent, out var existingVer ) || existingVer == null )
			{
				versions[fullIdent] = newVer;
				continue;
			}

			if ( newVer == null )
				continue;

			if ( existingVer != newVer )
			{
				// version conflict! choose newest
				var bestVer = Math.Max( newVer.Value, existingVer.Value );
				Log.Info( $"Found duplicate reference '{ident}' with conflicting versions (using: {bestVer})" );

				versions[fullIdent] = bestVer;
			}
		}

		IEnumerable<string> idents = versions.Select( x =>
		{
			var packageIdent = x.Key;
			if ( x.Value.HasValue ) packageIdent += $"#{x.Value}";
			return packageIdent;
		} );

		var undownloaded = idents.Where( x => !IsInstalled( x ) ).ToArray();

		int total = undownloaded.Count();
		int i = 0;

		await undownloaded.ForEachTaskAsync( async ident =>
		{
			Package package = await Package.FetchAsync( ident, true );
			if ( package == null )
				return;

			Progress.Update( $"Installing '{package.Title}'", ++i, total );
			Log.Info( $"Installing '{package.FullIdent}' (version: {package.Revision.VersionId})" );
			await AssetSystem.InstallAsync( package, false, null, cancel );

		}, token: cancel, maxRunning: 8 );


		return !cancel.IsCancellationRequested;
	}

	/// <summary>
	/// Install multiple packages, skipping what's already installed.
	/// </summary>
	public static async Task<bool> Install( IEnumerable<string> packages, CancellationToken token )
	{
		if ( !packages.Any() ) return true;

		if ( Backend.Package is null )
		{
			Log.Warning( $"Unable to install cloud assets, backend not available." );
			return false;
		}

		// Sol: for whatever reason some projects were being saved with multiple refs to different version of the same package?
		// make sure we're only trying one ref of any package (prefer the newest, version-pinned one) otherwise stuff gets confusing
		Dictionary<string, int?> versions = new();
		foreach ( var ident in packages )
		{
			if ( !Package.TryParseIdent( ident, out var parts ) )
				continue;

			string fullIdent = Package.FormatIdent( parts.org, parts.package );
			var newVer = parts.version;

			if ( !versions.TryGetValue( fullIdent, out var existingVer ) || existingVer == null )
			{
				versions[fullIdent] = newVer;
				continue;
			}

			if ( newVer == null )
				continue;

			if ( existingVer != newVer )
			{
				// version conflict! choose newest
				var bestVer = Math.Max( newVer.Value, existingVer.Value );
				Log.Info( $"Found duplicate reference '{ident}' with conflicting versions (using: {bestVer})" );

				versions[fullIdent] = bestVer;
			}
		}

		IEnumerable<string> idents = versions.Select( x =>
		{
			var packageIdent = x.Key;
			if ( x.Value.HasValue ) packageIdent += $"#{x.Value}";
			return packageIdent;
		} );

		var undownloaded = idents.Where( x => !IsInstalled( x ) ).ToArray();

		int total = undownloaded.Count();
		if ( total == 0 ) return true;

		Log.Info( $"Installing {total:n0} missing cloud assets.." );
		var tasks = undownloaded.Select( ident => InstallPackage( ident, total, token ) ).ToList();

		while ( !tasks.All( x => x.IsCompleted ) )
		{
			await Task.Delay( 10, token );
			token.ThrowIfCancellationRequested();
		}

		return !token.IsCancellationRequested;
	}

	static SemaphoreSlim packageDownloadSemaphore = new SemaphoreSlim( 8 );
	static int installedPackages;

	static async Task InstallPackage( string ident, int totalPackageNum, CancellationToken token )
	{
		try
		{
			await packageDownloadSemaphore.WaitAsync( token );

			Package package = await Package.FetchAsync( ident, false );
			if ( package == null )
				return;

			var asset = await AssetSystem.InstallAsync( package, false, null, token );

			var i = Interlocked.Add( ref installedPackages, 1 );

			Log.Info( $"[{i}/{totalPackageNum}] '{ident}' installed." );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error installing package {ident}" );
		}
		finally
		{
			packageDownloadSemaphore.Release();
		}
	}

	internal static async Task AddNewServerPackages()
	{
		// find out what stuff we're referencing in the project that's not already a part of the published project (a NEW package)
		// add these to our ServerPackages string table so connecting clients know to fetch these

		if ( IGameInstance.Current is null )
			return;

		var sw = System.Diagnostics.Stopwatch.StartNew();

		HashSet<string> filesInManifest = new( StringComparer.OrdinalIgnoreCase );

		var gamePackage = await Package.FetchAsync( IGameInstance.Current.Package.GetIdent( false, true ), false );
		if ( gamePackage is not null && gamePackage.Revision is not null )
		{
			await gamePackage.Revision.DownloadManifestAsync();

			foreach ( var file in gamePackage.Revision.Manifest.Files )
			{
				filesInManifest.Add( file.Path );
			}
		}

		var packages = GetAssetReferences( true );

		int count = 0;
		foreach ( var ident in packages )
		{
			if ( !Package.TryParseIdent( ident, out var parts ) )
				continue;

			var package = AssetSystem.CloudDirectory.FindPackage( Package.FormatIdent( parts.org, parts.package ) );
			if ( package is null )
				continue;

			string filepath = package.PrimaryAsset;
			if ( string.IsNullOrEmpty( filepath ) )
				continue;

			if ( !filepath.EndsWith( "_c" ) )
				filepath += "_c";

			if ( !filesInManifest.Contains( filepath ) )
			{
				ServerPackages.Current.AddRequirement( package );
				count++;
			}
		}

		Log.Info( $"Added new {count} cloud reference(s) to ServerPackage table.. (took {sw.Elapsed.TotalSeconds:0.000}s)" );
	}

	/// <summary>
	/// Gets all cloud packages referenced from assets
	/// </summary>
	public static HashSet<string> GetAssetReferences( bool currentProjectOnly )
	{
		string projectPath = Project.Current.GetAssetsPath().Replace( '\\', '/' );
		var packages = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		HashSet<string> validAssetPaths = null;
		if ( currentProjectOnly )
		{
			validAssetPaths = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

			// Include current project
			validAssetPaths.Add( projectPath );

			// Include all libraries used by the current project
			foreach ( var library in LibrarySystem.All )
			{
				var libraryAssetsPath = library.Project.GetAssetsPath()?.Replace( '\\', '/' );
				if ( !string.IsNullOrEmpty( libraryAssetsPath ) )
				{
					validAssetPaths.Add( libraryAssetsPath );
				}
			}
		}

		var gr = AssetSystem.All.Where( x => x.AssetType.IsGameResource && (!currentProjectOnly || validAssetPaths.Any( path => x.AbsolutePath.StartsWith( path, StringComparison.OrdinalIgnoreCase ) )) );
		foreach ( var r in gr )
		{
			string json = null;
			try
			{
				json = r.ReadJson();
				if ( string.IsNullOrWhiteSpace( json ) ) continue;

				if ( JsonNode.Parse( json ) is not JsonObject jso ) continue;
				if ( jso["__references"] is not JsonArray references ) continue;
				if ( references.Count == 0 ) continue;

				foreach ( var jsonNode in references )
				{
					string packageIdent = jsonNode.ToString();
					//Log.Info( $"{packageIdent} ({r.AbsolutePath})");
					packages.Add( packageIdent );
				}
			}
			catch ( JsonException e )
			{
				Log.Info( $"{r.AbsolutePath} - {e.Message}" );
				Log.Info( json );
			}
			catch ( Exception e )
			{
				Log.Info( $"{r.AbsolutePath} - {e.Message}" );
			}
		}

		var nativeResources = AssetSystem.All.Where( x => !x.AssetType.IsGameResource && (!currentProjectOnly || validAssetPaths.Any( path => x.AbsolutePath.StartsWith( path, StringComparison.OrdinalIgnoreCase ) )) ).ToArray();
		foreach ( var r in nativeResources )
		{
			var config = r?.Publishing?.ProjectConfig;
			if ( config is null ) continue;

			if ( config.EditorReferences is not null )
			{
				foreach ( var packageIdent in config.EditorReferences )
				{
					//Log.Info( $"{packageIdent} ({m.AbsolutePath})" );
					packages.Add( packageIdent );
				}
			}

			if ( config.DistinctPackageReferences is not null )
			{
				foreach ( var packageIdent in config.DistinctPackageReferences )
				{
					//Log.Info( $"{packageIdent} ({m.AbsolutePath})" );
					packages.Add( packageIdent );
				}
			}
		}

		return packages;
	}
}
