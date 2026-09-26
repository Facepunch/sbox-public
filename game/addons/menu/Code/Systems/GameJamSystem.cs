using Sandbox;
using Sandbox.Services;

namespace MenuProject;

/// <summary>
/// Owns the menu's active jam and shared nomination state. UI reads this state;
/// snapshot requests and backend notifications are handled once for the scene.
/// </summary>
public sealed class GameJamSystem : GameObjectSystem<GameJamSystem>, IBackendListener
{
	/// <summary>
	/// Creates the menu's jam state and listens for backend updates until the scene closes.
	/// </summary>
	public GameJamSystem( Scene scene ) : base( scene )
	{
		IBackendListener.Register( this );
		Listen( Stage.StartUpdate, 0, Tick, nameof( GameJamSystem ) );
	}

	/// <summary>
	/// The active jam, or null between jams and before the initial request completes.
	/// </summary>
	public Jam ActiveJam { get; private set; }

	/// <summary>
	/// The shared nomination snapshot, including the local player's selections.
	/// Null until a snapshot is available or when nominations are closed.
	/// </summary>
	public JamNominationSummary Nominations { get; private set; }

	/// <summary>
	/// Changes when jam or nomination state changes, so lists can update their filters.
	/// </summary>
	public int Version { get; private set; }

	/// <summary>
	/// The latest refresh error, if any. Previously loaded data remains available.
	/// </summary>
	public string Error { get; private set; }

	/// <summary>
	/// Whether a snapshot refresh is pending or in progress.
	/// </summary>
	public bool IsLoading => loading || refreshRequested;

	bool loading;
	bool refreshRequested = true;
	int previewDays = Jam.PreviewDays;
	RealTimeUntil nextRefresh;

	bool NominationsOpen => ActiveJam is { CommunityVoting: true, HasStarted: true }
		&& ActiveJam.Now < ActiveJam.NominationsEnd;

	/// <summary>
	/// The nomination count for a package in the active jam. Null means nominations
	/// are closed or unavailable; zero means the package has no nominations.
	/// </summary>
	public static int? GetNominations( string packageIdent )
	{
		var system = Current;
		if ( packageIdent is null || system?.Nominations is null || !system.NominationsOpen ) return null;

		return system.Nominations.Counts.GetValueOrDefault( packageIdent );
	}

	/// <summary>
	/// Requests a fresh snapshot, coalescing requests made before the next update.
	/// </summary>
	public void Refresh() => refreshRequested = true;

	void Tick()
	{
		if ( Scene.IsEditor ) return;

		if ( previewDays != Jam.PreviewDays )
		{
			previewDays = Jam.PreviewDays;
			ActiveJam = null;
			Nominations = null;
			Error = null;
			refreshRequested = true;
			Version++;
		}

		if ( !NominationsOpen && Nominations is not null )
		{
			Nominations = null;
			Version++;
		}

		if ( !loading && (refreshRequested || nextRefresh <= 0) )
		{
			_ = RefreshAsync();
		}
	}

	async Task RefreshAsync()
	{
		loading = true;
		refreshRequested = false;
		var days = previewDays;

		try
		{
			var jam = await Jam.GetActive();
			if ( !Scene.IsValid() || days != Jam.PreviewDays ) return;

			if ( ActiveJam?.Ident != jam?.Ident ) Nominations = null;
			ActiveJam = jam;

			var nominations = NominationsOpen ? await jam.GetNominationSummaryAsync() : null;
			if ( !Scene.IsValid() || days != Jam.PreviewDays ) return;

			Error = NominationsOpen && nominations is null ? "Couldn't refresh nominations. Try again in a moment." : null;
			if ( nominations is not null || !NominationsOpen ) Nominations = nominations;
			Version++;
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Couldn't refresh the active jam" );
			Error = "Couldn't refresh the jam. Try again in a moment.";
			Version++;
		}
		finally
		{
			loading = false;
			nextRefresh = 60;
		}
	}

	/// <summary>
	/// Reconciles changes missed while disconnected.
	/// </summary>
	public void OnBackendConnected() => Refresh();

	/// <summary>
	/// Refreshes personal selections after an accepted nomination or withdrawal.
	/// </summary>
	public void OnJamNominationsChanged( string jamIdent )
	{
		if ( ActiveJam?.Ident == jamIdent ) Refresh();
	}

	/// <summary>
	/// Applies an absolute public tally to the shared nomination state.
	/// </summary>
	public void OnJamVotesChanged( JamVoteUpdate update )
	{
		if ( Jam.PreviewDays != 0 || !NominationsOpen || update.Round != 0 ) return;
		if ( !string.Equals( ActiveJam.Ident, update.JamIdent, StringComparison.OrdinalIgnoreCase ) ) return;

		// Reconcile an overlapping snapshot instead of replaying possibly older pushes over it.
		if ( loading ) Refresh();
		if ( Nominations?.Apply( update ) == true ) Version++;
	}

	/// <summary>
	/// Stops backend notifications when the menu scene is disposed.
	/// </summary>
	public override void Dispose()
	{
		IBackendListener.Unregister( this );
		base.Dispose();
	}
}
