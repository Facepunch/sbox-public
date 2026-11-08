namespace Editor.Wizards;

partial class PublishWizard : BaseWizard
{
	public Project Project;
	PublishConfig Config = new();

	ResourcePublishContext context;

	public override string Title => "Publisher";
	public override string Icon => "upload_file";

	private PublishWizard( Project project, ResourcePublishContext publishContext )
	{
		Project = project;
		context = publishContext;

		BuildHeaderRight( HeaderRight );
		AddSteps();

		// Compiles while the first page is filled in - the badge in the header says how it's going
		_ = CompileAsync();
	}

	protected void AddSteps()
	{
		AddStep( new ReviewWizardPage() // this the right ident etc
		{
			Project = Project,
			PublishConfig = Config,
			CanUploadSourceFiles = context?.CanIncludeSourceFiles ?? true
		} );

		var projectType = Project.Config.Type;

		if ( projectType == "game" )
		{
			AddStep( new AssetReviewWizardPage() { Project = Project, PublishConfig = Config } );
		}

		// Maps and games go to the workshop - the backend only gets the thumb and code, and no video
		if ( ProjectPublisher.PublishesToWorkshop( Project ) )
		{
			AddStep( new WorkshopUploadPage() { Project = Project, PublishConfig = Config } );    // files to the item picked on the first page, then make live
		}
		else
		{
			AddStep( new UploadWizardPage() { Project = Project, PublishConfig = Config } );       // upload files
			AddStep( new UploadMediaPage() { Project = Project, PublishConfig = Config } );        // upload files
			AddStep( new FinalizeWizardPage() { Project = Project, PublishConfig = Config } );     // make live
		}

		AddStep( new SuccessWizardPage() { Project = Project, PublishConfig = Config } );     // make live

		Current = Steps.First();
	}

	/// <summary>
	/// What a revision's called if nobody says otherwise
	/// </summary>
	static string DefaultChangeTitle() => $"Changes on {DateTime.UtcNow:yyyy-MM-dd}";

	public override void OnSave()
	{
		EditorUtility.Projects.Updated( Project );
	}

	[Event( "compile.complete" )]
	void OnCompileComplete( CompileGroup group )
	{
		if ( !IsValid || !group.BuildResult.Success || compileState == CompileState.Compiling ) return;

		// Not while it's uploading - the upload has the code it started with
		if ( Current?.FinishTask is { IsCompleted: false } ) return;

		// It failed and they've fixed it - or code that's in this publish changed. Editor-only recompiles don't matter.
		var publishedChanged = Config.CompilerOutput is not null && group.BuildResult.Output.Any( output =>
			Config.CompilerOutput.Any( published => published.Compiler.AssemblyName == output.Compiler.AssemblyName ) );

		if ( compileState == CompileState.Failed || publishedChanged )
			_ = CompileAsync();
	}

	public static PublishWizard Open( Project project, ResourcePublishContext publishContext = default )
	{
		var w = new PublishWizard( project, publishContext );
		w.CreateWindow( 1235, 800 );
		return w;
	}
}

