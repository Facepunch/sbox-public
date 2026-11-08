namespace Editor;

public partial class SceneViewWidget : ResourceLibrary.IEventListener
{
	private PopupDialogWidget _externalChangesDialog;

	void ResourceLibrary.IEventListener.OnExternalChanges( GameResource resource )
	{
		// Scenes are edited from their own copy of the source, so they're matched to their session by path
		if ( resource is SceneFile sceneFile && SceneEditorSession.Resolve( sceneFile ) != Session ) return;
		if ( resource is not SceneFile && resource != Session.Scene.Source ) return;

		if ( _externalChangesDialog.IsValid() ) // already showing one
			return;

		// Session becomes the game's if play starts, keep this one for the buttons
		var session = Session;
		session.MakeActive();

		var popup = new PopupDialogWidget( "published_with_changes" );
		_externalChangesDialog = popup;

		popup.FixedWidth = 650;
		popup.WindowTitle = "External Changes Detected";

		string filename = System.IO.Path.GetFileName( session.Scene.Source.ResourcePath );
		popup.MessageLabel.Text = $"The asset '{filename}' has been modified outside of the s&box editor.\n\nPress Reload to discard any unsaved changes and load the new version.\nPress Keep My Work to overwrite the external changes with your working copy.";

		popup.ButtonLayout.Spacing = 4;
		popup.ButtonLayout.AddStretchCell();

		popup.ButtonLayout.Add( new Button.Primary( "Reload", "sync" )
		{
			Clicked = () =>
			{
				session.Reload();
				popup.Destroy();
			}
		} );

		popup.ButtonLayout.Add( new Button( "Keep My Work", "save" )
		{
			Clicked = () =>
			{
				// Save what we have already, discarding any external changes.
				// Saving skips scenes without unsaved changes, but this one differs from the file now.
				session.HasUnsavedChanges = true;
				session.Save( false );
				popup.Destroy();
			}
		} );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}
}
