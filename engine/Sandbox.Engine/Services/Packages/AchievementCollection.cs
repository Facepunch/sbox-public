using Sandbox.Services;

namespace Sandbox;

/// <summary>
/// Holds achievements for a package
/// </summary>
public sealed class AchievementCollection
{
	private string packageIdent;
	Dictionary<string, Achievement> _entries = new( StringComparer.OrdinalIgnoreCase );

	public IReadOnlyCollection<Achievement> All => _entries.Values;

	public AchievementCollection( string packageIdent )
	{
		this.packageIdent = packageIdent;
	}

	/// <summary>
	/// Get achievement by name, or null of it doesn't exist
	/// </summary>
	public Achievement Get( string name ) => _entries.GetValueOrDefault( name );

	internal async Task FetchFromBackend()
	{
		var list = await Backend.Achievements.GetList( packageIdent );

		foreach ( var ach in list )
		{
			var entry = new Achievement( ach );
			_entries[entry.Name] = entry;
		}

		await RecountProgression();
	}

	/// <summary>
	/// Use the current stats to recount the progression on stats with progression. This is purely for UI,
	/// you can't force an achivement to unlock early by calling this.
	/// </summary>
	public async Task RecountProgression()
	{
		var stats = Stats.GetLocalPlayerStats( packageIdent );

		await stats.Refresh();

		foreach ( var entry in _entries.Values )
		{
			if ( string.IsNullOrWhiteSpace( entry.Dto.SourceStat ) ) continue;

			var stat = stats.Get( entry.Dto.SourceStat );
			entry.UpdateProgressionFromStat( stat );
		}
	}

	/// <summary>
	/// Unlock this achievement. It must be a manual achievement.
	/// </summary>
	internal void ManualUnlock( string name )
	{
		if ( !_entries.TryGetValue( name, out Achievement entry ) ) return;
		if ( entry.IsUnlocked ) return;
		if ( !entry.IsUnlockedManually ) return;

		Unlock( name );
	}

	/// <summary>
	/// Unlock this achievement. It can be anything.
	/// </summary>
	async void Unlock( string name )
	{
		if ( !_entries.TryGetValue( name, out Achievement entry ) ) return;
		if ( entry.IsUnlocked ) return;
		if ( Backend.Achievements is null ) return;

		entry.UnlockTimestamp = DateTime.UtcNow;

		try
		{
			await Backend.Achievements.Unlock( packageIdent, entry.Name );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, "Exception when trying to unlock achievement" );

			Sentry.SentrySdk.CaptureException( e, scope =>
			{
				scope.SetExtra( "package", packageIdent );
				scope.SetExtra( "achievement", entry.Name );
			} );

		}
	}

	/// <summary>
	/// Check each achievement to see if it can be unlocked.
	/// </summary>
	internal void TestAchivementsForUnlock()
	{
		var stats = Sandbox.Services.Stats.GetLocalPlayerStats( packageIdent );

		foreach ( var entry in _entries.Values )
		{
			if ( string.IsNullOrWhiteSpace( entry.Dto.SourceStat ) ) continue;

			// If we unlock an achievement, we want to wait
			// for 5 seconds before checking again and triggering the next one.
			// The notifications are queued, I just don't want people starting a game
			// and immediately sending 30 unlock notices to the backend.
			if ( UnlockTest( entry, stats ) )
			{
				Services.Achievements.DelayAchievementUnlocks( 5 );
				return;
			}
		}
	}

	private bool UnlockTest( Achievement entry, Stats.PlayerStats stats )
	{
		if ( entry.IsUnlocked ) return false;
		if ( entry.IsUnlockedManually ) return false;

		if ( entry.IsUnlockedWithStat )
		{
			var stat = stats.Get( entry.Dto.SourceStat );
			var fraction = entry.GetFractionFromStat( stat );

			//Log.Info( $"{entry.Name} - {fraction} ({stat.Sum})" );

			if ( fraction >= 1 )
			{
				Unlock( entry.Name );
				return true;
			}
		}

		return false;
	}
}
