using Sandbox.UI;
using Sandbox.UI.Construct;
using Label = Sandbox.UI.Label;
using StatusBar = Sandbox.UI.StatusBar;

namespace Sandbox.PanelGallery;

/// <summary>
/// Normal and permanent status widgets with replaceable timed messages.
/// </summary>
public class StatusBarPage : GalleryPage
{
	public StatusBarPage() : base( "Status Bar", "Temporary messages replace the left widgets. Permanent information on the right stays visible." )
	{
		var row = Case( "Editor status · messages and permanent widgets", column: true );
		var status = row.AddChild( new StatusBar() );
		status.Style.Width = Length.Percent( 100 );
		status.AddLeft( new IconPanel( "check_circle" ) );
		status.AddLeft( new Label( "Ready" ) );
		status.AddSeparator();
		status.AddLeft( new Label( "3 objects selected" ) );
		status.AddRight( new Label( "Ln 12, Col 8" ) );
		status.AddSeparator( right: true );
		status.AddRight( new Label( "UTF-8" ) );
		var actions = row.AddChild( new Toolbar() );
		actions.AddButton( "Saved · 3 seconds", "save", () => status.ShowMessage( "Scene saved successfully", 3 ) );
		actions.AddButton( "Persistent message", "info", () => status.ShowMessage( "Waiting for connection…", 0 ) );
		actions.AddButton( "Replace · 5 seconds", "sync", () => status.ShowMessage( "Connection restored", 5 ) );
		actions.AddButton( "Clear", "close", status.ClearMessage );

		row = Case( "Interactive widgets", column: true );
		var interactive = row.AddChild( new StatusBar() );
		interactive.Style.Width = Length.Percent( 100 );
		interactive.AddLeft( new Label( "All changes saved" ) );
		var button = interactive.AddRight( new Sandbox.UI.Button( "Check for updates", "refresh" ) );
		button.AddEventListener( "onclick", () => interactive.ShowMessage( "You are up to date", 3 ) );

		row = Case( "Limited width · permanent status remains visible", column: true );
		var narrow = row.AddChild( new StatusBar() );
		narrow.Style.Width = 360;
		narrow.AddRight( new Label( "Online" ) );
		narrow.ShowMessage( "A long status message is clipped to the available space; hover to read it in full.", 0 );
		var output = Output();
		output.Text = "Show a timed message to see the normal widgets return after it expires.";
		status.MessageChanged += message => output.Text = message.Length == 0 ? "Message cleared — normal widgets restored." : $"Message: {message}";
	}
}
