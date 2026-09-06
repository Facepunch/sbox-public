namespace Editor.MeshEditor;

/// <summary>
/// The pivot buttons and the shortcuts that drive them. This is a widget of its own so that every
/// selection mode's sidebar can host one - shortcuts resolve against a live instance of their
/// declaring type, so putting them on a single mode's sidebar would leave them dead in the others.
/// </summary>
public class PivotButtonsWidget : Widget
{
	readonly SelectionTool _tool;

	public PivotButtonsWidget( ToolSidebarWidget sidebar, SelectionTool tool, bool enabled )
	{
		_tool = tool;

		Layout = Layout.Row();
		Layout.Spacing = 4;

		sidebar.CreateButton( "Previous", "meshtools/pivot_tools/previous.png", "mesh.previous-pivot", PreviousPivot, enabled, Layout );
		sidebar.CreateButton( "Next", "meshtools/pivot_tools/next.png", "mesh.next-pivot", NextPivot, enabled, Layout );
		sidebar.CreateButton( "Clear", "meshtools/pivot_tools/clear.png", "mesh.clear-pivot", ClearPivot, enabled, Layout );
		sidebar.CreateButton( "Center", "meshtools/object_selection_buttons/center_origin.png", "mesh.center-pivot", CenterPivot, enabled, Layout );
		sidebar.CreateButton( "World Origin", "meshtools/pivot_tools/world_origin.png", "mesh.zero-pivot", ZeroPivot, enabled, Layout );

		Layout.AddStretchCell();
	}

	[Shortcut( "mesh.previous-pivot", "Shift+MWheelDown", typeof( SceneViewWidget ) )]
	public void PreviousPivot() => _tool.Pivot.Previous();

	[Shortcut( "mesh.next-pivot", "Shift+MWheelUp", typeof( SceneViewWidget ) )]
	public void NextPivot() => _tool.Pivot.Next();

	[Shortcut( "mesh.center-pivot", "Ctrl+Home", typeof( SceneViewWidget ) )]
	public void CenterPivot() => _tool.Pivot.Center();

	[Shortcut( "mesh.clear-pivot", "Home", typeof( SceneViewWidget ) )]
	public void ClearPivot() => _tool.Pivot.Clear();

	[Shortcut( "mesh.zero-pivot", "Ctrl+End", typeof( SceneViewWidget ) )]
	public void ZeroPivot() => _tool.Pivot.Zero();
}
