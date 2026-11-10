namespace Editor.TerrainEditor;

partial class TerrainEditorTool
{
	private EditorToolButton selectionOptionsButton;

	public bool SimpleBrushMode { get; set; } = false;

	private bool _capsLockHeld;

	public override void OnUpdate()
	{
		var capsDown = Application.IsKeyDown( KeyCode.CapsLock );
		if ( capsDown && !_capsLockHeld )
		{
			SimpleBrushMode = !SimpleBrushMode;
			SaveSimpleBrush();
		}
		_capsLockHeld = capsDown;
	}

	public override Widget CreateToolbarWidget()
	{
		var group = new Widget();
		group.FixedHeight = Theme.RowHeight;
		group.Layout = Layout.Row();
		group.Layout.Spacing = 2;

		group.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( group.LocalRect, Theme.ControlRadius );
			return true;
		};

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

		AddCheckboxOption( menu, "Simple Brush", "brush", "Sets the brush to a simple outline of the brush. (Caps Lock)",
			SimpleBrushMode, ( v ) => { SimpleBrushMode = v; SaveSimpleBrush(); } );

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

	private void SaveSimpleBrush()
	{
		EditorCookie.Set( "TerrainTool.SimpleBrush", SimpleBrushMode );
	}

	private void LoadToolbarCookies()
	{
		SimpleBrushMode = EditorCookie.Get( "TerrainTool.SimpleBrush", false );
	}
}
