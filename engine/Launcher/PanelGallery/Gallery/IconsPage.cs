namespace Sandbox.PanelGallery;

/// <summary>
/// Icon glyphs at different sizes and colors. If a tinted icon renders white, class rules
/// on icon panels have stopped applying.
/// </summary>
public class IconsPage : GalleryPage
{
	public IconsPage() : base( "Icons", "Material icons through IconPanel. Sizes scale, tints color - none of these should render default white." )
	{
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
}
