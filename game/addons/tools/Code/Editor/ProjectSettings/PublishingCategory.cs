namespace Editor.ProjectSettingPages;

[Title( "Publishing" ), Icon( "cloud_upload" )]
internal sealed class PublishingCategory : ProjectSettingsWindow.Category
{
	PublishSettingsWidget settings;
	internal override bool ShowTitle => false;

	public override void OnInit( Project project )
	{
		base.OnInit( project );
		settings = BodyLayout.Add( new PublishSettingsWidget( this, project,
			"Preview includes unsaved changes. Save keeps your settings; Revert Changes discards them." ) );
		settings.Changed = () => StateHasChanged();
		if ( settings.Options.PublishUsedAssetsOnly ) _ = settings.Review.RefreshAsync();
	}

	public override void OnSave()
	{
		Project.Config.PublishUsedAssetsOnly = settings.Options.PublishUsedAssetsOnly;
		Project.Config.Resources = settings.Options.Resources;
		base.OnSave();
	}
}
