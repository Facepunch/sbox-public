using Refit;
using Sandbox.Internal;
using Sandbox.Protobuf;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sandbox;

public partial class Package
{
	static ConcurrentDictionary<string, Package> Packages = new( StringComparer.OrdinalIgnoreCase );
	static ConcurrentDictionary<string, Package> PartialPackages = new( StringComparer.OrdinalIgnoreCase );

	static Package()
	{
		Sandbox.Services.Messaging.OnMessage += HandleMessage;
	}

	internal static void ClearCache()
	{
		Packages.Clear();
		PartialPackages.Clear();
	}

	/// <summary>
	/// Remove a specific package from the cache
	/// </summary>
	internal static void ClearCache( string packageIdent )
	{
		Packages.Remove( packageIdent, out _ );
		PartialPackages.Remove( packageIdent, out _ );
	}

	/// <summary>
	/// Parse a package ident into parts. There are a few different formats you can pass to this.
	/// 
	///  - org/package
	///  - org.package
	///  - org.package#version
	///  - https://sbox.game/org/package
	///  - https://sbox.game/org/package#version
	///  
	///  If package version isn't specified version will be null
	/// 
	/// </summary>
	public static bool TryParseIdentParts( string ident, out (string org, string package, int? version) parsed )
	{
		parsed = default;

		if ( string.IsNullOrEmpty( ident ) )
			return false;

		//
		// url
		//
		if ( ident.Contains( "https://asset.party/" ) || ident.Contains( "http://asset.party/" ) )
		{
			var top = "://asset.party/";
			var idx = ident.IndexOf( top );
			if ( idx <= 0 ) return false;

			ident = ident.Substring( idx + top.Length );
		}

		//
		// url
		//
		if ( ident.Contains( "https://sbox.game/" ) || ident.Contains( "http://sbox.game/" ) )
		{
			var top = "://sbox.game/";
			var idx = ident.IndexOf( top );
			if ( idx <= 0 ) return false;

			ident = ident.Substring( idx + top.Length );
		}

		//
		// #version
		//
		if ( ident.Contains( '#' ) )
		{
			var parts = ident.Split( '#', StringSplitOptions.RemoveEmptyEntries );
			if ( parts.Length != 2 ) return false;

			ident = parts[0];

			if ( !int.TryParse( parts[1], out var version ) ) return false;

			parsed.version = version;
		}

		//
		// org.package or org/package
		//
		if ( ident.Contains( '.' ) || ident.Contains( '/' ) )
		{
			var parts = ident.Split( new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries );
			if ( parts.Length != 2 ) return false;

			parsed.org = parts[0];
			parsed.package = parts[1];
		}

		if ( string.IsNullOrEmpty( parsed.package ) )
			return false;

		if ( string.IsNullOrEmpty( parsed.org ) )
			return false;

		return true;
	}

	/// <summary>
	/// Parses a package identifier. The legacy local flag is always false.
	/// </summary>
	[Obsolete( "Use TryParseIdentParts instead." )]
	public static bool TryParseIdent( string ident, out (string org, string package, int? version, bool local) parsed )
	{
		var success = TryParseIdentParts( ident, out var parts );
		parsed = (parts.org, parts.package, parts.version, false);
		return success;
	}

	/// <summary>
	/// Formats a package identifier with an optional published revision.
	/// </summary>
	[OverloadResolutionPriority( 1 )]
	public static string FormatIdent( string org, string package, int? version = null )
	{
		return version is null ? $"{org}.{package}" : $"{org}.{package}#{version}";
	}

	/// <summary>
	/// Formats an unversioned package identifier. The legacy local flag is ignored.
	/// </summary>
	[Obsolete( "Use FormatIdent without the local argument." )]
	public static string FormatIdent( string org, string package, bool local )
	{
		return FormatIdent( org, package );
	}

	/// <summary>
	/// Formats a package identifier. The legacy local flag is ignored.
	/// </summary>
	[Obsolete( "Use FormatIdent without the local argument." )]
	public static string FormatIdent( string org, string package, int? version, bool local )
	{
		return FormatIdent( org, package, version );
	}

	/// <summary>
	/// Find package information, preferring registered local projects on disk.
	/// </summary>
	public static async Task<Package> FetchAsync( string identString, bool partial ) => await FetchAsync( identString, partial, true );

	/// <summary>
	/// Find package information, preferring registered local projects even when bypassing the remote cache.
	/// </summary>
	public static async Task<Package> FetchAsync( string identString, bool partial, bool useCache )
	{
		// split ident into parts
		if ( !TryParseIdentParts( identString, out var ident ) )
		{
			Log.Warning( $"Unable to fetch package info for {identString}: invalid ident." );
			return null;
		}

		if ( Project.FindByIdent( ident.org, ident.package ) is { } project )
			return project.Package;

		var packageIdent = FormatIdent( ident.org, ident.package, ident.version );
		if ( useCache && TryGetCachedRemote( packageIdent, out var cached, partial ) )
			return cached;

		if ( Backend.Package is null )
		{
			if ( FetchOffline( $"{ident.org}.{ident.package}" ) is { } installed )
				return installed;

			Log.Warning( $"Unable to fetch package info for {identString}: backend not available." );
			return null;
		}

		// A fetch already in flight for this ident answers everyone who asks meanwhile
		var fetch = Fetching.GetOrAdd( packageIdent, _ => FetchFromBackendAsync( identString, packageIdent, ident.version ) );
		try
		{
			var remote = await fetch;
			return Project.FindByIdent( ident.org, ident.package )?.Package ?? remote;
		}
		finally
		{
			Fetching.TryRemove( new KeyValuePair<string, Task<Package>>( packageIdent, fetch ) );
		}
	}

	static readonly ConcurrentDictionary<string, Task<Package>> Fetching = new( StringComparer.OrdinalIgnoreCase );

	static async Task<Package> FetchFromBackendAsync( string identString, string packageIdent, int? version )
	{
		try
		{
			var result = await Backend.Package.Get( packageIdent );
			if ( result is null ) return null;

			var package = new RemotePackage();
			package.UpdateFromDto( result );
			Cache( package, false, version );
			return package;
		}
		catch ( Exception e ) when ( e is HttpRequestException or TaskCanceledException )
		{
			// Offline, or the backend's timing out
			if ( FetchOffline( StripVersion( packageIdent ) ) is { } installed )
				return installed;

			Log.Warning( e, $"Failed to fetch package info for {identString}: {e.Message}" );
			return null;
		}
		catch ( ApiException e )
		{
			if ( e.StatusCode == System.Net.HttpStatusCode.NotFound )
			{
				Log.Warning( $"Failed to fetch package info for {identString}: package not found." );
				return null;
			}

			if ( FetchOffline( StripVersion( packageIdent ) ) is { } installed )
				return installed;

			Log.Warning( e, $"Failed to fetch package info for {identString}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Mount a package by ident. This is the same as FetchAsync but it also mounts the package, which means it will be available for use right away.
	/// If you just want the package information, use FetchAsync.
	/// </summary>
	public static async Task<Package> MountAsync( string identString, bool partial )
	{
		var pack = await FetchAsync( identString, partial, false );
		await pack.MountAsync();
		return pack;
	}

	/// <summary>
	/// Find package information
	/// </summary>
	public static bool TryGetCached( string identString, out Package package, bool allowPartial = true )
	{
		package = null;
		if ( !TryParseIdentParts( identString, out var ident ) ) return false;

		package = Project.FindByIdent( ident.org, ident.package )?.Package;
		if ( package is not null ) return true;

		var fullIdent = FormatIdent( ident.org, ident.package, ident.version );
		return TryGetCachedRemote( fullIdent, out package, allowPartial );
	}

	/// <summary>
	/// Find cached published metadata by its normalized ident, without resolving local projects.
	/// </summary>
	internal static bool TryGetCachedRemote( string fullIdent, out Package package, bool allowPartial = true )
	{
		if ( Packages.TryGetValue( fullIdent, out package ) )
			return true;

		if ( allowPartial && PartialPackages.TryGetValue( fullIdent, out package ) )
			return true;

		return false;
	}

	/// <summary>
	/// Find package information
	/// </summary>
	public static async Task<Package> Fetch( string identString, bool partial )
	{
		return await FetchAsync( identString, partial );
	}

	/// <summary>
	/// If we have this package information, try to get its name
	/// </summary>
	public static string GetCachedTitle( string ident )
	{
		if ( TryGetCached( ident, out var package ) )
			return package.Title;

		return ident;
	}

	internal static void Cache( IEnumerable<Package> list, bool partial )
	{
		foreach ( var p in list )
		{
			Cache( p, partial );
		}
	}

	internal static void Cache( Package package, bool partial, long? version = null )
	{
		var fullIdent = version is { } v ? $"{package.FullIdent}#{v}" : package.FullIdent;

		if ( partial )
		{
			PartialPackages[fullIdent] = package;
		}
		else
		{
			Packages[fullIdent] = package;
			PartialPackages[fullIdent] = package;
		}
	}

	/// <summary>
	/// These packages are created from local addons, and should be the only way 99% of systems interact with local addons.
	/// </summary>
	internal static IEnumerable<Package> GetMockPackages( string filter = null )
	{
		// return all the installed games
		foreach ( var addon in Project.All )
		{
			if ( !addon.Active ) continue;

			yield return addon.Package;
		}
	}

	/// <summary>
	/// Retrieve a list of packages
	/// </summary>
	public static async Task<FindResult> FindAsync( string query, int take = 200, int skip = 0, CancellationToken token = default )
	{
		ArgumentException.ThrowIfNullOrEmpty( query );

		// If the query is for local, we redirect to the local packages
		if ( query.Split( ' ' ).Contains( "local:true" ) )
		{
			var list = GetMockPackages( query ).ToArray();

			return new FindResult
			{
				Milliseconds = 10,
				Packages = list
			};
		}

		try
		{
			var l = await Backend.Package.Find( query, take, skip );
			return FindResult.FromDto( l );
		}
		catch ( Exception e ) when ( e is ApiException or HttpRequestException or TaskCanceledException )
		{
			return new FindResult { Packages = Array.Empty<Package>(), TotalCount = 0, Tags = Array.Empty<TagEntry>(), Orders = Array.Empty<SortOrder>() };
		}
	}

	/// <summary>
	/// Retrieve a list of packages, organised into groups, for discovery
	/// </summary>
	public static async Task<ListResult> ListAsync( string id, CancellationToken token = default )
	{
		var result = await Backend.Package.GetList( id );
		if ( result.Groupings is null )
			return null;

		return ListResult.From( result );
	}

	/// <summary>
	/// Sort the given list of packages so that referenced packages are ordered before the packages that reference them.
	/// </summary>
	/// <param name="unordered">Unordered list of packages.</param>
	/// <returns>A new enumerable, ordered to maintain references.</returns>
	public static IEnumerable<Package> SortByReferences( IEnumerable<Package> unordered )
	{
		return SortByReferences( unordered, x => x );
	}

	/// <summary>
	/// Sort the given list of items so that referenced packages are ordered before the packages that reference them.
	/// </summary>
	/// <param name="unordered">Unordered list of items with a corresponding package.</param>
	/// <param name="getPackageFunc">Delegate that maps each item to its corresponding package.</param>
	/// <returns>A new enumerable, ordered to maintain references.</returns>
	public static IEnumerable<T> SortByReferences<T>( IEnumerable<T> unordered, Func<T, Package> getPackageFunc )
	{
		var remaining = new List<T>( unordered );

		if ( remaining.Count <= 1 )
		{
			return remaining;
		}

		var sorted = new List<T>();

		while ( remaining.Count > 0 )
		{
			var addedPackage = false;

			foreach ( var item in remaining )
			{
				var referencesRemaining = false;
				var package = getPackageFunc( item );

				foreach ( var reference in package.EnumeratePackageReferences() )
				{
					if ( package.IsNamed( reference ) )
					{
						// Package references itself - let's pretend that didn't happen
						continue;
					}

					if ( remaining.Any( x => getPackageFunc( x ).IsNamed( reference ) ) )
					{
						referencesRemaining = true;
						break;
					}
				}

				if ( referencesRemaining )
				{
					continue;
				}

				remaining.Remove( item );
				sorted.Add( item );

				addedPackage = true;
				break;
			}

			if ( !addedPackage )
			{
				// Throwing here would also be an option, but let's have a go at continuing

				Log.Error( $"Cyclic dependency found between packages: [ {string.Join( ", ", remaining.Select( x => getPackageFunc( x ).FullIdent ) )} ]" );

				var first = remaining.MinBy( x => getPackageFunc( x ).EnumeratePackageReferences().Count() );

				remaining.Remove( first );
				sorted.Add( first );
			}
		}

		return sorted;
	}

	static void UpdatePackage( string packageIdent, Action<Package> action )
	{
		if ( Packages.TryGetValue( packageIdent, out var p1 ) )
		{
			action( p1 );
		}

		if ( PartialPackages.TryGetValue( packageIdent, out var p2 ) )
		{
			action( p2 );
		}
	}

	static void HandleMessage( Sandbox.Services.Messaging.Message msg )
	{
		if ( msg.Data is PackageMsg.UsageChanged usageChanged )
		{
			UpdatePackage( usageChanged.PackageIdent, p =>
			{
				var u = p.Usage;
				u.UsersNow = usageChanged.UserCount;
				p.Usage = u;
			} );

			IMenuSystem.Current?.PackageUsageChanged( usageChanged.PackageIdent, usageChanged.UserCount );
		}

		if ( msg.Data is PackageMsg.FavouritesChanged favouriteChanged )
		{
			UpdatePackage( favouriteChanged.PackageIdent, p => p.Favourited = (int)favouriteChanged.Value );
			IMenuSystem.Current?.PackageFavouritesChanged( favouriteChanged.PackageIdent, favouriteChanged.Value );
		}

		if ( msg.Data is PackageMsg.VotesChanged votesChanged )
		{
			UpdatePackage( votesChanged.PackageIdent, p =>
			{
				p.VotesUp = (int)votesChanged.VotesUp;
				p.VotesDown = (int)votesChanged.VotesDown;
			} );
		}

		if ( msg.Data is PackageMsg.Update packageUpdated )
		{
			ClearCache( packageUpdated.PackageIdent );
		}

		if ( msg.Data is PackageMsg.Changed packageChanged )
		{
			ClearCache( packageChanged.PackageIdent );
		}
	}


	/// <summary>
	/// Get package version list
	/// </summary>
	public static async Task<List<IRevision>> FetchVersions( string identString, CancellationToken token = default )
	{
		// split ident into parts
		if ( !TryParseIdentParts( identString, out var ident ) )
			return null;

		if ( Backend.Version is not { } versions ) return null;

		var result = await versions.GetList( FormatIdent( ident.org, ident.package ) ).WaitAsync( token );
		if ( result is null )
			return null;

		return result.Select( x => PackageRevision.FromDto( x ) ).Cast<IRevision>().ToList();
	}
}
