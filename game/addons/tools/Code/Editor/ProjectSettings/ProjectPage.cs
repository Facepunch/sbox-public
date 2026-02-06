namespace Editor.ProjectSettingPages;

[Title( "Configuration" )]
internal sealed class ProjectPage : ProjectSettingsWindow.Category
{
	private class SelectorProperties
	{
		public string GameSupport { get; set; }
		public string ParentGame { get; set; }
		public string TargetGame { get; set; }
	}

	private SelectorProperties Properties { get; set; }

	// These are used to screen the Package Ident before saving
	[Title( "Title" )]
	public string CurrentPackageTitle { get; set; }

	[Title( "Package Ident" )]
	public string CurrentPackageIdent { get; set; }

	[Title( "Organization Ident" ), Editor( "organization" )]
	public string CurrentPackageOrgIdent { get; set; }

	/// <summary>
	/// Have we changed the parent package? If so, on save / exit we want to restart the editor
	/// </summary>
	private bool HasChangedParentPackage { get; set; }

	[Editor( "package:game" ), Title( "Supported Games" )]
	public string GameSupport
	{
		get => Properties.GameSupport;
		set
		{
			Properties.GameSupport = value;
			Project.Config.Metadata["GameSupport"] = Properties.GameSupport
				.Replace( "#local", "" )
				.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToList();

			StateHasChanged();
		}
	}

	[Editor( "package:game" ), Title( "Parent Game" )]
	public string ParentGame
	{
		get => Properties.ParentGame;
		set
		{
			Properties.ParentGame = value;
			HasChangedParentPackage = true;
			Project.Config.Metadata["ParentPackage"] = Properties.ParentGame;

			StateHasChanged();
		}
	}

	[Editor( "package:game" ), Title( "Target Game" )]
	public string TargetGame
	{
		get => Properties.TargetGame;
		set
		{
			Properties.TargetGame = value;
			HasChangedParentPackage = true;
			// Further down references will always favour the local version
			Project.Config.Metadata["ParentPackage"] = Properties.TargetGame.Replace( "#local", "" );

			StateHasChanged();
		}
	}

	public override void OnInit( Project project )
	{
		base.OnInit( project );

		CurrentPackageTitle = project.Config.Title;
		CurrentPackageIdent = project.Config.Ident;
		CurrentPackageOrgIdent = project.Config.Org;
		Properties = new();

		var cs = new ControlSheet();

		{
			{

				var thisSerialized = this.GetSerialized();
				cs.AddRow( thisSerialized.GetProperty( nameof( CurrentPackageTitle ) ) );
				cs.AddRow( thisSerialized.GetProperty( nameof( CurrentPackageIdent ) ) );
				cs.AddRow( thisSerialized.GetProperty( nameof( CurrentPackageOrgIdent ) ) );

				ListenForChanges( thisSerialized );
			}
		}

		//
		// For targetting specific games or maps.
		//
		if ( project.Config.Type == "map" )
		{
			if ( project.Config.TryGetMeta<List<string>>( "GameSupport", out var value ) )
			{
				Properties.GameSupport = string.Join( ';', value );
			}

			var section = StartSection( "Target Information" );

			cs.AddProperty( this, x => x.GameSupport );
		}

		//
		// Forking hell
		//
		if ( project.Config.Type == "game" )
		{
			if ( project.Config.TryGetMeta<string>( "ParentPackage", out var value ) )
			{
				Properties.ParentGame = value;
			}

			cs.AddProperty( this, x => x.ParentGame );
		}

		BodyLayout.Add( cs );

		//
		// Game targeting extension
		//
		if ( project.Config.Type == "addon" )
		{
			if ( project.Config.TryGetMeta<string>( "ParentPackage", out var value ) )
			{
				Properties.TargetGame = value;
			}

			BodyLayout.Add( new InformationBox( "Are you targeting a specific game? If so you can select a target game or put the ident of the target game here." ) );

			var sheet = new ControlSheet();
			sheet.AddProperty( this, x => x.TargetGame );
			BodyLayout.Add( sheet );
		}
	}

	public override void OnSave()
	{
		var type = Project.Config.Type;
		if ( (type == "game" || type == "library") && CurrentPackageIdent != Project.Config.Ident )
		{
			bool sharesIdent = false;
			// Make sure Libraries don't have the same ident as the game
			if ( type == "library" ) sharesIdent = CurrentPackageIdent.ToLower() == Project.Current.Package.Ident.ToLower();
			// Make sure Game/Libraries don't have the same ident as another library
			if ( !sharesIdent ) sharesIdent = LibrarySystem.All.Any( x => x.Project.Package.Ident.ToLower() == CurrentPackageIdent.ToLower() );
			if ( sharesIdent )
			{
				Dialog.AskConfirm( () =>
				{
					CurrentPackageIdent = Project.Config.Ident;
					base.OnSave();
				},
				$"This Package cannot share it's Ident with another Package. Would you like to continue with the ident \"{Project.Config.Ident}\"?", "Error changing Package Ident", "OK", "Cancel" );
				return;
			}
		}

		Project.Config.Title = CurrentPackageTitle;
		Project.Config.Ident = CurrentPackageIdent;
		Project.Config.Org = CurrentPackageOrgIdent;
		base.OnSave();

		if ( HasChangedParentPackage )
		{
			EditorUtility.RestartEditorPrompt( "You need to restart the editor after changing the parent package." );
		}
	}
}
