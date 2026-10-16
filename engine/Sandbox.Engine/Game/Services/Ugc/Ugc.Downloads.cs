namespace Sandbox.Services;

/// <summary>
/// What's coming down from the workshop, for showing it. Every download starts with us asking Steam for it
/// (<see cref="RequestDownload"/>) and ends with Steam saying how it went (<see cref="DownloadEnded"/>) - the
/// way Steam means it to be used. In between, how far it's got is Steam's to say, whenever someone asks.
/// </summary>
internal static partial class Ugc
{
	record struct Details( string Title, string Thumb );

	/// <summary>
	/// A download's ended - finished, or failed and why. Ones that never got going (an update check on
	/// something that's already up to date) end too, so only take it as finishing something you saw come down.
	/// </summary>
	internal static event Action<WorkshopDownload> DownloadEnded;

	/// <summary>
	/// Items we've asked Steam for, until it says how they went
	/// </summary>
	static readonly HashSet<ulong> requested = new();

	/// <summary>
	/// What we know to call each item. Items we haven't installed yet are looked up on the workshop.
	/// </summary>
	static readonly Dictionary<ulong, Details> details = new();

	/// <summary>
	/// Ask Steam for the latest of this item, at high priority - Steam holds normal ones back while the game's
	/// running. False if Steam won't; otherwise <see cref="DownloadEnded"/> says how it went.
	/// </summary>
	static bool RequestDownload( ulong id )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !NativeEngine.CUgcSubscriptions.DownloadItem( id, true ) )
			return false;

		requested.Add( id );

		// Find out what it's called while Steam gets going, so it has a name by the time it's coming down
		GetDetails( id );
		return true;
	}

	/// <summary>
	/// Steam says how a download went
	/// </summary>
	static void OnDownloadResult( ulong id, Steamworks.Result result )
	{
		if ( !requested.Contains( id ) ) return;

		if ( result == Steamworks.Result.OK )
			End( id, null );
		else
			FailDownload( id, Explain( result ) );
	}

	/// <summary>
	/// A download didn't work out - say so, and why
	/// </summary>
	static void FailDownload( ulong id, Failure failure )
	{
		Log.Warning( $"Workshop item {id} didn't download: {failure.Message} - {failure.Help}" );
		End( id, failure );
	}

	static void End( ulong id, Failure? failure )
	{
		requested.Remove( id );
		DownloadEnded?.Invoke( Describe( id, 0, 0 ) with { Error = failure?.Message, ErrorHelp = failure?.Help } );
	}

	/// <summary>
	/// What's coming down from the workshop right now - only what Steam's actually sending, not what it's
	/// still to start. This asks Steam about each one, so poll it, don't call it every frame.
	/// </summary>
	internal static WorkshopDownload[] GetDownloads()
	{
		ThreadSafe.AssertIsMainThread();

		// Nothing to say - which is most of the time
		if ( requested.Count == 0 )
			return Array.Empty<WorkshopDownload>();

		var list = new List<WorkshopDownload>();

		foreach ( var id in requested )
		{
			// Nothing coming in yet - it's not a download until it is
			if ( GetItem( id ) is { BytesTotal: > 0 } item )
				list.Add( Describe( id, (long)item.BytesDownloaded, (long)item.BytesTotal ) );
		}

		return list.ToArray();
	}

	/// <summary>
	/// The item, called what it's called
	/// </summary>
	static WorkshopDownload Describe( ulong id, long downloaded, long total )
	{
		var d = GetDetails( id );
		return new WorkshopDownload( id, d.Title ?? "Workshop Item", d.Thumb, downloaded, total );
	}

	/// <summary>
	/// What we know to call this item - looked up on the workshop the first time we don't know
	/// </summary>
	static Details GetDetails( ulong id )
	{
		if ( details.TryGetValue( id, out var d ) )
			return d;

		details[id] = default;
		_ = LookUpDetails( id );
		return default;
	}

	static async Task LookUpDetails( ulong id )
	{
		try
		{
			if ( await Workshop.FetchAsync( id ) is { } item )
				details[id] = new Details( item.Title, item.Thumb );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't look up workshop item {id} ({e.Message})" );
		}
	}
}
