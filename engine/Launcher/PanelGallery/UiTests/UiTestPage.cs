namespace PanelGallery.UiTests;

/// <summary>
/// A page in the UI test section. Derive from this and it shows up in the list - nothing to
/// register. <c>[Title]</c>, <c>[Description]</c>, <c>[Icon]</c> and <c>[Order]</c> place it.
/// </summary>
public abstract class UiTestPage : Panel
{
	public UiTestPage()
	{
		AddClass( "uitest-page" );
		StyleSheet.Load( "/styles/uitest.scss" );
	}
}
