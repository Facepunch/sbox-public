namespace Editor.Wizards;

using Sandbox.Services;

partial class PublishWizard
{
	/// <summary>
	/// Step 1 - is the org and ident all correct? 
	/// Does the package already exist? Does it match this package type?
	/// </summary>
	class ReviewWizardPage : PublishWizardPage
	{
		public bool CanUploadSourceFiles { get; set; } = true;

		public override string PageTitle => "Publishing";
		public override string PageSubtitle => "Please follow the steps below to upload and publish your addon.";

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

		public override async Task OpenAsync()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = new Sandbox.UI.Margin( 64, 0 );
			BodyLayout.Spacing = 16;

			//
			// Todo look at account information and see if this guy hasn't made an organisation yet?
			//

			BodyLayout.AddStretchCell();

			BodyLayout.Add( new Label.Body( "Make sure you're publishing to the right organisation and the package ident is correct. Idents cannot have special characters or spaces." ) );
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

					// Load from local metadata as initial value
					if ( Project.Config.TryGetMeta<string>( "AssetLicense", out var localLicense ) )
					{
						SelectedLicense = localLicense;
					}

					var pageSo = this.GetSerialized();
					LicenseDropdown = cs.AddControl<LicenseControlWidget>( pageSo.GetProperty( nameof( SelectedLicense ) ) );
					LicenseDropdown.SetLicenseOptions( licenseOptions );
				}

				BodyLayout.Add( cs );
			}

			BodyLayout.Add( new InformationBox( "This creates your package ident which will look like \"orgname.packageident\". This is a unique persistant identifier for your package." ) );

			UploadWarning = new WarningBox( WarningText, this );
			UploadWarning.BackgroundColor = Theme.Red;
			UploadWarning.Visible = false;
			BodyLayout.Add( UploadWarning );

			BodyLayout.AddStretchCell();

			Visible = true;
			GetPackage();

			await Task.CompletedTask;
		}

		public override void ChildValuesChanged( Widget source )
		{
			GetPackage();
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

			ArchivedWarning.Visible = Package?.Archived ?? false;
			WrongTypeWarning.Visible = Package != null && Package.TypeName != Project.Config.Type;

			UploadWarning.Label.Text = WarningText;
			UploadWarning.Visible = false;

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
			if ( Package != null && Package.TypeName != Project.Config.Type ) return false;

			if ( Package != null && Package.Archived ) return false;
			if ( Project.Config.Ident == null ) return false;
			if ( Project.Config.Org == "local" ) return false;
			if ( !EditorTypeLibrary.CheckValidationAttributes( Project.Config ) ) return false;

			return true;
		}

		public override void OnSave()
		{
			_ = UpdatePackage();
		}

		public override Task<bool> FinishAsync()
		{
			// Persist locally even if the package doesn't exist yet.
			if ( AvailableLicenses is null )
				return Task.FromResult( true );

			Project.Config.SetMeta( "AssetLicense", SelectedLicense ?? "" );

			// Update the backend if this is an existing remote package, but don't block navigation.
			if ( Package is not null )
				_ = Package.UpdateValue( "assetLicense", SelectedLicense ?? "" );

			return Task.FromResult( true );
		}
	}
}

