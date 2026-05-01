namespace Sandbox.Services;

public enum ReviewScore
{
	None = 0,
	Negative = 1,
	Positive = 2,
	Promise = 3,
}

[Flags]
public enum ReviewPositiveTags : int
{
	None = 0,

	Graphics = 1 << 0,
	Audio = 1 << 1,
	Gameplay = 1 << 2,
	Story = 1 << 3,
	Multiplayer = 1 << 4,

	Originality = 1 << 5,
	Performance = 1 << 6,
	Polish = 1 << 7,
	Addictive = 1 << 8,
	Replayability = 1 << 9,
	Controls = 1 << 10,
	Updates = 1 << 11,
}

[Flags]
public enum ReviewNegativeTags : int
{
	None = 0,
	Unfinished = 1 << 1,
	Unoptimized = 1 << 2,
	BadControls = 1 << 3,
	Confusing = 1 << 4,
	Slop = 1 << 5,
	GeneratedArt = 1 << 6,
	PayToWin = 1 << 7,
	Stolen = 1 << 8,
	Errors = 1 << 9,
	LoadTimes = 1 << 10,
	Buggy = 1 << 11,
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
