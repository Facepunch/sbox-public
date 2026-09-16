namespace Sandbox.PanelGallery;

/// <summary>
/// The engine button in the editor stylesheet's classes.
/// </summary>
public class ButtonsPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;
	int _clicks;

	public ButtonsPage() : base( "Buttons", "Sandbox.UI.Button in the editor stylesheet classes. Every one should react to hover and report its clicks." )
	{
		var states = Case( "Button states" );
		foreach ( var state in new[] { "Normal", "Hovered", "Focused", "Disabled" } )
		{
			var specimen = states.Add.Panel( "control-state" );
			specimen.Add.Label( state, "reference-title" );
			var button = new Sandbox.UI.Button( "Save", "save", "primarybutton", Clicked );
			specimen.AddChild( button );
			if ( state == "Hovered" ) button.Switch( PseudoClass.Hover, true );
			if ( state == "Focused" ) button.Switch( PseudoClass.Focus, true );
			if ( state == "Disabled" ) button.Disabled = true;
		}
		var row = Case( "Primary" );
		row.AddChild( new Sandbox.UI.Button( "Create", "add_box", "primarybutton", Clicked ) );
		row.AddChild( new Sandbox.UI.Button( "Save", null, "primarybutton", Clicked ) );

		row = Case( "Flat" );
		row.AddChild( new Sandbox.UI.Button( "Back", null, "flatbutton", Clicked ) );
		row.AddChild( new Sandbox.UI.Button( "Browse", "folder_open", "flatbutton", Clicked ) );

		row = Case( "Icon only" );
		row.AddChild( new Sandbox.UI.Button( null, "settings", "iconbutton", Clicked ) );
		row.AddChild( new Sandbox.UI.Button( null, "calendar_month", "iconbutton", Clicked ) );
		row.AddChild( new Sandbox.UI.Button( null, "delete", "iconbutton", Clicked ) );

		_output = Output();
	}

	void Clicked()
	{
		_output.Text = $"clicked {++_clicks}";
	}
}
