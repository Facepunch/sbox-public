namespace Sandbox.PanelGallery;

/// <summary>
/// A page of the gallery - an entry in the sidebar and the panel of tests it opens.
/// </summary>
public record GalleryPageInfo( string Title, string Icon, Func<Panel> Create )
{
	/// <summary>
	/// Every page, in sidebar order.
	/// </summary>
	public static readonly GalleryPageInfo[] All =
	[
		new( "Buttons", "smart_button", () => new ButtonsPage() ),
		new( "Text Entry", "edit", () => new TextEntryPage() ),
		new( "Value Controls", "123", () => new InputControlsPage() ),
		new( "Checkbox", "check_box", () => new CheckboxPage() ),
		new( "Focus", "keyboard_tab", () => new FocusPage() ),
		new( "Colour", "palette", () => new ColorControlsPage() ),
		new( "Grouping", "table_rows", () => new LayoutControlsPage() ),
		new( "Folder Select", "folder_open", () => new FolderSelectorPage() ),
		new( "Sliders", "tune", () => new SlidersPage() ),
		new( "Split Container", "vertical_split", () => new SplitContainerPage() ),
		new( "Tree View", "account_tree", () => new TreeViewPage() ),
		new( "Images", "image", () => new DisplayPanelsPage() ),
		new( "Dragging", "drag_indicator", () => new DragPage() ),
		new( "Drag & Drop", "move_to_inbox", () => new DropPage() ),
		new( "Popups", "menu", () => new PopupsPage() ),
		new( "Windows", "web_asset", () => new WindowsPage() ),
		new( "Mouse Capture", "mouse", () => new MouseCapturePage() ),
		new( "Menus", "menu_open", () => new MenusPage() ),
		new( "Tooltips", "chat_bubble_outline", () => new TooltipsPage() ),
		new( "Icons", "mood", () => new IconsPage() ),
		new( "Typography", "text_fields", () => new TypographyPage() ),
	];
}

/// <summary>
/// Base for a gallery page - a heading, a blurb, and titled test cases under it. Pages show
/// the real engine controls styled by the editor stylesheet, so a rendering or styling break
/// is visible here the moment it happens.
/// </summary>
public abstract class GalleryPage : Panel
{
	protected GalleryPage( string title, string blurb )
	{
		AddClass( "gallery-page" );

		this.Add.Label( title, "page-title" );
		this.Add.Label( blurb, "page-blurb" );
	}

	/// <summary>
	/// A titled row of test subjects. Pass <paramref name="column"/> for ones that stack.
	/// </summary>
	protected Panel Case( string title, bool column = false )
	{
		var section = this.Add.Panel( "case" );
		section.Add.Label( title, "case-title" );

		var row = section.Add.Panel( "row" );
		row.SetClass( "column", column );
		return row;
	}

	/// <summary>
	/// A label the tests write into, proving their events actually fired.
	/// </summary>
	protected Sandbox.UI.Label Output()
	{
		var section = this.Add.Panel( "case" );
		section.Add.Label( "Output", "case-title" );
		return section.Add.Label( "-", "output" );
	}
}
