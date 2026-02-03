namespace Editor.ProjectSettingPages;

[Title( "Packages" ), Icon( "inventory" )]
internal sealed class ReferencesCategory : ProjectSettingsWindow.Category
{
	public List<string> PackageReferences { get; set; } = new List<string>();

	public override void OnInit( Project project )
	{
		base.OnInit( project );

		project.Config.PackageReferences ??= new();

		PackageReferences.Clear();
		PackageReferences.AddRange( project.Config.PackageReferences );

		var warning = new WarningBox( "This stuff hasn't been properly end to end tested - please don't expect it to work just yet!", this );
		BodyLayout.Add( warning );

		{
			var thisSerialized = this.GetSerialized();

			BodyLayout.AddSpacingCell( 8 );
			BodyLayout.Add( new Label.Body( "Your project can reference other packages. These will be downloaded when your package is downloaded and your package will be able to use code and resources " ) );

			var sheet = new ControlSheet();
			sheet.AddRow( thisSerialized.GetProperty( nameof( PackageReferences ) ) );

			ListenForChanges( thisSerialized );
			BodyLayout.Add( sheet );
		}
	}

	public override void OnSave()
	{
		Project.Config.PackageReferences = [.. PackageReferences];
		base.OnSave();
	}
}

