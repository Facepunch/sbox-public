namespace Sandbox.Services;

/// <summary>
/// A game jam as the game sees it. Everything the main menu needs to show the current jam
/// and send people to the entries: the theme (withheld until the jam starts), the schedule,
/// the categories and their prizes, and the package search query that lists the entries.
/// </summary>
public class JamDto
{
	/// <summary>
	/// Short url name, e.g. "three". Stable for the life of the jam.
	/// </summary>
	public string Ident { get; set; }

	public string Title { get; set; }
	public string Description { get; set; }

	/// <summary>
	/// The jam page on the website.
	/// </summary>
	public string Url { get; set; }

	/// <summary>
	/// The theme. Null until the jam has started - it's a secret before that.
	/// </summary>
	public string Theme { get; set; }

	/// <summary>
	/// Headline prize figure as it should read, e.g. "£50,000".
	/// </summary>
	public string PrizePool { get; set; }

	/// <summary>
	/// The step the jam is on right now. Matches the steps in <see cref="Timeline"/>.
	/// </summary>
	public JamPhase Phase { get; set; }

	/// <summary>
	/// True from the theme drop until the winners are crowned.
	/// </summary>
	public bool IsLive { get; set; }

	/// <summary>
	/// When the teaser went public. Null when the jam had no teaser phase.
	/// </summary>
	public DateTimeOffset? Announced { get; set; }

	/// <summary>
	/// The theme drop. Building and entries open here.
	/// </summary>
	public DateTimeOffset Starts { get; set; }

	/// <summary>
	/// Entries close.
	/// </summary>
	public DateTimeOffset Deadline { get; set; }

	/// <summary>
	/// Nominations lock and the finalists are cut. Same as <see cref="Deadline"/> for a jam without a separate window.
	/// </summary>
	public DateTimeOffset NominationsEnd { get; set; }

	/// <summary>
	/// The first elimination round opens.
	/// </summary>
	public DateTimeOffset FinalsStart { get; set; }

	/// <summary>
	/// The last two go head to head. Null when the jam doesn't have a distinct grand final.
	/// </summary>
	public DateTimeOffset? GrandFinal { get; set; }

	/// <summary>
	/// Winners crowned.
	/// </summary>
	public DateTimeOffset Results { get; set; }

	/// <summary>
	/// Whether the winners are decided by the players who play the entries.
	/// </summary>
	public bool CommunityVoting { get; set; }

	public int EntryCount { get; set; }

	/// <summary>
	/// Package search query that lists the entries, e.g. "jam:three". Combine with the
	/// usual sort and type tokens.
	/// </summary>
	public string Query { get; set; }

	public JamCategoryDto[] Categories { get; set; } = [];

	/// <summary>
	/// The jam's steps in order, each with the moment it starts.
	/// </summary>
	public JamStepDto[] Timeline { get; set; } = [];
}

public class JamCategoryDto
{
	public int Id { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }

	/// <summary>
	/// An emoji.
	/// </summary>
	public string Icon { get; set; }

	/// <summary>
	/// The headline prize. When <see cref="Places"/> is filled this is the pool the places share.
	/// </summary>
	public string Prize { get; set; }

	/// <summary>
	/// Prize per finishing place, best first. Empty for a single-winner category.
	/// </summary>
	public string[] Places { get; set; } = [];

	/// <summary>
	/// True when the players decide this one; false when staff pick the winner.
	/// </summary>
	public bool Community { get; set; }

	/// <summary>
	/// Where the category is in its life.
	/// </summary>
	public JamCategoryPhase State { get; set; }

	/// <summary>
	/// Full ident of the winning package, e.g. "facepunch.construct". Null until decided.
	/// </summary>
	public string Winner { get; set; }
}

public class JamStepDto
{
	public JamPhase Phase { get; set; }

	/// <summary>
	/// Display name for the step, e.g. "Grand final".
	/// </summary>
	public string Name { get; set; }
	public DateTimeOffset At { get; set; }
}

/// <summary>
/// The voting picture of a jam: what each community category is doing right now, who is
/// standing, the live counts, and the caller's own votes. Poll it while a voting screen is
/// open; the counts are cached a few seconds server-side.
/// </summary>
public class JamVotingDto
{
	public string Ident { get; set; }

	public JamCategoryVotingDto[] Categories { get; set; } = [];
}

public class JamCategoryVotingDto
{
	public int Id { get; set; }
	public string Title { get; set; }
	public string Icon { get; set; }

	/// <summary>
	/// What the category is doing right now.
	/// </summary>
	public JamVotingMode Mode { get; set; }

	/// <summary>
	/// Why nothing is open, as a sentence to show the player. Null while a round is open.
	/// </summary>
	public string ClosedReason { get; set; }

	/// <summary>
	/// The open round: 0 is nominations, then 1.. up the ladder. Null when nothing is open.
	/// </summary>
	public int? Round { get; set; }

	public DateTimeOffset? RoundEnds { get; set; }

	/// <summary>
	/// When the next round opens, while nothing is open. Null when there isn't one.
	/// </summary>
	public DateTimeOffset? NextRoundOpens { get; set; }

	/// <summary>
	/// The open round is the last two head to head, counted live.
	/// </summary>
	public bool GrandFinal { get; set; }

	/// <summary>
	/// How many entries a player may hold a vote for in the open round. Null = as many as they like (nominations).
	/// </summary>
	public int? VotesPerPlayer { get; set; }

	/// <summary>
	/// How many entries make the cut when nominations lock.
	/// </summary>
	public int Slots { get; set; }

	/// <summary>
	/// The slate once nominations have locked, in seed order. Empty before that.
	/// </summary>
	public JamNomineeDto[] Nominees { get; set; } = [];

	/// <summary>
	/// Live counts for the open round, or the grand final once decided. Most votes first.
	/// A nominee with no votes isn't listed; a withdrawn one can be (see <see cref="JamNomineeDto.Withdrawn"/>).
	/// </summary>
	public JamTallyDto[] Tally { get; set; } = [];

	/// <summary>
	/// Entries the caller currently holds a vote for in the open round. Empty when not authenticated.
	/// </summary>
	public string[] MyVotes { get; set; } = [];

	/// <summary>
	/// Full ident of the winning package. Null until decided.
	/// </summary>
	public string Winner { get; set; }
}

public class JamNomineeDto
{
	/// <summary>
	/// Full ident, e.g. "facepunch.construct". Null only if the package no longer exists.
	/// </summary>
	public string Package { get; set; }

	/// <summary>
	/// Rank at the nomination lock, 1 = most nominated.
	/// </summary>
	public int Seed { get; set; }

	/// <summary>
	/// The round it dropped out in. Null while still standing.
	/// </summary>
	public int? EliminatedRound { get; set; }

	/// <summary>
	/// Pulled from the slate because the entry was hidden. Takes no place.
	/// </summary>
	public bool Withdrawn { get; set; }

	/// <summary>
	/// Finishing place once the category is decided, 1 = winner. 0 before that.
	/// </summary>
	public int Place { get; set; }
}

public class JamTallyDto
{
	public string Package { get; set; }
	public int Votes { get; set; }
}

/// <summary>
/// Whether the caller can vote for one entry right now, and if not why. Ask it when a play
/// session ends: a true <see cref="CanVote"/> is the moment to offer the vote.
/// </summary>
public class JamEntryVoteDto
{
	public string Package { get; set; }
	public bool IsEntry { get; set; }
	public bool CanVote { get; set; }

	/// <summary>
	/// Why not, as a sentence to show the player. Null when they can.
	/// </summary>
	public string Reason { get; set; }

	/// <summary>
	/// Category ids a vote for this entry can land in right now.
	/// </summary>
	public int[] OpenCategories { get; set; } = [];

	/// <summary>
	/// Category ids the caller already holds a vote for this entry in, in the open round.
	/// </summary>
	public int[] VotedCategories { get; set; } = [];
}
