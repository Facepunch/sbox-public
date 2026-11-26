using Sandbox.Services;

namespace Sandbox;

[Expose]
public sealed class Achievement
{
	public string Name { get; internal set; }
	public string Title { get; internal set; }
	public string Description { get; internal set; }
	public string Icon { get; internal set; }
	public bool IsUnlocked => UnlockTimestamp.HasValue && UnlockTimestamp.Value.Year > 1000;
	public DateTimeOffset? UnlockTimestamp { get; internal set; }
	public int Score { get; internal set; }
	public Vector2 Range { get; internal set; }
	public float CurrentValue { get; internal set; }

	/// <summary>
	/// Returns whether this achievement should be visible to the player
	/// </summary>
	public bool IsVisible
	{
		get
		{
			if ( dto.Visibility == AchievementDto.VisibilityModes.Visible ) return true;
			if ( dto.Visibility == AchievementDto.VisibilityModes.VisibleWhenUnlocked ) return IsUnlocked;

			return false;
		}
	}

	bool unlockPending;
	internal bool CanUnlock => !IsUnlocked && !unlockPending;

	/// <summary>
	/// Records the award locally once the backend confirms it.
	/// </summary>
	internal async Task Unlock( string packageIdent, ServiceApi.IAchievementApi api )
	{
		if ( !CanUnlock || api is null ) return;

		unlockPending = true;
		try
		{
			var response = await api.UnlockWithResult( packageIdent, Name );
			if ( response.Valid && response.Result.Actioned )
			{
				UnlockTimestamp = DateTimeOffset.UtcNow;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not unlock achievement {packageIdent}/{Name}" );
		}
		finally
		{
			unlockPending = false;
		}
	}

	internal bool IsUnlockedManually => dto.UnlockMode == AchievementDto.UnlockModes.Manual;
	internal bool IsUnlockedWithStat => dto.UnlockMode == AchievementDto.UnlockModes.Stat;

	public bool HasProgression { get; internal set; }

	public int GlobalUnlocked { get; internal set; }
	public float GlobalFraction { get; internal set; }

	/// <summary>
	/// A float, representing the progression of this stat. 0 is 0%, 1 is 100%. Not clamped.
	/// </summary>
	public float ProgressionFraction { get; internal set; }

	AchievementDto dto;
	internal AchievementDto Dto => dto;

	internal Achievement( AchievementDto ach )
	{
		Name = ach.Name;
		Title = ach.Title;
		Description = ach.Description;
		Icon = ach.Icon;
		UnlockTimestamp = ach.Unlocked;
		Score = ach.Score;
		dto = ach;
		GlobalUnlocked = ach.GlobalUnlocks;
		GlobalFraction = (float)ach.GlobalFraction;
		HasProgression = ach.ShowProgress;
		Range = new Vector2( (float)ach.Min, (float)ach.Max );

	}

	/// <summary>
	/// Given a stat return the fraction completed this achievement is. The assumption
	/// is that the stat you pass in matches the stat this achievement is looking for.
	/// The number returned is unclamped.
	/// </summary>
	internal double GetFractionFromStat( Stats.PlayerStat stat )
	{
		var val = stat.GetValue( dto.SourceAggregation );

		if ( dto.Min == dto.Max )
		{
			return val >= dto.Min ? 1.0 : 0.0;
		}

		return MathX.Remap( val, dto.Min, dto.Max, 0, 1, false );
	}

	internal void UpdateProgressionFromStat( Stats.PlayerStat stat )
	{
		HasProgression = dto.ShowProgress;
		ProgressionFraction = (float)GetFractionFromStat( stat );
		CurrentValue = (float)stat.GetValue( dto.SourceAggregation );
	}
}
