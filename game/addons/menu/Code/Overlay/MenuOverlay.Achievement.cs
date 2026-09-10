using Sandbox;

public partial class MenuOverlay : IBackendListener
{
	public void OnAchievementUnlocked( IBackendListener.AchievementUnlock data )
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

	public void OnNotice( IBackendListener.Notice data )
	{
		var popup = new Sandbox.OverlayPopups.NoticePopup();
		popup.Title = data.Title;
		popup.Text = data.Text;
		popup.Icon = data.Icon;
		popup.Type = data.Type;
		popup.Link = data.Link;
		Top.Queue( popup, duration: 8f, clickToDismiss: true );
	}
}
