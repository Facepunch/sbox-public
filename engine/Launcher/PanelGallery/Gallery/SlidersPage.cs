namespace Sandbox.PanelGallery;

/// <summary>
/// Sliders - click jumps, dragging follows. The thumb staying under the cursor is the test:
/// it reads the surface's mouse, not the game window's.
/// </summary>
public class SlidersPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;

	public SlidersPage() : base( "Sliders", "SliderControl. Click to jump, drag to follow - the thumb should stay exactly under the cursor." )
	{
		var row = Case( "Whole numbers, 0 to 100" );
		var whole = new Sandbox.UI.SliderControl( 0, 100, 1 ) { Value = 30 };
		whole.OnValueChanged = x => _output.Text = $"{x:0}";
		row.AddChild( whole );

		row = Case( "Hundredths, 0 to 1" );
		var fine = new Sandbox.UI.SliderControl( 0, 1, 0.01f ) { Value = 0.5f };
		fine.OnValueChanged = x => _output.Text = $"{x:0.00}";
		row.AddChild( fine );

		_output = Output();
	}
}
