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

		// A mark every ten, so the value can be read off the track
		row = Case( "Ticks every 10" );
		var ticked = new Sandbox.UI.SliderControl( 0, 100, 1 ) { Value = 40, TickStep = 10 };
		ticked.OnValueChanged = x => _output.Text = $"{x:0}";
		row.AddChild( ticked );

		// Where the fill goes - right of the thumb, out from the middle, or nowhere
		row = Case( "Fill from the right" );
		var fromRight = new Sandbox.UI.SliderControl( 0, 100, 1 ) { Value = 70, Fill = Sandbox.UI.SliderFill.Right };
		fromRight.OnValueChanged = x => _output.Text = $"{x:0}";
		row.AddChild( fromRight );

		row = Case( "Fill from the centre, -1 to 1" );
		var centred = new Sandbox.UI.SliderControl( -1, 1, 0.01f ) { Value = 0.4f, Fill = Sandbox.UI.SliderFill.Center, TickStep = 0.5f };
		centred.OnValueChanged = x => _output.Text = $"{x:0.00}";
		row.AddChild( centred );

		row = Case( "No fill" );
		var bare = new Sandbox.UI.SliderControl( 0, 100, 1 ) { Value = 50, Fill = Sandbox.UI.SliderFill.None };
		bare.OnValueChanged = x => _output.Text = $"{x:0}";
		row.AddChild( bare );

		_output = Output();
	}
}
