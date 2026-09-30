using System;
using Sandbox.Services;

namespace Services;

/// <summary>
/// Verifies that the reveal uses the backend's slate and round state rather than local tallies or time.
/// </summary>
[TestClass]
public class JamFinalistsTest
{
	/// <summary>
	/// Voting requires an open snapshot and stops exactly at its advertised deadline.
	/// </summary>
	[TestMethod]
	public void VotingWindowHonorsModeAndDeadline()
	{
		var deadline = DateTimeOffset.UtcNow;
		var dto = new JamCategoryVotingDto { Mode = JamVotingMode.Voting, RoundEnds = deadline };
		var category = new JamFinalistCategory( dto );
		Assert.IsTrue( category.IsVotingAt( deadline.AddTicks( -1 ) ) );
		Assert.IsFalse( category.IsVotingAt( deadline ) );
		Assert.IsFalse( category.IsVotingAt( deadline.AddTicks( 1 ) ) );

		dto.RoundEnds = null;
		Assert.IsTrue( new JamFinalistCategory( dto ).IsVotingAt( deadline ) );

		dto.Mode = JamVotingMode.Closed;
		dto.NextRoundOpens = deadline.AddSeconds( -1 );
		Assert.IsFalse( new JamFinalistCategory( dto ).IsVotingAt( deadline ) );
	}

	/// <summary>
	/// A tied final retains the server's winner and places, and ignores late live updates.
	/// </summary>
	[TestMethod]
	public void DecidedFinalPreservesAuthoritativeTieBreak()
	{
		var category = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Id = 7,
			Mode = JamVotingMode.Decided,
			Winner = "test.second",
			Nominees =
			[
				new() { Package = "test.first", Seed = 1, Place = 2 },
				new() { Package = "test.second", Seed = 2, Place = 1 }
			],
			Tally = [new() { Package = "test.first", Votes = 50 }, new() { Package = "test.second", Votes = 50 }]
		} );

		Assert.IsTrue( category.Decided );
		Assert.IsFalse( category.VotingOpen );
		Assert.IsNull( category.Round );
		Assert.AreEqual( "test.second", category.Winner );
		Assert.IsTrue( category.GrandFinal );
		Assert.AreEqual( 2, category.Contenders.Count );
		Assert.AreEqual( "test.second", category.GetFinalistAtPlace( 1 )?.PackageIdent );
		Assert.AreEqual( "test.first", category.GetFinalistAtPlace( 2 )?.PackageIdent );
		CollectionAssert.AreEqual( new[] { 2, 1 }, category.Nominees.Select( x => x.Place ).ToArray() );
		Assert.IsFalse( category.Apply( new( "jam", 7, 4, "test.first", 110, 60 ) ) );
		Assert.AreEqual( 100, category.TotalVotes );
		Assert.AreEqual( 50, category.Counts["test.first"] );
	}

	/// <summary>
	/// Small slates remain small, including an empty category or a single automatic winner.
	/// </summary>
	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 5 )]
	public void PreservesSlateSize( int count )
	{
		var category = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Slots = 5,
			Nominees = Enumerable.Range( 1, count ).Select( seed => new JamNomineeDto { Package = $"test.game{seed}", Seed = seed } ).ToArray(),
			Tally = [new() { Package = "test.outside-slate", Votes = 1000 }]
		} );

		Assert.AreEqual( count, category.Nominees.Count );
		Assert.AreEqual( count, category.Contenders.Count );
		Assert.AreEqual( count == 2, category.GrandFinal );
		Assert.IsFalse( category.Nominees.Any( x => x.PackageIdent == "test.outside-slate" ) );
	}

	/// <summary>
	/// The decided final retains its runner-up in seed order even without explicit places.
	/// </summary>
	[TestMethod]
	public void DecidedFinalRetainsPairAndEliminationPlaces()
	{
		var category = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Mode = JamVotingMode.Decided,
			Winner = "test.winner",
			Nominees =
			[
				new() { Package = "test.runner-up", Seed = 1, EliminatedRound = 2 },
				new() { Package = "test.winner", Seed = 2 },
				new() { Package = "test.third", Seed = 3, EliminatedRound = 1 },
				new() { Package = "test.withdrawn", Seed = 4, Withdrawn = true }
			]
		} );

		Assert.IsTrue( category.GrandFinal );
		CollectionAssert.AreEqual( new[] { "test.runner-up", "test.winner" }, category.Contenders.Select( x => x.PackageIdent ).ToArray() );
		Assert.AreEqual( "test.winner", category.GetFinalistAtPlace( 1 )?.PackageIdent );
		Assert.AreEqual( "test.runner-up", category.GetFinalistAtPlace( 2 )?.PackageIdent );
		Assert.AreEqual( "test.third", category.GetFinalistAtPlace( 3 )?.PackageIdent );
		Assert.IsNull( category.GetFinalistAtPlace( 0 ) );
		Assert.IsNull( category.GetFinalistAtPlace( 4 ) );
	}

	/// <summary>
	/// A final's live tally cannot resolve places, including tied earlier eliminations.
	/// </summary>
	[TestMethod]
	public void LiveFinalDoesNotInventPlacesFromCountsOrTime()
	{
		var category = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Mode = JamVotingMode.Voting,
			RoundEnds = DateTimeOffset.UtcNow.AddMinutes( -1 ),
			Nominees =
			[
				new() { Package = "test.first", Seed = 1 },
				new() { Package = "test.second", Seed = 2 },
				new() { Package = "test.third", Seed = 3, EliminatedRound = 1 },
				new() { Package = "test.fourth", Seed = 4, EliminatedRound = 1 }
			],
			Tally = [new() { Package = "test.first", Votes = 100 }]
		} );

		Assert.IsTrue( category.GrandFinal );
		Assert.AreEqual( 2, category.Contenders.Count );
		for ( var place = 1; place <= 4; place++ )
		{
			Assert.IsNull( category.GetFinalistAtPlace( place ) );
		}
	}

	/// <summary>
	/// Removed entries leave seed gaps, missing packages remain visible, and vote tallies do not reorder seeds.
	/// </summary>
	[TestMethod]
	public void UsesServerSeedsAndKeepsMissingPackages()
	{
		var category = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Nominees =
			[
				new() { Package = "test.third", Seed = 3, EliminatedRound = 1 },
				new() { Package = "test.withdrawn", Seed = 2, Withdrawn = true },
				new() { Package = null, Seed = 4 },
				new() { Package = "test.first", Seed = 1 }
			],
			Tally = [new() { Package = "test.third", Votes = 500 }]
		} );

		CollectionAssert.AreEqual( new[] { 1, 3, 4 }, category.Nominees.Select( x => x.Seed ).ToArray() );
		Assert.AreEqual( "test.first", category.Nominees[0].PackageIdent );
		Assert.AreEqual( 1, category.Nominees[1].EliminatedRound );
		Assert.IsNull( category.Nominees[2].PackageIdent );
	}

	/// <summary>
	/// Passing the advertised opening time never opens voting without a server transition.
	/// </summary>
	[TestMethod]
	public void OnlyBackendModeOpensVoting()
	{
		var dto = new JamCategoryVotingDto
		{
			Mode = JamVotingMode.Closed,
			NextRoundOpens = DateTimeOffset.UtcNow.AddMinutes( -1 )
		};
		Assert.IsFalse( new JamFinalistCategory( dto ).VotingOpen );

		dto.Mode = JamVotingMode.Nominating;
		Assert.IsFalse( new JamFinalistCategory( dto ).VotingOpen );

		dto.Mode = JamVotingMode.Voting;
		dto.Round = 1;
		Assert.IsTrue( new JamFinalistCategory( dto ).VotingOpen );

		dto.Mode = JamVotingMode.Decided;
		var decided = new JamFinalistCategory( dto );
		Assert.IsTrue( decided.Decided );
		Assert.IsFalse( decided.VotingOpen );
	}

	/// <summary>
	/// Public updates replace absolute counts without changing personal votes or applying another round.
	/// </summary>
	[TestMethod]
	public void LiveCountsAreRoundScopedAndPreserveSelection()
	{
		var category = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Id = 7,
			Mode = JamVotingMode.Voting,
			Round = 2,
			MyVotes = ["test.first"],
			Nominees = [new() { Package = "test.first", Seed = 1 }, new() { Package = "test.second", Seed = 2 }],
			Tally = [new() { Package = "test.first", Votes = 10 }]
		} );

		Assert.IsFalse( category.Apply( new( "jam", 7, 1, "test.first", 100, 100 ) ) );
		Assert.IsFalse( category.Apply( new( "jam", 8, 2, "test.first", 100, 100 ) ) );
		Assert.IsTrue( category.Apply( new( "jam", 7, 2, "test.second", 14, 4 ) ) );
		Assert.IsTrue( category.Apply( new( "jam", 7, 2, "test.second", 14, 4 ) ) );
		Assert.AreEqual( 4, category.Counts["test.second"] );
		Assert.AreEqual( 14, category.TotalVotes );
		CollectionAssert.AreEqual( new[] { "test.first" }, category.MyVotes.ToArray() );
		Assert.AreEqual( "test.first", category.Nominees[0].PackageIdent );
	}

	/// <summary>
	/// A new snapshot owns the next round's empty counts and personal selection.
	/// </summary>
	[TestMethod]
	public void NewRoundStartsWithoutPreviousVotes()
	{
		var category = new JamFinalistCategory( new JamCategoryVotingDto
		{
			Mode = JamVotingMode.Voting,
			Round = 3,
			Nominees = [new() { Package = "test.first", Seed = 1, EliminatedRound = 2 }, new() { Package = "test.second", Seed = 2 }]
		} );

		Assert.AreEqual( 0, category.TotalVotes );
		Assert.AreEqual( 0, category.Counts.Count );
		Assert.AreEqual( 0, category.MyVotes.Count );
		Assert.AreEqual( 2, category.Nominees[0].EliminatedRound );
	}
}
