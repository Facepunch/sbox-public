using Refit;
using Sandbox.Engine;
using Sandbox.Protobuf;
using System.Collections.Concurrent;
using System.Net.Http;
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
	public static bool TryParseIdent( string ident, out (string org, string package, int? version, bool local) parsed )
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

			if ( parts[1] == "local" )
			{
				parsed.local = true;
			}
			else if ( int.TryParse( parts[1], out var version ) )
			{
				parsed.version = version;
			}
			else
			{
				return false;
			}
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
	/// Produces a package ident of the form <c><paramref name="org"/>.<paramref name="package"/>[#<paramref name="local"/>|#<paramref name="version"/>]</c>.
	/// </summary>
	public static string FormatIdent( string org, string package, int? version = null, bool local = false )
	{
		return $"{org}.{package}{(local ? "#local" : version is { } v ? $"#{v}" : "")}";
	}

	/// <summary>
	/// Find package information
	/// </summary>
	public static async Task<Package> FetchAsync( string identString, bool partial ) => await FetchAsync( identString, partial, true );

	/// <summary>
	/// Find package information
	/// </summary>
	public static async Task<Package> FetchAsync( string identString, bool partial, bool useCache )
	{
		// split ident into parts
		if ( !TryParseIdent( identString, out var ident ) && !ident.local )
		{
			Log.Warning( $"Unable to fetch package info for {identString}: invalid ident." );
			return null;
		}

		Package package = default;

		if ( useCache && TryGetCached( identString, out package, partial ) )
			return package;

		// GetCached should have returned the local mock package
		if ( ident.local || string.Compare( "local", ident.org, StringComparison.OrdinalIgnoreCase ) == 0 )
		{
			return null;
		}

		if ( Backend.Package is null )
		{
			Log.Warning( $"Unable to fetch package info for {identString}: backend not available." );
			return null;
		}

		try
		{
			var packageIdent = $"{ident.org}.{ident.package}";
			if ( ident.version is not null ) packageIdent += $"#{ident.version}";
			var result = await Backend.Package.Get( packageIdent );
			if ( result is null ) return null;


			if ( package is not RemotePackage )
			{
				package = new RemotePackage();
			}

			(package as RemotePackage).UpdateFromDto( result );
		}
		catch ( HttpRequestException e )
		{
			Log.Warning( e, $"Failed to fetch package info for {identString}: {e.Message}" );
			return null;
		}
		catch ( ApiException e )
		{
			if ( e.StatusCode == System.Net.HttpStatusCode.NotFound )
			{
				Log.Warning( $"Failed to fetch package info for {identString}: package not found." );
			}
			else
			{
				Log.Warning( e, $"Failed to fetch package info for {identString}: {e.Message}" );
			}

			return null;
		}

		if ( package is not null )
		{
			Cache( package, false, ident.version );
		}
		else
		{
			Log.Warning( $"Failed to fetch package info for {identString}." );
		}

		return package;
	}

	/// <summary>
	/// Find package information
	/// </summary>
	public static bool TryGetCached( string identString, out Package package, bool allowPartial = true )
	{
		package = null;

		// split ident into parts
		if ( !TryParseIdent( identString, out var ident ) )
			return false;

		var fullIdent = FormatIdent( ident.org, ident.package, ident.version );

		// if it starts with local, then it's local - skip search anywhere else
		if ( ident.local )
		{
			package = GetMockPackages().FirstOrDefault( x => string.Equals( x.Ident, ident.package, StringComparison.OrdinalIgnoreCase ) && string.Equals( x.Org.Ident, ident.org, StringComparison.OrdinalIgnoreCase ) );

			// if this is a local package we need to say this is it
			// whether we found it or not. We don't want it to look up
			// the real package because that gets confusing.
			return true;
		}

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
		if ( PartialPackages.TryGetValue( ident, out var package ) )
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
		catch ( ApiException )
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

			IMenuDll.Current?.RunEvent( "package.update.users", usageChanged.PackageIdent, usageChanged.UserCount );
		}

		if ( msg.Data is PackageMsg.FavouritesChanged favouriteChanged )
		{
			UpdatePackage( favouriteChanged.PackageIdent, p => p.Favourited = (int)favouriteChanged.Value );
			IMenuDll.Current?.RunEvent( "package.update.favourites", favouriteChanged.PackageIdent, favouriteChanged.Value );
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
		if ( !TryParseIdent( identString, out var ident ) || ident.local )
			return null;

		var result = await Backend.Version.GetList( $"{ident.org}.{ident.package}" );
		if ( result is null )
			return null;

		return result.Select( x => PackageRevision.FromDto( x ) ).Cast<IRevision>().ToList();
	}
}
