using System.IO;
using System.Text.Json;

namespace Editor.AnimGraph;

public partial class Window : AssetSystem.IEventListener
{
	private PopupDialogWidget _externalChangesDialog;

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset != _asset || _saving || !asset.HasSourceFile || _externalChangesDialog.IsValid() )
			return;

		try
		{
			if ( !_session.HasExternalChanges() )
				return;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( e, $"AnimGraph: could not check external changes to {asset.Path}" );
			return;
		}

		var popup = new PopupDialogWidget( "published_with_changes" )
		{
			WindowTitle = "External Changes Detected",
			DeleteOnClose = true
		};
		_externalChangesDialog = popup;

		popup.MessageLabel.Text = $"The asset '{asset.Name}' has been modified outside of the s&box editor.\n\nPress Reload to discard any unsaved changes and load the new version.\nPress Keep My Work to overwrite the external changes with your working copy.";
		popup.ButtonLayout.Spacing = 4;
		popup.ButtonLayout.AddStretchCell();

		popup.ButtonLayout.Add( new Button.Primary( "Reload", "sync" )
		{
			Clicked = () =>
			{
				Session session;
				try
				{
					session = Session.Load( asset );
				}
				catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException )
				{
					Log.Warning( e, $"AnimGraph: failed to reload {asset.Path}" );
					popup.MessageLabel.Text = $"Could not reload '{asset.Name}'. Your current graph has been kept.\n\n{e.Message}";
					return;
				}

				OpenSession( session );
			}
		} );

		popup.ButtonLayout.Add( new Button( "Keep My Work", "save" )
		{
			Clicked = () => Save( _session.SourceFile )
		} );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}
}
