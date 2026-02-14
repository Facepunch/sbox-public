namespace Editor.MeshEditor;

partial class MeshTool
{
	private EditorToolButton vertexSnapButton;
	private EditorToolButton selectionOptionsButton;

	public bool VertexSnappingEnabled { get; set; } = false;
	public bool OverlaySelection { get; set; } = true;
	public bool LassoPartialSelection { get; set; } = true;
	public bool SelectionThrough { get; set; } = true;

	public override Widget CreateToolbarWidget()
	{
		var group = new Widget();
		group.FixedHeight = Theme.RowHeight;
		group.Layout = Layout.Row();
		group.Layout.Spacing = 6;
		group.Layout.Margin = new( 2, 0 );

		group.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( group.LocalRect, Theme.ControlRadius );
			return true;
		};

		vertexSnapButton = new EditorToolButton();
		vertexSnapButton.GetIcon = () => "trip_origin";
		vertexSnapButton.ToolTip = "Toggle Vertex Snapping";
		vertexSnapButton.Action = ToggleVertexSnapping;
		vertexSnapButton.IsActive = () => VertexSnappingEnabled;

		group.Layout.Add( vertexSnapButton );

		selectionOptionsButton = new EditorToolButton();
		selectionOptionsButton.GetIcon = () => "rule";
		selectionOptionsButton.ToolTip = "Selection Options";
		selectionOptionsButton.Action = ShowSelectionOptionsMenu;

		group.Layout.Add( selectionOptionsButton );

		return group;
	}

	private void ShowSelectionOptionsMenu()
	{
		var menu = new Menu();
		menu.ContentMargins = 0;

		var header = new Widget
		{
			FixedWidth = 250f,
			FixedHeight = Theme.RowHeight,
			Layout = Layout.Row()
		};
		header.Layout.Spacing = 4;
		header.Layout.Margin = new Sandbox.UI.Margin( 8, 0 );
		header.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( header.LocalRect );
			return true;
		};

		var label = header.Layout.Add( new Label( "Selection Options" ) );
		label.SetStyles( "font-weight: bold;" );

		menu.AddWidget( header );
		menu.AddSeparator();

		AddCheckboxOption( menu, "Selection Overlay", "blur_on", "Highlight selected elements with an overlay",
			OverlaySelection, ( v ) => { OverlaySelection = v; SaveOverlaySelection(); } );

		AddCheckboxOption( menu, "Lasso Partial Selection", "photo_size_select_small", "Allow lasso to select elements that are partially contained",
			LassoPartialSelection, ( v ) => { LassoPartialSelection = v; SaveLassoPartialSelection(); } );

		AddCheckboxOption( menu, "Selection Through Geometry", "select_all", "Allow selection of elements behind other geometry",
			SelectionThrough, ( v ) => { SelectionThrough = v; SaveSelectionThrough(); } );

		AddCheckboxOption( menu, "Backface Selection", "flip_to_back", "Allow selection of backfacing elements",
			EditorPreferences.BackfaceSelection, ( v ) => { EditorPreferences.BackfaceSelection = v; } );

		menu.OpenAtCursor();
	}

	private void AddCheckboxOption( Menu menu, string title, string icon, string tooltip, bool currentValue, Action<bool> onChanged )
	{
		var row = new Widget
		{
			FixedWidth = 250f,
			FixedHeight = Theme.RowHeight,
			Layout = Layout.Row()
		};
		row.Layout.Spacing = 4;
		row.Layout.Margin = new Sandbox.UI.Margin( 8, 0 );

		row.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Paint.HasMouseOver ? Theme.ControlBackground : Theme.WidgetBackground );
			Paint.DrawRect( row.LocalRect );
			return true;
		};

		row.MouseClick = () =>
		{
			onChanged( !currentValue );
		};

		var iconWidget = row.Layout.Add( new IconLabel( icon )
		{
			FixedSize = 16
		} );

		row.Layout.Add( new Label( title ) { ToolTip = tooltip } );
		row.Layout.AddStretchCell();

		var checkbox = row.Layout.Add( new Checkbox
		{
			State = currentValue ? CheckState.On : CheckState.Off
		} );
		checkbox.Clicked = () => onChanged( checkbox.State == CheckState.On );

		menu.AddWidget( row );
	}

	private void ToggleVertexSnapping()
	{
		VertexSnappingEnabled = !VertexSnappingEnabled;
		SaveVertexSnapping();
	}

	private void SaveVertexSnapping()
	{
		EditorCookie.Set( "MeshTool.VertexSnapping", VertexSnappingEnabled );
	}

	private void SaveOverlaySelection()
	{
		EditorCookie.Set( "MeshTool.OverlaySelection", OverlaySelection );
	}

	private void SaveLassoPartialSelection()
	{
		EditorCookie.Set( "MeshTool.LassoPartialSelection", LassoPartialSelection );
	}

	private void SaveSelectionThrough()
	{
		EditorCookie.Set( "MeshTool.SelectionThrough", SelectionThrough );
	}

	private void LoadToolbarCookies()
	{
		OverlaySelection = EditorCookie.Get( "MeshTool.OverlaySelection", true );
		VertexSnappingEnabled = EditorCookie.Get( "MeshTool.VertexSnapping", false );
		LassoPartialSelection = EditorCookie.Get( "MeshTool.LassoPartialSelection", true );
		SelectionThrough = EditorCookie.Get( "MeshTool.SelectionThrough", true );
	}
}
