namespace Editor.ProjectSettingPages;

[Title( "Resource Files" ), Icon( "dataset" )]
internal sealed class ResourcesCategory : ProjectSettingsWindow.Category
{
	WildcardPathWidget pathWidget;

	public override void OnInit( Project project )
	{
		base.OnInit( project );

		BodyLayout.Add( new Label.Body( "By default we'll upload compiled asset files and stylesheets with your asset. You can use the controls below to define extra paths that need to be uploaded. You can use wildcards." ) );

		var column = BodyLayout.AddColumn( 1 );
		pathWidget = new WildcardPathWidget( this, showListView: false );
		pathWidget.HideAssets = true;
		pathWidget.Value = project.Config.Resources;
		pathWidget.Directory = project.Config.AssetsDirectory;
		pathWidget.ValueChanged = () => StateHasChanged();

		column.Add( pathWidget );
	}

	public override void OnSave()
	{
		Project.Config.Resources = pathWidget.Value;

		base.OnSave();
	}
}
