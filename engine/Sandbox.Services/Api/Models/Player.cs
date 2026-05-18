namespace Sandbox.Services;

public class Player
{
	public long Id { get; set; }
	public string Name { get; set; }
	public string Url { get; set; }
	public string Avatar { get; set; }
	public bool Online { get; set; }
	public bool Private { get; set; }
	public int Score { get; set; }
}

public class PlayerOverview
{
	public Player Player { get; set; }

	public long GamesPlayed { get; set; }
	public long TotalSessions { get; set; }
	public long SecondsPlayed { get; set; }
	public long Achievements { get; set; }
	public long TotalFavourites { get; set; }
	public long TotalReviews { get; set; }
	public long NegativeReviews { get; set; }
	public long PositiveReviews { get; set; }
	public string Avatar { get; set; }
	public PackageReviewDto[] LatestReviews { get; set; }

	public PackageWrapMinimal MostPlayed { get; set; }
	public PackageWrapMinimal LatestPlayed { get; set; }
}

public class PlayerFeedEntry
{
	public DateTimeOffset Timestamp { get; set; }
	public string Text { get; set; }
	public string Url { get; set; }
	public string EntryType { get; set; }
	public string Image { get; set; }
	public string Data { get; set; }
	public string Emoji { get; set; }
	public Player Player { get; set; }
	public PackageWrapMinimal Package { get; set; }
}



public class PlayerAchievementProgress
{
	public PackageWrapMinimal Package { get; set; }
	public AchievementDto[] Achievements { get; set; }
	public DateTimeOffset LastSeen { get; set; }
	public int Unlocked { get; set; }
	public int Score { get; set; }
	public int Total { get; set; }
	public int TotalScore { get; set; }
}

public struct StorageEntry
{
	public DateTimeOffset Updated { get; set; }
	public long SteamId { get; set; }
	public string Key { get; set; }
	public string Value { get; set; }
}
