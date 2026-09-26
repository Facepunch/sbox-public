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
public sealed record JamFinalist( string PackageIdent, int Seed, int? EliminatedRound );

/// <summary>
/// A community category's confirmed slate and round status, available to the menu
/// without referencing the backend's service assembly.
/// </summary>
public sealed class JamFinalistCategory
{
	/// <summary>
	/// Copies the authoritative slate. Nomination tallies never determine finalists locally.
	/// </summary>
	public JamFinalistCategory( JamCategoryVotingDto category )
	{
		Id = category.Id;
		Title = category.Title;
		ClosedReason = category.ClosedReason;
		VotingOpen = category.Mode == JamVotingMode.Voting;
		Decided = category.Mode == JamVotingMode.Decided;
		Round = category.Round;
		RoundEnds = category.RoundEnds;
		NextRoundOpens = category.NextRoundOpens;
		Nominees = category.Nominees
			.Where( x => !x.Withdrawn )
			.OrderBy( x => x.Seed )
			.Select( x => new JamFinalist( x.Package, x.Seed, x.EliminatedRound ) )
			.ToArray();
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
	/// Whether the backend has decided this category, including a single-entry slate.
	/// </summary>
	public bool Decided { get; }

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
}
