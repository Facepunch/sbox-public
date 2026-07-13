namespace Editor;

public partial class DockManager
{
	internal enum Area
	{
		NoDockWidgetArea = 0x00,
		LeftDockWidgetArea = 0x01,
		RightDockWidgetArea = 0x02,
		TopDockWidgetArea = 0x04,
		BottomDockWidgetArea = 0x08,
		CenterDockWidgetArea = 0x10,
		LeftAutoHideArea = 0x20,
		RightAutoHideArea = 0x40,
		TopAutoHideArea = 0x80,
		BottomAutoHideArea = 0x100,
	};

	internal enum DockWidgetFeature
	{
		/// <summary>
		/// dock widget has a close button
		/// </summary>
		Closable = 0x001,

		/// <summary>
		/// dock widget is movable and can be moved to a new position in the current dock container
		/// </summary>
		Movable = 0x002,

		/// <summary>
		/// dock widget can be dragged into a floating window
		/// </summary>
		Floatable = 0x004,

		/// <summary>
		/// deletes the dock widget when it is closed
		/// </summary>
		DeleteOnClose = 0x008,

		/// <summary>
		/// clicking the close button will not close the dock widget but emits the closeRequested() signal instead
		/// </summary>
		CustomCloseHandling = 0x010,

		/// <summary>
		/// if this is enabled, a dock widget can get focus highlighting
		/// </summary>
		Focusable = 0x020,

		/// <summary>
		/// dock widget will be closed when the dock area hosting it is closed
		/// </summary>
		ForceCloseWithArea = 0x040,

		/// <summary>
		/// dock widget tab will never be shown if this flag is set
		/// </summary>
		NoTab = 0x080,

		/// <summary>
		/// deletes only the contained widget on close, keeping the dock widget intact and in place. Attempts to rebuild the contents widget on show if there is a widget factory set.
		/// </summary>
		DeleteContentOnClose = 0x100,

		/// <summary>
		/// dock widget can be pinned and added to an auto hide dock container
		/// </summary>
		Pinnable = 0x200,

		// DefaultDockWidgetFeatures = DockWidgetClosable | DockWidgetMovable | DockWidgetFloatable | DockWidgetFocusable | DockWidgetPinnable,
		// AllDockWidgetFeatures = DefaultDockWidgetFeatures | DockWidgetDeleteOnClose | CustomCloseHandling,
		// DockWidgetAlwaysCloseAndDelete = DockWidgetForceCloseWithArea | DockWidgetDeleteOnClose,
		// GloballyLockableFeatures = DockWidgetClosable | DockWidgetMovable | DockWidgetFloatable | DockWidgetPinnable,
		// NoDockWidgetFeatures = 0x000
	}
}
