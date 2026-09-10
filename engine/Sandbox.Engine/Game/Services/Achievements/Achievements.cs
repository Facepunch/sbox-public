
namespace Sandbox.Services;

public static partial class Achievements
{
	public static void Unlock( string name )
	{
		// Achievements belong to the local player — a dedicated server has no
		// authenticated player context, so the backend would 401 the request.
		if ( Application.IsDedicatedServer ) return;

		var package = Application.GamePackage;
		if ( package is null ) return;

		var collection = package.GetCachedAchievements();
		if ( collection is null ) return;

		collection.ManualUnlock( name );
	}

	public static IEnumerable<Achievement> All => Application.GamePackage?.GetCachedAchievements()?.All ?? Enumerable.Empty<Achievement>();

	/// <summary>
	/// Delay automatic achievement unlocks for this many seconds. Useful when loading etc.
	/// </summary>
	internal static void DelayAchievementUnlocks( float seconds )
	{
		timeSinceTest = -seconds;
	}

	static RealTimeSince timeSinceTest = 0;
	internal static void Tick()
	{
		if ( timeSinceTest < 1.0f )
			return;

		timeSinceTest = Random.Shared.Float( 0, 0.5f );

		Application.GamePackage?.GetCachedAchievements()?.TestAchivementsForUnlock();
		Application.MapPackage?.GetCachedAchievements()?.TestAchivementsForUnlock();
	}
}

