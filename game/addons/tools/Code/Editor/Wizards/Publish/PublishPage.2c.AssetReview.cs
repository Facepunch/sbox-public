using static Editor.ProjectPublisher.PackageManifest;

namespace Editor.Wizards;

partial class PublishWizard
{
	class AssetReviewWizardPage : PublishWizardPage
	{
		public override string PageTitle => "Review Published Content";
		public override string PageSubtitle => "Choose what ships with your game. Publishing settings are saved to your project.";

		PublishSettingsWidget settings;

		public override async Task OpenAsync()
		{
			Visible = true;
			BodyLayout.Clear( true );
			PublishConfig.Publisher = null;
			settings = BodyLayout.Add( new PublishSettingsWidget( this, Project,
				"Review missing runtime content before continuing. Changes are saved to your project.", SaveSettings ), 1 );
			settings.Changed = () =>
			{
				// Save mode changes immediately; batch rule edits with the debounced preview.
				if ( Project.Config.PublishUsedAssetsOnly != settings.Options.PublishUsedAssetsOnly ) SaveSettings( settings.Options );
			};
			if ( settings.Options.PublishUsedAssetsOnly ) await settings.Review.RefreshAsync();
		}

		void SaveSettings( PublishOptions options )
		{
			if ( Project.Config.PublishUsedAssetsOnly == options.PublishUsedAssetsOnly && Project.Config.Resources == options.Resources ) return;
			Project.Config.PublishUsedAssetsOnly = options.PublishUsedAssetsOnly;
			ProjectPublisher.PackageManifest.SetResourceRules( Project, options.Resources );
		}

		public override void Rebuild() => settings?.Review.Rebuild();

		public override bool CanProceed() => settings is not null && (!settings.Options.PublishUsedAssetsOnly
			|| settings.Review is { IsCurrent: true } && settings.Review.Manifest.Errors.Count == 0);

		public override Task<bool> FinishAsync()
		{
			SaveSettings( settings.Options );
			// Upload builds the full manifest and hashes once, after review.
			PublishConfig.Publisher = null;
			return Task.FromResult( CanProceed() );
		}
	}
}
