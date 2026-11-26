using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// A dedicated server has no Steam user to subscribe to anything. What it's configured with -
/// <see cref="Workshop.HostWorkshopCollection"/> and <see cref="Workshop.ServerItemsFile"/> - stands in for its
/// subscriptions, so whatever asks what's subscribed finds those.
/// </summary>
internal static partial class Ugc
{
	/// <summary>
	/// A dedicated server's items, once they've been worked out
	/// </summary>
	static HashSet<ulong> serverItems = new();

	/// <summary>
	/// Working out and installing the server's items
	/// </summary>
	static Task serverItemsLoad;

	/// <summary>
	/// On a dedicated server, wait until its items are known and installed. Does nothing anywhere else.
	/// This happens once - if it fails, the server goes without them.
	/// </summary>
	internal static Task WaitForServerItemsAsync( CancellationToken token = default )
	{
		if ( !Application.IsDedicatedServer ) return Task.CompletedTask;

		serverItemsLoad ??= LoadServerItemsAsync();
		return serverItemsLoad.WaitAsync( token );
	}

	static async Task LoadServerItemsAsync()
	{
		Workshop.Item[] items;
		try
		{
			items = await Workshop.GetServerItemsAsync();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't work out the server's Workshop items ({e.Message})" );
			return;
		}

		ThreadSafe.AssertIsMainThread();

		serverItems = items.Select( x => x.Id ).ToHashSet();
		subscribed = serverItems;

		if ( items.Length == 0 ) return;

		Log.Info( $"Installing {items.Length} Workshop item(s)" );

		var failed = 0;
		for ( var i = 0; i < items.Length; i++ )
		{
			if ( !await InstallServerItemAsync( items[i], i + 1, items.Length ) )
				failed++;
		}

		Log.Info( failed == 0 ? "Workshop complete" : $"Workshop complete, {failed} item(s) failed" );
	}

	/// <summary>
	/// Install or update one of the server's items, saying how it's going in the console. False if it couldn't be.
	/// </summary>
	static async Task<bool> InstallServerItemAsync( Workshop.Item item, int number, int count )
	{
		Log.Info( $"Processing item {number}/{count}: {item.Title} ({item.Id})..." );

		var needsDownload = GetItem( item.Id )?.NeedsDownload ?? true;
		if ( needsDownload )
			Log.Info( "   Item needs downloading..." );

		var downloading = false;
		var lastReported = -1f;

		void Progress( ItemInfo info )
		{
			if ( !downloading )
			{
				downloading = true;

				if ( !needsDownload )
					Log.Info( "   Item has an update, downloading..." );
			}

			if ( info.BytesTotal == 0 || lastReported >= 1f ) return;

			var fraction = (float)((double)info.BytesDownloaded / info.BytesTotal);
			if ( fraction < 1f && fraction - lastReported < 0.1f ) return;

			lastReported = fraction;
			Log.Info( $"   Downloading [ {fraction * 100:0}% of {info.BytesTotal.FormatBytes()} ]" );
		}

		try
		{
			if ( await InstallAsync( item.Id, Progress ) is null )
			{
				// Why it failed has already been said, if Steam told us
				Log.Warning( "   Failed!" );
				return false;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"   Failed! ({e.Message})" );
			return false;
		}

		Log.Info( downloading ? "   Downloaded!" : "   Up to date!" );
		return true;
	}
}
