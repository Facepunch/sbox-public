namespace PanelGallery.UiTests;

/// <summary>
/// A page in the UI test section. Derive from this and it shows up in the list - nothing to
/// register. <c>[Title]</c>, <c>[Description]</c>, <c>[Icon]</c> describe it. Pages are sorted alphabetically.
/// </summary>
public abstract class UiTestPage : Panel
{
	public UiTestPage()
	{
		AddClass( "uitest-page" );
		StyleSheet.Load( "/styles/uitest.scss" );
	}
}
