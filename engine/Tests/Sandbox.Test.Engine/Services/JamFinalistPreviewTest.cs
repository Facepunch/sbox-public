using System;
using Sandbox.Services;

namespace Services;

/// <summary>
/// Rehearsals must publish normal snapshots without changing live models or contacting voting APIs.
/// </summary>
[TestClass]
public class JamFinalistPreviewTest
{
	static readonly DateTimeOffset Start = new( 2026, 9, 28, 18, 30, 0, TimeSpan.Zero );
	static Jam Schedule() => new() { FinalsStart = Start, GrandFinal = Start.AddDays( 3 ), Results = Start.AddDays( 4 ) };
	static JamFinalistCategory Nominations( int count = 5 ) => new( new JamCategoryVotingDto
	{
		Id = 7,
		Title = "Best game",
		Mode = JamVotingMode.Nominating,
		Tally = Enumerable.Range( 1, count ).Select( i => new JamTallyDto { Package = $"test.game{i}", Votes = i * 10 } ).ToArray()
	} );

	/// <summary>
	/// The reveal, knockout and final cross their scheduled boundaries at the exact deadline.
	/// </summary>
	[TestMethod]
	public async Task ScheduledTransitions()
	{
		var jam = Schedule();
		var now = Start.AddSeconds( -10 );
		var source = new JamFinalistPreview( jam, [Nominations()], () => now );
		var reveal = (await source.ReadAsync()).Single();
		Assert.IsFalse( reveal.VotingOpen );
		Assert.AreEqual( Start, reveal.NextRoundOpens );
		Assert.IsFalse( source.ReceivesUpdates );

		now = Start;
		var first = (await source.ReadAsync()).Single();
		Assert.IsTrue( first.VotingOpen );
		Assert.AreEqual( 1, first.Round );
		Assert.AreEqual( Start.AddDays( 1 ), first.RoundEnds );
		now = first.RoundEnds.Value.AddSeconds( -10 );
		Assert.AreSame( first, (await source.ReadAsync()).Single() );
		now = first.RoundEnds.Value;
		Assert.AreEqual( 2, (await source.ReadAsync()).Single().Round );

		now = jam.GrandFinal.Value;
		var final = (await source.ReadAsync()).Single();
		Assert.IsTrue( final.GrandFinal );
		Assert.AreEqual( 4, final.Round );
		Assert.AreEqual( jam.Results, final.RoundEnds );
		Assert.IsNull( final.GetFinalistAtPlace( 1 ) );
		now = jam.Results;
		var result = (await source.ReadAsync()).Single();
		Assert.IsTrue( result.Decided );
		Assert.IsFalse( result.VotingOpen );
		Assert.AreEqual( "test.game5", result.Winner );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 4, 5 }, result.Nominees.Select( x => x.Place ).ToArray() );
		Assert.AreSame( result, (await source.ReadAsync()).Single() );
		Assert.IsFalse( final.Decided );
	}

	/// <summary>
	/// Smaller slates without a separate final still use the entire voting window.
	/// </summary>
	[TestMethod]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 5 )]
	public async Task SmallerSlates( int count )
	{
		var jam = new Jam { FinalsStart = Start, Results = Start.AddHours( 12 ) };
		var now = Start;
		var source = new JamFinalistPreview( jam, [Nominations( count )], () => now );
		Assert.AreEqual( 1, (await source.ReadAsync()).Single().Round );
		now = jam.Results.AddTicks( -1 );
		var final = (await source.ReadAsync()).Single();
		Assert.AreEqual( count - 1, final.Round );
		Assert.AreEqual( 2, final.Contenders.Count );
		Assert.AreEqual( jam.Results, final.RoundEnds );
	}

	/// <summary>
	/// Eliminated entries leave the vote tally while remaining in the standings.
	/// </summary>
	[TestMethod]
	[DataRow( 1, 5, 150 )]
	[DataRow( 2, 4, 140 )]
	[DataRow( 3, 3, 120 )]
	[DataRow( 4, 2, 90 )]
	public async Task PriorKnockouts( int round, int remaining, int total )
	{
		var source = new JamFinalistPreview( Schedule(), [Nominations()], () => Start.AddDays( round - 1 ) );
		var category = (await source.ReadAsync()).Single();
		Assert.AreEqual( round, category.Round );
		Assert.AreEqual( remaining, category.Contenders.Count );
		Assert.AreEqual( total, category.TotalVotes );
		Assert.AreEqual( 150, category.NominationCounts.Values.Sum() );
		foreach ( var eliminated in category.Nominees.Where( x => x.EliminatedRound.HasValue ) )
		{
			Assert.AreEqual( 6 - eliminated.Seed, eliminated.EliminatedRound );
			Assert.IsFalse( category.Counts.ContainsKey( eliminated.PackageIdent ) );
			Assert.IsFalse( (await source.GetEntryStatusAsync( category.Id, eliminated.PackageIdent )).Value.CanNominate );
			await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => source.VoteAsync( category, eliminated.PackageIdent ) );
		}
	}

	/// <summary>
	/// Selections survive refreshes, replace prior votes, and reset when seeking to another round.
	/// </summary>
	[TestMethod]
	public async Task LocalVotesAndClockRewind()
	{
		var now = Start;
		var source = new JamFinalistPreview( Schedule(), [Nominations()], () => now );
		var first = (await source.ReadAsync()).Single();
		var voted = await source.VoteAsync( first, "test.game4" );
		Assert.AreSame( voted, (await source.ReadAsync()).Single() );
		Assert.AreEqual( 151, voted.TotalVotes );
		Assert.AreEqual( 150, first.TotalVotes );
		var moved = await source.VoteAsync( voted, "test.game5" );
		Assert.AreEqual( 40, moved.Counts["test.game4"] );
		Assert.AreEqual( 51, moved.Counts["test.game5"] );
		CollectionAssert.AreEqual( new[] { "test.game5" }, moved.MyVotes.ToArray() );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => source.VoteAsync( first, "test.game4" ) );

		now = Start.AddDays( 2 );
		Assert.AreEqual( 3, (await source.ReadAsync()).Single().Round );
		now = Start;
		var rewound = (await source.ReadAsync()).Single();
		Assert.AreEqual( 1, rewound.Round );
		Assert.AreEqual( 0, rewound.MyVotes.Count );
		Assert.AreEqual( 150, rewound.TotalVotes );
		now = Start.AddDays( 4 );
		Assert.IsTrue( (await source.ReadAsync()).Single().Decided );
		now = Start.AddDays( 3 );
		Assert.IsTrue( (await source.ReadAsync()).Single().VotingOpen );
	}

	/// <summary>
	/// A final vote affects its confirmed result, and ties remain unresolved after the deadline.
	/// </summary>
	[TestMethod]
	public async Task FinalVoteAndTie()
	{
		var now = Start.AddDays( 3 );
		var seed = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Tally = [new() { Package = "test.first", Votes = 10 }, new() { Package = "test.second", Votes = 9 }]
		} );
		var source = new JamFinalistPreview( Schedule(), [seed], () => now );
		var final = (await source.ReadAsync()).Single();
		await source.VoteAsync( final, "test.second" );
		now = Start.AddDays( 4 );
		var tied = (await source.ReadAsync()).Single();
		Assert.IsFalse( tied.Decided );
		Assert.IsFalse( tied.VotingOpen );
		Assert.IsNull( tied.GetFinalistAtPlace( 1 ) );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => source.VoteAsync( tied, "test.first" ) );
	}

	/// <summary>
	/// An empty category stays empty and a lone entry can be crowned without inventing opponents.
	/// </summary>
	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	public async Task EmptyAndSingleEntry( int count )
	{
		var source = new JamFinalistPreview( Schedule(), [Nominations( count )], () => Start.AddDays( 4 ) );
		var category = (await source.ReadAsync()).Single();
		Assert.AreEqual( count, category.Nominees.Count );
		Assert.AreEqual( count == 1, category.Decided );
	}
}
