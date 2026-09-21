using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A tab bar and pages. Inactive pages remain alive; removing a tab deletes its page.
/// </summary>
[Library( "tabpanel" )]
[StyleSheet.Inline( "tabpanel", Styles )]
public class TabPanel : Panel
{
	const string Styles = """
		.tabpanel { flex-direction: column; flex-grow: 1; min-width: 0; min-height: 0; }
		.tabpanel > .tab-body, .tab-page { flex-grow: 1; min-width: 0; min-height: 0; }
		.tab-page > * { flex-grow: 1; min-width: 0; min-height: 0; }
		""";
	readonly Dictionary<Tab, Panel> _pages = new();

	/// <summary>
	/// The shared bar. Configure reordering, closing and selection here.
	/// </summary>
	public TabBar TabBar { get; }

	/// <summary>
	/// Container for pages.
	/// </summary>
	public Panel Body { get; }

	public TabPanel()
	{
		AddClass( "tabpanel" );
		TabBar = AddChild( new TabBar() );
		Body = Add.Panel( "tab-body" );
		TabBar.SelectionChanged += _ => UpdatePages();
		TabBar.TabRemoved += tab => { if ( _pages.Remove( tab, out var page ) ) page.Delete( true ); };
	}

	/// <summary>
	/// Add and own a page. Content survives tab changes and reordering.
	/// </summary>
	public Tab AddTab( string text, Panel content, string icon = null, bool canClose = false, int? count = null )
	{
		ArgumentNullException.ThrowIfNull( content );
		if ( !content.IsValid || content.IsDeleting || content is RootPanel || AncestorsAndSelf.Contains( content ) || content.Ancestors.OfType<TabPanel>().Any() )
			throw new ArgumentException( "The content is invalid or already belongs to a tab panel.", nameof( content ) );
		var page = Body.Add.Panel( "tab-page" );
		content.Parent = page;
		var tab = TabBar.AddTab( text, icon, canClose, count );
		_pages.Add( tab, page );
		UpdatePages();
		return tab;
	}

	void UpdatePages()
	{
		foreach ( var (tab, page) in _pages )
		{
			if ( tab != TabBar.SelectedTab ) UISystem?.ReleaseFocusSubtree( page );
			page.Style.Display = tab == TabBar.SelectedTab ? DisplayMode.Flex : DisplayMode.None;
		}
	}
}
