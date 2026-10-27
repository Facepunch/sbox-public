namespace Editor.Wizards;

using Editor.ProjectSettingPages;
using Sandbox.Services;

partial class PublishWizard
{
	/// <summary>
	/// Step 1 - is the org and ident all correct? Does the package already exist? Does it match this package type?
	/// For a game, how it's played. For a game or map, the Steam Workshop item it goes to - everything that's picked
	/// is picked here, the rest of the steps check and upload.
	/// </summary>
	class ReviewWizardPage : PublishWizardPage
	{
		/// <summary>
		/// A header across the top saying packages are moving to the Steam Workshop - click anywhere on it to read why
		/// </summary>
		class MigrationBanner : Widget
		{
			public MigrationBanner( Widget parent ) : base( parent )
			{
				Cursor = CursorShape.Finger;
				ToolTip = MigrationUrl;

				Layout = Layout.Row();
				Layout.Margin = new Sandbox.UI.Margin( 16, 10 );
				Layout.Spacing = 8;

				Layout.Add( new Label( "We're moving to the Steam Workshop" ) { Color = Color.White } ).SetStyles( "font-size: 14px; font-weight: 600;" );
				Layout.AddStretchCell();
				var icon = Layout.Add( new Widget( this ) { FixedSize = 16 } );
				icon.OnPaintOverride = () =>
				{
					Paint.SetPen( Color.White );
					Paint.DrawIcon( icon.LocalRect, "open_in_new", 14 );
					return true;
				};
			}

			protected override void OnMouseClick( MouseEvent e )
			{
				System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo( MigrationUrl ) { UseShellExecute = true } );
			}

			protected override void OnPaint()
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Green.Darken( Paint.HasMouseOver ? 0.45f : 0.55f ) );
				Paint.DrawRect( LocalRect, 4 );
			}
		}

		public bool CanUploadSourceFiles { get; set; } = true;

		public override string PageTitle => "Publishing";
		public override string PageSubtitle => $"Check everything below, then publish your {TypeName}.";

		string WarningText => "You cannot proceed until the following issues are resolved:";

		PropertyRowError ArchivedWarning { get; set; }
		PropertyRowError WrongTypeWarning { get; set; }
		WarningBox UploadWarning { get; set; }

		/// <summary>
		/// Available licenses for this package type.
		/// </summary>
		IReadOnlyList<(string Name, string Title, string Description)> AvailableLicenses { get; set; }

		/// <summary>
		/// Currently selected license name.
		/// </summary>
		[Title( "Asset License" )]
		public string SelectedLicense { get; set; }

		/// <summary>
		/// Dropdown control widget for license selection.
		/// </summary>
		LicenseControlWidget LicenseDropdown { get; set; }

		/// <summary>
		/// Games only - how it's played, the same settings as Project Settings > Multiplayer
		/// </summary>
		PlayerSettings Players;
		WarningBox PlayersWarning;

		WorkshopChoices Workshop => PublishConfig.Workshop;

		/// <summary>
		/// The workshop section's contents - rebuilt on their own as the item's looked up, so the fields above
		/// don't lose what's being typed
		/// </summary>
		PublishSection WorkshopSection;
		Widget WorkshopContents;

		void RebuildWorkshop()
		{
			if ( !IsValid || !Visible || !WorkshopSection.IsValid() )
				return;

			BuildWorkshop();
		}

		/// <summary>
		/// The section's contents in a widget of their own, made again from scratch - clearing a layout leaves the
		/// widgets in the rows and grids inside it behind
		/// </summary>
		void BuildWorkshop()
		{
			if ( WorkshopContents.IsValid() )
			{
				WorkshopContents.Visible = false;
				WorkshopContents.Destroy();
			}

			WorkshopContents = WorkshopSection.Layout.Add( new Widget( WorkshopSection ) );
			WorkshopContents.Layout = Layout.Column();
			WorkshopContents.Layout.Spacing = 8;
			Workshop.Build( WorkshopContents.Layout, this );
		}

		public override async Task OpenAsync()
		{
			// Only initialize the selection when opening, not when rebuilding after hotload.
			if ( Project.Config.TryGetMeta<string>( "AssetLicense", out var localLicense ) )
			{
				SelectedLicense = localLicense;
			}

			if ( ProjectPublisher.PublishesToWorkshop( Project ) && PublishConfig.Workshop is null )
			{
				PublishConfig.Workshop = new WorkshopChoices( Project, PublishConfig )
				{
					Changed = RebuildWorkshop
				};
			}

			Visible = true;

			// The full fetch gives the old website values to start from if the project doesn't declare its play modes yet
			if ( Project.Config.Type == "game" && Players is null )
			{
				await Package.FetchAsync( Project.Config.FullIdent, false );
				Players = PlayerSettings.Load( Project );
			}

			Rebuild();
			GetPackage();

			if ( Workshop is not null )
				await Workshop.LoadAsync();
		}

		/// <summary>
		/// Why packages are going - linked from the banner at the top
		/// </summary>
		const string MigrationUrl = "https://sbox.game/news/rethinking";

		public override void Rebuild()
		{
			BodyLayout?.Clear( true );
			// The wizard's own 32 is the edge - lined up with the title, the compile badge and the footer
			BodyLayout.Margin = 0;
			BodyLayout.Spacing = 16;

			//
			// Todo look at account information and see if this guy hasn't made an organisation yet?
			//

			if ( Workshop is not null )
				BodyLayout.Add( new MigrationBanner( this ) );

			// The package and how it's played side by side, the workshop item under them
			var top = BodyLayout.AddRow();
			top.Spacing = 16;

			var package = top.Add( new PublishSection( this, "Package", Theme.Text, "inventory_2",
				"Your org and ident make its permanent ident, orgname.packageident." ), 1 );
			{

				var so = Project.Config.GetSerialized();
				var cs = new ControlSheet();

				cs.AddRow( so.GetProperty( nameof( Project.Config.Title ) ) );
				cs.AddRow( so.GetProperty( nameof( Project.Config.Ident ) ) );

				ArchivedWarning = cs.Add( new PropertyRowError( "This package already exists and is currently marked as archived. You can't currently publish to an archived package." ) );
				ArchivedWarning.Visible = false;

				WrongTypeWarning = cs.Add( new PropertyRowError( "This package is a different type to the one you're trying to upload." ) );
				WrongTypeWarning.Visible = false;

				cs.AddRow( so.GetProperty( nameof( Project.Config.Org ) ) );

				if ( !Project.IsSourcePublish() && CanUploadSourceFiles )
				{
					// tony: Disabled this until we implement it in a better way
					// cs.AddRow( so.GetProperty( nameof( Project.Config.IncludeSourceFiles ) ) );
				}
				else
				{
					Project.Config.IncludeSourceFiles = false;
				}

				// Show license dropdown if this package type supports it
				var packageType = PackageType.Get( Project.Config.Type );
				if ( packageType is { HasAssetLicenses: true } )
				{
					var licenseOptions = packageType.GetAssetLicenseOptions();
					AvailableLicenses = licenseOptions;

					var pageSo = this.GetSerialized();
					LicenseDropdown = cs.AddControl<LicenseControlWidget>( pageSo.GetProperty( nameof( SelectedLicense ) ) );
					LicenseDropdown.SetLicenseOptions( licenseOptions );
				}

				package.Layout.Add( cs );
			}

			package.Layout.AddStretchCell();

			if ( Players is not null )
			{
				var players = top.Add( new PublishSection( this, "Players", Theme.Text, "groups", PlayerSettings.About ), 1 );
				Players.BuildFields( players.Layout, so => so.OnPropertyChanged = _ => UpdateWarnings() );

				PlayersWarning = new WarningBox( "", this );
				players.Layout.Add( PlayersWarning );
				players.Layout.AddStretchCell();
			}

			if ( Workshop is not null )
			{
				WorkshopSection = BodyLayout.Add( new PublishSection( this, "Steam Workshop", Theme.Text, "cloud_upload",
					"What players download - its files, thumbnail, visibility and tags." ) );
				WorkshopContents = null;
				BuildWorkshop();
			}

			UploadWarning = new WarningBox( WarningText, this );
			UploadWarning.BackgroundColor = Theme.Red;
			UploadWarning.Visible = false;
			BodyLayout.Add( UploadWarning );

			UpdateWarnings();
		}

		public override void ChildValuesChanged( Widget source )
		{
			GetPackage();

			// Another ident has another item
			if ( Workshop is { Loaded: true } && Workshop.LoadedIdent != Project.Config.FullIdent )
				_ = Workshop.LoadAsync();
		}

		Package Package;
		Task PackageTask;
		bool LicenseLoaded;

		void GetPackage()
		{
			PackageTask = UpdatePackage();
		}

		async Task UpdatePackage()
		{
			// complete in order
			if ( PackageTask != null )
				await PackageTask;

			Package = await Package.FetchAsync( Project.Config.FullIdent, true, useCache: false );

			if ( !IsValid )
				return;

			// Load license from remote only on first fetch, and only if local metadata was unset
			if ( Package is not null && !LicenseLoaded )
			{
				if ( string.IsNullOrEmpty( SelectedLicense ) && !string.IsNullOrEmpty( Package.AssetLicense ) )
				{
					SelectedLicense = Package.AssetLicense;
					LicenseDropdown?.Update();
				}
				LicenseLoaded = true;
			}

			UpdateWarnings();
		}

		void UpdateWarnings()
		{
			ArchivedWarning.Visible = Package?.Archived ?? false;
			WrongTypeWarning.Visible = Package != null && Package.TypeName != Project.Config.Type;

			UploadWarning.Label.Text = WarningText;
			UploadWarning.Visible = false;

			if ( PlayersWarning is { IsValid: true } )
			{
				PlayersWarning.Visible = Players.Warning is not null;
				PlayersWarning.Label.Text = Players.Warning ?? "";
			}

			if ( Players?.Error is { } playersError )
			{
				UploadWarning.Visible = true;
				UploadWarning.Label.Text += $"\n• {playersError}";
			}

			if ( Project.Config.Type == "game" && string.IsNullOrEmpty( Project.Config.GetMetaOrDefault( "StartupScene", "" ) ) )
			{
				UploadWarning.Visible = true;
				UploadWarning.Label.Text += "\n• Startup scene is not set.";

			}

			if ( Project.Config.Org == "local" )
			{
				UploadWarning.Visible = true;
				UploadWarning.Label.Text += "\n• You must specify an organisation to publish under.";
			}

			if ( Project.Config.IsStandaloneOnly )
			{
				UploadWarning.Visible = true;
				UploadWarning.Label.Text += $"\n• \"Disable Whitelist\" is on (Project Settings > Game Exporting). Projects with the whitelist disabled can't be published to {Global.BackendTitle}.";
			}

			if ( !EditorTypeLibrary.CheckValidationAttributes( Project.Config, out var errors ) )
			{
				UploadWarning.Visible = true;
				foreach ( var error in errors )
				{
					UploadWarning.Label.Text += $"\n• {error}";
				}
			}
		}

		public override bool CanProceed()
		{
			if ( UploadWarning.Visible ) return false;
			if ( (PackageTask?.IsCompleted ?? true) == false ) return false;
			if ( Workshop?.Busy ?? false ) return false;
			if ( Players?.Error is not null ) return false;
			if ( Package != null && Package.TypeName != Project.Config.Type ) return false;

			if ( Package != null && Package.Archived ) return false;
			if ( Project.Config.Ident == null ) return false;
			if ( Project.Config.Org == "local" ) return false;
			if ( Project.Config.IsStandaloneOnly ) return false;
			if ( !EditorTypeLibrary.CheckValidationAttributes( Project.Config ) ) return false;

			return true;
		}

		public override void OnSave()
		{
			_ = UpdatePackage();
		}

		public override async Task<bool> FinishAsync()
		{
			Players?.Save( Project );

			// Persist locally even if the package doesn't exist yet.
			if ( AvailableLicenses is not null )
				Project.Config.SetMeta( "AssetLicense", SelectedLicense ?? "" );

			// The publisher reads the tags from the project when it uploads
			if ( Workshop is not null )
				await Workshop.SaveTags();
			else if ( Players is not null )
				await EditorUtility.Projects.Updated( Project );

			if ( AvailableLicenses is null )
				return true;

			// Update the backend if this is an existing remote package, but don't block navigation.
			if ( Package is not null )
				_ = Package.UpdateValue( "assetLicense", SelectedLicense ?? "" );

			return true;
		}
	}
}

