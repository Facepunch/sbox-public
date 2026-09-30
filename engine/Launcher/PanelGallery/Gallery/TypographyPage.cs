namespace Sandbox.PanelGallery;

/// <summary>
/// Text styles and the editor palette.
/// </summary>
public class TypographyPage : GalleryPage
{
	public TypographyPage() : base( "Typography", "The text roles and the palette from the editor theme." )
	{
		var column = Case( "Text", column: true );
		column.Add.Label( "Title - the heavy one", "t-title" );
		column.Add.Label( "Body - what most things read as", "t-body" );
		column.Add.Label( "Muted - secondary information", "t-muted" );
		column.Add.Label( "Faint - hints and placeholders", "t-faint" );

		var row = Case( "Palette" );
		Swatch( row, "ground", "sw-ground" );
		Swatch( row, "surface", "sw-surface" );
		Swatch( row, "raised", "sw-raised" );
		Swatch( row, "line", "sw-line" );
		Swatch( row, "accent", "sw-accent" );
		Swatch( row, "positive", "sw-positive" );
		Swatch( row, "danger", "sw-danger" );
	}

	static void Swatch( Panel row, string name, string classname )
	{
		var swatch = row.Add.Panel( $"swatch {classname}" );
		swatch.Add.Label( name );
	}
}
