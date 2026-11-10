using Native;
using System;

namespace Editor;

/// <summary>
/// Container for docked widgets
/// </summary>
public partial class DockWidget : Frame
{
	internal Native.CDockWidget _nativeDockWidget;

	internal DockWidget( IntPtr widget ) : base()
	{
		NativeInit( widget );
	}

	internal override void NativeInit( IntPtr ptr )
	{
		_nativeDockWidget = ptr;
		base.NativeInit( ptr );
	}

	internal override void NativeShutdown()
	{
		_nativeDockWidget = default;
		base.NativeShutdown();
	}

	public bool IsClosed => _nativeDockWidget.isClosed();

	/// <summary>
	/// The Widget this DockWidget is wrapping.
	/// </summary>
	public Widget Widget
	{
		get => (Widget)Widget.FindOrCreate( _nativeDockWidget.widget() );
		set => _nativeDockWidget.setWidget( value._widget );
	}

	/// <summary>
	/// Text tooltip for the title bar widget.
	/// </summary>
	public string TabTooltip
	{
		set => _nativeDockWidget.setTabToolTip( value );
	}

	/// <summary>
	/// Prevents the user from closing, moving, floating or pinning this dock.
	/// </summary>
	public bool Locked
	{
		set
		{
			_nativeDockWidget.setFeature( DockManager.DockWidgetFeature.Closable, !value );
			_nativeDockWidget.setFeature( DockManager.DockWidgetFeature.Movable, !value );
			_nativeDockWidget.setFeature( DockManager.DockWidgetFeature.Floatable, !value );
			_nativeDockWidget.setFeature( DockManager.DockWidgetFeature.Pinnable, !value );
		}
	}

	/// <summary>
	/// When true, the dock's minimum size follows its content widget's minimum size.
	/// By default docks can shrink to a tiny fixed size regardless of content.
	/// </summary>
	public bool MinimumSizeFromContent
	{
		set => _nativeDockWidget.setMinimumSizeHintFromContent( value );
	}

	/// <summary>
	/// Raise this dock to the front of any tab group it belongs to.
	/// </summary>
	public void SetAsCurrentTab() => _nativeDockWidget.setAsCurrentTab();

	/// <summary>
	/// Whether this dock is floating in its own top-level window.
	/// </summary>
	public bool IsFloating => _nativeDockWidget.isFloating();

	/// <summary>
	/// Detach this dock into its own floating top-level window.
	/// </summary>
	public void Float() => _nativeDockWidget.setFloating();

	/// <summary>
	/// Toggle the dock open or closed.
	/// </summary>
	public void ToggleView( bool open ) => _nativeDockWidget.toggleView( open );

	internal void CloseDockWidget() => _nativeDockWidget.closeDockWidget();

	internal CDockManager GetDockManager() => _nativeDockWidget.dockManager();
	internal CDockAreaWidget GetDockAreaWidget() => _nativeDockWidget.dockAreaWidget();
}
