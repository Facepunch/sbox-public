namespace Sandbox.PanelGallery;

/// <summary>
/// The engine checkbox - the tick should be invisible when off, white on accent when on,
/// and centered in its box either way.
/// </summary>
public class CheckboxPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;

	public CheckboxPage() : base( "Checkbox", "Sandbox.UI.Checkbox. No ghost ticks - an off checkbox shows an empty box." )
	{
		var row = Case( "States" );
		row.AddChild( new Sandbox.UI.Checkbox { LabelText = "Unchecked" } );
		row.AddChild( new Sandbox.UI.Checkbox { LabelText = "Checked", Checked = true } );

		row = Case( "Events" );
		var notify = new Sandbox.UI.Checkbox { LabelText = "Tell me about it" };
		notify.ValueChanged = x => _output.Text = $"changed: {x}";
		row.AddChild( notify );

		_output = Output();
	}
}
