namespace Editor;

public partial class SceneViewWidget : ResourceLibrary.IEventListener
{
	private PopupDialogWidget _externalChangesDialog;

	void ResourceLibrary.IEventListener.OnExternalChanges( GameResource resource )
	{
		if ( resource != Session.Scene.Source ) return;

		if ( _externalChangesDialog.IsValid() ) // already showing one
			return;

		switch ( EditorPreferences.ExternalSceneChange )
		{
			case EditorPreferences.ExternalSceneChangeAction.AlwaysReload:
				Session.Reload();
				return;

			case EditorPreferences.ExternalSceneChangeAction.ReloadIfSaved:
				if ( !Session.HasUnsavedChanges )
				{
					Session.Reload();
					return;
				}
				break; // unsaved work -> fall through and prompt

			case EditorPreferences.ExternalSceneChangeAction.Ask:
			default:
				break; // show prompt as before
		}

		Session.MakeActive();

		var popup = new PopupDialogWidget( "published_with_changes" );
		_externalChangesDialog = popup;

		popup.FixedWidth = 650;
		popup.WindowTitle = "External Changes Detected";

		string filename = System.IO.Path.GetFileName( Session.Scene.Source.ResourcePath );
		popup.MessageLabel.Text = $"The asset '{filename}' has been modified outside of the s&box editor.\n\nPress Reload to discard any unsaved changes and load the new version.\nPress Keep My Work to overwrite the external changes with your working copy.";

		popup.ButtonLayout.Spacing = 4;
		popup.ButtonLayout.AddStretchCell();

		popup.ButtonLayout.Add( new Button.Primary( "Reload", "sync" )
		{
			Clicked = () =>
			{
				Session.Reload();
				popup.Destroy();
			}
		} );

		popup.ButtonLayout.Add( new Button( "Keep My Work", "save" )
		{
			Clicked = () =>
			{
				// Save what we have already, discarding any external changes.
				Session.Save( false );
				popup.Destroy();
			}
		} );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}
}
