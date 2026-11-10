namespace Sandbox.Services;

/// <summary>
/// Activity Feed
/// </summary>
public sealed class AchievementOverview
{
	public Package Package { get; set; }
	public Achievement[] Achievements { get; set; }
	public DateTimeOffset LastSeen { get; set; }
	public int Unlocked { get; set; }
	public int Score { get; set; }
	public int Total { get; set; }
	public int TotalScore { get; set; }

	// Internal - because exposed through MenuUtility because games don't need to access this
	internal static async Task<AchievementOverview[]> GetFeed( int take = 20 )
	{
		take = take.Clamp( 1, 50 );

		try
		{
			var posts = await Sandbox.Backend.Players.GetAchievementProgress( AccountInformation.SteamId, take );
			if ( posts is null ) return Array.Empty<AchievementOverview>();

			return posts.Select( x => From( x ) ).ToArray();
		}
		catch ( Exception )
		{
			return Array.Empty<AchievementOverview>();
		}
	}
	internal static AchievementOverview From( PlayerAchievementProgress p )
	{
		if ( p is null ) return default;

		return new AchievementOverview
		{
			Achievements = p.Achievements.Select( x => new Achievement( x ) ).ToArray(),
			LastSeen = p.LastSeen,
			Unlocked = p.Unlocked,
			Score = p.Score,
			Total = p.Total,
			TotalScore = p.TotalScore,
			Package = RemotePackage.FromDto( p.Package ),
		};
	}
}
