using System.IO;
using System.Threading;
using System;
using Editor;

namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Look for files, upload missing
	/// </summary>
	class SuccessWizardPage : PublishWizardPage
	{
		public override string PageTitle => "Package Publish Successful";
		public override string PageSubtitle => "We did it!";


		public string ChangeTitle { get; set; }
		public string ChangeDetail { get; set; }

		public override async Task OpenAsync()
		{
			// Play a sound for people to moan about
			EditorUtility.PlayRawSound( "sounds/editor/published.wav" );

			// Clear the cache so if we try to use this addon it'll download the latest version
			EditorUtility.ClearPackageCache();

			Rebuild();
			Visible = true;
			await Task.CompletedTask;
		}

		public override void Rebuild()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = new Sandbox.UI.Margin( 200, 0 );

			BodyLayout.AddStretchCell( 1 );

			BodyLayout.Add( new Label( "😃" ) { Alignment = TextFlag.Center } ).SetStyles( "font-size: 100px" );
			BodyLayout.Add( new Label.Subtitle( "Package Published!" ) { Alignment = TextFlag.Center } );
			BodyLayout.AddSpacingCell( 16 );
			BodyLayout.Add( new Label.Body( "Congratulations. Your package has been published.\n\n If you opted to make your package public, then other people will now be able to acquire this updated version. If not, then it will only be accessible to people within your organisation." ) { Alignment = TextFlag.Center } );
			BodyLayout.AddSpacingCell( 16 );

			BodyLayout.Add( new Label.Body( "You can edit more package information and upload screenshots on its webpage:" ) { Alignment = TextFlag.Center } );

			var r = BodyLayout.AddRow();

			r.AddStretchCell();
			r.Add( new Button.Primary( "View and Edit on Web", "open_in_browser" )
			{
				Clicked = () => EditorUtility.OpenFolder( Project.EditUrl )
			} );
			r.AddStretchCell();


			BodyLayout.AddStretchCell( 1 );
		}

		public override bool CanProceed()
		{
			return true;
		}
	}
}

