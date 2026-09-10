using System.ComponentModel.DataAnnotations;

namespace Sandbox.Services;

public enum ReviewScore
{
	None = 0,
	Negative = 1,
	Positive = 2,
	Promise = 3,
}

// Convention for review tags: [Display(Name = title, ShortName = material-symbols-icon)].
// DisplayAttribute is the standard .NET attribute for human-facing UI metadata on enum members.

[Flags]
public enum ReviewPositiveTags : int
{
	None = 0,

	[Display( Name = "Graphics", ShortName = "palette" )] Graphics = 1 << 0,
	[Display( Name = "Audio", ShortName = "volume_up" )] Audio = 1 << 1,
	[Display( Name = "Gameplay", ShortName = "sports_esports" )] Gameplay = 1 << 2,
	[Display( Name = "Story", ShortName = "menu_book" )] Story = 1 << 3,
	[Display( Name = "Multiplayer", ShortName = "groups" )] Multiplayer = 1 << 4,

	[Display( Name = "Originality", ShortName = "lightbulb" )] Originality = 1 << 5,
	[Display( Name = "Performance", ShortName = "speed" )] Performance = 1 << 6,
	[Display( Name = "Polish", ShortName = "auto_awesome" )] Polish = 1 << 7,
	[Display( Name = "Addictive", ShortName = "favorite" )] Addictive = 1 << 8,
	[Display( Name = "Replayability", ShortName = "replay" )] Replayability = 1 << 9,
	[Display( Name = "Controls", ShortName = "gamepad" )] Controls = 1 << 10,
	[Display( Name = "Updates", ShortName = "update" )] Updates = 1 << 11,
}

[Flags]
public enum ReviewNegativeTags : int
{
	None = 0,

	[Display( Name = "Unfinished", ShortName = "construction" )] Unfinished = 1 << 1,
	[Display( Name = "Unoptimized", ShortName = "slow_motion_video" )] Unoptimized = 1 << 2,
	[Display( Name = "Bad Controls", ShortName = "gamepad" )] BadControls = 1 << 3,
	[Display( Name = "Confusing", ShortName = "help" )] Confusing = 1 << 4,
	[Display( Name = "Slop", ShortName = "mop" )] Slop = 1 << 5,
	[Display( Name = "Generated Art", ShortName = "smart_toy" )] GeneratedArt = 1 << 6,
	[Display( Name = "Pay to Win", ShortName = "paid" )] PayToWin = 1 << 7,
	[Display( Name = "Stolen", ShortName = "report" )] Stolen = 1 << 8,
	[Display( Name = "Errors", ShortName = "error" )] Errors = 1 << 9,
	[Display( Name = "Load Times", ShortName = "hourglass_top" )] LoadTimes = 1 << 10,
	[Display( Name = "Buggy", ShortName = "bug_report" )] Buggy = 1 << 11,
	[Display( Name = "Clicker", ShortName = "touch_app" )] Clicker = 1 << 12,
	[Display( Name = "Idle", ShortName = "autorenew" )] Idle = 1 << 13,
}

public enum DisplayMode
{
	/// <summary>
	/// Regular display mode
	/// </summary>
	Normal = 0,

	/// <summary>
	/// Set by admins when content is abusive etc
	/// </summary>
	HiddenfromPublic = 1
}

public class PackageReviewList
{
	public int Count { get; set; }
	public int Skip { get; set; }
	public int Take { get; set; }
	public List<PackageReviewDto> Entries { get; set; } = new();
}

public class PackageReviewDto
{
	/// <summary>
	/// The player that made the review
	/// </summary>
	public Player Player { get; set; }

	/// <summary>
	/// SteamId of the reviewer
	/// </summary>
	public long SteamId { get; set; }

	/// <summary>
	/// Id of the reviewed package
	/// </summary>
	public long PackageId { get; set; }

	/// <summary>
	/// The actual content
	/// </summary>
	public string Content { get; set; }

	/// <summary>
	/// The score of the review
	/// </summary>
	public ReviewScore Score { get; set; }

	/// <summary>
	/// Whether this review is publicly visible. Hidden reviews are only returned to admins.
	/// </summary>
	public DisplayMode DisplayMode { get; set; }

	/// <summary>
	/// The package being reviewed
	/// </summary>
	public PackageWrapMinimal Package { get; set; }

	/// <summary>
	/// How many seconds this user played
	/// </summary>
	public int SecondsPlayed { get; set; }

	/// <summary>
	/// When it was created
	/// </summary>
	public DateTimeOffset Created { get; set; }

	/// <summary>
	/// When it was updated
	/// </summary>
	public DateTimeOffset Updated { get; set; }

	/// <summary>
	/// Positive tags for this review
	/// </summary>
	public ReviewPositiveTags Positives { get; set; }

	/// <summary>
	/// Negative tags for this review
	/// </summary>
	public ReviewNegativeTags Negatives { get; set; }
}
