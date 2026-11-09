using Sandbox;

namespace MenuProject.Modals;

/// <summary>
/// What a game's page says, as it's fetched - what's cached, its Workshop item if it's known to be on the Workshop, the
/// full package, then its Workshop item if only that says it's there. The game modal and the platform's game page both.
/// </summary>
public sealed class GamePageLoad( string ident, Package start )
{
	public string Ident => ident;

	/// <summary>
	/// Its package - what it was opened with or what's cached, then the full one once that's come
	/// </summary>
	public Package Package { get; private set; } = start;

	public bool HasFullPackage { get; private set; }

	/// <summary>
	/// The full package didn't come - the page is showing what was cached, which has no gallery
	/// </summary>
	public bool FetchFailed { get; private set; }

	/// <summary>
	/// What the Steam Workshop says about it, once it's been asked - null before then, or if Steam couldn't say
	/// </summary>
	public WorkshopItemDetails WorkshopItem { get; private set; }

	/// <summary>
	/// Its Steam Workshop item, once it's been worked out - 0 before then
	/// </summary>
	public ulong WorkshopId { get; private set; }

	/// <summary>
	/// The Workshop's been asked about it - whether or not it answered
	/// </summary>
	public bool WorkshopLoaded { get; private set; }

	/// <summary>
	/// It's on the Steam Workshop - the page says what its item says, rather than the backend
	/// </summary>
	public bool OnWorkshop => WorkshopId != 0 || (Package?.InstallsFromWorkshop ?? false);

	/// <summary>
	/// Still working out what the page says - whether it's on the workshop at all (the full package says), and if it
	/// is, its item. Nothing that's only one or the other is shown until then, so it doesn't swap from one to the other.
	/// </summary>
	public bool Loading => OnWorkshop ? !WorkshopLoaded : !HasFullPackage && !FetchFailed;

	/// <summary>
	/// Fetch it all. <paramref name="changed"/> is called after each step, and says whether the page still wants it -
	/// false once it's moved on, which stops it there.
	/// </summary>
	public async Task Run( Func<bool> changed )
	{
		if ( Package.TryGetCached( ident, out var cached, true ) ) Package = cached;

		// Known to be on the workshop already - ask Steam about its item while the backend's asked about the package
		var known = Package is null ? 0 : MenuUtility.KnownWorkshopId( Package );
		var workshop = known != 0 ? FetchWorkshopItem( known, changed ) : null;

		// Keeping what we had if it doesn't come, so the page still has something on it and can say what didn't load
		Package full = null;
		try
		{
			full = await Package.FetchAsync( ident, false );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't fetch {ident}: {e.Message}" );
		}

		if ( full is not null ) Package = full;
		HasFullPackage = full is not null;
		FetchFailed = full is null;
		if ( !changed() || Package is null ) return;

		// Only the full package says it's on the workshop
		if ( workshop is null && OnWorkshop )
			workshop = FetchWorkshopItem( await MenuUtility.GetWorkshopIdAsync( Package ), changed );

		if ( workshop is not null )
			await workshop;
	}

	async Task FetchWorkshopItem( ulong id, Func<bool> changed )
	{
		// Its change notes can be asked for while its item is
		WorkshopId = id;
		if ( !changed() ) return;

		var item = await MenuUtility.FetchWorkshopItemAsync( id );

		WorkshopItem = item;
		WorkshopLoaded = true;
		changed();
	}
}
