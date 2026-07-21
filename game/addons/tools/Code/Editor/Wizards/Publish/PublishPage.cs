namespace Editor.Wizards;

partial class PublishWizard : BaseWizard
{
	public Project Project;
	PublishConfig Config = new();

	ResourcePublishContext context;

	public override string Title => $"Upload to {Global.BackendTitle}";
	public override string Icon => "upload_file";

	private PublishWizard( Project project, ResourcePublishContext publishContext )
	{
		Project = project;
		context = publishContext;

		if ( context != null )
		{
			ConfigureResource();
		}

		AddSteps();
	}

	protected void AddSteps()
	{
		AddStep( new ReviewWizardPage() // this the right ident etc
		{
			Project = Project,
			PublishConfig = Config,
			CanUploadSourceFiles = context?.CanIncludeSourceFiles ?? true
		} );

		// Show license warnings for games/maps/scenes that reference cloud assets
		var projectType = Project.Config.Type;
		if ( projectType is "game" or "map" && CloudAsset.GetAssetReferences( true ).Count > 0 )
		{
			AddStep( new LicenseCheckWizardPage() { Project = Project, PublishConfig = Config } );
		}

		if ( Project.HasCodePath() )
		{
			AddStep( new CompileWizardPage() { Project = Project, PublishConfig = Config } );  // compile everything
		}

		AddStep( new UploadWizardPage() { Project = Project, PublishConfig = Config } );       // upload files
		AddStep( new UploadMediaPage() { Project = Project, PublishConfig = Config } );           // upload files
		AddStep( new FinalizeWizardPage() { Project = Project, PublishConfig = Config } );        // make live
		AddStep( new SuccessWizardPage() { Project = Project, PublishConfig = Config } );     // make live

		Current = Steps.First();
	}

	public override void OnSave()
	{
		EditorUtility.Projects.Updated( Project );
	}

	public static PublishWizard Open( Project project, ResourcePublishContext publishContext = default )
	{
		var w = new PublishWizard( project, publishContext );
		w.CreateWindow( 800, 600 );
		return w;
	}

	/// <summary>
	/// Take ResourcePublishContext and apply any changes to Project
	/// which we will assume is a temporary project, and we're uploading
	/// an asset, rather than a game.
	/// </summary>
	void ConfigureResource()
	{
		if ( context.IncludeCode )
		{
			//
			// We don't have a better way right now. In the future
			// we'll allow them to define which code to include and whatever.
			//
			Project.RootDirectory = Project.Current.RootDirectory;
		}
		else
		{
			// Don't drag in code from whatever project happens to be open right now.
			Project.RootDirectory = null;
		}
	}
}

