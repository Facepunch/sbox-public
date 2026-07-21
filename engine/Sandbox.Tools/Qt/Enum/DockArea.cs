namespace Editor;

public enum DockArea
{
	Hidden = DockManager.Area.NoDockWidgetArea,

	Left = DockManager.Area.LeftDockWidgetArea,
	Right = DockManager.Area.RightDockWidgetArea,
	Top = DockManager.Area.TopDockWidgetArea,
	Bottom = DockManager.Area.BottomDockWidgetArea,
	Center = DockManager.Area.CenterDockWidgetArea,

	AutoHideBottom = DockManager.Area.BottomAutoHideArea,
}
