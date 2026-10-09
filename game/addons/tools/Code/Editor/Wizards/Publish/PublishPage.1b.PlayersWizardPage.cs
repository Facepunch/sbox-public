namespace Editor.Wizards;

using Editor.ProjectSettingPages;

partial class PublishWizard
{
	/// <summary>
	/// Games only - how the game is played, shown on its card in the menu. The same settings as
	/// Project Settings > Multiplayer, here for a last look before publishing.
	/// </summary>
	class PlayersWizardPage : PublishWizardPage
	{
		public override string PageTitle => "Players";
		public override string PageSubtitle => "How your game is meant to be played. Players see this on your game's card in the menu.";

		PlayerSettings Settings;
		WarningBox Problem;

		public override async Task OpenAsync()
		{
			// Only load when opening, not when rebuilding after hotload. The full fetch gives the old
			// website values to start from if the project doesn't declare its play modes yet.
			if ( Settings is null )
			{
				await Package.FetchAsync( Project.Config.FullIdent, false );
				Settings = PlayerSettings.Load( Project );
			}

			Rebuild();
			Visible = true;
		}

		public override void Rebuild()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = new Sandbox.UI.Margin( 64, 0 );
			BodyLayout.Spacing = 16;

			BodyLayout.AddStretchCell();

			Settings.Build( BodyLayout, so => so.OnPropertyChanged = _ => UpdateProblem() );

			Problem = new WarningBox( "", this );
			BodyLayout.Add( Problem );

			BodyLayout.AddStretchCell();

			UpdateProblem();
		}

		void UpdateProblem()
		{
			var error = Settings.Error;
			var text = error ?? Settings.Warning;

			Problem.Visible = text is not null;
			Problem.Label.Text = text ?? "";
			Problem.BackgroundColor = error is not null ? Theme.Red : Theme.Yellow;
		}

		public override bool CanProceed() => Settings is not null && Settings.Error is null;

		public override Task<bool> FinishAsync()
		{
			Settings.Save( Project );
			EditorUtility.Projects.Updated( Project );

			return Task.FromResult( true );
		}
	}
}
