using System.IO;
using System.Threading;

namespace Sandbox;

internal static partial class PackageManager
{
	static Logger log = new Logger( "PackageManager" );

	/// <summary>
	/// The library used to load assemblies
	/// </summary>
	internal static AccessControl AccessControl { get; } = new AccessControl();

	public static BaseFileSystem MountedFileSystem { get; private set; } = new AggregateFileSystem();
	public static HashSet<ActivePackage> ActivePackages { get; private set; } = new HashSet<ActivePackage>();

	/// <summary>
	/// Called when a new package is installed
	/// </summary>
	public static event Action<ActivePackage, string> OnPackageInstalledToContext;

	static async Task<Package> FetchPackageAsync( string ident, bool localPriority )
	{
		if ( localPriority && Package.TryParseIdent( ident, out var parts ) && !parts.local )
		{
			if ( await Package.Fetch( $"{parts.org}.{parts.package}#local", false ) is Package package )
			{
				return package;
			}
		}

		return await Package.Fetch( ident, false );
	}


	/// <summary>
	/// Install a package
	/// </summary>
	internal static async Task<ActivePackage> InstallAsync( PackageLoadOptions options )
	{
		if ( options.PackageIdent == "local.base" )
			options.PackageIdent = "local.base#local";

		//
		// If this package exists then mark it with our tag and move on
		//
		var existingPackage = Find( options.PackageIdent, options.AllowLocalPackages );
		if ( existingPackage != null )
		{
			existingPackage.AddContextTag( options.ContextTag );
			log.Info( $"Install Package (Already Mounted) {options.PackageIdent} [{options.ContextTag}]" );
			return existingPackage;
		}

		log.Trace( $"Install Package {options.PackageIdent} [{options.ContextTag}]" );
		var package = await FetchPackageAsync( options.PackageIdent, options.AllowLocalPackages );

		options.CancellationToken.ThrowIfCancellationRequested();

		if ( package == null )
		{
			throw new FileNotFoundException( $"Unable to find package '{options.PackageIdent}'" );
		}

		//
		// If this package has dependencies then download them first
		//
		await InstallDependencies( package, options );

		var ap = await ActivePackage.Create( package, options.CancellationToken, options );
		options.CancellationToken.ThrowIfCancellationRequested();

		if ( package.IsRemote )
		{
			//
			// Games should always have code archives. If they don't then they probably pre-date code archives, and need to be updated.
			//
			if ( package.TypeName == "game" && !ap.HasCodeArchives() )
			{
				throw new System.Exception( "This game has no code archive!" );
			}

			if ( ap.HasCodeArchives() )
			{
				if ( !await ap.CompileCodeArchive() )
				{
					//
					// If there was a compile error in a game, report it to our backend so we can keep tabs.
					//
					if ( package.TypeName == "game" )
					{
						throw new System.Exception( "There were errors when compiling this game!" );
					}

					Log.Warning( "There were errors when compiling this game!" );
				}
			}
		}

		ap.AddContextTag( options.ContextTag );
		return ap;
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
		foreach ( var item in ActivePackages.Where( x => x.Tags.Count() == 0 ).ToArray() )
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

		bool hasLocalBase = false;

		//
		// This is the right way to reference packages. We should move everything else
		// to use this.
		//
		foreach ( var i in package.EnumeratePackageReferences() )
		{
			dependancies.Add( i );

			// if we have a gamemode reference - then that contains the base library!
			if ( package.TypeName == "game" )
			{
				hasLocalBase = true;
			}
		}

		if ( package is LocalPackage packageLocal )
		{
			//
			// Hack Sadface: If this is a local game then include the base as a dependency
			//
			if ( !hasLocalBase && packageLocal.NeedsLocalBasePackage() )
			{
				dependancies.Add( "local.base#local" );
			}
		}

		//
		// Install them all
		//
		foreach ( var packageName in dependancies )
		{
			await InstallAsync( options with { PackageIdent = packageName } );
			options.CancellationToken.ThrowIfCancellationRequested();
		}

		options.CancellationToken.ThrowIfCancellationRequested();
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
				await InstallAsync( new PackageLoadOptions() { PackageIdent = project.Package.FullIdent, ContextTag = "local", CancellationToken = token, AllowLocalPackages = true } );
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
	/// Retrieve a package by ident.
	/// </summary>
	internal static ActivePackage Find( string packageIdent )
	{
		return ActivePackages.Where( x => x.Package.IsNamed( packageIdent ) ).First();
	}

	/// <summary>
	/// Retrieve a package by ident and minimum download mode.
	/// </summary>
	internal static ActivePackage Find( string packageIdent, bool allowLocalPackages, bool exactName = false )
	{
		// don't search for exact name if it starts with local
		// because it might be #local, or not
		if ( packageIdent.StartsWith( "local." ) )
			exactName = false;

		return ActivePackages.FirstOrDefault( x =>
			(exactName ? string.Equals( x.Package.FullIdent, packageIdent, StringComparison.OrdinalIgnoreCase ) : x.Package.IsNamed( packageIdent ))
			&& (allowLocalPackages || x.Package is not LocalPackage) );
	}
}

