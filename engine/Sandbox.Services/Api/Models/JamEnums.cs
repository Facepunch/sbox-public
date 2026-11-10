namespace Sandbox.Services;

/// <summary>
/// The step a jam is on. Same order as the steps in <see cref="JamDto.Timeline"/>; a jam
/// without a teaser or a distinct grand final simply skips those.
/// </summary>
public enum JamPhase : int
{
	Upcoming = 0,
	Teased = 1,
	Building = 2,
	Judging = 3,
	Nominations = 4,
	Finals = 5,
	GrandFinal = 6,
	Crowned = 7,
}

/// <summary>
/// Where a category is in its life. Mirrors <c>JamCategoryState</c> in Base/Data/Models/Jam/Jam.cs; keep the values in step.
/// </summary>
public enum JamCategoryPhase : int
{
	Pending = 0,
	Nominating = 1,
	Eliminating = 2,
	GrandFinal = 3,
	Decided = 4,
}

/// <summary>
/// What a category is doing right now, from the voter's side.
/// </summary>
public enum JamVotingMode : int
{
	/// <summary>Nothing open: not started, between the lock and the first round, or between rounds.</summary>
	Closed = 0,
	Nominating = 1,
	Voting = 2,
	Decided = 3,
}
