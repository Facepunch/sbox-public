namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Look for files, upload missing
	/// </summary>
	class FinalizeWizardPage : PublishWizardPage
	{
		public override string PageTitle => "Revision Details";
		public override string PageSubtitle => "You can put some information about what changed here and then we'll publish a new version";

		ProjectPublisher Publisher => PublishConfig.Publisher;

		public string ChangeTitle { get; set; }

		[Title( "Change Detail (optional)" ), TextArea]
		public string ChangeDetail { get; set; }

		public override async Task OpenAsync()
		{
			ChangeTitle ??= DefaultChangeTitle();
			Rebuild();
			Visible = true;
			await Task.CompletedTask;
		}

		public override void Rebuild()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = new Sandbox.UI.Margin( 64, 0 );

			//
			// Todo look at account information and see if this guy hasn't made an organisation yet?
			//
			BodyLayout.AddSpacingCell( 16 );
			BodyLayout.AddStretchCell();

			{
				var sheet = new ControlSheet();
				sheet.AddProperty( this, x => ChangeTitle );
				sheet.AddProperty( this, x => ChangeDetail );

				BodyLayout.Add( sheet );

				// todo - option to make this version the current version immediatly

			}

			BodyLayout.AddStretchCell();
		}

		public override async Task<bool> FinishAsync()
		{
			Publisher.SetChangeDetails( ChangeTitle, ChangeDetail );

			try
			{
				await Publisher.Publish();
			}
			catch ( System.Exception e )
			{
				// Stay here, so they can fix it and try again
				Log.Error( e );
				EditorUtility.DisplayDialog( "Publish Failed", e.Message );
				return false;
			}

			return true;
		}

		public override string NextButtonText => "Publish New Revision";

		public override bool CanProceed()
		{
			if ( Publisher is null )
				return false;

			return true;
		}
	}
}

