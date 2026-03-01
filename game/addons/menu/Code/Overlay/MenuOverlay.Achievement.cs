using Sandbox;

public partial class MenuOverlay : IAchievementListener
{
	public void OnAchievementUnlocked( IAchievementListener.UnlockDescription data )
	{
		// should we async this and wait for the achievement texture to load?
		// should we pre-download the achievement textures with the package?

		var popup = new Sandbox.OverlayPopups.AchievementUnlocked();
		popup.Title = data.Title;
		popup.Description = data.Description;
		popup.Icon = data.Icon;
		popup.Score = data.ScoreAdded;
		popup.PlayerScore = data.TotalPlayerScore;
		Top.Queue( popup, duration: 6f, clickToDismiss: false );
	}

	/*
	RealTimeSince timeSinceRun;

	public override void Tick()
	{
		base.Tick();

		if ( timeSinceRun > 6 )
		{
			timeSinceRun = 0;

			var popup = new Sandbox.OverlayPopups.AchievementUnlocked();
			AddPopup( popup, null );
		}
	}
	*/
}
