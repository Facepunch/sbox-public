using System;
using Sandbox.Services;

namespace Sandbox;

/// <summary>
/// A local voting rehearsal. Owns seed data, round timing, selections and result simulation;
/// everything it publishes is an ordinary finalist snapshot. Never submits backend votes.
/// </summary>
public sealed class JamFinalistPreview : JamFinalistSource
{
	readonly Func<DateTimeOffset> clock;
	JamFinalistCategory[] seeds;
	readonly Dictionary<int, (RoundState State, JamFinalistCategory Category)> snapshots = new();

	// Keep the rehearsal's clock state explicit instead of inferring it from published results.
	readonly record struct RoundState( int Number, DateTimeOffset EndsAt, bool Started, bool Finished );

	internal JamFinalistPreview( Jam jam ) : base( jam ) => clock = () => jam.Now;

	/// <summary>
	/// Creates a deterministic rehearsal from supplied categories and an optional controlled clock.
	/// Categories without a confirmed slate use their five highest nomination tallies.
	/// </summary>
	public JamFinalistPreview( Jam jam, IEnumerable<JamFinalistCategory> categories, Func<DateTimeOffset> clock = null ) : base( jam )
	{
		this.clock = clock ?? (() => jam.Now);
		seeds = categories.Select( Seed ).ToArray();
	}

	/// <summary>
	/// Local snapshots never consume live backend notifications.
	/// </summary>
	public override bool ReceivesUpdates => false;

	/// <summary>
	/// Reads seed data once, then advances the rehearsal using its local clock.
	/// </summary>
	public override async Task<JamFinalistCategory[]> ReadAsync()
	{
		if ( seeds is null )
		{
			var voting = await Backend.Jam.GetVoting( Jam.Ident );
			if ( voting is null || !string.Equals( voting.Ident, Jam.Ident, StringComparison.OrdinalIgnoreCase ) )
				throw new InvalidOperationException( "No nomination snapshot for this jam." );

			seeds = voting.Categories.Select( x => Seed( new JamFinalistCategory( x ) ) ).ToArray();
		}

		return seeds.Select( Snapshot ).ToArray();
	}

	/// <summary>
	/// Rehearsal contenders can be voted for without launching their games.
	/// </summary>
	public override Task<JamEntryStatus?> GetEntryStatusAsync( int categoryId, string ident )
	{
		var category = snapshots.GetValueOrDefault( categoryId ).Category;
		var entry = category?.Contenders.Any( x => x.PackageIdent == ident ) == true;
		return Task.FromResult<JamEntryStatus?>( new( entry, entry && category.VotingOpen,
			category?.MyVotes.Contains( ident ) == true, null ) );
	}

	/// <summary>
	/// Replaces the local selection with a new snapshot, preserving previously published snapshots.
	/// </summary>
	public override async Task<JamFinalistCategory> VoteAsync( JamFinalistCategory category, string ident )
	{
		var current = (await ReadAsync()).FirstOrDefault( x => x.Id == category.Id );
		if ( current != category || !current.VotingOpen || !current.Contenders.Any( x => x.PackageIdent == ident ) )
			throw new InvalidOperationException( "The round has changed. Review the refreshed slate before voting." );

		var counts = new Dictionary<string, int>( current.Counts, StringComparer.OrdinalIgnoreCase );
		foreach ( var previous in current.MyVotes )
		{
			counts[previous] = Math.Max( 0, counts.GetValueOrDefault( previous ) - 1 );
		}

		counts[ident] = counts.GetValueOrDefault( ident ) + 1;

		var updated = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Id = current.Id,
			Title = current.Title,
			Mode = JamVotingMode.Voting,
			Round = current.Round,
			RoundEnds = current.RoundEnds,
			MyVotes = [ident],
			Nominees = current.Nominees.Select( x => new JamNomineeDto
			{
				Package = x.PackageIdent,
				Seed = x.Seed,
				EliminatedRound = x.EliminatedRound
			} ).ToArray(),
			Tally = counts.Select( x => new JamTallyDto { Package = x.Key, Votes = x.Value } ).ToArray()
		}, current.NominationCounts );
		snapshots[current.Id] = (snapshots[current.Id].State, updated);
		return updated;
	}

	static JamFinalistCategory Seed( JamFinalistCategory category )
	{
		var nominees = category.Nominees.Count > 0 ? category.Nominees.ToArray()
			: category.Counts.OrderByDescending( x => x.Value ).ThenBy( x => x.Key, StringComparer.OrdinalIgnoreCase )
				.Take( 5 ).Select( ( x, i ) => new JamFinalist( x.Key, i + 1, null ) ).ToArray();

		return new JamFinalistCategory( new JamCategoryVotingDto
		{
			Id = category.Id,
			Title = category.Title,
			Nominees = nominees.Select( x => new JamNomineeDto { Package = x.PackageIdent, Seed = x.Seed } ).ToArray(),
			Tally = nominees.Where( x => x.PackageIdent is not null ).Select( x => new JamTallyDto
			{
				Package = x.PackageIdent,
				Votes = category.Counts.GetValueOrDefault( x.PackageIdent )
			} ).ToArray()
		}, category.Nominees.Count == 0 ? category.Counts : category.NominationCounts );
	}

	DateTimeOffset RoundEnd( int round, int finalRound )
	{
		if ( round >= finalRound ) return Jam.Results;

		var end = Jam.GrandFinal ?? Jam.Results;
		var rounds = Jam.GrandFinal.HasValue ? finalRound - 1 : finalRound;
		return Jam.FinalsStart.AddTicks( (end - Jam.FinalsStart).Ticks * round / rounds );
	}

	RoundState GetRoundState( int nomineeCount )
	{
		var now = clock();
		var finalRound = Math.Max( 1, nomineeCount - 1 );
		var round = 1;
		while ( round < finalRound && now >= RoundEnd( round, finalRound ) )
		{
			round++;
		}

		return new RoundState( round, RoundEnd( round, finalRound ), now >= Jam.FinalsStart, now >= Jam.Results );
	}

	JamFinalistCategory Snapshot( JamFinalistCategory seed )
	{
		var state = GetRoundState( seed.Nominees.Count );
		var cached = snapshots.GetValueOrDefault( seed.Id );
		var current = cached.Category;

		// Refreshing the same round must preserve local votes and object identity.
		if ( current is not null && cached.State == state )
		{
			return current;
		}

		var keepFinalVotes = current?.Round == state.Number && state.Finished;
		var remaining = Math.Min( seed.Nominees.Count, Math.Max( 2, seed.Nominees.Count - state.Number + 1 ) );
		var counts = keepFinalVotes ? current.Counts : seed.Counts;
		var leaders = seed.Nominees.Take( remaining ).Where( x => x.PackageIdent is not null )
			.OrderByDescending( x => counts.GetValueOrDefault( x.PackageIdent ) ).ToArray();
		var winner = state.Finished && (leaders.Length == 1 || leaders.Length == 2
			&& counts.GetValueOrDefault( leaders[0].PackageIdent ) > counts.GetValueOrDefault( leaders[1].PackageIdent ))
			? leaders[0].PackageIdent : null;

		var result = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Id = seed.Id,
			Title = seed.Title,
			Mode = winner is not null ? JamVotingMode.Decided
				: state.Started && !state.Finished ? JamVotingMode.Voting : JamVotingMode.Closed,
			Round = state.Number,
			RoundEnds = state.EndsAt,
			NextRoundOpens = state.Started ? null : Jam.FinalsStart,
			Winner = winner,
			MyVotes = keepFinalVotes ? current.MyVotes.ToArray() : [],
			Nominees = seed.Nominees.Select( ( x, i ) => new JamNomineeDto
			{
				Package = x.PackageIdent,
				Seed = x.Seed,
				EliminatedRound = i >= remaining ? seed.Nominees.Count - i : null,
				Place = winner is null ? 0 : i >= remaining ? i + 1 : x.PackageIdent == winner ? 1 : 2
			} ).ToArray(),
			Tally = leaders.Select( x => new JamTallyDto { Package = x.PackageIdent, Votes = counts.GetValueOrDefault( x.PackageIdent ) } ).ToArray()
		}, seed.NominationCounts );
		snapshots[seed.Id] = (state, result);
		return result;
	}
}
