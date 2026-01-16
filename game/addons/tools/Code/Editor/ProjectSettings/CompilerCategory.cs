namespace Editor.ProjectSettingPages;

[Title( "Compiler Setup" )]
internal sealed class CompilerCategory : ProjectInspector.Category
{
	Compiler.Configuration configuration;

	public string DefineConstants
	{
		get => configuration.DefineConstants;
		set => configuration.DefineConstants = value;
	}

	public bool Nullables
	{
		get => configuration.Nullables;
		set => configuration.Nullables = value;
	}

	public string RootNamespace
	{
		get => configuration.RootNamespace;
		set => configuration.RootNamespace = value;
	}

	public string NoWarn
	{
		get => configuration.NoWarn;
		set => configuration.NoWarn = value;
	}

	public string WarningsAsErrors
	{
		get => configuration.WarningsAsErrors;
		set => configuration.WarningsAsErrors = value;
	}

	public bool Whitelist
	{
		get => configuration.Whitelist;
		set => configuration.Whitelist = value;
	}

	public bool Unsafe
	{
		get => configuration.Unsafe;
		set => configuration.Unsafe = value;
	}

	public CompilerReleaseMode ReleaseMode
	{
		get => (CompilerReleaseMode)configuration.ReleaseMode;
		set => configuration.ReleaseMode = value == CompilerReleaseMode.Debug ? Compiler.ReleaseMode.Debug : Compiler.ReleaseMode.Release;
	}

	public string ProjectFilename
	{
		get => Project.Config.Metadata.TryGetValue( "CsProjName", out var value ) ? value.ToString() : default;
		set
		{
			Project.Config.Metadata["CsProjName"] = value;
		}
	}

	public enum CompilerReleaseMode
	{
		Debug,
		Release
	}

	public CompilerCategory()
	{

	}

	public override void OnInit( Project project )
	{
		if ( !project.Config.TryGetMeta( "Compiler", out configuration ) )
			configuration = new Compiler.Configuration();

		configuration.Clean();

		var so = this.GetSerialized();

		{
			var sheet = new ControlSheet();
			sheet.AddRow( so.GetProperty( nameof( DefineConstants ) ) );
			sheet.AddRow( so.GetProperty( nameof( ProjectFilename ) ) );
			sheet.AddRow( so.GetProperty( nameof( Nullables ) ) );
			sheet.AddRow( so.GetProperty( nameof( RootNamespace ) ) );
			sheet.AddRow( so.GetProperty( nameof( NoWarn ) ) );
			sheet.AddRow( so.GetProperty( nameof( WarningsAsErrors ) ) );

			if ( project.Config.IsStandaloneOnly )
			{
				sheet.AddRow( so.GetProperty( nameof( Unsafe ) ) );
				sheet.AddRow( so.GetProperty( nameof( Whitelist ) ) );
			}

			BodyLayout.Add( sheet );
		}

		BodyLayout.Add( new WarningBox( "This only matters when developing locally. Any published game will <b>always be in release mode</b>." +
			"<p>Notable changes when compiling in release mode:</p> " +
			"<ul style=\"-qt-list-indent: 0; margin-left: 10px;\" >" +
			"<li>Release Mode enables all optimizations, debugging experience might be degraded." +
			"<li>Sequence points may be optimized away. As a result it might not be possible to place or hit a breakpoint." +
			"<li>User-defined locals might be optimized away. They might not be available while debugging." +
			"<li>The `DEBUG` define constant will always be off." +
			"</ul>" ) );

		BodyLayout.AddSpacingCell( 8 );

		{
			var sheet = new ControlSheet();
			sheet.AddRow( so.GetProperty( nameof( ReleaseMode ) ) );

			BodyLayout.Add( sheet );
		}

		ListenForChanges( so );

		BodyLayout.AddStretchCell();
	}

	public override void OnSave()
	{
		Project.Config.SetMeta( "Compiler", configuration );
		EditorUtility.Projects.GenerateSolution();

		base.OnSave();
	}

}
