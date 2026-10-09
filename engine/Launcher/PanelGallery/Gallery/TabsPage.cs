using Sandbox.UI;
using Sandbox.UI.Construct;
using Label = Sandbox.UI.Label;
using TextEntry = Sandbox.UI.TextEntry;
using TabBar = Sandbox.UI.TabBar;

namespace Sandbox.PanelGallery;

/// <summary>
/// Shared tabs with persistent pages, optional dragging and contextual actions.
/// </summary>
public class TabsPage : GalleryPage
{
	readonly Label _output;
	int _documents;

	public TabsPage() : base( "Tabs", "Select with the mouse or Left/Right, Home/End. Drag to reorder, middle-click to close, or right-click for actions. Docking uses the same tabs." )
	{
		var row = Case( "Document tabs · reorder, close and context menus", column: true );
		var pages = row.AddChild( new TabPanel() );
		pages.Style.Width = Length.Percent( 100 );
		pages.Style.Height = 180;
		pages.TabBar.AllowReorder = true;
		AddDocument( pages, "Welcome", "home", false );
		AddDocument( pages, "Scene", "view_in_ar", true );
		AddDocument( pages, "Material", "texture", true );
		pages.TabBar.SelectionChanged += tab => Report( $"Selected {tab?.Text ?? "nothing"}" );
		pages.TabBar.TabReordered += ( tab, index ) => Report( $"Moved {tab.Text} to position {index + 1}" );
		pages.TabBar.TabRemoved += tab => Report( $"Closed {tab.Text}" );
		var toolbar = row.AddChild( new Toolbar() );
		toolbar.AddButton( "New document", "add", () =>
		{
			var tab = AddDocument( pages, $"Document {++_documents}", "description", true );
			pages.TabBar.SelectTab( tab );
		} );
		toolbar.AddToggle( "Allow reordering", "swap_horiz", true, enabled => pages.TabBar.AllowReorder = enabled );

		row = Case( "Standalone tab bar · fixed order", column: true );
		var bar = row.AddChild( new TabBar() );
		bar.Style.Width = Length.Percent( 100 );
		bar.AddTab( "Inspector", "tune" );
		bar.AddTab( "History", "history", count: 8 );
		bar.AddTab( "Settings", "settings" );
		bar.SelectionChanged += tab => Report( $"Standalone: {tab.Text}" );

		row = Case( "Count badges · narrow the strip to switch to icons", column: true );
		var responsive = row.AddChild( new TabBar { AllowReorder = true } );
		responsive.Style.Width = 540;
		responsive.Style.MaxWidth = Length.Percent( 100 );
		var messages = responsive.AddTab( "Messages", "mail", count: 12 );
		responsive.AddTab( "Warnings", "warning", count: 3 );
		responsive.AddTab( "Errors", "error", count: 0 );
		responsive.AddTab( "Activity", "history" );
		var width = row.AddChild( new SliderControl( 180, 640, 10 ) { Value = 540 } );
		width.Style.Width = 320;
		width.OnValueChanged = value => responsive.Style.Width = value;
		var badgeTools = row.AddChild( new Toolbar() );
		badgeTools.AddButton( "Add message", "add", () => messages.Count = (messages.Count ?? 0) + 1 );
		badgeTools.AddToggle( "Show count", "tag", true, visible => messages.Count = visible ? 12 : null );

		row = Case( "Overflow · icons scroll when even compact tabs cannot fit", column: true );
		var overflow = row.AddChild( new TabBar { AllowReorder = true } );
		overflow.Style.Width = 360;
		for ( var i = 1; i <= 8; i++ ) overflow.AddTab( $"Document {i}", "description", true );
		_output = Output();
		_output.Text = "Edit a page, switch tabs, then return — the text is retained.";
	}

	Tab AddDocument( TabPanel pages, string title, string icon, bool closable )
	{
		var content = new Panel();
		content.Style.Padding = 16;
		content.Style.FlexDirection = FlexDirection.Column;
		content.Add.Label( title ).Style.MarginBottom = 12;
		content.AddChild( new TextEntry { Text = $"Notes for {title}", Placeholder = "Type here…" } );
		var tab = pages.AddTab( title, content, icon, closable );
		tab.BuildContextMenu = menu =>
		{
			menu.AddOption( "Duplicate", "content_copy", () => AddDocument( pages, title + " copy", icon, true ) );
			menu.AddOption( "Close other tabs", () =>
			{
				foreach ( var other in pages.TabBar.Tabs.Where( x => x != tab ).ToArray() ) pages.TabBar.CloseTab( other );
			} );
		};
		return tab;
	}

	void Report( string message ) { if ( _output is not null ) _output.Text = message; }
}
