using Sandbox.Services;

namespace Sandbox;

/// <summary>
/// Holds achievements for a package
/// </summary>
public sealed class AchievementCollection
{
	private string packageIdent;
	readonly OrderedDictionary<string, Achievement> _entries = new( StringComparer.OrdinalIgnoreCase );
	int _nextUnlockIndex;

	public IReadOnlyCollection<Achievement> All => _entries.Values;

	public AchievementCollection( string packageIdent )
	{
		this.packageIdent = packageIdent;
	}

	/// <summary>
	/// Get achievement by name, or null of it doesn't exist
	/// </summary>
	public Achievement Get( string name ) => _entries.GetValueOrDefault( name );

	/// <summary>
	/// Manual unlocks asked for before the backend list arrived. The game can start before the
	/// fetch finishes, so they wait here and go through once the list is in.
	/// </summary>
	List<string> _pendingManualUnlocks;
	bool _fetched;

	internal async Task FetchFromBackend()
	{
		var list = await Backend.Achievements.GetList( packageIdent );

		foreach ( var ach in list )
		{
			var entry = new Achievement( ach );
			_entries[entry.Name] = entry;
		}

		_fetched = true;

		if ( _pendingManualUnlocks is not null )
		{
			foreach ( var name in _pendingManualUnlocks )
			{
				ManualUnlock( name );
			}

			_pendingManualUnlocks = null;
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
		if ( !ThreadSafe.IsMainThread )
		{
			MainThread.Queue( () => ManualUnlock( name ) );
			return;
		}

		if ( Application.GamePackage?.GetCachedAchievements() != this || AccountInformation.SteamId == 0 ) return;

		if ( !_fetched )
		{
			_pendingManualUnlocks ??= new();
			_pendingManualUnlocks.Add( name );
			return;
		}

		if ( !_entries.TryGetValue( name, out Achievement entry ) ) return;
		if ( !entry.IsUnlockedManually ) return;

		_ = entry.Unlock( packageIdent, Backend.Achievements );
	}

	/// <summary>
	/// Check each achievement to see if it can be unlocked.
	/// </summary>
	internal void TestAchivementsForUnlock()
	{
		if ( Backend.Achievements is null || AccountInformation.SteamId == 0 ) return;

		var stats = Stats.GetLocalPlayerStats( packageIdent );

		var index = _nextUnlockIndex;
		for ( int remaining = _entries.Count; remaining > 0; remaining-- )
		{
			var entry = _entries.GetAt( index ).Value;
			if ( ++index == _entries.Count ) index = 0;

			if ( !entry.CanUnlock || !entry.IsUnlockedWithStat || string.IsNullOrWhiteSpace( entry.Dto.SourceStat ) ) continue;

			var stat = stats.Get( entry.Dto.SourceStat );
			if ( entry.GetFractionFromStat( stat ) is not >= 1 ) continue;

			// Give the next eligible achievement a turn even if this request fails.
			_nextUnlockIndex = index;
			_ = entry.Unlock( packageIdent, Backend.Achievements );
			Services.Achievements.DelayAchievementUnlocks( 5 );
			return;
		}
	}
}
