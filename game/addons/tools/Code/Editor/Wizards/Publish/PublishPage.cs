namespace Editor.Wizards;

partial class PublishWizard : BaseWizard
{
	public Project Project;
	PublishConfig Config = new();

	ResourcePublishContext context;
	bool restartingCompile;

	public override string Title => $"Upload to {Global.BackendTitle}";
	public override string Icon => "upload_file";

	private PublishWizard( Project project, ResourcePublishContext publishContext )
	{
		Project = project;
		context = publishContext;

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

	[Event( "compile.complete" )]
	void OnCompileComplete( CompileGroup group )
	{
		if ( !IsValid || !group.BuildResult.Success || Config.AssemblyFiles is null ) return;

		var compileStep = Steps.FindIndex( x => x is CompileWizardPage );
		if ( compileStep < 0 || Steps.IndexOf( Current ) <= compileStep ) return;

		// Only code included in this publish matters; editor-only recompiles don't invalidate it.
		if ( Config.CompilerOutput is null || !group.BuildResult.Output.Any( output =>
			Config.CompilerOutput.Any( published => published.Compiler.AssemblyName == output.Compiler.AssemblyName ) ) )
			return;

		if ( restartingCompile ) return;
		_ = RestartCompileAsync( Steps[compileStep] );
	}

	async Task RestartCompileAsync( BaseWizardPage compilePage )
	{
		restartingCompile = true;
		try
		{
			await ReturnToPageAsync( compilePage );
		}
		finally
		{
			restartingCompile = false;
		}
	}

	public static PublishWizard Open( Project project, ResourcePublishContext publishContext = default )
	{
		var w = new PublishWizard( project, publishContext );
		w.CreateWindow( 800, 600 );
		return w;
	}
}

