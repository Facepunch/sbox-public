using System;

namespace Editor;

public partial class DockManager : Widget
{
	internal Native.CDockManager _nativeDockManager;

	public DockManager( Widget parent = null ) : base( false )
	{
		Sandbox.InteropSystem.Alloc( this );

		Native.CDockManager.Setup();

		_nativeDockManager = Native.CDockManager.Create( parent?._widget ?? default );
		NativeInit( _nativeDockManager );
	}

	internal unsafe override void NativeInit( IntPtr ptr )
	{
		_nativeDockManager = ptr;

		base.NativeInit( ptr );
	}

	internal override void NativeShutdown()
	{
		base.NativeShutdown();

		_nativeDockManager = default;
	}

	/// <summary>
	/// Find an existing dock widget by name. Returns null if not found.
	/// </summary>
	public DockWidget FindDockWidget( string name )
	{
		var native = _nativeDockManager.findDockWidget( name );
		if ( native.IsNull ) return null;

		if ( QObject.AllObjects.TryGetValue( native, out var existing ) && existing is DockWidget dock )
			return dock;

		return new DockWidget( native );
	}

	/// <summary>
	/// Find the dock widget containing the given widget. Returns null if it isn't docked.
	/// </summary>
	public DockWidget FindDockWidget( Widget widget )
	{
		for ( var w = widget; w.IsValid(); w = w.Parent )
		{
			if ( w is DockWidget dock )
				return dock;
		}

		return null;
	}

	/// <summary>
	/// Creates a native dock widget container for the given widget.
	/// </summary>
	public DockWidget CreateDockWidget( string name, string icon, Widget widget )
	{
		var dockWidget = _nativeDockManager.createDockWidget( name );

		dockWidget.setWidget( widget._widget );
		dockWidget.setIcon( icon );

		return new DockWidget( dockWidget );
	}

	/// <summary>
	/// Adds a <see cref="DockWidget"/> into the given area.
	/// </summary>
	public void AddDock( DockWidget widget, DockArea area )
	{
		ArgumentNullException.ThrowIfNull( widget );

		if ( area == DockArea.Hidden )
		{
			// dock it so it's registered for state save/restore, but start closed
			_nativeDockManager.addDockWidgetTab( Area.CenterDockWidgetArea, widget._nativeDockWidget );
			widget.ToggleView( false );
			return;
		}

		_nativeDockManager.addDockWidgetTab( (Area)area, widget._nativeDockWidget );
	}

	/// <summary>
	/// Adds a <see cref="DockWidget"/> into an area relative to an existing dock, splitting its space.
	/// <see cref="DockArea.Center"/> docks it as a tab. Pass null to dock relative to the whole window.
	/// </summary>
	public void AddDock( DockWidget widget, DockArea area, DockWidget relativeTo )
	{
		ArgumentNullException.ThrowIfNull( widget );

		var areaWidget = relativeTo is null ? default : relativeTo.GetDockAreaWidget();
		_nativeDockManager.addDockWidget( (Area)area, widget._nativeDockWidget, areaWidget, -1 );
	}

	/// <summary>
	/// Adds a <see cref="DockWidget"/> as a tab alongside an existing dock.
	/// </summary>
	public void AddDock( DockWidget widget, DockWidget tabWith )
	{
		ArgumentNullException.ThrowIfNull( tabWith );

		AddDock( widget, DockArea.Center, tabWith );
	}

	/// <summary>
	/// Creates and docks a widget into the given area, registering it so it appears in view menus.
	/// This is the primary entry point for adding docks. If a dock with this name already exists
	/// (e.g. after a hotload), its content is replaced instead. Pass <paramref name="relativeTo"/>
	/// to place it relative to an existing dock instead - <see cref="DockArea.Center"/> docks it
	/// as a tab, other areas split the target dock's space.
	/// </summary>
	public DockWidget AddDock( string name, string icon, Widget widget, DockArea area = DockArea.Left, DockWidget relativeTo = null )
	{
		ArgumentNullException.ThrowIfNull( widget );

		docks[name] = new DockInfo { Title = name, Icon = icon, Area = area, CreateAction = () => widget };

		if ( FindDockWidget( name ) is { } existing )
		{
			var previous = existing.Widget;
			if ( previous == widget ) return existing;

			existing.Widget = widget;
			previous?.Destroy();
			return existing;
		}

		var dockWidget = CreateDockWidget( name, icon, widget );

		if ( relativeTo is not null )
			AddDock( dockWidget, area, relativeTo );
		else
			AddDock( dockWidget, area );

		return dockWidget;
	}

	/// <summary>
	/// Sets a widget as the central (always present, non-closable) area. Must be
	/// called before any other dock is added. Returns the dock hosting it, which
	/// can be used as a target for relative docking.
	/// </summary>
	public DockWidget SetCentralWidget( Widget widget )
	{
		ArgumentNullException.ThrowIfNull( widget );

		var dock = CreateDockWidget( "CentralWidget", string.Empty, widget );
		dock._nativeDockWidget.setFeature( DockWidgetFeature.NoTab, true );

		_nativeDockManager.setCentralWidget( dock._nativeDockWidget );

		return dock;
	}

	/// <summary>
	/// Sets the relative proportions of every visible area in the splitter containing <paramref name="dock"/>.
	/// Values are ordered left-to-right or top-to-bottom.
	/// </summary>
	/// <param name="dock">Any dock in the splitter.</param>
	/// <param name="proportions">One relative proportion for each visible area. Values do not need to total 1.</param>
	/// <returns>Whether the proportions were applied.</returns>
	public unsafe bool SetSplitterProportions( DockWidget dock, params ReadOnlySpan<float> proportions )
	{
		if ( dock is null || proportions.Length < 2 )
			return false;

		foreach ( var proportion in proportions )
		{
			if ( !float.IsFinite( proportion ) || proportion < 0.0f )
				return false;
		}

		fixed ( float* proportionPtr = proportions )
		{
			_nativeDockManager.layout().activate();
			return _nativeDockManager.setSplitterProportions( dock.GetDockAreaWidget(), proportionPtr, proportions.Length );
		}
	}

	/// <summary>
	/// Whether the named dock is currently open (visible).
	/// </summary>
	public bool IsDockOpen( string name )
	{
		var dock = FindDockWidget( name );
		return dock is not null && !dock.IsClosed;
	}

	/// <summary>
	/// Whether the dock containing the given widget is currently open.
	/// </summary>
	public bool IsDockOpen( Widget widget )
	{
		var dock = FindDockWidget( widget );
		return dock is not null && !dock.IsClosed;
	}

	/// <summary>
	/// Remove a dock widget from the manager.
	/// </summary>
	public void RemoveDock( DockWidget widget )
	{
		if ( widget is null ) return;

		_nativeDockManager.removeDockWidget( widget._nativeDockWidget );
	}

	/// <summary>
	/// Raise a dock to the front of its tab group.
	/// </summary>
	public bool RaiseDock( string name )
	{
		var dock = FindDockWidget( name );
		if ( dock is null ) return false;

		dock.SetAsCurrentTab();
		return true;
	}

	/// <summary>
	/// Raise the dock containing the given widget to the front of its tab group.
	/// </summary>
	public bool RaiseDock( Widget widget )
	{
		var dock = FindDockWidget( widget );
		if ( dock is null ) return false;

		dock.SetAsCurrentTab();
		return true;
	}

	/// <summary>
	/// Toggle a registered dock's visibility by name. If the dock doesn't exist yet, it will
	/// be created from its registered <see cref="DockInfo"/>.
	/// </summary>
	public void SetDockState( string name, bool visible )
	{
		var existing = FindDockWidget( name );
		if ( existing is not null )
		{
			existing.ToggleView( visible );
			return;
		}

		// Nothing to hide if it doesn't exist
		if ( !visible )
			return;

		// Create from registered dock type
		if ( !docks.TryGetValue( name, out var info ) )
			return;

		var widget = info.CreateAction();
		if ( widget is null )
			return;

		AddDock( info.Title, info.Icon, widget, info.Area );
	}

	/// <summary>
	/// Open a registered dock and move it into the given area, creating it if needed.
	/// </summary>
	public DockWidget OpenDock( string name, DockArea area, DockWidget relativeTo = null )
	{
		SetDockState( name, true );

		var dock = FindDockWidget( name );
		if ( dock is null ) return null;

		AddDock( dock, area, relativeTo );
		return dock;
	}

	/// <summary>
	/// Clear all registered dock types.
	/// </summary>
	public void Clear()
	{
		docks.Clear();
	}
}
