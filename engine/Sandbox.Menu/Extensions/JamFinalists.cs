using System;
using Sandbox.Services;

namespace Sandbox;

/// <summary>
/// One finalist's server-assigned seed and package. A missing package keeps its place
/// in the slate, but cannot be played. Withdrawn entries are excluded by the category.
/// </summary>
/// <param name="PackageIdent">The package ident, or null if it no longer exists.</param>
/// <param name="Seed">The seed assigned when nominations locked.</param>
/// <param name="EliminatedRound">The round in which this entry was eliminated, if any.</param>
/// <param name="Place">The confirmed finishing place, or zero before the result.</param>
public sealed record JamFinalist( string PackageIdent, int Seed, int? EliminatedRound, int Place = 0 );

/// <summary>
/// A community category's confirmed slate and round status, available to the menu
/// without referencing the backend's service assembly.
/// </summary>
public sealed class JamFinalistCategory
{
	/// <summary>
	/// Copies the authoritative slate. Nomination tallies never determine finalists locally.
	/// </summary>
	public JamFinalistCategory( JamCategoryVotingDto category, IReadOnlyDictionary<string, int> nominationCounts = null )
	{
		NominationCounts = nominationCounts?.ToDictionary( x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase ) ?? [];
		Id = category.Id;
		Title = category.Title;
		ClosedReason = category.ClosedReason;
		VotingOpen = category.Mode == JamVotingMode.Voting;
		Decided = category.Mode == JamVotingMode.Decided;
		Winner = category.Winner;
		Round = category.Round;
		RoundEnds = category.RoundEnds;
		NextRoundOpens = category.NextRoundOpens;
		MyVotes = category.MyVotes.ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var tally in category.Tally )
		{
			if ( !string.IsNullOrEmpty( tally.Package ) ) Counts[tally.Package] = tally.Votes;
		}
		TotalVotes = Counts.Values.Sum();
		Nominees = category.Nominees
			.Where( x => !x.Withdrawn )
			.OrderBy( x => x.Seed )
			.Select( x => new JamFinalist( x.Package, x.Seed, x.EliminatedRound, x.Place ) )
			.ToArray();
		Contenders = GetContenders();
		GrandFinal = category.GrandFinal || Contenders.Count == 2;
	}

	/// <summary>
	/// The category's stable backend identifier.
	/// </summary>
	public int Id { get; }

	/// <summary>
	/// The category's display name.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// The backend's explanation when voting is closed.
	/// </summary>
	public string ClosedReason { get; }

	/// <summary>
	/// Whether the backend has opened a finals round. A local countdown cannot set this.
	/// </summary>
	public bool VotingOpen { get; }

	/// <summary>
	/// Whether the supplied time is still inside a round opened by the voting source.
	/// Reaching a scheduled opening time alone never opens voting.
	/// </summary>
	public bool IsVotingAt( DateTimeOffset time ) => VotingOpen && (RoundEnds is null || time < RoundEnds);

	/// <summary>
	/// Whether the backend has decided this category, including a single-entry slate.
	/// </summary>
	public bool Decided { get; }

	/// <summary>
	/// The winner confirmed by the backend, including any server-side tie-break.
	/// </summary>
	public string Winner { get; }

	/// <summary>
	/// The open round number, or null between rounds.
	/// </summary>
	public int? Round { get; }

	/// <summary>
	/// The open round's closing time, or null while voting is closed.
	/// </summary>
	public DateTimeOffset? RoundEnds { get; }

	/// <summary>
	/// The next opening time provided by the backend, or null when none is scheduled.
	/// </summary>
	public DateTimeOffset? NextRoundOpens { get; }

	/// <summary>
	/// The qualifying slate in server seed order, with no replacement for withdrawn entries.
	/// </summary>
	public IReadOnlyList<JamFinalist> Nominees { get; }

	/// <summary>
	/// The remaining games in seed order, retaining both finalists after the result is decided.
	/// </summary>
	public IReadOnlyList<JamFinalist> Contenders { get; }

	/// <summary>
	/// Whether the server marks this as a grand final or the confirmed slate has reached its final pair.
	/// </summary>
	public bool GrandFinal { get; }

	/// <summary>
	/// Historical nomination totals when available, separate from the current round's votes.
	/// </summary>
	public IReadOnlyDictionary<string, int> NominationCounts { get; }

	/// <summary>
	/// Absolute counts for this round, keyed by package ident.
	/// </summary>
	public Dictionary<string, int> Counts { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Personal selections from the authenticated snapshot; public pushes never change these.
	/// </summary>
	public HashSet<string> MyVotes { get; }

	/// <summary>
	/// The category's total, including votes for subsequently withdrawn entries.
	/// </summary>
	public int TotalVotes { get; private set; }

	/// <summary>
	/// Finds a confirmed finishing place from server places or completed eliminations.
	/// Ambiguous eliminations remain unresolved; live tallies and deadlines cannot confirm a winner.
	/// </summary>
	public JamFinalist GetFinalistAtPlace( int place )
	{
		if ( place < 1 || place > Nominees.Count ) return null;
		if ( Decided && Nominees.FirstOrDefault( x => x.Place == place ) is { } confirmed ) return confirmed;

		var winner = Decided ? Winner : null;
		if ( place <= 2 && winner is not null && Contenders.Count == 2
			&& Contenders.Any( x => x.PackageIdent == winner ) )
		{
			return place == 1 ? Contenders.First( x => x.PackageIdent == winner )
				: Contenders.First( x => x.PackageIdent != winner );
		}

		var survivors = Nominees.Where( x => !x.EliminatedRound.HasValue ).ToArray();
		if ( place == 1 && Decided && survivors.Length == 1 ) return survivors[0];

		foreach ( var group in Nominees.Where( x => x.EliminatedRound.HasValue ).GroupBy( x => x.EliminatedRound.Value ) )
		{
			var rank = 1 + survivors.Length + Nominees.Count( x => x.EliminatedRound > group.Key );
			if ( rank == place && group.Count() == 1 ) return group.First();
		}

		return null;
	}

	/// <summary>
	/// Preserves the final pair across a decided snapshot, including a runner-up marked eliminated.
	/// </summary>
	JamFinalist[] GetContenders()
	{
		if ( Decided )
		{
			var placed = Nominees.Where( x => x.Place is 1 or 2 ).ToArray();
			if ( placed.Length == 2 ) return placed;
		}

		var remaining = Nominees.Where( x => !x.EliminatedRound.HasValue ).ToArray();
		if ( !Decided || remaining.Length != 1 ) return remaining;

		var last = Nominees.Where( x => x.EliminatedRound.HasValue )
			.GroupBy( x => x.EliminatedRound ).OrderByDescending( x => x.Key ).FirstOrDefault();
		return last?.Count() == 1 ? remaining.Concat( last ).OrderBy( x => x.Seed ).ToArray() : remaining;
	}

	/// <summary>
	/// Applies an absolute update for this category and round. The caller checks the jam identifier.
	/// </summary>
	public bool Apply( JamVoteUpdate update )
	{
		if ( !VotingOpen || update.CategoryId != Id || update.Round != Round ) return false;
		if ( string.IsNullOrEmpty( update.PackageIdent ) || update.Votes < 0 || update.TotalVotes < update.Votes ) return false;

		TotalVotes = update.TotalVotes;
		Counts[update.PackageIdent] = update.Votes;
		return true;
	}
}

public static partial class SandboxMenuExtensions
{
	/// <summary>
	/// Fetches confirmed finalists and round status, bypassing the ordinary GET cache.
	/// Failures propagate so the page can retain its last slate and offer a retry.
	/// </summary>
	public static async Task<JamFinalistCategory[]> GetFinalistsAsync( this Jam jam )
	{
		var voting = await Backend.Jam.GetVoting( jam.Ident, days: PreviewDays() );
		if ( voting is null || !string.Equals( voting.Ident, jam.Ident, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "No voting snapshot for this jam." );

		return voting.Categories.Select( x => new JamFinalistCategory( x ) ).ToArray();
	}

	/// <summary>
	/// Checks play eligibility afresh for a particular category, including after returning from play.
	/// </summary>
	public static async Task<JamEntryStatus?> GetFinalistEntryStatusAsync( this Jam jam, int categoryId, string packageIdent )
	{
		var entry = await Backend.Jam.GetEntry( jam.Ident, packageIdent, PreviewDays() );
		if ( entry is null ) return null;

		return new JamEntryStatus( entry.IsEntry, entry.CanVote && entry.OpenCategories.Contains( categoryId ),
			entry.VotedCategories.Contains( categoryId ), entry.Reason );
	}

	/// <summary>
	/// Changes one category's vote and returns its authoritative response.
	/// The backend decides eligibility and whether the round is open.
	/// </summary>
	public static async Task<JamFinalistCategory> VoteForFinalistAsync( this Jam jam, JamFinalistCategory category, string packageIdent, bool remove )
	{
		if ( Jam.PreviewDays != 0 ) throw new InvalidOperationException( "Preview votes stay local." );

		try
		{
			var result = remove
				? await Backend.Jam.Unvote( jam.Ident, category.Id, packageIdent )
				: await Backend.Jam.Vote( jam.Ident, category.Id, packageIdent );
			return new JamFinalistCategory( result );
		}
		catch ( Refit.ApiException e )
		{
			throw new InvalidOperationException( ReasonFrom( e ), e );
		}
	}
}
