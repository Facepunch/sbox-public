namespace Sandbox.PanelGallery;

/// <summary>
/// Icon glyphs at different sizes and colors. If a tinted icon renders white, class rules
/// on icon panels have stopped applying.
/// </summary>
public class IconsPage : GalleryPage
{
	/// <summary>
	/// Builds the interactive icon specimens.
	/// </summary>
	public IconsPage() : base( "Icons", "Material Symbols through IconPanel. Adjust the axes to compare all three families." )
	{
		AddVariationControls();

		var row = Case( "Sizes" );
		row.Add.Icon( "rocket_launch", "s14" );
		row.Add.Icon( "rocket_launch", "s18" );
		row.Add.Icon( "rocket_launch", "s24" );
		row.Add.Icon( "rocket_launch", "s32" );
		row.Add.Icon( "rocket_launch", "s48" );

		row = Case( "Tints" );
		row.Add.Icon( "favorite", "s24 tint-muted" );
		row.Add.Icon( "favorite", "s24 tint-accent" );
		row.Add.Icon( "favorite", "s24 tint-positive" );
		row.Add.Icon( "favorite", "s24 tint-danger" );

		row = Case( "A pile of glyphs" );
		foreach ( var icon in new[] { "home", "settings", "search", "folder_open", "check", "close", "add_box", "delete", "edit", "visibility", "lock", "public" } )
		{
			row.Add.Icon( icon, "s24 tint-muted" );
		}
	}

	void AddVariationControls()
	{
		var preview = Case( "Variable Material Symbols" );
		foreach ( var family in new[] { "Outlined", "Rounded", "Sharp" } )
		{
			var specimen = preview.Add.Panel( "control-state" );
			specimen.Add.Label( family, "reference-title" );
			var icon = specimen.Add.Icon( "favorite", "s48 tint-accent" );
			icon.Style.FontFamily = $"Material Symbols {family}";
		}

		SliderControl Slider( string name, float min, float max, float step, float initial )
		{
			return Case( name ).AddChild( new SliderControl( min, max, step ) { Value = initial } );
		}

		var fill = Slider( "Fill", 0, 1, 0.05f, 0 );
		var weight = Slider( "Weight", 100, 700, 10, 400 );
		var grade = Slider( "Grade", -50, 200, 5, 0 );
		var opticalSize = Slider( "Optical size", 20, 48, 1, 24 );
		var output = Output();

		void Update()
		{
			preview.Style.FontVariationSettings = new FontVariationSettings { Fill = fill.Value, Weight = weight.Value, Grade = grade.Value, OpticalSize = opticalSize.Value };
			output.Text = FormattableString.Invariant( $"font-variation-settings: 'FILL' {fill.Value:0.##}, 'wght' {weight.Value}, 'GRAD' {grade.Value}, 'opsz' {opticalSize.Value};" );
		}

		foreach ( var slider in new[] { fill, weight, grade, opticalSize } )
			slider.OnValueChanged = _ => Update();

		Update();
	}
}
