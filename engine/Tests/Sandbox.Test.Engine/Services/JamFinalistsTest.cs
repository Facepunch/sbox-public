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
		Assert.IsFalse( category.Nominees.Any( x => x.PackageIdent == "test.outside-slate" ) );
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
}
