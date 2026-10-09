using Sandbox;
using Sandbox.Services;

namespace MenuProject;

/// <summary>
/// Owns the menu's active jam, nominations and finalist voting state. UI reads this state;
/// snapshot requests and backend notifications are handled once for the scene.
/// </summary>
public sealed partial class GameJamSystem : GameObjectSystem<GameJamSystem>, IBackendListener
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
	/// Changes when jam, nomination or finalist state changes, so views can update.
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
	DateTimeOffset? nextRefresh;

	bool NominationsOpen => ActiveJam is { CommunityVoting: true, HasStarted: true }
		&& ActiveJam.Now < ActiveJam.NominationsEnd;

	/// <summary>
	/// The jam state for a panel to show - its own scene's, or with none (the pause menu's pages, up in
	/// the menu overlay, aren't on one) the menu's.
	/// </summary>
	public static GameJamSystem For( Sandbox.UI.Panel panel ) => Get( panel?.Scene ) ?? Current;

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
	public void Refresh()
	{
		refreshRequested = true;
		RefreshFinalists();
	}

	/// <summary>
	/// Rebuilds preview snapshots when seeking in either direction, including before the finals.
	/// </summary>
	public void SeekEditorPreview( DateTimeOffset target )
	{
		if ( !Jam.IsEditorPreview ) return;
		Jam.SeekEditorPreview( target );
		previewDays = Jam.PreviewDays;
		Nominations = null;
		nextRefresh = ActiveJam?.NextStep?.At;
		ResetFinalists();
		Refresh();
		Version++;
	}

	/// <summary>
	/// Rehearses a confirmed result, casting a local deciding vote if the preview final is tied.
	/// </summary>
	public async Task SeekEditorPreviewResultsAsync( DateTimeOffset target )
	{
		if ( !Jam.IsEditorPreview || ActiveJam is null ) return;

		var jam = ActiveJam;
		var source = finalistSource as JamFinalistPreview ?? (JamFinalistPreview)JamFinalistSource.Create( jam );
		Jam.SeekEditorPreview( jam.Results.AddSeconds( -30 ) );
		try
		{
			foreach ( var category in await source.ReadAsync() )
			{
				if ( !category.VotingOpen || category.Contenders.Count < 2 ) continue;
				var leaders = category.Contenders.OrderByDescending( x => category.Counts.GetValueOrDefault( x.PackageIdent ) ).ToArray();
				if ( category.Counts.GetValueOrDefault( leaders[0].PackageIdent ) == category.Counts.GetValueOrDefault( leaders[1].PackageIdent ) )
					await source.VoteAsync( category, leaders[0].PackageIdent );
			}
		}
		finally
		{
			SeekEditorPreview( target );
			// Retain the rehearsal's accepted vote across the seek's normal snapshot reset.
			finalistSource = source;
		}
	}

	void Tick()
	{
		if ( Scene.IsEditor ) return;

		if ( previewDays != Jam.PreviewDays )
		{
			previewDays = Jam.PreviewDays;
			ActiveJam = null;
			nextRefresh = null;
			Nominations = null;
			ResetFinalists();
			Error = null;
			refreshRequested = true;
			Version++;
		}

		if ( !NominationsOpen && Nominations is not null )
		{
			Nominations = null;
			Version++;
		}

		if ( ActiveJam is not null && nextRefresh <= ActiveJam.Now )
		{
			nextRefresh = null;
			Refresh();
		}

		if ( !loading && refreshRequested )
		{
			_ = RefreshAsync();
		}

		TickFinalists();
	}

	async Task RefreshAsync()
	{
		loading = true;
		refreshRequested = false;
		var days = previewDays;

		try
		{
			var jam = Jam.IsEditorPreview && ActiveJam is not null ? ActiveJam : await Jam.GetActive();
			if ( !Scene.IsValid() || days != Jam.PreviewDays ) return;

			if ( ActiveJam?.Ident != jam?.Ident )
			{
				Nominations = null;
				ResetFinalists();
			}
			ActiveJam = jam;
			nextRefresh = jam?.NextStep?.At;

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
	/// Applies public tallies to shared nominations or finalists without changing personal selections.
	/// </summary>
	public void OnJamVotesChanged( JamVoteUpdate update )
	{
		if ( Jam.PreviewDays != 0 || ActiveJam is null ) return;
		if ( !string.Equals( ActiveJam.Ident, update.JamIdent, StringComparison.OrdinalIgnoreCase ) ) return;

		if ( update.Round != 0 )
		{
			ApplyFinalistVotes( update );
			return;
		}

		if ( !NominationsOpen ) return;

		// Reconcile an overlapping snapshot instead of replaying possibly older pushes over it.
		if ( loading ) Refresh();
		if ( Nominations?.Apply( update ) == true ) Version++;
	}

	/// <summary>
	/// Stops backend notifications when the menu scene is disposed.
	/// </summary>
	public override void Dispose()
	{
		ResetFinalists();
		IBackendListener.Unregister( this );
		base.Dispose();
	}
}
