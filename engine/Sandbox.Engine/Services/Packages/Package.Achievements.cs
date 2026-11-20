namespace Sandbox;

public partial class Package
{
	internal AchievementCollection GetCachedAchievements() => achievements;

	AchievementCollection achievements;

	Task _fetchAchievementsTask;

	/// <summary>
	/// Get a list of achievements
	/// </summary>
	public async ValueTask<AchievementCollection> GetAchievements()
	{
		if ( _fetchAchievementsTask is not null )
			await _fetchAchievementsTask;

		if ( achievements is not null )
			return achievements;

		achievements = new AchievementCollection( FullIdent );

		try
		{
			_fetchAchievementsTask = achievements.FetchFromBackend();
			await _fetchAchievementsTask;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when fetching achievements ({e.Message})" );
			return default;
		}
		finally
		{
			_fetchAchievementsTask = null;
		}

		return achievements;
	}
}
