using Sandbox;
using Sandbox.Services;

namespace MenuProject;

public sealed partial class GameJamSystem
{
	/// <summary>
	/// Shared finalist snapshots, including the local player's votes. Null until first loaded.
	/// </summary>
	public JamFinalistCategory[] Finalists { get; private set; }

	/// <summary>
	/// The latest finalist refresh error, while retaining the last confirmed slate.
	/// </summary>
	public string FinalistsError { get; private set; }

	/// <summary>
	/// The latest vote submission error, cleared by the next submission or a context change.
	/// </summary>
	public string FinalistVoteError { get; private set; }

	/// <summary>
	/// Whether finalist data is being refreshed or waiting for reconciliation.
	/// </summary>
	public bool IsLoadingFinalists => finalistsLoading || finalistsRefreshRequested;

	/// <summary>
	/// Whether a finalist vote is awaiting the backend response.
	/// </summary>
	public bool IsSubmittingFinalistVote { get; private set; }

	readonly List<JamVoteUpdate> pendingFinalistVotes = new();
	readonly Dictionary<(int Category, int? Round), float> finalistVoteCooldowns = new();
	JamFinalistSource finalistSource;
	bool finalistsLoading;
	bool finalistsRefreshRequested = true;
	DateTimeOffset? nextFinalistsRefresh;
	int finalistVoteVersion;
	RealTimeUntil nextPreviewVote;
	int previewVoteBatch;

	bool HasFinalists => ActiveJam is not null
		&& ActiveJam.Now >= (ActiveJam.CommunityVoting ? ActiveJam.NominationsEnd : ActiveJam.FinalsStart);

	/// <summary>
	/// Keeps the final result on display for a day after the scheduled crowning.
	/// </summary>
	public DateTimeOffset FinalResultsUntil => ActiveJam.Results.AddDays( 1 );

	/// <summary>
	/// Chooses the jam view from confirmed voting state. An expired timer cannot declare a winner.
	/// </summary>
	public Jam.Phase Phase
	{
		get
		{
			if ( ActiveJam is null ) return Jam.Phase.Upcoming;

			var index = ActiveJam.CurrentStepIndex;
			var scheduled = index >= 0 ? ActiveJam.Timeline[index].Phase : Jam.Phase.Upcoming;
			if ( !ActiveJam.CommunityVoting || ActiveJam.Now < ActiveJam.NominationsEnd ) return scheduled;
			if ( ActiveJam.Now < ActiveJam.FinalsStart ) return Jam.Phase.Finals;
			if ( Finalists is null ) return scheduled >= Jam.Phase.GrandFinal ? Jam.Phase.GrandFinal : Jam.Phase.Finals;
			if ( Finalists.Length == 0 ) return scheduled;

			if ( Finalists.All( x => x.Decided ) )
			{
				return ActiveJam.Now >= FinalResultsUntil ? Jam.Phase.Crowned : Jam.Phase.GrandFinal;
			}

			return Finalists.Any( x => !x.Decided && !x.GrandFinal ) ? Jam.Phase.Finals : Jam.Phase.GrandFinal;
		}
	}

	/// <summary>
	/// Seconds until the current round's accepted vote can be changed, independent of page lifetime.
	/// </summary>
	public int GetFinalistVoteCooldown( int categoryId )
	{
		var category = Finalists?.FirstOrDefault( x => x.Id == categoryId );
		return category is null ? 0 : (int)Math.Ceiling( Math.Max( 0,
			finalistVoteCooldowns.GetValueOrDefault( (category.Id, category.Round) ) - RealTime.Now ) );
	}

	/// <summary>
	/// Requests one shared finalist refresh, coalescing reconnect, transition and UI requests.
	/// </summary>
	public void RefreshFinalists() => finalistsRefreshRequested = true;

	void ResetFinalists()
	{
		finalistSource = null;
		Finalists = null;
		FinalistsError = null;
		FinalistVoteError = null;
		pendingFinalistVotes.Clear();
		finalistVoteCooldowns.Clear();
		nextFinalistsRefresh = null;
		finalistsLoading = false;
		IsSubmittingFinalistVote = false;
		finalistsRefreshRequested = true;
		nextPreviewVote = 0;
		previewVoteBatch = 0;
	}

	bool IsCurrentFinalists( JamFinalistSource source ) => Scene.IsValid() && finalistSource == source;

	void TickFinalists()
	{
		if ( !HasFinalists ) return;
		TickPreviewVotes();

		if ( nextFinalistsRefresh <= ActiveJam.Now )
		{
			nextFinalistsRefresh = null;
			RefreshFinalists();
		}

		if ( !finalistsLoading && !IsSubmittingFinalistVote && finalistsRefreshRequested )
		{
			_ = RefreshFinalistsAsync();
		}
	}

	/// <summary>
	/// Rehearses incoming final tallies in the shared state, so the shelf and jam page animate
	/// the same updates. These are local preview votes and never reach the backend.
	/// </summary>
	void TickPreviewVotes()
	{
		if ( !Jam.IsEditorPreview || finalistSource is not JamFinalistPreview || Finalists is null
			|| finalistsLoading || IsSubmittingFinalistVote || nextPreviewVote > 0 ) return;

		var finals = Finalists.Where( x => x.GrandFinal && x.Round.HasValue && x.IsVotingAt( ActiveJam.Now ) ).ToArray();
		if ( finals.Length == 0 ) return;

		nextPreviewVote = 1.2f;
		foreach ( var category in finals )
		{
			var contenders = category.Contenders.Where( x => x.PackageIdent is not null ).ToArray();
			for ( var i = 0; i < contenders.Length; i++ )
			{
				var ident = contenders[i].PackageIdent;
				var boost = (previewVoteBatch / 5) % 2 == i ? 24 : 0;
				var added = previewVoteBatch == 0 ? 800 + i * 35 : 8 + (previewVoteBatch * 17 + i * 11) % 23 + boost;
				category.Apply( new JamVoteUpdate( ActiveJam.Ident, category.Id, category.Round.Value, ident,
					category.TotalVotes + added, category.Counts.GetValueOrDefault( ident ) + added ) );
			}
		}
		previewVoteBatch++;
		Version++;
	}

	async Task RefreshFinalistsAsync()
	{
		finalistsLoading = true;
		finalistsRefreshRequested = false;
		var jam = ActiveJam;
		var source = finalistSource ??= JamFinalistSource.Create( jam );
		var voteVersion = finalistVoteVersion;
		var requestedAt = jam.Now;

		try
		{
			var categories = await source.ReadAsync();
			if ( !IsCurrentFinalists( source ) ) return;

			// Voting remains available during a read. Don't overwrite a vote accepted since it began.
			if ( IsSubmittingFinalistVote || voteVersion != finalistVoteVersion )
			{
				RefreshFinalists();
				return;
			}

			Finalists = categories;
			FinalistsError = null;
			ScheduleFinalistsRefresh( requestedAt );
			Version++;
		}
		catch ( Exception e )
		{
			if ( !IsCurrentFinalists( source ) ) return;

			Log.Warning( $"Couldn't refresh jam finalists ({e.Message})" );
			FinalistsError = Finalists is null ? "Couldn't load finalists. You can still browse all entries."
				: "Couldn't refresh finalists. Showing the last confirmed slate.";
			nextFinalistsRefresh = jam.Now.AddSeconds( 15 );
		}
		finally
		{
			if ( IsCurrentFinalists( source ) )
			{
				finalistsLoading = false;
				ApplyPendingFinalistVotes();
				Version++;
			}
		}
	}

	/// <summary>
	/// Reads again at the next scheduled transition. Vote totals arrive through backend messages.
	/// </summary>
	void ScheduleFinalistsRefresh( DateTimeOffset requestedAt )
	{
		// Include deadlines crossed while the request was in flight so they are handled next tick.
		nextFinalistsRefresh = (Finalists ?? []).Where( x => !x.Decided )
			.Select( x => x.VotingOpen ? x.RoundEnds : x.NextRoundOpens )
			.Concat( new DateTimeOffset?[] { ActiveJam.FinalsStart, ActiveJam.GrandFinal, ActiveJam.Results } )
			.Where( x => x > requestedAt ).Min();

		// Round transitions can reach us before the backend has opened the next round. Retry the
		// closed/expired snapshot instead of waiting until the next day's scheduled phase.
		if ( (Finalists ?? []).Any( x => !x.Decided && (x.VotingOpen ? !x.IsVotingAt( ActiveJam.Now )
			: !x.NextRoundOpens.HasValue || x.NextRoundOpens <= ActiveJam.Now) ) )
		{
			var retry = ActiveJam.Now.AddSeconds( 15 );
			if ( !nextFinalistsRefresh.HasValue || nextFinalistsRefresh > retry ) nextFinalistsRefresh = retry;
		}
	}

	void ApplyFinalistVotes( JamVoteUpdate update )
	{
		if ( !HasFinalists || finalistSource?.ReceivesUpdates != true ) return;

		if ( finalistsLoading || IsSubmittingFinalistVote )
		{
			pendingFinalistVotes.Add( update );
		}

		var category = Finalists?.FirstOrDefault( x => x.Id == update.CategoryId );
		if ( category?.Decided != true && (category?.Round is null || category.Round < update.Round) ) RefreshFinalists();
		if ( category?.Apply( update ) == true ) Version++;
	}

	/// <summary>
	/// Preserves live tally messages received while a snapshot or vote response was in flight.
	/// </summary>
	void ApplyPendingFinalistVotes()
	{
		if ( finalistsLoading || IsSubmittingFinalistVote ) return;

		foreach ( var update in pendingFinalistVotes )
		{
			ApplyFinalistVotes( update );
		}

		pendingFinalistVotes.Clear();
	}

	/// <summary>
	/// Submits the player's choice and displays the backend's reason if it is rejected.
	/// Eligibility is decided by the backend without a separate preflight request.
	/// An accepted vote starts a five-second cooldown before it can be changed.
	/// </summary>
	public async Task SubmitFinalistVoteAsync( JamFinalistCategory category, string ident )
	{
		if ( ActiveJam is null || category is null || string.IsNullOrEmpty( ident ) || IsSubmittingFinalistVote ) return;
		if ( GetFinalistVoteCooldown( category.Id ) > 0 ) return;

		IsSubmittingFinalistVote = true;
		finalistVoteVersion++;
		FinalistVoteError = null;
		Version++;
		var jam = ActiveJam;
		var source = finalistSource ??= JamFinalistSource.Create( jam );
		var requestedAt = jam.Now;

		try
		{
			var updated = await source.VoteAsync( category, ident );
			if ( !IsCurrentFinalists( source ) ) return;

			Finalists = Finalists.Select( x => x.Id == updated.Id ? updated : x ).ToArray();
			finalistVoteCooldowns[(updated.Id, updated.Round)] = RealTime.Now + 5;
			ScheduleFinalistsRefresh( requestedAt );
		}
		catch ( Exception e )
		{
			if ( IsCurrentFinalists( source ) )
			{
				FinalistVoteError = e is InvalidOperationException ? e.Message : "Couldn't update your vote. Try again.";
			}
		}
		finally
		{
			if ( IsCurrentFinalists( source ) )
			{
				IsSubmittingFinalistVote = false;
				ApplyPendingFinalistVotes();
				Version++;
			}
		}
	}
}
