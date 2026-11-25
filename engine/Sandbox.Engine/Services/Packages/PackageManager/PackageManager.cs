using Sandbox.Menu;
using System.IO;
using System.Threading;

namespace Sandbox;

internal static partial class PackageManager
{
	static Logger log = new Logger( "PackageManager" );

	/// <summary>
	/// Resolves Cecil assembly definitions for hotload. Package assemblies are supplied on demand.
	/// </summary>
	internal static PackageAssemblyResolver AssemblyResolver { get; } = new PackageAssemblyResolver { PackageBytes = GetPackageAssemblyBytes };

	private static byte[] GetPackageAssemblyBytes( string assemblyName )
	{
		var filename = $"{assemblyName}.dll";
		foreach ( var ap in ActivePackages )
		{
			if ( ap.AssemblyFileSystem?.FileExists( filename ) != true ) continue;
			return ap.AssemblyFileSystem.ReadAllBytes( filename ).ToArray();
		}
		return null;
	}

	public static BaseFileSystem MountedFileSystem { get; private set; } = new AggregateFileSystem();

	/// <summary>
	/// Goes up whenever a package's files are mounted or unmounted, so a reset can tell nothing's changed since the last.
	/// </summary>
	internal static int MountGeneration;
	public static HashSet<ActivePackage> ActivePackages { get; private set; } = new HashSet<ActivePackage>();

	/// <summary>
	/// Called when a new package is installed
	/// </summary>
	public static event Action<ActivePackage, string> OnPackageInstalledToContext;

	/// <summary>
	/// Install a package
	/// </summary>
	internal static async Task<ActivePackage> InstallAsync( PackageLoadOptions options )
	{
		options.CancellationToken.ThrowIfCancellationRequested();

		var existing = Find( options.PackageIdent );
		var package = Project.FindByIdent( options.PackageIdent )?.Package ?? existing?.Package;
		if ( package is null && options.Package is not LocalPackage )
			package = options.Package;

		package ??= await Package.FetchAsync( options.PackageIdent, false );
		if ( package is null )
			throw new FileNotFoundException( $"Unable to find package '{options.PackageIdent}'" );

		if ( package.IsRemoteMap )
			throw new InvalidOperationException( $"Load map '{package.FullIdent}' by Workshop item ID or asset path." );

		if ( package is LocalPackage local )
		{
			if ( local.Project.Broken )
				throw new InvalidOperationException( $"Couldn't load local project '{local.FullIdent}'" );

			if ( local.Project.NeedsInstallCompilation )
			{
				options.Loading?.LoadingProgress( LoadingProgress.Create( $"Compiling {local.Title}" ) );
				if ( await Project.AddGameFromFile( local.Project.ConfigFilePath ) is null )
					throw new InvalidOperationException( $"Couldn't load local project '{local.FullIdent}'" );
			}
		}

		options.CancellationToken.ThrowIfCancellationRequested();
		if ( existing?.Package == package && existing.FileSystem is not null )
		{
			existing.AddContextTag( options.ContextTag );
			options.Loading?.LoadingProgress( LoadingProgress.Create( $"Loading {package.Title}" ) );
			return existing;
		}

		options.Loading?.LoadingProgress( LoadingProgress.Create( $"Loading {package.Title}" ) );

		//
		// Dependencies install one at a time and before this package, so they mount in a fixed order.
		// Their files and ours all download at once in the background, and each install finds them cached.
		//
		using var prefetchCancel = CancellationTokenSource.CreateLinkedTokenSource( options.CancellationToken );
		var prefetch = options.IsDependency || options.SkipAssetDownload || !package.EnumerateInstallDependencies().Any()
			? Task.CompletedTask
			: PrefetchAsync( package, true, prefetchCancel.Token );

		try
		{
			await InstallDependencies( package, options with { IsDependency = true } );
			options.CancellationToken.ThrowIfCancellationRequested();

			// Another request can finish mounting this project while its dependencies install.
			if ( Find( options.PackageIdent ) is { } mounted )
			{
				mounted.AddContextTag( options.ContextTag );
				return mounted;
			}

			var prepared = await ActivePackage.Create( package, options.CancellationToken, options );
			var registered = Project.FindByIdent( options.PackageIdent )?.Package;

			// Project registration can change while published files download or compile.
			if ( registered != (package as LocalPackage) )
			{
				prepared.DisposeFileSystems();
				return await InstallAsync( options with { Package = null } );
			}

			if ( Find( options.PackageIdent ) is { } installed )
			{
				prepared.DisposeFileSystems();
				installed.AddContextTag( options.ContextTag );
				return installed;
			}

			prepared.Activate( options );
			return prepared;
		}
		finally
		{
			prefetchCancel.Cancel();
			await prefetch;
		}
	}

	public static void UnmountTagged( string tag )
	{
		log.Trace( $"Removing tags '{tag}'" );

		foreach ( var item in ActivePackages )
		{
			item.RemoveContextTag( tag );
		}

		UnmountUntagged();
	}

	private static void UnmountUntagged()
	{
		var unmounted = ActivePackages.Where( x => x.Tags.Count() == 0 ).ToArray();

		foreach ( var item in unmounted )
		{
			log.Trace( $"Unmounting '{item.Package.FullIdent}' - no tags remaining" );

			item.Delete();
			ActivePackages.Remove( item );
		}
	}

	internal static void UnmountAll()
	{
		foreach ( var item in ActivePackages.ToArray() )
		{
			item.Delete();
			ActivePackages.Remove( item );
		}
	}

	private static async Task InstallDependencies( Package package, PackageLoadOptions options )
	{
		HashSet<string> dependancies = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		//
		// This is the right way to reference packages. We should move everything else
		// to use this.
		//
		foreach ( var i in package.EnumerateInstallDependencies() )
		{
			dependancies.Add( i );
		}

		//
		// Install them all
		//
		foreach ( var packageName in dependancies )
		{
			await InstallAsync( options with { PackageIdent = packageName, Package = null } );
			options.CancellationToken.ThrowIfCancellationRequested();
		}

		options.CancellationToken.ThrowIfCancellationRequested();
	}

	/// <summary>
	/// Download the files of everything a package depends on into the asset cache, all at once, and the
	/// package's own files with <paramref name="includeRoot"/>. Nothing is mounted. Never throws, the
	/// installs report any failure.
	/// </summary>
	internal static async Task PrefetchAsync( Package root, bool includeRoot, CancellationToken token )
	{
		// Keep upgrades paused across the entire dependency tree, including manifest fetches.
		using var textureDownload = TextureWebStreaming.BeginDownload();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		bool Unseen( string ident ) { lock ( seen ) return seen.Add( ident ); }

		async Task Prefetch( Package package, bool own )
		{
			var dependencies = package.EnumerateInstallDependencies().Where( Unseen ).ToArray();

			var fetches = dependencies.Select( async ident =>
			{
				if ( Find( ident ) is not null ) return;
				if ( await Package.FetchAsync( ident, false ) is Package dependency )
					await Prefetch( dependency, true );
			} );

			var files = own && package.IsRemote ? package.Prefetch( token ) : Task.CompletedTask;
			await Task.WhenAll( fetches.Append( files ) );
		}

		try
		{
			await Prefetch( root, includeRoot );
		}
		catch ( OperationCanceledException ) { }
		catch ( Exception e )
		{
			log.Trace( $"Prefetching {root.FullIdent} failed: {e.Message}" );
		}
	}

	/// <summary>
	/// Install all of the projects as packages
	/// </summary>
	internal static async Task InstallProjects( Project[] projects, CancellationToken token = default )
	{
		foreach ( var project in projects )
		{
			try
			{
				// install this package
				await InstallAsync( new PackageLoadOptions() { PackageIdent = project.Package.FullIdent, ContextTag = "local", CancellationToken = token } );
			}
			catch ( Exception ex )
			{
				log.Warning( ex, $"Error installing local package {project.Package.FullIdent}: {ex.Message}" );
			}
		}

		var removedPackages = ActivePackages
			.Where( x => x.Package is LocalPackage )
			.Where( x => !projects.Any( y => y.Package == x.Package ) )
			.ToArray();

		// loop through each local package
		// remove any that aren't in our list
		foreach ( var package in removedPackages )
		{
			package.RemoveContextTag( "local" );
			log.Trace( $"Remove local package {package.Package.FullIdent}" );
		}

		// we might have packages that can be removed now
		if ( removedPackages.Length > 0 )
		{
			UnmountUntagged();
		}
	}

	/// <summary>
	/// Retrieve a package by ident and minimum download mode.
	/// </summary>
	internal static ActivePackage Find( string packageIdent, bool exactName = false )
	{
		if ( Project.FindByIdent( packageIdent ) is { } project )
		{
			var local = project.Package;
			return ActivePackages.FirstOrDefault( x => x.Package == local );
		}

		return ActivePackages.FirstOrDefault( x => x.Package is not LocalPackage && (exactName
			? string.Equals( x.Package.FullIdent, packageIdent, StringComparison.OrdinalIgnoreCase )
			: x.Package.IsNamed( packageIdent )) );
	}
}
