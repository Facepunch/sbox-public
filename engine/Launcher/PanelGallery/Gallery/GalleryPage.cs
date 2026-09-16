namespace Sandbox.PanelGallery;

/// <summary>
/// A page of the gallery - an entry in the sidebar and the panel of tests it opens.
/// </summary>
public record GalleryPageInfo( string Title, string Icon, Func<Panel> Create, string Folder = "Controls" )
{
	/// <summary>
	/// Every page, in sidebar order.
	/// </summary>
	public static readonly GalleryPageInfo[] All =
	[
		new( "transform", "zoom_out_map", () => new TransformPage(), "Css Styles" ),
		new( "Virtual List", "view_list", () => new VirtualControlsPage( false ), "Controls" ),
		new( "Virtual Grid", "grid_view", () => new VirtualControlsPage( true ), "Controls" ),
		new( "Scene Panel", "view_in_ar", () => new ScenePanelPage(), "Controls" ),
		new( "SVG Rendering", "draw", () => new SvgRenderingPage(), "System" ),
		new( "Scene View", "public", () => new SceneViewDemoPage(), "Scene" ),
		new( "SpriteRenderer", "animation", () => new SpriteRendererPage(), "Scene" ),
		new( "Sprite Lighting & Depth", "light_mode", () => new SpriteLightingPage(), "Scene" ),
		new( "Calculator", "calculate", () => new CalculatorPage(), "Demos" ),
		new( "Buttons", "smart_button", () => new ButtonsPage(), "Controls/Input" ),
		new( "Text Entry", "edit", () => new TextEntryPage(), "Controls/Input" ),
		new( "Value Controls", "123", () => new InputControlsPage(), "Controls/Input" ),
		new( "Checkbox", "check_box", () => new CheckboxPage(), "Controls/Input" ),
		new( "Focus", "keyboard_tab", () => new FocusPage(), "System" ),
		new( "Colour", "palette", () => new ColorControlsPage(), "Controls/Input" ),
		new( "Grouping", "table_rows", () => new LayoutControlsPage(), "Controls/Layout" ),
		new( "Folder Select", "folder_open", () => new FolderSelectorPage(), "Controls/Input" ),
		new( "Sliders", "tune", () => new SlidersPage(), "Controls/Input" ),
		new( "Split Container", "vertical_split", () => new SplitContainerPage(), "Controls/Layout" ),
		new( "Docking", "tab", () => new DockingPage(), "Controls/Layout" ),
		new( "Tree View", "account_tree", () => new TreeViewPage(), "Controls/Layout" ),
		new( "Images", "image", () => new DisplayPanelsPage(), "Controls/Display" ),
		new( "Rect", "crop_landscape", () => new DrawPage( "Rectangle" ), "Painter Shapes" ),
		new( "Circle", "circle", () => new DrawPage( "Circle" ), "Painter Shapes" ),
		new( "Star", "star", () => new DrawPage( "Star" ), "Painter Shapes" ),
		new( "Cross", "add", () => new DrawPage( "Cross" ), "Painter Shapes" ),
		new( "Tick", "check", () => new DrawPage( "Tick" ), "Painter Shapes" ),
		new( "Capsule", "medication", () => new DrawPage( "Capsule" ), "Painter Shapes" ),
		new( "Crescent", "dark_mode", () => new DrawPage( "Crescent" ), "Painter Shapes" ),
		new( "Heart", "favorite", () => new DrawPage( "Heart" ), "Painter Shapes" ),
		new( "Triangle", "change_history", () => new DrawPage( "Triangle" ), "Painter Shapes" ),
		new( "Quad", "crop_din", () => new DrawPage( "Quad" ), "Painter Shapes" ),
		new( "Polygon", "pentagon", () => new DrawPage( "Polygon" ), "Painter Shapes" ),
		new( "Line", "horizontal_rule", () => new DrawPage( "Line" ), "Painter Shapes" ),
		new( "Arrow", "east", () => new DrawPage( "Arrow" ), "Painter Shapes" ),
		new( "Ring", "radio_button_unchecked", () => new DrawPage( "Ring" ), "Painter Shapes" ),
		new( "Arc", "rotate_right", () => new DrawPage( "Arc" ), "Painter Shapes" ),
		new( "Pie", "pie_chart", () => new DrawPage( "Pie" ), "Painter Shapes" ),
		new( "Bezier", "gesture", () => new DrawPage( "Bezier" ), "Painter Shapes" ),
		new( "Fill", "format_color_fill", () => new DrawPage( "Fill" ), "Painter/Appearance" ),
		new( "Stroke", "line_style", () => new DrawPage( "Stroke" ), "Painter/Appearance" ),
		new( "Outlines", "border_outer", () => new DrawPage( "Outlines" ), "Painter/Appearance" ),
		new( "Coverage", "blur_on", () => new DrawPage( "Coverage" ), "Painter/Appearance" ),
		new( "Text", "text_fields", () => new DrawPage( "Text" ), "Painter/Text" ),
		new( "Sprites", "animation", () => new SpritePage(), "Painter/Appearance" ),
		new( "Text measurement", "straighten", () => new DrawPage( "Text measurement" ), "Painter/Text" ),
		new( "Clipping", "content_cut", () => new DrawPage( "Clipping" ), "Painter/State" ),
		new( "Transforms", "transform", () => new DrawPage( "Transforms" ), "Painter/State" ),
		new( "Scopes", "restore", () => new DrawPage( "Scopes" ), "Painter/State" ),
		new( "Painter Draw", "sports_esports", () => new DrawPage( "Painter Draw" ), "Demos" ),
		new( "Mock Editor", "web", () => new MockEditorPage(), "Demos" ),
		new( "Panel integration", "web", () => new DrawPage( "Panel integration" ), "Painter/Integration" ),
		new( "Invalidation", "refresh", () => new DrawPage( "Invalidation" ), "Painter/Integration" ),
		new( "Destinations", "devices", () => new PainterPage(), "Painter/Integration" ),
		new( "Layers", "layers", () => new PainterCompositionPage( "Layers" ), "Painter/Compositing" ),
		new( "Layer masks", "filter_frames", () => new PainterCompositionPage( "Layer masks" ), "Painter/Compositing" ),
		new( "Backdrop", "blur_circular", () => new PainterCompositionPage( "Backdrop" ), "Painter/Compositing" ),
		new( "Dragging", "drag_indicator", () => new DragPage(), "System" ),
		new( "Drag & Drop", "move_to_inbox", () => new DropPage(), "System" ),
		new( "Popups", "menu", () => new PopupsPage(), "Controls/Windows" ),
		new( "Windows", "web_asset", () => new WindowsPage(), "Controls/Windows" ),
		new( "Mouse Capture", "mouse", () => new MouseCapturePage(), "System" ),
		new( "Menus", "menu_open", () => new MenusPage(), "Controls/Windows" ),
		new( "Tooltips", "chat_bubble_outline", () => new TooltipsPage(), "System" ),
		new( "Icons", "mood", () => new IconsPage(), "Controls/Display" ),
		new( "Typography", "text_fields", () => new TypographyPage(), "System" ),
	];
}

/// <summary>
/// Base for a gallery page - a heading, a blurb, and titled test cases under it. Pages show
/// the real engine controls styled by the editor stylesheet, so a rendering or styling break
/// is visible here the moment it happens.
/// </summary>
public abstract class GalleryPage : Panel
{
	ScenePanel sceneView;
	CameraComponent sceneCamera;
	float sceneHorizontalFov;
	float sceneOrthoHeight;

	/// <summary>Give the scene the window height, with independently scrolling controls alongside it.</summary>
	protected void UseSceneLayout( ScenePanel view, CameraComponent camera )
	{
		AddClass( "full-page scene-gallery" );
		view.RemoveClass( "scene-control-demo" );
		view.AddClass( "scene-gallery-viewport" );
		var children = Children.ToArray();
		var controls = Add.Panel( "scene-gallery-controls" );
		foreach ( var child in children )
			if ( child != view ) controls.AddChild( child );
		sceneView = view;
		sceneCamera = camera;
		sceneHorizontalFov = camera.FieldOfView;
		sceneOrthoHeight = camera.OrthographicHeight;
	}

	public override void Tick()
	{
		base.Tick();
		if ( sceneCamera is null || !sceneCamera.IsValid() ) return;
		var size = sceneView.Box.RectInner.Size;
		if ( size.x <= 0 || size.y <= 0 ) return;
		// Orthographic projection derives world units per pixel from this height.
		// Screen.Size describes the game screen, not this panel's render target.
		sceneCamera.CustomSize = size;
		// Preserve the composition in both axes: wide windows gain space at the sides,
		// tall windows gain space above and below instead of cropping the subject.
		var aspect = size.x / size.y;
		sceneCamera.OrthographicHeight = sceneOrthoHeight * MathF.Max( 1, 1.5f / aspect );
		sceneCamera.FovAxis = CameraComponent.Axis.Vertical;
		sceneCamera.FieldOfView = Math.Clamp( MathF.Atan( MathF.Tan( sceneHorizontalFov * MathF.PI / 360 ) / MathF.Min( aspect, 1.5f ) ) * 360 / MathF.PI, 1, 179 );
	}

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
	/// A section of independent, square example tiles.
	/// </summary>
	protected Panel Examples( string title )
	{
		var section = Add.Panel( "case reference-section" );
		section.Add.Label( title, "case-title" );
		return section.Add.Panel( "row reference-examples" );
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
