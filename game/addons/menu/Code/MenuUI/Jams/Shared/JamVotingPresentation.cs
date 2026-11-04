using Sandbox;
using Sandbox.Services;

namespace MenuProject.MenuUI.Jams;

/// <summary>
/// A scheduled opening is a waiting state, not a finished round. Keep waiting after its clock
/// expires until a fresh backend snapshot actually opens voting.
/// </summary>
public static class JamVotingPresentation
{
	public static bool Waiting( JamFinalistCategory category ) => category is { Decided: false, VotingOpen: false }
		&& category.NextRoundOpens.HasValue;

	public static DateTimeOffset? OpensAt( Jam jam, JamFinalistCategory category ) => category?.NextRoundOpens
		?? (category?.GrandFinal == true ? jam.GrandFinal : jam.FinalsStart);
}
