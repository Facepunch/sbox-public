using System;

namespace Sandbox;

public static partial class MenuUtility
{
	/// <summary>
	/// Are we subscribed to this package's workshop item? Always false for packages that aren't on the workshop.
	/// Works with the partial packages search results give us, as long as we've seen the full one or it's installed.
	/// </summary>
	public static bool IsSubscribed( Package package )
	{
		var id = package?.WorkshopFilesId ?? 0;
		return id != 0 && Workshop.IsSubscribed( id );
	}

	/// <summary>
	/// Subscribe to this package's workshop item, so Steam keeps it installed and up to date, and it's
	/// available offline. Fetches the package first if all we've got is a search result. False if it
	/// isn't on the workshop.
	/// </summary>
	public static async Task<bool> SubscribeAsync( Package package )
	{
		var full = await FetchFullAsync( package );
		if ( full is not { InstallsFromWorkshop: true } ) return false;

		// So it can be played offline before it's ever been played
		full.RememberForOffline();

		Workshop.Subscribe( full.WorkshopId );
		return true;
	}

	/// <summary>
	/// Unsubscribe from this package's workshop item. Steam uninstalls it when the game quits.
	/// </summary>
	public static async Task<bool> UnsubscribeAsync( Package package )
	{
		var id = await GetWorkshopIdAsync( package );
		if ( id == 0 ) return false;

		Workshop.Unsubscribe( id );
		return true;
	}

	/// <summary>
	/// Open this package's Steam Workshop page - in the Steam overlay, or Steam itself without one. Fetches
	/// the package first if all we've got is a search result. False if it isn't on the workshop.
	/// </summary>
	public static async Task<bool> OpenWorkshopPageAsync( Package package )
	{
		var id = await GetWorkshopIdAsync( package );
		if ( id == 0 ) return false;

		if ( !Steamworks.SteamFriends.OpenWebOverlay( Workshop.GetUrl( id ) ) )
			OpenUrl( $"steam://url/CommunityFilePage/{id}" );

		return true;
	}

	/// <summary>
	/// Did we download this package's workshop item to play, rather than subscribe to it? Those can be removed.
	/// </summary>
	public static bool IsDownloadedOnly( Package package )
	{
		var id = package?.WorkshopFilesId ?? 0;
		return id != 0 && Workshop.IsDownloadedOnly( id );
	}

	/// <summary>
	/// Let Steam delete a package's workshop item we downloaded to play. It does in its own time.
	/// </summary>
	public static void RemoveDownloaded( Package package )
	{
		var id = package?.WorkshopFilesId ?? 0;
		if ( id != 0 ) Workshop.RemoveDownloaded( id );
	}

	/// <summary>
	/// The packages of the workshop items on this machine - just the ones subscribed to with
	/// <paramref name="subscribedOnly"/>, otherwise every one downloaded - of these package types (any, if none), with
	/// <paramref name="search"/> in their title. Works offline.
	/// </summary>
	public static async Task<Package[]> FindWorkshopPackagesAsync( string search, bool subscribedOnly, IEnumerable<string> packageTypes )
	{
		var typeNames = packageTypes?.ToArray() ?? [];
		var types = typeNames.Select( x => Enum.TryParse<Workshop.ItemType>( x, true, out var type ) ? type : (Workshop.ItemType?)null ).OfType<Workshop.ItemType>().ToArray();

		// Asked for types that aren't on the workshop - there's none of them here
		if ( typeNames.Length > 0 && types.Length == 0 )
			return [];

		var query = new Workshop.Query( search );

		foreach ( var type in types )
			query.WithType( type );

		var result = await (subscribedOnly ? query.WithSubscribed() : query.WithInstalled()).RunAsync();

		var packages = await Task.WhenAll( result.Items
			.Select( x => x.PackageIdent )
			.Where( x => x is not null )
			.Select( x => Package.FetchAsync( x, true ) ) );

		return packages.Where( x => x is not null ).ToArray();
	}

	/// <summary>
	/// Is this package's files on the Steam Workshop? Search results don't say, so this fetches the full package if
	/// it has to - it's cached after that.
	/// </summary>
	public static async Task<bool> IsOnWorkshopAsync( Package package )
	{
		return await GetWorkshopIdAsync( package ) != 0;
	}

	/// <summary>
	/// This package's workshop item - fetching the full package if all we've got is a search result. 0 if it isn't on the workshop.
	/// </summary>
	static async Task<ulong> GetWorkshopIdAsync( Package package )
	{
		if ( package is null ) return 0;

		var id = package.WorkshopFilesId;
		if ( id != 0 ) return id;

		return (await FetchFullAsync( package ))?.WorkshopFilesId ?? 0;
	}

	/// <summary>
	/// The full package - search results don't have its meta. Cached after the first fetch.
	/// </summary>
	static async Task<Package> FetchFullAsync( Package package )
	{
		if ( package is null ) return null;
		if ( package.InstallsFromWorkshop ) return package;

		return await Package.FetchAsync( package.FullIdent, false );
	}

	/// <summary>
	/// What's coming down from the workshop right now - subscriptions, and packages we're about to play.
	/// Only what Steam's actually sending. This asks Steam about each one, so poll it every so often rather
	/// than every frame.
	/// </summary>
	public static WorkshopDownload[] GetWorkshopDownloads() => Workshop.GetDownloads();

	/// <summary>
	/// Be told when a download ends - finished, or failed with <see cref="WorkshopDownload.Error"/> saying why.
	/// Ones that never got going end too, so only take it as finishing something you saw come down. Dispose
	/// what this returns to stop.
	/// </summary>
	public static IDisposable OnWorkshopDownloadEnded( Action<WorkshopDownload> ended )
	{
		Workshop.DownloadEnded += ended;
		return new Sandbox.Utility.DisposeAction( () => Workshop.DownloadEnded -= ended );
	}
}
