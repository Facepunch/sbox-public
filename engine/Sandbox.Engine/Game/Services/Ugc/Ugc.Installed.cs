using Steamworks;
using Steamworks.Data;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// Installing workshop items, and keeping track of the ones we've got
/// </summary>
internal static partial class Ugc
{
	/// <summary>
	/// Steam's EItemState
	/// </summary>
	[Flags]
	internal enum ItemState : uint
	{
		None = 0,
		Subscribed = 1,
		LegacyItem = 2,
		Installed = 4,
		NeedsUpdate = 8,
		Downloading = 16,
		DownloadPending = 32,
	}

	/// <summary>
	/// What Steam knows about a workshop item on this machine
	/// </summary>
	internal sealed class ItemInfo
	{
		public ulong PublishedFileId { get; set; }
		public ItemState State { get; set; }
		public string InstallFolder { get; set; }
		public ulong SizeOnDisk { get; set; }

		/// <summary>
		/// When the installed copy was last updated on the workshop, in unix seconds
		/// </summary>
		public long Timestamp { get; set; }

		public ulong BytesDownloaded { get; set; }
		public ulong BytesTotal { get; set; }

		public bool IsInstalled => State.HasFlag( ItemState.Installed ) && !string.IsNullOrEmpty( InstallFolder );
		public bool IsSubscribed => State.HasFlag( ItemState.Subscribed );
		public bool IsDownloading => (State & (ItemState.Downloading | ItemState.DownloadPending)) != 0;
		public bool NeedsDownload => !IsInstalled || State.HasFlag( ItemState.NeedsUpdate );
	}

	/// <summary>
	/// Every item on this machine, by workshop id - the ones we're subscribed to, and the ones we've downloaded to play
	/// </summary>
	static HashSet<ulong> installed = new();

	static HashSet<ulong> subscribed = new();

	/// <summary>
	/// Downloaded items we've told Steam it can delete. It does in its own time, and until then still lists them.
	/// </summary>
	static readonly HashSet<ulong> removed = new();

	/// <summary>
	/// How long a download can go without moving before we give up on it
	/// </summary>
	const double StallTimeout = 60.0;

	/// <summary>
	/// How long after we ask for a download we wait for Steam to start on it
	/// </summary>
	const double StartGrace = 15.0;

	/// <summary>
	/// Get what Steam knows about this item, without downloading anything. Null if there's no Steam.
	/// </summary>
	internal static ItemInfo GetItem( ulong id )
	{
		var json = NativeEngine.CUgcSubscriptions.GetItemJson( id );
		if ( string.IsNullOrEmpty( json ) ) return null;

		return Json.Deserialize<ItemInfo>( json );
	}

	/// <summary>
	/// Make sure this item is downloaded and up to date, and return the folder it's installed in.
	/// This doesn't subscribe to it. Returns null if it couldn't be installed.
	/// </summary>
	internal static async Task<string> InstallAsync( ulong id, Action<ItemInfo> progress = null, CancellationToken token = default )
	{
		// A dedicated server downloads through the game server, which might not be up yet
		if ( Application.IsDedicatedServer )
			await Sandbox.Network.DedicatedServer.LogOnToSteamAsync();

		var item = GetItem( id );
		if ( item is null ) return null;

		// Offline, what's installed is what we've got - Steam would leave a download pending forever
		if ( !IsSteamOnline() )
			return item.IsInstalled ? item.InstallFolder : null;

		// Being asked for it again, so it's not to be thrown away
		removed.Remove( id );

		// In use - what's mounted is what we've got, Steam rewriting files we're using breaks things
		if ( item.IsInstalled && IsMounted( id ) )
			return item.InstallFolder;

		var downloaded = false;

		if ( item.NeedsDownload || item.IsDownloading || await IsOutdatedAsync( item, token ) )
		{
			downloaded = true;

			if ( !RequestDownload( id ) )
			{
				if ( item.IsInstalled ) return item.InstallFolder;

				FailDownload( id, CouldntStart );
				return null;
			}

			// Steam marks it pending as soon as we ask for it, so wait until that's cleared - unless it stops
			// moving, Steam never gives up on a download by itself
			var lastBytes = item.BytesDownloaded;
			var lastMoved = RealTime.NowDouble;
			var asked = RealTime.NowDouble;

			do
			{
				await Task.Delay( 50, token );

				item = GetItem( id );
				if ( item is null ) return null;

				progress?.Invoke( item );

				if ( item.BytesDownloaded != lastBytes )
				{
					lastBytes = item.BytesDownloaded;
					lastMoved = RealTime.NowDouble;
				}
				else if ( RealTime.NowDouble - lastMoved > StallTimeout )
				{
					FailDownload( id, Stalled );
					return item.IsInstalled ? item.InstallFolder : null;
				}
			}
			// Steam can take a moment to pick it up - not downloading yet isn't done
			while ( item.IsDownloading || (item.NeedsDownload && RealTime.NowDouble - asked < StartGrace) );
		}

		// Steam's result has said why
		if ( !item.IsInstalled )
			return null;

		// So we know we've got it, if it's new
		if ( downloaded || !installed.Contains( id ) )
			Refresh();

		return item.InstallFolder;
	}

	static bool IsSteamOnline()
	{
		var user = NativeEngine.Steam.SteamUser();
		if ( user.IsValid )
			return user.BLoggedOn();

		// A dedicated server, which needs to be online to have got a workshop item at all
		return true;
	}

	/// <summary>
	/// Is this item's content mounted by a package right now? Steam mustn't rewrite it while it is.
	/// </summary>
	static bool IsMounted( ulong id )
	{
		return PackageManager.ActivePackages.Any( x => x.Package is { InstallsFromWorkshop: true } p && p.WorkshopId == id );
	}

	/// <summary>
	/// Download the latest version of this item - unless it's mounted, Steam rewriting files we're using
	/// breaks things. It's picked up again once it isn't, see <see cref="OnPackagesUnmounted"/>.
	/// </summary>
	static void StartUpdate( ulong id )
	{
		if ( !IsMounted( id ) )
			RequestDownload( id );
	}

	/// <summary>
	/// Packages were unmounted - anything that wanted updating while they were can be now
	/// </summary>
	internal static void OnPackagesUnmounted()
	{
		Refresh();
		_ = CheckForUpdatesAsync();
	}

	/// <summary>
	/// Are we subscribed to this item? Cheap, it's updated when the subscriptions change.
	/// </summary>
	internal static bool IsSubscribed( ulong id ) => subscribed.Contains( id );

	/// <summary>
	/// Subscribe to a workshop item, so Steam keeps it installed and up to date
	/// </summary>
	internal static void Subscribe( ulong id )
	{
		NativeEngine.CUgcSubscriptions.SubscribeItem( id );
		Log.Info( $"Subscribed to workshop item {NameOf( id )}" );

		// Steam tells us it's done in a callback, until then assume it worked
		subscribed.Add( id );
		removed.Remove( id );

		// Left to itself, Steam holds subscriptions back while the game's running - start it now
		StartUpdate( id );
	}

	/// <summary>
	/// Unsubscribe from a workshop item. Steam uninstalls it after the game quits.
	/// </summary>
	internal static void Unsubscribe( ulong id )
	{
		NativeEngine.CUgcSubscriptions.UnsubscribeItem( id );
		Log.Info( $"Unsubscribed from workshop item {NameOf( id )}" );
		subscribed.Remove( id );

		// Steam stops downloading it, stop showing it
		requested.Remove( id );
	}

	/// <summary>
	/// Is this an item we downloaded to play, rather than one we're subscribed to?
	/// </summary>
	internal static bool IsDownloadedOnly( ulong id ) => installed.Contains( id ) && !subscribed.Contains( id );

	/// <summary>
	/// Tell Steam it can delete an item we downloaded to play. Doesn't touch subscriptions.
	/// </summary>
	internal static void RemoveDownloaded( ulong id )
	{
		if ( subscribed.Contains( id ) ) return;

		NativeEngine.CUgcSubscriptions.MarkDownloadedItemAsUnused( id );
		removed.Add( id );
		installed.Remove( id );
	}

	/// <summary>
	/// The items the user's subscribed to - or every one downloaded on this machine, subscribed or not
	/// </summary>
	internal static ulong[] LocalItemIds( bool subscribedOnly ) => subscribedOnly ? subscribed.ToArray() : installed.ToArray();

	/// <summary>
	/// Start tracking what we've got, download any subscriptions that are missing, and update any that are out of date
	/// </summary>
	internal static void InitializeSubscriptions()
	{
		Dispatch.Install<ItemInstalled_t>( x =>
		{
			Log.Info( $"Workshop item {NameOf( x.PublishedFileId )} downloaded" );
			Refresh();
		} );
		Dispatch.Install<DownloadItemResult_t>( x => OnDownloadResult( x.PublishedFileId, x.Result ) );
		Dispatch.Install<RemoteStoragePublishedFileSubscribed_t>( x => Refresh() );
		Dispatch.Install<RemoteStoragePublishedFileUnsubscribed_t>( x => Refresh() );

		Refresh();

		_ = CheckForUpdatesAsync();
	}

	/// <summary>
	/// Re-read what we've got from Steam
	/// </summary>
	internal static void Refresh()
	{
		ThreadSafe.AssertIsMainThread();

		var subscribedItems = ReadItems( NativeEngine.CUgcSubscriptions.GetSubscribedJson() );
		var downloadedItems = ReadItems( NativeEngine.CUgcSubscriptions.GetDownloadedJson() );

		subscribed = subscribedItems.Select( x => x.PublishedFileId ).ToHashSet();

		// Steam downloads subscriptions when it gets round to it - ask for them, so they come down now and we
		// hear how they went
		foreach ( var item in subscribedItems.Where( x => x.NeedsDownload || x.IsDownloading ) )
		{
			if ( !requested.Contains( item.PublishedFileId ) )
				StartUpdate( item.PublishedFileId );
		}

		var found = new HashSet<ulong>();

		foreach ( var item in subscribedItems.Concat( downloadedItems ) )
		{
			if ( !item.IsInstalled )
				continue;

			if ( !subscribed.Contains( item.PublishedFileId ) && removed.Contains( item.PublishedFileId ) )
				continue;

			found.Add( item.PublishedFileId );
		}

		installed = found;
	}

	/// <summary>
	/// What to call an item in the log - its id, and its title if we know it
	/// </summary>
	static string NameOf( ulong id )
	{
		var title = details.GetValueOrDefault( id ).Title;
		return string.IsNullOrEmpty( title ) ? $"{id}" : $"{id} ({title})";
	}

	static ItemInfo[] ReadItems( string json )
	{
		return string.IsNullOrEmpty( json ) ? Array.Empty<ItemInfo>() : Json.Deserialize<ItemInfo[]>( json ) ?? Array.Empty<ItemInfo>();
	}

	/// <summary>
	/// Is the installed copy older than the one on the workshop? Steam's own "needs update" isn't reliable - it
	/// often doesn't know there's a newer version - so go by when the workshop says it was last updated.
	/// </summary>
	static bool IsOutdated( ItemInfo item, Workshop.Item latest )
	{
		return item is { IsInstalled: true } && latest.Updated.ToUnixTimeSeconds() > item.Timestamp;
	}

	/// <summary>
	/// <see cref="IsOutdated"/>, asking the workshop about the one item. False if it can't be asked.
	/// </summary>
	static async Task<bool> IsOutdatedAsync( ItemInfo item, CancellationToken token )
	{
		if ( !item.IsInstalled ) return false;

		var latest = await Workshop.FetchAsync( [item.PublishedFileId], token, 0 );
		return latest.Length > 0 && IsOutdated( item, latest[0] );
	}

	/// <summary>
	/// Update any subscriptions that are older than what's on the workshop
	/// </summary>
	static async Task CheckForUpdatesAsync()
	{
		if ( !IsSteamOnline() ) return;

		var ids = subscribed.Where( installed.Contains ).ToList();
		if ( ids.Count == 0 ) return;

		try
		{
			foreach ( var latest in await Workshop.FetchAsync( ids, default, 0 ) )
			{
				var item = GetItem( latest.Id );
				if ( item is null || item.IsDownloading ) continue;

				if ( IsOutdated( item, latest ) )
				{
					Log.Info( $"Workshop item {latest.Id} ({latest.Title}) has an update" );
					StartUpdate( latest.Id );
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't check workshop items for updates ({e.Message})" );
		}
	}
}
