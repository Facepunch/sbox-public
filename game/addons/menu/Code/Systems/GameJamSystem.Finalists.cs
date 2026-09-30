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
	/// Play eligibility for one category, keyed by package ident. Null until loaded.
	/// </summary>
	public IReadOnlyDictionary<string, JamEntryStatus?> GetFinalistEligibility( int categoryId )
		=> finalistEligibility.GetValueOrDefault( categoryId );

	/// <summary>
	/// The latest finalist refresh or eligibility error, while retaining the last confirmed slate.
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

	readonly Dictionary<int, Dictionary<string, JamEntryStatus?>> finalistEligibility = new();
	readonly Dictionary<(int Category, int? Round), float> finalistVoteCooldowns = new();
	JamFinalistSource finalistSource;
	bool finalistsLoading;
	bool finalistsRefreshRequested = true;
	bool finalistsWasFocused;
	RealTimeUntil nextFinalistsRefresh;

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
			var scheduled = index >= 0 ? ActiveJam.Timeline[index].Phase : ActiveJam.CurrentPhase;
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
	/// Requests one shared finalist refresh, coalescing reconnect, focus and UI requests.
	/// </summary>
	public void RefreshFinalists() => finalistsRefreshRequested = true;

	void ResetFinalists()
	{
		finalistSource = null;
		Finalists = null;
		FinalistsError = null;
		FinalistVoteError = null;
		finalistEligibility.Clear();
		finalistVoteCooldowns.Clear();
		finalistsLoading = false;
		IsSubmittingFinalistVote = false;
		finalistsRefreshRequested = true;
	}

	bool IsCurrentFinalists( JamFinalistSource source ) => Scene.IsValid() && finalistSource == source;

	void TickFinalists()
	{
		if ( !HasFinalists ) return;

		if ( Application.IsFocused && !finalistsWasFocused ) RefreshFinalists();
		finalistsWasFocused = Application.IsFocused;

		if ( !finalistsLoading && !IsSubmittingFinalistVote && (finalistsRefreshRequested || nextFinalistsRefresh <= 0) )
		{
			_ = RefreshFinalistsAsync();
		}
	}

	async Task RefreshFinalistsAsync()
	{
		finalistsLoading = true;
		finalistsRefreshRequested = false;
		var jam = ActiveJam;
		var source = finalistSource ??= JamFinalistSource.Create( jam );

		try
		{
			var categories = await source.ReadAsync();
			if ( !IsCurrentFinalists( source ) ) return;

			Finalists = categories;
			finalistEligibility.Clear();
			FinalistsError = null;
			Version++;

			var checks = new List<Task>();
			foreach ( var category in categories.Where( x => x.IsVotingAt( jam.Now ) ) )
			{
				finalistEligibility[category.Id] = new( StringComparer.OrdinalIgnoreCase );
				foreach ( var entry in category.Contenders.Where( x => x.PackageIdent is not null ) )
				{
					checks.Add( LoadFinalistEligibility( source, category.Id, entry.PackageIdent ) );
				}
			}

			await Task.WhenAll( checks );
		}
		catch ( Exception e )
		{
			if ( !IsCurrentFinalists( source ) ) return;

			Log.Warning( $"Couldn't refresh jam finalists ({e.Message})" );
			FinalistsError = Finalists is null ? "Couldn't load finalists. You can still browse all entries."
				: "Couldn't refresh finalists. Showing the last confirmed slate.";
		}
		finally
		{
			if ( IsCurrentFinalists( source ) )
			{
				finalistsLoading = false;

				// Wake at the next advertised transition, then poll while awaiting confirmation.
				var deadline = Finalists?.Where( x => !x.Decided )
					.Select( x => x.VotingOpen ? x.RoundEnds : x.NextRoundOpens ).Min();
				var remaining = (deadline - jam.Now)?.TotalSeconds ?? 20;
				nextFinalistsRefresh = (float)(remaining > 0 ? Math.Min( 20, remaining ) : 2);
				Version++;
			}
		}
	}

	async Task LoadFinalistEligibility( JamFinalistSource source, int categoryId, string ident )
	{
		try
		{
			var status = await source.GetEntryStatusAsync( categoryId, ident );
			if ( !IsCurrentFinalists( source ) ) return;

			finalistEligibility[categoryId][ident] = status;
			if ( status is null ) FinalistsError = "Couldn't check voting eligibility for every finalist. Try again.";
		}
		catch ( Exception e )
		{
			if ( !IsCurrentFinalists( source ) ) return;

			Log.Warning( $"Couldn't check finalist eligibility ({e.Message})" );
			FinalistsError = "Couldn't check voting eligibility for every finalist. Try again.";
		}
	}

	void ApplyFinalistVotes( JamVoteUpdate update )
	{
		if ( !HasFinalists || finalistSource?.ReceivesUpdates != true ) return;

		var category = Finalists?.FirstOrDefault( x => x.Id == update.CategoryId );
		if ( category?.RoundEnds <= ActiveJam.Now )
		{
			RefreshFinalists();
			return;
		}

		if ( finalistsLoading || IsSubmittingFinalistVote || category?.Round != update.Round ) RefreshFinalists();
		if ( category?.Apply( update ) == true ) Version++;
	}

	/// <summary>
	/// Moves the current round's vote once, with a five-second lock after acceptance.
	/// Rejects stale snapshots, duplicate selections and ineligible entries before submitting to the source.
	/// </summary>
	public async Task SubmitFinalistVoteAsync( JamFinalistCategory category, string ident )
	{
		if ( !HasFinalists || category is null || string.IsNullOrEmpty( ident ) || IsSubmittingFinalistVote || IsLoadingFinalists ) return;
		if ( Finalists?.Contains( category ) != true || GetFinalistVoteCooldown( category.Id ) > 0 ) return;
		if ( !category.IsVotingAt( ActiveJam.Now ) || category.MyVotes.Contains( ident ) ) return;
		if ( !category.Contenders.Any( x => string.Equals( x.PackageIdent, ident, StringComparison.OrdinalIgnoreCase ) ) ) return;
		if ( GetFinalistEligibility( category.Id )?.GetValueOrDefault( ident )?.CanNominate != true ) return;

		IsSubmittingFinalistVote = true;
		FinalistVoteError = null;
		Version++;
		var jam = ActiveJam;
		var source = finalistSource ??= JamFinalistSource.Create( jam );

		try
		{
			var updated = await source.VoteAsync( category, ident );
			if ( !IsCurrentFinalists( source ) ) return;

			Finalists = Finalists.Select( x => x.Id == updated.Id ? updated : x ).ToArray();
			finalistVoteCooldowns[(updated.Id, updated.Round)] = RealTime.Now + 5;
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
				RefreshFinalists();
				Version++;
			}
		}
	}
}
