namespace Sandbox.PanelGallery;

/// <summary>
/// Split containers - the splitter should track the cursor exactly while dragging.
/// </summary>
public class SplitContainerPage : GalleryPage
{
	public SplitContainerPage() : base( "Split Container", "SplitContainer. Drag the splitter - it should sit under the cursor the whole way." )
	{
		var row = Case( "Horizontal" );
		row.AddChild( Split( vertical: false, "Left", "Right" ) );

		row = Case( "Vertical" );
		row.AddChild( Split( vertical: true, "Top", "Bottom" ) );
	}

	static Sandbox.UI.SplitContainer Split( bool vertical, string first, string second )
	{
		var split = new Sandbox.UI.SplitContainer { Vertical = vertical };
		split.AddClass( "demo-split" );

		split.Left.Add.Label( first );
		split.Right.Add.Label( second );

		return split;
	}
}
